#include "shell/shell.hpp"
#include <stdexcept>
#include <iostream>
struct Mock : miku::Platform {
    uint64_t now = 0; std::string output;
    uint32_t saved_period = 1000;
    uint64_t milliseconds() const override { return now; }
    uint32_t free_heap() const override { return 123456; }
    std::string model() const override { return "Mock"; }
    void write(const std::string& s) override { output += s; }
    void reboot() override { now = 0; }
    bool config_write(const char*, uint32_t value) override { saved_period = value; return true; }
    uint32_t config_read(const char*, uint32_t) const override { return saved_period; }
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
    check(k.dropped() == 1);
    check(k.set_telemetry_period(250)); check(p.saved_period == 250); check(!k.set_telemetry_period(1));
    miku::Kernel restored(p); check(restored.telemetry_period() == 250);
    int calls = 0; check(k.register_service("custom", 100, true, [&] { ++calls; return false; }));
    check(!k.register_service("custom", 100, true, [] { return true; }));
    p.now = 1100; k.tick(); check(calls == 1); check(k.services().back().state == miku::TaskState::Faulted);
    p.now = 1500; k.tick(); check(calls == 1); check(k.set_service("custom", true));
    p.now = 1600; k.tick(); check(calls == 2);
    for (int i = 0; i < 100; ++i) k.log(std::to_string(i));
    check(k.logs().size() < 3500); check(k.logs().find("1600s") == std::string::npos);
    unsigned invalid = 0;
    decoder.feed("1|CMD|0|cGluZw==\n1|CMD|1|cGluZx==\n1|CMD|1|/w==\n", [&](const miku::Frame&) { ++invalid; });
    check(invalid == 0); check(decoder.rejected == 5);
    const auto unicode = miku::encode({"CMD", 123, "\xd0\x9f\xd1\x80\xd0\xb8\xd0\xb2\xd0\xb5\xd1\x82 |\n"});
    for (size_t split = 0; split <= unicode.size(); ++split) {
        miku::Decoder fragmented; int frames = 0;
        auto accept = [&](const miku::Frame& f) { check(f.id == 123); ++frames; };
        fragmented.feed(unicode.substr(0, split), accept); fragmented.feed(unicode.substr(split), accept); check(frames == 1);
    }
    std::cout << "PASS kernel, IPC bounds, framing, shell and telemetry\n";
}
