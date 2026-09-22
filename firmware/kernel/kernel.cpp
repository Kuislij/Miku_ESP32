#include "kernel/kernel.hpp"
#include <sstream>
#include <utility>
namespace miku {
bool Kernel::set_service(const std::string& name, bool enabled) {
    for (auto& service : services_) if (name == service.name) { service.state = enabled ? TaskState::Ready : TaskState::Stopped; post({"services.changed", name}); return true; }
    return false;
}
bool Kernel::post(Message message) { if (messages_.size() >= 32) return false; messages_.push_back(std::move(message)); return true; }
bool Kernel::receive(Message& message) { if (messages_.empty()) return false; message = std::move(messages_.front()); messages_.pop_front(); return true; }
void Kernel::tick() {
    const auto now = platform_.milliseconds(); if (now - last_tick_ < 1000) return; last_tick_ = now;
    for (auto& s : services_) if (s.state != TaskState::Stopped && s.state != TaskState::Faulted) {
        s.state = TaskState::Running; ++s.runs;
        if (std::string(s.name) == "telemetry") post({"STAT", stats()});
        else if (std::string(s.name) == "heartbeat") post({"HB", "alive"});
        else if (s.runs % 5 == 0) post({"LOG", "demo service tick"});
        s.state = TaskState::Waiting;
    }
}
std::string Kernel::stats() const {
    std::ostringstream out; unsigned count = 0; for (auto& s : services_) if (s.state != TaskState::Stopped) ++count;
    out << "{\"model\":\"" << platform_.model() << "\",\"firmware\":\"0.1.0\",\"uptime\":" << platform_.milliseconds()/1000
        << ",\"freeHeap\":" << platform_.free_heap() << ",\"taskCount\":" << count << ",\"load\":null,\"services\":{";
    bool first = true; for (auto& s : services_) { if (!first) out << ','; first = false; out << '"' << s.name << "\":" << (s.state != TaskState::Stopped ? "true" : "false"); }
    return out.str() + "}}";
}
std::string Kernel::tasks() const {
    std::ostringstream out; out << '['; unsigned id = 0;
    for (auto& s : services_) { if (id) out << ','; out << "{\"id\":" << ++id << ",\"name\":\"" << s.name << "\",\"state\":\"" << (s.state == TaskState::Stopped ? "Stopped" : "Running") << "\"}"; }
    return out.str() + ']';
}
}
