#pragma once
#include <cstdint>
#include <string>
namespace miku {
struct Platform {
    virtual ~Platform() = default;
    virtual uint64_t milliseconds() const = 0;
    virtual uint32_t free_heap() const = 0;
    virtual std::string model() const = 0;
    virtual void write(const std::string& bytes) = 0;
    virtual void reboot() = 0;
};
}
