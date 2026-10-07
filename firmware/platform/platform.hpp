#pragma once
#include <cstdint>
#include <string>
namespace miku {
class FileStore;
struct Platform {
    virtual ~Platform() = default;
    virtual uint64_t milliseconds() const = 0;
    virtual uint32_t free_heap() const = 0;
    virtual uint32_t minimum_heap() const { return free_heap(); }
    virtual uint32_t largest_block() const { return free_heap(); }
    virtual uint32_t psram_size() const { return 0; }
    virtual uint32_t free_psram() const { return 0; }
    virtual uint32_t flash_size() const { return 0; }
    virtual std::string reset_reason() const { return "host"; }
    virtual uint32_t config_read(const char*, uint32_t fallback) const { return fallback; }
    virtual bool config_write(const char*, uint32_t) { return false; }
    virtual std::string model() const = 0;
    virtual void write(const std::string& bytes) = 0;
    virtual void reboot() = 0;
    virtual FileStore* files() { return nullptr; }
    virtual uint32_t storage_nonce() const { return 1; }
    virtual void cooperate() {}
};
}
