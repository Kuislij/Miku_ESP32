#include "storage/file_service.hpp"
#include <chrono>
#include <filesystem>
#include <iostream>
#include <stdexcept>
namespace fs = std::filesystem;
static void check(bool condition, const char* name) { if (!condition) throw std::runtime_error(name); std::cout << "PASS " << name << '\n'; }
struct StoragePlatform : miku::Platform {
    miku::FileStore store;
    uint64_t now = 0;
    explicit StoragePlatform(const std::string& root) : store(root, true) {}
    uint64_t milliseconds() const override { return now; }
    uint32_t free_heap() const override { return 0; }
    std::string model() const override { return "Storage test"; }
    void write(const std::string&) override {}
    void reboot() override {}
    miku::FileStore* files() override { return &store; }
};
static std::string token(const miku::Frame& reply) { auto begin = reply.payload.find(":\""); auto end = reply.payload.find('"', begin + 2); if (reply.type != "RES" || begin == std::string::npos || end == std::string::npos) throw std::runtime_error(reply.payload); return reply.payload.substr(begin + 2, end - begin - 2); }
int main() {
    auto temp = fs::absolute(fs::temp_directory_path()).lexically_normal();
    if (temp.filename().empty()) temp = temp.parent_path();
    auto root = (temp / ("miku-storage-tests-" + std::to_string(std::chrono::steady_clock::now().time_since_epoch().count()))).lexically_normal();
    // The only recursive removal is this test-owned, verified direct child of temp.
    if (root.parent_path() != temp || root.filename().string().rfind("miku-storage-tests-", 0) != 0) return 1;
    fs::create_directory(root);
    try {
        StoragePlatform p(root.u8string()); auto& store = p.store;
        check((miku::crc32_update(0xffffffff, "123456789") ^ 0xffffffff) == 0xcbf43926 && (miku::crc32_update(0xffffffff, "") ^ 0xffffffff) == 0, "CRC32 reference vectors");
        { miku::FileService service(p); check(service.handle({"CMD", 1, "fs info"}).payload.find("\"ready\":true") != std::string::npos, "ready file service"); }
        check(store.write("/note.txt", 0, "old", true), "create original");
        check(store.write("/.miku/commit.txt", 0, "/note.txt", true) && store.rename("/note.txt", "/.miku/old.bin") && store.write("/.miku/upload.bin", 0, "new", true), "simulate interrupted replacement before target rename");
        { miku::FileService service(p); std::string data; check(store.read("/note.txt", 0, 3, data) && data == "old", "boot recovery restores original after interrupted rename"); }
        check(store.write("/.miku/commit.txt", 0, "/note.txt", true) && store.rename("/note.txt", "/.miku/old.bin") && store.write("/note.txt", 0, "new", true), "simulate interrupted cleanup after target rename");
        { miku::FileService service(p); std::string data; miku::FileEntry e; check(store.read("/note.txt", 0, 3, data) && data == "new" && !store.stat("/.miku/old.bin", e) && !store.stat("/.miku/commit.txt", e), "boot recovery retains committed file and reclaims backup"); }
        check(store.write("/.miku/commit.txt", 0, "/../escape", true) && store.write("/.miku/old.bin", 0, "precious", true), "simulate invalid recovery record");
        { miku::FileService service(p); std::string data; check(service.handle({"CMD", 1, "fs info"}).payload.find("\"ready\":false") != std::string::npos && store.read("/.miku/old.bin", 0, 8, data) && data == "precious", "invalid journal disables writes and preserves recovery files"); }
        store.remove("/.miku/commit.txt"); store.remove("/.miku/old.bin");
        { miku::FileService service(p); auto handle = token(service.handle({"CMD", 1, "fs begin " + miku::base64_encode("/note.txt") + " 4 3632233996 *"}));
            p.now = 30001; service.tick(); auto result = service.handle({"CMD", 2, "fs commit " + handle}); std::string data;
            check(result.type == "ERR" && result.payload == "INVALID_TOKEN" && store.read("/note.txt", 0, 3, data) && data == "new", "idle upload expires without changing original");
        }
        {
            miku::FileService service(p); check(store.write("/source.bin", 0, std::string(8193, 'x'), true), "create multi-step copy source");
            auto start = service.handle({"CMD", 1, "fs copy " + miku::base64_encode("/source.bin") + " " + miku::base64_encode("/copy.bin")}); auto job = token(start);
            service.tick(); miku::FileEntry temporary; check(store.stat("/.miku/upload.bin", temporary) && temporary.size == 2048, "copy advances one bounded block per scheduler step");
            check(service.handle({"CMD", 2, "fs abort " + job}).type == "RES" && service.handle({"CMD", 3, "fs status " + job}).payload.find("\"state\":\"failed\"") != std::string::npos && !store.stat("/copy.bin", temporary), "cancelled background copy leaves no target");
            job = token(service.handle({"CMD", 4, "fs copy " + miku::base64_encode("/source.bin") + " " + miku::base64_encode("/copy.bin")}));
            for (int i = 0; i < 5; ++i) service.tick(); std::string result;
            check(store.read("/copy.bin", 0, 8193, result) && result == std::string(8193, 'x') && service.handle({"CMD", 5, "fs status " + job}).payload.find("\"state\":\"complete\"") != std::string::npos, "background job commits verified complete contents");
        }
        fs::remove_all(root);
    } catch (...) { fs::remove_all(root); throw; }
}
