#include "storage/file_service.hpp"
#include <algorithm>
#include <iomanip>
#include <sstream>
namespace miku {
static constexpr uint32_t max_file = 4 * 1024 * 1024, chunk_size = 2048;
static const std::string staging = "/.miku/upload.bin", backup = "/.miku/old.bin", journal = "/.miku/commit.txt";
static bool number(const std::string& text, uint32_t& value) {
    if (text.empty() || text.size() > 10) return false;
    uint64_t n = 0;
    for (char c : text) { if (c < '0' || c > '9') return false; n = n * 10 + c - '0'; if (n > UINT32_MAX) return false; }
    value = static_cast<uint32_t>(n); return true;
}
static std::string parent(const std::string& path) { auto pos = path.find_last_of('/'); return pos == 0 ? "/" : path.substr(0, pos); }
std::string FileService::version() const { return epoch_ + "-" + std::to_string(revision_); }
FileService::FileService(Platform& platform) : platform_(platform), store_(platform.files()), epoch_(std::to_string(platform.storage_nonce())) {
    if (!store_ || !store_->info().ready) return;
    FileEntry entry; if (!store_->stat("/.miku", entry) && !store_->mkdir("/.miku")) return;
    if (store_->stat(journal, entry)) {
        std::string path; if (entry.size > 160 || !store_->read(journal, 0, entry.size, path) || !valid_file_path(path) || path == "/") return;
        FileEntry target, old;
        if (!store_->stat(path, target) && store_->stat(backup, old) && !store_->rename(backup, path)) return;
        store_->remove(backup); store_->remove(staging); if (!store_->remove(journal)) return;
    } else if (store_->stat(backup, entry)) return;
    store_->remove(staging); recovered_ = true;
}
bool FileService::room(const std::string& path) const {
    FileEntry existing; if (store_->stat(path, existing)) return true;
    std::vector<FileEntry> entries; return store_->list(parent(path), entries) && entries.size() < 128;
}
void FileService::cooperate() {
    platform_.cooperate(); auto now = platform_.milliseconds();
    if (now - last_yield_ >= 1000) { last_yield_ = now; platform_.write(encode({"HB", 0, "storage busy"})); }
}
bool FileService::checksum(const std::string& path, uint32_t size, uint32_t& result) {
    uint32_t state = 0xffffffff;
    for (uint32_t offset = 0; offset < size; offset += chunk_size) { std::string data; if (!store_->read(path, offset, std::min(chunk_size, size - offset), data)) return false; state = crc32_update(state, data); cooperate(); }
    result = state ^ 0xffffffff; return true;
}
void FileService::abort() {
    if (store_) store_->remove(staging);
    if (!copy_source_.empty()) { copy_state_ = "failed"; copy_error_ = "CANCELLED"; copy_source_.clear(); }
    token_.clear(); target_.clear();
}
void FileService::tick() {
    if (!token_.empty() && platform_.milliseconds() - touched_ > 30000) { abort(); platform_.write(encode({"EVT", 0, "storage.upload.expired"})); }
    if (copy_source_.empty()) return;
    auto fail = [&] { abort(); copy_state_ = "failed"; copy_error_ = "COPY_FAILED"; platform_.write(encode({"EVT", 0, "storage.copy.failed"})); };
    if (received_ < total_) {
        std::string data, verification;
        auto count = std::min(chunk_size, total_ - received_);
        if (!store_->read(copy_source_, received_, count, data) || data.size() != count || !store_->write(staging, received_, data, false)
            || !store_->read(staging, received_, count, verification) || data != verification) { fail(); return; }
        crc_ = crc32_update(crc_, data); received_ += count; copy_done_ = received_; touched_ = platform_.milliseconds(); cooperate();
    }
    if (received_ == total_) {
        copy_source_.clear(); wanted_crc_ = crc_ ^ 0xffffffff;
        auto result = rpc({"CMD", 0, ""}, {"commit", token_});
        if (result.type == "ERR") { copy_state_ = "failed"; copy_error_ = result.payload; }
        else { copy_state_ = "complete"; copy_version_ = version(); }
        platform_.write(encode({"EVT", 0, result.type == "ERR" ? "storage.copy.failed" : "storage.copy.completed"}));
    }
}
bool FileService::accepts(const std::string& cmd) { return cmd == "fs" || cmd == "ls" || cmd == "cat" || cmd == "mkdir" || cmd == "rm" || cmd == "mv" || cmd == "cp" || cmd == "touch" || cmd == "write" || cmd == "df"; }
Frame FileService::handle(const Frame& frame) {
    std::istringstream input(frame.payload); std::string cmd, arg; input >> cmd; std::vector<std::string> args;
    while (input >> std::quoted(arg)) args.push_back(arg);
    if (!input.eof()) return {"ERR", frame.id, "INVALID_ARGUMENTS"};
    if (cmd == "fs") return rpc(frame, args);
    auto error = [&](const char* text) { return Frame{"ERR", frame.id, text}; };
    if (cmd == "df" && args.empty()) return rpc(frame, {"info"});
    if (cmd == "ls" && args.size() <= 1) {
        auto path = args.empty() ? std::string("/") : args[0]; if (!valid_file_path(path)) return error("INVALID_PATH");
        if (!recovered_) return error("STORAGE_UNAVAILABLE");
        std::vector<FileEntry> entries; if (!store_->list(path, entries)) return error("NOT_A_DIRECTORY");
        std::string text; for (const auto& entry : entries) { auto line = (entry.directory ? "[dir] " : "[file] ") + entry.name + (entry.directory ? "" : "  " + std::to_string(entry.size) + " B") + '\n'; if (text.size() + line.size() > 3900) { text += "... use Files for the complete directory\n"; break; } text += line; }
        return {"RES", frame.id, text.empty() ? "Empty directory" : text};
    }
    if (cmd == "cat" && args.size() == 1) {
        if (!valid_file_path(args[0])) return error("INVALID_PATH");
        if (!recovered_) return error("STORAGE_UNAVAILABLE");
        FileEntry entry; if (!store_->stat(args[0], entry) || entry.directory) return error("NOT_A_FILE"); if (entry.size > 3072) return error("Use Files to open or download files larger than 3072 bytes");
        std::string text; if (!store_->read(args[0], 0, entry.size, text)) return error("READ_FAILED"); if (!utf8_valid(text) || text.find('\0') != std::string::npos) return error("Binary file: use download"); return {"RES", frame.id, text};
    }
    if ((cmd == "mkdir" || cmd == "rm") && args.size() == 1) return rpc(frame, {cmd == "rm" ? "remove" : "mkdir", base64_encode(args[0])});
    if ((cmd == "mv" || cmd == "cp") && args.size() == 2) return rpc(frame, {cmd == "mv" ? "rename" : "copy", base64_encode(args[0]), base64_encode(args[1])});
    if ((cmd == "touch" && args.size() == 1) || (cmd == "write" && args.size() == 2)) {
        if (cmd == "touch" && recovered_) { FileEntry entry; if (valid_file_path(args[0]) && store_->stat(args[0], entry)) return entry.directory ? error("NOT_A_FILE") : Frame{"RES", frame.id, "File already exists"}; }
        auto data = cmd == "touch" ? std::string() : args[1]; auto crc = crc32_update(0xffffffff, data) ^ 0xffffffff;
        auto start = rpc(frame, {"begin", base64_encode(args[0]), std::to_string(data.size()), std::to_string(crc), version()}); if (start.type == "ERR") return start;
        if (!data.empty()) { auto chunk = rpc(frame, {"chunk", token_, "0", base64_encode(data)}); if (chunk.type == "ERR") { abort(); return chunk; } }
        return rpc(frame, {"commit", token_});
    }
    return error("Usage: ls [path] | cat path | mkdir path | rm path | mv from to | cp from to | touch path | write path \"text\" | df");
}
Frame FileService::rpc(const Frame& f, const std::vector<std::string>& a) {
    auto fail = [&](const char* code) { return Frame{"ERR", f.id, code}; };
    auto ok = [&](const std::string& json) { return Frame{"RES", f.id, json}; };
    auto changed = [&] { ++revision_; platform_.write(encode({"EVT", 0, "storage.changed"})); return ok("{\"version\":" + json_string(version()) + "}"); };
    if (a.empty()) return fail("INVALID_ARGUMENTS");
    const auto& op = a[0];
    if (op == "info" && a.size() == 1) {
        auto info = store_ ? store_->info() : StorageInfo{false, 0, 0};
        return ok("{\"ready\":" + std::string(recovered_ && info.ready ? "true" : "false") + ",\"filesystem\":\"FATFS\",\"total\":" + std::to_string(info.total) + ",\"free\":" + std::to_string(info.free) + ",\"maxFile\":" + std::to_string(max_file) + ",\"chunkSize\":2048,\"maxEntries\":128,\"version\":" + json_string(version()) + "}");
    }
    if (!recovered_) return fail("STORAGE_UNAVAILABLE");
    if (!token_.empty() && platform_.milliseconds() - touched_ > 30000) abort();
    if (op == "status" && a.size() == 2) {
        if (copy_token_.empty() || a[1] != copy_token_) return fail("INVALID_TOKEN");
        return ok("{\"state\":" + json_string(copy_state_) + ",\"received\":" + std::to_string(copy_done_) + ",\"total\":" + std::to_string(copy_size_) + ",\"version\":" + json_string(copy_version_) + ",\"error\":" + json_string(copy_error_) + "}");
    }
    if (op == "abort" && a.size() == 2) { if (token_.empty() || a[1] != token_) return fail("INVALID_TOKEN"); abort(); return ok("{}"); }
    if (op == "chunk" && a.size() == 4) {
        if (!copy_source_.empty()) return fail("UPLOAD_BUSY");
        uint32_t offset; std::string bytes;
        if (token_.empty() || a[1] != token_) return fail("INVALID_TOKEN");
        if (!number(a[2], offset) || offset != received_) return fail("OFFSET_MISMATCH");
        if (!base64_decode(a[3], bytes) || bytes.empty() || bytes.size() > chunk_size || bytes.size() > total_ - received_) return fail("INVALID_CHUNK");
        if (!store_->write(staging, offset, bytes, false)) { abort(); return fail("WRITE_FAILED"); }
        crc_ = crc32_update(crc_, bytes); received_ += bytes.size(); touched_ = platform_.milliseconds(); return ok("{\"received\":" + std::to_string(received_) + "}");
    }
    if (op == "commit" && a.size() == 2) {
        if (!copy_source_.empty()) return fail("UPLOAD_BUSY");
        if (token_.empty() || a[1] != token_) return fail("INVALID_TOKEN");
        if (received_ != total_ || (crc_ ^ 0xffffffff) != wanted_crc_) { abort(); return fail("CHECKSUM_MISMATCH"); }
        if (expected_ != "*" && expected_ != version()) { abort(); return fail("CONFLICT"); }
        if (!store_->write(journal, 0, target_, true)) { abort(); return fail("WRITE_FAILED"); }
        FileEntry old; bool had_old = store_->stat(target_, old);
        if (had_old && !store_->rename(target_, backup)) { store_->remove(journal); abort(); return fail("RENAME_FAILED"); }
        if (!store_->rename(staging, target_)) {
            bool restored = !had_old || store_->rename(backup, target_); if (restored) store_->remove(journal); else recovered_ = false;
            abort(); return fail("COMMIT_FAILED");
        }
        token_.clear(); target_.clear();
        if (had_old && !store_->remove(backup)) { recovered_ = false; return fail("RECOVERY_REQUIRED"); }
        if (!store_->remove(journal)) { recovered_ = false; return fail("RECOVERY_REQUIRED"); }
        return changed();
    }
    std::string path;
    if (a.size() < 2 || !base64_decode(a[1], path) || !valid_file_path(path)) return fail("INVALID_PATH");
    FileEntry entry;
    if (op == "list" && a.size() == 4) {
        uint32_t cursor; if (!number(a[2], cursor)) return fail("INVALID_ARGUMENTS"); if (a[3] != "*" && a[3] != version()) return fail("CONFLICT");
        std::vector<FileEntry> entries; if (!store_->list(path, entries)) return fail("NOT_A_DIRECTORY");
        std::sort(entries.begin(), entries.end(), [](const FileEntry& x, const FileEntry& y) { return x.directory != y.directory ? x.directory : x.name < y.name; });
        if (cursor > entries.size()) return fail("INVALID_ARGUMENTS");
        std::string out = "{\"version\":" + json_string(version()) + ",\"entries\":["; auto end = std::min<size_t>(cursor + 8, entries.size());
        for (size_t i = cursor; i < end; ++i) { if (i != cursor) out += ','; const auto& e = entries[i]; out += "{\"name\":" + json_string(e.name) + ",\"directory\":" + (e.directory ? "true" : "false") + ",\"size\":" + std::to_string(e.size) + '}'; }
        return ok(out + "],\"next\":" + (end == entries.size() ? "null" : std::to_string(end)) + "}");
    }
    if ((op == "stat" || op == "hash") && a.size() == 2) {
        if (!store_->stat(path, entry)) return fail("NOT_FOUND");
        uint32_t crc = 0;
        if (!entry.directory && (entry.size > max_file || !checksum(path, entry.size, crc))) return fail("READ_FAILED");
        return ok("{\"size\":" + std::to_string(entry.size) + ",\"directory\":" + (entry.directory ? "true" : "false") + ",\"crc32\":" + std::to_string(crc) + ",\"version\":" + json_string(version()) + "}");
    }
    if (op == "read" && a.size() == 5) {
        uint32_t offset, count; if (!number(a[2], offset) || !number(a[3], count) || !count || count > chunk_size) return fail("INVALID_ARGUMENTS"); if (a[4] != version()) return fail("CONFLICT");
        std::string bytes; if (!store_->read(path, offset, count, bytes)) return fail("READ_FAILED");
        return ok("{\"offset\":" + std::to_string(offset) + ",\"data\":" + json_string(base64_encode(bytes)) + ",\"crc32\":" + std::to_string(crc32_update(0xffffffff, bytes) ^ 0xffffffff) + ",\"version\":" + json_string(version()) + "}");
    }
    if (!token_.empty()) return fail("UPLOAD_BUSY");
    if (op == "begin" && a.size() == 5) {
        uint32_t size, crc; if (path == "/" || !number(a[2], size) || !number(a[3], crc) || size > max_file) return fail("FILE_TOO_LARGE");
        if (a[4] != "*" && a[4] != version()) return fail("CONFLICT");
        if (store_->stat(path, entry) && entry.directory) return fail("NOT_A_FILE");
        if (!room(path)) return fail("DIRECTORY_FULL_OR_MISSING");
        if (store_->info().free < static_cast<uint64_t>(size) + 65536) return fail("NO_SPACE");
        if (!store_->write(staging, 0, "", true)) return fail("WRITE_FAILED");
        token_ = epoch_ + "u" + std::to_string(++token_counter_); target_ = path; expected_ = a[4]; total_ = size; wanted_crc_ = crc; received_ = 0; crc_ = 0xffffffff; touched_ = platform_.milliseconds(); return ok("{\"token\":" + json_string(token_) + "}");
    }
    if (path == "/") return fail("ROOT_PROTECTED");
    if (op == "mkdir" && a.size() == 2) { if (!room(path)) return fail("DIRECTORY_FULL_OR_MISSING"); if (!store_->mkdir(path)) return fail("CREATE_FAILED"); return changed(); }
    if (op == "remove" && a.size() == 2) { if (!store_->remove(path)) return fail("DELETE_FAILED_OR_NOT_EMPTY"); return changed(); }
    if ((op == "rename" || op == "copy") && a.size() == 3) {
        std::string to; if (!base64_decode(a[2], to) || !valid_file_path(to) || to == "/") return fail("INVALID_PATH");
        if (store_->stat(to, entry)) return fail("ALREADY_EXISTS");
        if (!room(to) && !(op == "rename" && parent(to) == parent(path))) return fail("DIRECTORY_FULL_OR_MISSING");
        if (op == "rename") {
            if (to.rfind(path + '/', 0) == 0 || !store_->stat(path, entry)) return fail("RENAME_FAILED");
            if (entry.directory) {
                std::vector<std::string> pending{path}; unsigned visited = 0;
                while (!pending.empty()) {
                    auto directory = std::move(pending.back()); pending.pop_back(); std::vector<FileEntry> children;
                    if (++visited > 4096 || !store_->list(directory, children)) return fail("READ_FAILED");
                    for (const auto& child : children) {
                        auto source = directory + '/' + child.name;
                        if (!valid_file_path(to + source.substr(path.size()))) return fail("INVALID_PATH");
                        if (child.directory) pending.push_back(std::move(source));
                    }
                    cooperate();
                }
            }
            if (!store_->rename(path, to)) return fail("RENAME_FAILED");
            return changed();
        }
        if (!store_->stat(path, entry) || entry.directory || entry.size > max_file) return fail("NOT_A_FILE");
        auto start = rpc(f, {"begin", base64_encode(to), std::to_string(entry.size), "0", version()}); if (start.type == "ERR") return start;
        copy_source_ = path; copy_token_ = token_; copy_size_ = entry.size; copy_done_ = 0; copy_state_ = "running"; copy_error_.clear(); copy_version_.clear();
        return start;
    }
    return fail("INVALID_ARGUMENTS");
}
}
