#include "shell/shell.hpp"
#include "platform/esp32.hpp"
#include "driver/uart.h"
#include "esp_task_wdt.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
extern "C" void app_main() {
    miku::Esp32Platform::initialize();
    ESP_ERROR_CHECK(esp_task_wdt_add(nullptr));
    miku::Esp32Platform platform; miku::Kernel kernel(platform); miku::Shell shell(kernel); miku::Decoder decoder;
    platform.write(miku::encode({"EVT", 0, "system.connected"}));
    platform.write(miku::encode({"STAT", 0, kernel.stats()}));
    char buffer[256];
    unsigned previous_rejected = 0;
    for (;;) {
        const int count = uart_read_bytes(UART_NUM_0, buffer, sizeof(buffer), pdMS_TO_TICKS(20));
        if (count > 0) decoder.feed(std::string(buffer, count), [&](const miku::Frame& f) { shell.handle(f); });
        if (decoder.rejected != previous_rejected) { kernel.log("Rejected protocol frames: " + std::to_string(decoder.rejected)); previous_rejected = decoder.rejected; }
        shell.pump(); ESP_ERROR_CHECK(esp_task_wdt_reset());
    }
}
