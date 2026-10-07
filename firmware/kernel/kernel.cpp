#include "kernel/kernel.hpp"
#include <sstream>
#include <utility>
namespace miku {
const char* state_name(TaskState state) {
    switch (state) {
        case TaskState::Stopped: return "Stopped";
        case TaskState::Ready: return "Ready";
        case TaskState::Running: return "Running";
        case TaskState::Waiting: return "Waiting";
        case TaskState::Faulted: return "Faulted";
    }
    return "Unknown";
}
Kernel::Kernel(Platform& platform) : platform_(platform) {
    auto period = platform_.config_read("telemetry_ms", 1000);
    if (period < 200 || period > 10000) period = 1000;
    register_service("telemetry", period, true, [this] { post({"STAT", stats()}); return true; });
    register_service("heartbeat", 1000, true, [this] { post({"HB", "alive"}); return true; });
    register_service("demo", 5000, false, [this] { log("demo service tick"); return true; });
    log("MikuOS 0.2.0 boot complete");
}
bool Kernel::register_service(std::string name, uint32_t period, bool enabled, std::function<bool()> step) {
    if (ticking_ || services_.size() >= 16 || name.empty() || name.size() > 24 || !period || !step) return false;
    for (char c : name) if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_')) return false;
    for (const auto& s : services_) if (s.name == name) return false;
    services_.push_back({std::move(name), enabled ? TaskState::Ready : TaskState::Stopped, period, std::move(step), platform_.milliseconds() + period, 0});
    return true;
}
bool Kernel::set_service(const std::string& name, bool enabled) {
    for (auto& service : services_) if (name == service.name) {
        service.state = enabled ? TaskState::Ready : TaskState::Stopped;
        service.next_run = platform_.milliseconds() + service.period_ms;
        log(name + (enabled ? " started" : " stopped")); post({"services.changed", name}); return true;
    }
    return false;
}
bool Kernel::set_telemetry_period(uint32_t period) {
    if (period < 200 || period > 10000 || !platform_.config_write("telemetry_ms", period)) return false;
    for (auto& s : services_) if (s.name == "telemetry") { s.period_ms = period; s.next_run = platform_.milliseconds() + period; }
    log("telemetry interval set to " + std::to_string(period) + "ms"); return true;
}
uint32_t Kernel::telemetry_period() const { for (const auto& s : services_) if (s.name == "telemetry") return s.period_ms; return 1000; }
bool Kernel::post(Message message) {
    if (messages_.size() >= 32 || message.payload.size() > 4096) { ++dropped_; return false; }
    messages_.push_back(std::move(message)); return true;
}
bool Kernel::receive(Message& message) { if (messages_.empty()) return false; message = std::move(messages_.front()); messages_.pop_front(); return true; }
void Kernel::log(const std::string& message) {
    auto line = std::to_string(platform_.milliseconds()/1000) + "s " + message.substr(0, 120);
    if (logs_.size() == 24) logs_.pop_front();
    logs_.push_back(line); post({"LOG", line});
}
std::string Kernel::logs() const { std::string text; for (const auto& line : logs_) text += line + '\n'; return text; }
void Kernel::tick() {
    if (ticking_) return;
    ticking_ = true;
    const auto now = platform_.milliseconds();
    for (auto& s : services_) if (s.state != TaskState::Stopped && s.state != TaskState::Faulted && now >= s.next_run) {
        s.state = TaskState::Running; ++s.runs; s.next_run = now + s.period_ms;
        const bool ok = s.step(); s.state = ok ? TaskState::Waiting : TaskState::Faulted;
        if (!ok) { log(s.name + " faulted"); post({"services.changed", s.name}); }
    }
    ticking_ = false;
}
std::string Kernel::stats() const {
    std::ostringstream out; unsigned count = 0; for (const auto& s : services_) if (s.state != TaskState::Stopped && s.state != TaskState::Faulted) ++count;
    out << "{\"model\":\"" << platform_.model() << "\",\"firmware\":\"0.2.0\",\"uptime\":" << platform_.milliseconds()/1000
        << ",\"freeHeap\":" << platform_.free_heap() << ",\"minHeap\":" << platform_.minimum_heap()
        << ",\"largestBlock\":" << platform_.largest_block() << ",\"psramSize\":" << platform_.psram_size() << ",\"freePsram\":" << platform_.free_psram()
        << ",\"flashSize\":" << platform_.flash_size() << ",\"resetReason\":\"" << platform_.reset_reason()
        << "\",\"droppedMessages\":" << dropped_ << ",\"telemetryMs\":" << telemetry_period()
        << ",\"taskCount\":" << count << ",\"load\":null,\"services\":{";
    bool first = true; for (const auto& s : services_) { if (!first) out << ','; first = false; out << '"' << s.name << "\":" << (s.state != TaskState::Stopped && s.state != TaskState::Faulted ? "true" : "false"); }
    return out.str() + "}}";
}
std::string Kernel::tasks() const {
    std::ostringstream out; out << '['; unsigned id = 0;
    for (const auto& s : services_) { if (id) out << ','; out << "{\"id\":" << ++id << ",\"name\":\"" << s.name << "\",\"state\":\"" << state_name(s.state) << "\",\"runs\":" << s.runs << ",\"periodMs\":" << s.period_ms << '}'; }
    return out.str() + ']';
}
}
