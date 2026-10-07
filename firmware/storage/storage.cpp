#include "storage/storage.hpp"
#include "protocol/protocol.hpp"
#include <algorithm>
#include <cstdio>
#ifdef MIKU_HOST
#include <filesystem>
#include <fstream>
namespace fs = std::filesystem;
#else
#include "esp_vfs_fat.h"
#include <dirent.h>
#include <sys/stat.h>
#include <unistd.h>
#endif
namespace miku {
uint32_t crc32_update(uint32_t state, const std::string& data) {
    for (unsigned char c : data) { state ^= c; for (int i = 0; i < 8; ++i) state = (state >> 1) ^ (0xedb88320u & (0u - (state & 1u))); }
    return state;
}
std::string json_string(const std::string& value) {
    std::string out = "\""; const char* hex = "0123456789abcdef";
    for (unsigned char c : value) { if (c == '"' || c == '\\') { out += '\\'; out += c; } else if (c < 32) { out += "\\u00"; out += hex[c >> 4]; out += hex[c & 15]; } else out += c; }
    return out + '"';
}
bool valid_file_path(const std::string& path) {
    if (path.empty() || path[0] != '/' || path.size() > 160 || !utf8_valid(path)) return false;
    if (path == "/") return true;
    if (path.back() == '/') return false;
    size_t begin = 1;
    while (begin < path.size()) {
        auto end = path.find('/', begin); if (end == std::string::npos) end = path.size();
        auto name = path.substr(begin, end - begin); auto lower = name;
        std::transform(lower.begin(), lower.end(), lower.begin(), [](unsigned char c) { return static_cast<char>(c >= 'A' && c <= 'Z' ? c + 32 : c); });
        if (name.empty() || name.size() > 128 || name == "." || name == ".." || name.back() == '.' || name.back() == ' ' || lower == ".miku") return false;
        for (unsigned char c : name) if (c < 32 || c == 127 || std::string("<>:\"\\|?*").find(c) != std::string::npos) return false;
        begin = end + 1;
    }
    return true;
}
#ifdef MIKU_HOST
static fs::path native(const std::string& root, const std::string& path) { return fs::u8path(root) / fs::u8path(path.substr(1)); }
static bool no_links(const fs::path& root, fs::path target) {
    std::error_code error;
    while (target != root && !target.empty()) { if (fs::is_symlink(fs::symlink_status(target, error))) return false; error.clear(); target = target.parent_path(); }
    return target == root;
}
StorageInfo FileStore::info() const {
    uint64_t used = 0; std::error_code error;
    if (!ready_) return {false, 0, 0};
    for (fs::recursive_directory_iterator it(fs::u8path(root_), error), end; it != end && !error; it.increment(error)) if (it->is_regular_file(error)) used += ((it->file_size(error) + 4095) / 4096) * 4096;
    const uint64_t total = 12 * 1024 * 1024;
    return {!error, total, used < total ? total - used : 0};
}
bool FileStore::stat(const std::string& path, FileEntry& entry) const {
    auto target = native(root_, path); std::error_code error;
    if (!ready_ || !no_links(fs::u8path(root_), target)) return false;
    auto state = fs::status(target, error); if (error || (!fs::is_directory(state) && !fs::is_regular_file(state))) return false;
    entry = {target.filename().u8string(), fs::is_directory(state), 0};
    if (!entry.directory) { auto size = fs::file_size(target, error); if (error || size > UINT32_MAX) return false; entry.size = static_cast<uint32_t>(size); }
    return true;
}
bool FileStore::list(const std::string& path, std::vector<FileEntry>& entries) const {
    FileEntry dir; if (!stat(path, dir) || !dir.directory) return false;
    std::error_code error; entries.clear();
    for (fs::directory_iterator it(native(root_, path), error), end; it != end && !error; it.increment(error)) {
        auto name = it->path().filename().u8string(); if (name == ".miku") continue;
        FileEntry entry; if (stat((path == "/" ? "" : path) + "/" + name, entry)) entries.push_back(entry);
        if (entries.size() > 128) return false;
    }
    return !error;
}
bool FileStore::read(const std::string& path, uint32_t offset, uint32_t count, std::string& data) const {
    FileEntry entry; if (!stat(path, entry) || entry.directory || offset > entry.size) return false;
    std::ifstream file(native(root_, path), std::ios::binary); if (!file) return false;
    file.seekg(offset); data.resize(std::min(count, entry.size - offset)); file.read(data.data(), data.size()); return static_cast<size_t>(file.gcount()) == data.size();
}
bool FileStore::write(const std::string& path, uint32_t offset, const std::string& data, bool truncate) const {
    auto target = native(root_, path); if (!ready_ || !no_links(fs::u8path(root_), target)) return false;
    if (truncate) { std::ofstream file(target, std::ios::binary | std::ios::trunc); file.write(data.data(), data.size()); file.flush(); return !!file; }
    std::fstream file(target, std::ios::binary | std::ios::in | std::ios::out); file.seekp(offset); file.write(data.data(), data.size()); file.flush(); return !!file;
}
bool FileStore::mkdir(const std::string& path) const { std::error_code error; auto target = native(root_, path); return ready_ && no_links(fs::u8path(root_), target) && fs::create_directory(target, error) && !error; }
bool FileStore::remove(const std::string& path) const { std::error_code error; auto target = native(root_, path); return ready_ && no_links(fs::u8path(root_), target) && fs::remove(target, error) && !error; }
bool FileStore::rename(const std::string& from, const std::string& to) const {
    std::error_code error; FileEntry entry; if (!ready_ || stat(to, entry) || !no_links(fs::u8path(root_), native(root_, from)) || !no_links(fs::u8path(root_), native(root_, to))) return false;
    fs::rename(native(root_, from), native(root_, to), error); return !error;
}
#else
StorageInfo FileStore::info() const { uint64_t total = 0, free = 0; bool ok = ready_ && esp_vfs_fat_info(root_.c_str(), &total, &free) == ESP_OK; return {ok, total, free}; }
bool FileStore::stat(const std::string& path, FileEntry& entry) const {
    struct stat s{}; if (!ready_ || ::stat((root_ + path).c_str(), &s) != 0 || (!S_ISDIR(s.st_mode) && !S_ISREG(s.st_mode))) return false;
    entry = {path.substr(path.find_last_of('/') + 1), S_ISDIR(s.st_mode), static_cast<uint32_t>(s.st_size)}; return true;
}
bool FileStore::list(const std::string& path, std::vector<FileEntry>& entries) const {
    if (!ready_) return false;
    auto* dir = opendir((root_ + path).c_str()); if (!dir) return false; entries.clear(); bool ok = true;
    while (auto* item = readdir(dir)) { std::string name = item->d_name; if (name == "." || name == ".." || name == ".miku") continue;
        FileEntry entry; if (!stat((path == "/" ? "" : path) + '/' + name, entry)) { ok = false; break; } entries.push_back(entry); if (entries.size() > 128) { ok = false; break; }
    }
    closedir(dir); return ok;
}
bool FileStore::read(const std::string& path, uint32_t offset, uint32_t count, std::string& data) const {
    FileEntry entry; if (!stat(path, entry) || entry.directory || offset > entry.size) return false;
    auto* file = fopen((root_ + path).c_str(), "rb"); if (!file) return false; data.resize(std::min(count, entry.size - offset));
    bool ok = fseek(file, offset, SEEK_SET) == 0 && fread(data.data(), 1, data.size(), file) == data.size(); fclose(file); return ok;
}
bool FileStore::write(const std::string& path, uint32_t offset, const std::string& data, bool truncate) const {
    if (!ready_) return false;
    auto* file = fopen((root_ + path).c_str(), truncate ? "wb" : "r+b"); if (!file) return false;
    bool ok = fseek(file, offset, SEEK_SET) == 0 && fwrite(data.data(), 1, data.size(), file) == data.size();
    if (fflush(file) != 0 || fsync(fileno(file)) != 0) ok = false;
    if (fclose(file) != 0) ok = false;
    return ok;
}
bool FileStore::mkdir(const std::string& path) const { return ready_ && ::mkdir((root_ + path).c_str(), 0777) == 0; }
bool FileStore::remove(const std::string& path) const { FileEntry e; return stat(path, e) && (e.directory ? ::rmdir((root_ + path).c_str()) : ::unlink((root_ + path).c_str())) == 0; }
bool FileStore::rename(const std::string& from, const std::string& to) const { FileEntry e; return ready_ && !stat(to, e) && ::rename((root_ + from).c_str(), (root_ + to).c_str()) == 0; }
#endif
}
