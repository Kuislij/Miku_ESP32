#include "shell/shell.hpp"
#include <stdexcept>
#include <iostream>
struct Mock : miku::Platform {
    uint64_t now = 0; std::string output;
    uint64_t milliseconds() const override { return now; }
    uint32_t free_heap() const override { return 123456; }
    std::string model() const override { return "Mock"; }
    void write(const std::string& s) override { output += s; }
    void reboot() override { now = 0; }
};
void check(bool b) { if (!b) throw std::runtime_error("check failed"); }
int main() {
    Mock p; miku::Kernel k(p); miku::Shell shell(k); miku::Decoder decoder;
    auto wire = miku::encode({"CMD", 42, "ping"});
    for (char c : wire) decoder.feed(std::string(1, c), [&](const miku::Frame& f) { shell.handle(f); });
    check(p.output == miku::encode({"RES", 42, "pong"}));
    decoder.feed(std::string(9000, 'x') + "\ninvalid\n", [](const miku::Frame&){}); check(decoder.rejected == 2);
    check(k.set_service("demo", true)); check(!k.set_service("missing", true));
    miku::Message m; while (k.receive(m)) {};
    for (int i = 0; i < 32; ++i) check(k.post({"test", "data"})); check(!k.post({"overflow", ""}));
    for (int i = 0; i < 32; ++i) check(k.receive(m)); check(!k.receive(m));
    p.now = 1000; shell.pump(); check(p.output.find("1|STAT|0|") != std::string::npos);
    std::cout << "PASS kernel, IPC bounds, framing, shell and telemetry\n";
}
