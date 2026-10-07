#pragma once
#include "platform/platform.hpp"
#include "storage/storage.hpp"
namespace miku {
class Esp32Platform final : public Platform {
    FileStore store_{"/data", false};
public:
    Esp32Platform();
    static void initialize();
    uint64_t milliseconds() const override;
    uint32_t free_heap() const override;
    uint32_t minimum_heap() const override;
    uint32_t largest_block() const override;
    uint32_t psram_size() const override;
    uint32_t free_psram() const override;
    uint32_t flash_size() const override;
    std::string reset_reason() const override;
    std::string model() const override;
    void write(const std::string& bytes) override;
    void reboot() override;
    uint32_t config_read(const char* key, uint32_t fallback) const override;
    bool config_write(const char* key, uint32_t value) override;
    FileStore* files() override { return &store_; }
    uint32_t storage_nonce() const override;
    void cooperate() override;
};
}
