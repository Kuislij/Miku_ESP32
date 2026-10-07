#pragma once
#include "storage/storage.hpp"
#include "platform/platform.hpp"
#include "protocol/protocol.hpp"
namespace miku {
class FileService {
    Platform& platform_;
    FileStore* store_;
    bool recovered_ = false;
    uint32_t revision_ = 0, token_counter_ = 0;
    std::string epoch_, token_, target_, expected_;
    uint32_t total_ = 0, received_ = 0, crc_ = 0xffffffff, wanted_crc_ = 0;
    uint64_t touched_ = 0, last_yield_ = 0;
    std::string copy_source_, copy_token_, copy_state_, copy_error_, copy_version_;
    uint32_t copy_size_ = 0, copy_done_ = 0;
    bool room(const std::string& path) const;
    bool checksum(const std::string& path, uint32_t size, uint32_t& result);
    void cooperate();
    void abort();
    std::string version() const;
    Frame rpc(const Frame& frame, const std::vector<std::string>& args);
public:
    explicit FileService(Platform& platform);
    static bool accepts(const std::string& command);
    Frame handle(const Frame& frame);
    void tick();
};
}
