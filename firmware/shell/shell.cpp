#include "shell/shell.hpp"
#include <sstream>
namespace miku {
void Shell::handle(const Frame& f) {
    auto send = [&](const std::string& type, uint32_t id, const std::string& text) { kernel_.platform().write(encode({type, id, text})); };
    if (f.payload.find('\0') != std::string::npos) { send("ERR", f.id, "Command contains a null byte"); return; }
    std::istringstream input(f.payload); std::string cmd; input >> cmd; std::vector<std::string> args; std::string word; while (input >> word) args.push_back(word);
    auto error = [&](const std::string& message) { send("ERR", f.id, message); };
    if (cmd.empty()) { error("Empty command"); return; }
    if (FileService::accepts(cmd)) { auto reply = files_.handle(f); send(reply.type, reply.id, reply.payload); return; }
    if (cmd != "start" && cmd != "stop" && cmd != "config" && !args.empty()) { error("Command takes no arguments"); return; }
    std::string result;
    if (cmd == "ping") result = "pong";
    else if (cmd == "help") result = "help ping uname version uptime mem info tasks services start <service> stop <service> logs config [telemetry_ms 200..10000] clear video reboot neofetch miku\nFiles: ls [path], cat path, mkdir path, rm path, mv from to, cp from to, touch path, write path \"text\", df\nQuote paths with spaces. Large and binary files: Files in Control Center.";
    else if (cmd == "uname" || cmd == "version") result = "MikuOS 0.3.0 / miku-kernel / protocol 1 / " + kernel_.platform().model();
    else if (cmd == "uptime") result = std::to_string(kernel_.platform().milliseconds()/1000) + " seconds";
    else if (cmd == "mem") result = "Internal free: " + std::to_string(kernel_.platform().free_heap()) + " bytes\nMinimum: " + std::to_string(kernel_.platform().minimum_heap()) + " bytes\nPSRAM free/total: " + std::to_string(kernel_.platform().free_psram()) + "/" + std::to_string(kernel_.platform().psram_size());
    else if (cmd == "info") { send("STAT", 0, kernel_.stats()); result = "System snapshot emitted"; }
    else if (cmd == "tasks") { send("TASKS", 0, kernel_.tasks()); for (const auto& s : kernel_.services()) result += s.name + "  " + state_name(s.state) + "  runs=" + std::to_string(s.runs) + '\n'; }
    else if (cmd == "services") { for (const auto& s : kernel_.services()) result += s.name + "  " + state_name(s.state) + '\n'; }
    else if (cmd == "start" || cmd == "stop") {
        if (args.size() != 1 || !kernel_.set_service(args[0], cmd == "start")) { error("Usage: start|stop <known service>"); return; }
        result = args[0] + (cmd == "start" ? " started" : " stopped"); send("STAT", 0, kernel_.stats());
    }
    else if (cmd == "logs") result = kernel_.logs();
    else if (cmd == "config") {
        if (args.empty()) result = "telemetry_ms=" + std::to_string(kernel_.telemetry_period());
        else if (args.size() == 2 && args[0] == "telemetry_ms") {
            uint32_t period = 0; for (char c : args[1]) { if (c < '0' || c > '9' || period > 10000) { error("Invalid interval"); return; } period = period * 10 + c - '0'; }
            if (!kernel_.set_telemetry_period(period)) { error("Interval must be 200..10000ms; storage must be available"); return; }
            result = "Saved telemetry_ms=" + std::to_string(period);
        } else { error("Usage: config [telemetry_ms 200..10000]"); return; }
    }
    else if (cmd == "clear") send("EVT", 0, "terminal.clear");
    else if (cmd == "video") send("EVT", 0, "media.ascii.open");
    else if (cmd == "miku" || cmd == "neofetch") result = " /\\ /\\  MikuOS\n | <> |  miku-kernel 0.3.0\n Device: " + kernel_.platform().model() + "\n Uptime: " + std::to_string(kernel_.platform().milliseconds()/1000) + "s\n Internal heap: " + std::to_string(kernel_.platform().free_heap()) + " bytes\n Link: Serial / protocol 1";
    else if (cmd == "reboot") { send("RES", f.id, "Rebooting"); kernel_.platform().reboot(); return; }
    else { error("Unknown command: " + cmd); return; }
    send("RES", f.id, result);
}
void Shell::pump() { files_.tick(); kernel_.tick(); Message m; while (kernel_.receive(m)) { const bool event = m.topic == "services.changed"; kernel_.platform().write(encode({event ? "EVT" : m.topic, 0, event ? m.topic : m.payload})); } }
}
