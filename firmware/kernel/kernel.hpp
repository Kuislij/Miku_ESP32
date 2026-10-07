#pragma once
#include "platform/platform.hpp"
#include <deque>
#include <functional>
#include <vector>
namespace miku {
enum class TaskState { Stopped, Ready, Running, Waiting, Faulted };
const char* state_name(TaskState state);
struct Service {
    std::string name;
    TaskState state;
    uint32_t period_ms;
    std::function<bool()> step;
    uint64_t next_run = 0, runs = 0;
};
struct Message { std::string topic; std::string payload; };
class Kernel {
    Platform& platform_;
    std::vector<Service> services_;
    std::deque<Message> messages_;
    std::deque<std::string> logs_;
    uint32_t dropped_ = 0;
    bool ticking_ = false;
public:
    explicit Kernel(Platform& platform);
    Platform& platform() { return platform_; }
    const auto& services() const { return services_; }
    bool register_service(std::string name, uint32_t period, bool enabled, std::function<bool()> step);
    bool set_service(const std::string& name, bool enabled);
    bool set_telemetry_period(uint32_t period);
    uint32_t telemetry_period() const;
    bool post(Message message);
    bool receive(Message& message);
    void log(const std::string& message);
    std::string logs() const;
    void tick();
    uint32_t dropped() const { return dropped_; }
    std::string stats() const;
    std::string tasks() const;
};
}
