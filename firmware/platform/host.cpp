#include "shell/shell.hpp"
#include <chrono>
#include <condition_variable>
#include <iostream>
#include <memory>
#include <mutex>
#include <thread>
class Host final : public miku::Platform {
    std::chrono::steady_clock::time_point boot = std::chrono::steady_clock::now();
    uint32_t telemetry_period = 1000;
public:
    bool restarting = false;
    uint64_t milliseconds() const override { return std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::steady_clock::now()-boot).count(); }
    uint32_t free_heap() const override { return 220000; }
    std::string model() const override { return "Host mock"; }
    void write(const std::string& bytes) override { std::cout << bytes << std::flush; }
    void reboot() override { boot = std::chrono::steady_clock::now(); restarting = true; }
    uint32_t config_read(const char*, uint32_t) const override { return telemetry_period; }
    bool config_write(const char*, uint32_t value) override { telemetry_period = value; return true; }
};
int main() {
    Host platform; auto kernel = std::make_unique<miku::Kernel>(platform); auto shell = std::make_unique<miku::Shell>(*kernel); miku::Decoder decoder;
    std::mutex gate; std::condition_variable available; std::deque<std::string> input; bool done = false;
    std::thread reader([&] {
        std::string chunk; char c;
        while (std::cin.get(c)) {
            chunk += c;
            if (c == '\n' || chunk.size() == 256) {
                std::unique_lock<std::mutex> lock(gate); available.wait(lock, [&] { return input.size() < 32; });
                input.push_back(std::move(chunk)); chunk.clear(); lock.unlock(); available.notify_one();
            }
        }
        std::lock_guard<std::mutex> lock(gate); if (!chunk.empty()) input.push_back(std::move(chunk)); done = true;
    });
    platform.write(miku::encode({"EVT", 0, "system.connected"}));
    for (;;) {
        std::string chunk;
        { std::lock_guard<std::mutex> lock(gate); if (input.empty() && done) break; if (!input.empty()) { chunk = std::move(input.front()); input.pop_front(); } }
        available.notify_one();
        if (!chunk.empty()) decoder.feed(chunk, [&](const miku::Frame& f) {
            shell->handle(f);
            if (platform.restarting) { platform.restarting = false; kernel = std::make_unique<miku::Kernel>(platform); shell = std::make_unique<miku::Shell>(*kernel); platform.write(miku::encode({"EVT", 0, "system.rebooted"})); }
        });
        shell->pump(); std::this_thread::sleep_for(std::chrono::milliseconds(10));
    }
    reader.join();
}
