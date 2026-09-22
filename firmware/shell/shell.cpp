#include "shell/shell.hpp"
#include <sstream>
namespace miku {
void Shell::handle(const Frame& f) {
    auto send = [&](const std::string& type, uint32_t id, const std::string& text) { kernel_.platform().write(encode({type, id, text})); };
    std::istringstream input(f.payload); std::string cmd, arg, extra; input >> cmd >> arg >> extra;
    std::string result;
    if (cmd == "ping") result = "pong";
    else if (cmd == "help") result = "help ping uname version uptime mem info tasks services start stop clear video reboot neofetch miku";
    else if (cmd == "uname" || cmd == "version") result = "MikuOS 0.1.0 / miku-kernel / protocol 1";
    else if (cmd == "uptime") result = std::to_string(kernel_.platform().milliseconds()/1000) + " seconds";
    else if (cmd == "mem") result = std::to_string(kernel_.platform().free_heap()) + " bytes free";
    else if (cmd == "info") { send("STAT", 0, kernel_.stats()); result = "System snapshot emitted"; }
    else if (cmd == "tasks") { send("TASKS", 0, kernel_.tasks()); result = kernel_.tasks(); }
    else if (cmd == "services") result = kernel_.tasks();
    else if (cmd == "start" || cmd == "stop") { if (!extra.empty() || !kernel_.set_service(arg, cmd == "start")) { send("ERR", f.id, "Unknown service or invalid arguments"); return; } result = arg + " " + cmd; send("STAT", 0, kernel_.stats()); }
    else if (cmd == "clear") send("EVT", 0, "terminal.clear");
    else if (cmd == "video") send("EVT", 0, "media.ascii.open");
    else if (cmd == "miku" || cmd == "neofetch") result = " /\\ /\\  MikuOS\n | <> |  miku-kernel\n" + kernel_.stats();
    else if (cmd == "reboot") { send("RES", f.id, "Rebooting"); kernel_.platform().reboot(); return; }
    else { send("ERR", f.id, "Unknown command: " + cmd); return; }
    send("RES", f.id, result);
}
void Shell::pump() { kernel_.tick(); Message m; while (kernel_.receive(m)) { const bool event = m.topic == "services.changed"; kernel_.platform().write(encode({event ? "EVT" : m.topic, 0, event ? m.topic : m.payload})); } }
}
