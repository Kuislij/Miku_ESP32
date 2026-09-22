#pragma once
#include "platform/platform.hpp"
#include <array>
#include <deque>
namespace miku {
enum class TaskState { Stopped, Ready, Running, Waiting, Faulted };
struct Service { const char* name; TaskState state; uint64_t runs = 0; };
struct Message { std::string topic; std::string payload; };
class Kernel {
    Platform& platform_;
    std::array<Service, 3> services_{{{"telemetry", TaskState::Ready}, {"heartbeat", TaskState::Ready}, {"demo", TaskState::Stopped}}};
    std::deque<Message> messages_;
    uint64_t last_tick_ = 0;
public:
    explicit Kernel(Platform& platform) : platform_(platform) {}
    Platform& platform() { return platform_; }
    const auto& services() const { return services_; }
    bool set_service(const std::string& name, bool enabled);
    bool post(Message message);
    bool receive(Message& message);
    void tick();
    std::string stats() const;
    std::string tasks() const;
};
}
