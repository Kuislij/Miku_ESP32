#include "shell/shell.hpp"
#include "driver/uart.h"
#include "esp_timer.h"
#include "esp_system.h"
#include "esp_task_wdt.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
class Esp32Platform final : public miku::Platform {
public:
    uint64_t milliseconds() const override { return esp_timer_get_time() / 1000; }
    uint32_t free_heap() const override { return esp_get_free_heap_size(); }
    std::string model() const override { return CONFIG_IDF_TARGET; }
    void write(const std::string& bytes) override { uart_write_bytes(UART_NUM_0, bytes.data(), bytes.size()); }
    void reboot() override { uart_wait_tx_done(UART_NUM_0, pdMS_TO_TICKS(500)); esp_restart(); }
};
extern "C" void app_main() {
    uart_config_t config{}; config.baud_rate = 115200; config.data_bits = UART_DATA_8_BITS; config.parity = UART_PARITY_DISABLE; config.stop_bits = UART_STOP_BITS_1; config.flow_ctrl = UART_HW_FLOWCTRL_DISABLE; config.source_clk = UART_SCLK_DEFAULT;
    ESP_ERROR_CHECK(uart_param_config(UART_NUM_0, &config));
    ESP_ERROR_CHECK(uart_driver_install(UART_NUM_0, 4096, 0, 0, nullptr, 0));
    ESP_ERROR_CHECK(esp_task_wdt_add(nullptr));
    Esp32Platform platform; miku::Kernel kernel(platform); miku::Shell shell(kernel); miku::Decoder decoder;
    platform.write(miku::encode({"EVT", 0, "system.connected"}));
    char buffer[256];
    for (;;) {
        const int count = uart_read_bytes(UART_NUM_0, buffer, sizeof(buffer), pdMS_TO_TICKS(20));
        if (count > 0) decoder.feed(std::string(buffer, count), [&](const miku::Frame& f) { shell.handle(f); });
        shell.pump(); ESP_ERROR_CHECK(esp_task_wdt_reset());
    }
}
