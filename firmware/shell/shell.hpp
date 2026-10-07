#pragma once
#include "kernel/kernel.hpp"
#include "protocol/protocol.hpp"
#include "storage/file_service.hpp"
namespace miku {
class Shell {
    Kernel& kernel_;
    FileService files_;
public:
    explicit Shell(Kernel& kernel) : kernel_(kernel), files_(kernel.platform()) {}
    void handle(const Frame& frame);
    void pump();
};
}
