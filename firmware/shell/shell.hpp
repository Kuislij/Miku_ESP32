#pragma once
#include "kernel/kernel.hpp"
#include "protocol/protocol.hpp"
namespace miku {
class Shell {
    Kernel& kernel_;
public:
    explicit Shell(Kernel& kernel) : kernel_(kernel) {}
    void handle(const Frame& frame);
    void pump();
};
}
