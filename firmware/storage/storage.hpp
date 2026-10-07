#pragma once
#include <string>
#include <vector>
#include <cstdint>
#include <functional>
namespace miku {
struct FileEntry { std::string name; bool directory; uint32_t size; };
struct StorageInfo { bool ready; uint64_t total, free; };
// Files are opened only for each bounded operation; no handle survives a command.
class FileStore {
    std::string root_;
    bool ready_;
public:
    FileStore(std::string root, bool ready) : root_(std::move(root)), ready_(ready) {}
    StorageInfo info() const;
    bool stat(const std::string& path, FileEntry& entry) const;
    bool list(const std::string& path, std::vector<FileEntry>& entries) const;
    bool read(const std::string& path, uint32_t offset, uint32_t count, std::string& data) const;
    bool write(const std::string& path, uint32_t offset, const std::string& data, bool truncate) const;
    bool mkdir(const std::string& path) const;
    bool remove(const std::string& path) const;
    bool rename(const std::string& from, const std::string& to) const;
};
uint32_t crc32_update(uint32_t state, const std::string& data);
std::string json_string(const std::string& value);
bool valid_file_path(const std::string& path);
}
