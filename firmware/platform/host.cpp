#include "shell/shell.hpp"
#include <chrono>
#include <iostream>
class Host final : public miku::Platform {
    std::chrono::steady_clock::time_point boot = std::chrono::steady_clock::now();
public:
    uint64_t milliseconds() const override { return std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::steady_clock::now()-boot).count(); }
    uint32_t free_heap() const override { return 220000; }
    std::string model() const override { return "Host mock"; }
    void write(const std::string& bytes) override { std::cout << bytes << std::flush; }
    void reboot() override { boot = std::chrono::steady_clock::now(); }
};
int main() { Host platform; miku::Kernel kernel(platform); miku::Shell shell(kernel); miku::Decoder decoder; std::string line; while (std::getline(std::cin, line)) { decoder.feed(line + '\n', [&](const miku::Frame& f) { shell.handle(f); }); shell.pump(); } }
