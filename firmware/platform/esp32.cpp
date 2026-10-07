#include "platform/esp32.hpp"
#include "driver/uart.h"
#include "esp_timer.h"
#include "esp_system.h"
#include "esp_heap_caps.h"
#include "esp_flash.h"
#include "nvs_flash.h"
#include "nvs.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "esp_vfs_fat.h"
#include "esp_random.h"
#include "esp_task_wdt.h"
namespace miku {
Esp32Platform::Esp32Platform() {
    esp_vfs_fat_mount_config_t config{};
    config.format_if_mount_failed = false; config.max_files = 4; config.allocation_unit_size = 4096;
    wl_handle_t handle = WL_INVALID_HANDLE;
    bool mounted = esp_vfs_fat_spiflash_mount_rw_wl("/data", "storage", &config, &handle) == ESP_OK;
    store_ = FileStore("/data", mounted);
}
uint32_t Esp32Platform::storage_nonce() const { return esp_random(); }
void Esp32Platform::cooperate() { esp_task_wdt_reset(); vTaskDelay(1); }
void Esp32Platform::initialize() {
    // Keep existing NVS intact. Incompatible/full storage is surfaced rather than erased.
    ESP_ERROR_CHECK(nvs_flash_init());
    uart_config_t config{}; config.baud_rate = 115200; config.data_bits = UART_DATA_8_BITS;
    config.parity = UART_PARITY_DISABLE; config.stop_bits = UART_STOP_BITS_1;
    config.flow_ctrl = UART_HW_FLOWCTRL_DISABLE; config.source_clk = UART_SCLK_DEFAULT;
    ESP_ERROR_CHECK(uart_param_config(UART_NUM_0, &config));
    ESP_ERROR_CHECK(uart_set_pin(UART_NUM_0, 43, 44, UART_PIN_NO_CHANGE, UART_PIN_NO_CHANGE));
    ESP_ERROR_CHECK(uart_driver_install(UART_NUM_0, 4096, 4096, 0, nullptr, 0));
}
uint64_t Esp32Platform::milliseconds() const { return esp_timer_get_time()/1000; }
uint32_t Esp32Platform::free_heap() const { return heap_caps_get_free_size(MALLOC_CAP_INTERNAL | MALLOC_CAP_8BIT); }
uint32_t Esp32Platform::minimum_heap() const { return heap_caps_get_minimum_free_size(MALLOC_CAP_INTERNAL | MALLOC_CAP_8BIT); }
uint32_t Esp32Platform::largest_block() const { return heap_caps_get_largest_free_block(MALLOC_CAP_INTERNAL | MALLOC_CAP_8BIT); }
uint32_t Esp32Platform::psram_size() const { return heap_caps_get_total_size(MALLOC_CAP_SPIRAM); }
uint32_t Esp32Platform::free_psram() const { return heap_caps_get_free_size(MALLOC_CAP_SPIRAM); }
uint32_t Esp32Platform::flash_size() const { uint32_t size = 0; esp_flash_get_size(nullptr, &size); return size; }
std::string Esp32Platform::model() const { return "ESP32-S3 N16R8"; }
std::string Esp32Platform::reset_reason() const {
    switch (esp_reset_reason()) {
        case ESP_RST_POWERON: return "power-on"; case ESP_RST_SW: return "software";
        case ESP_RST_TASK_WDT: return "task-watchdog"; case ESP_RST_INT_WDT: return "interrupt-watchdog";
        case ESP_RST_PANIC: return "panic"; case ESP_RST_BROWNOUT: return "brownout"; default: return "other";
    }
}
void Esp32Platform::write(const std::string& bytes) { uart_write_bytes(UART_NUM_0, bytes.data(), bytes.size()); }
void Esp32Platform::reboot() { uart_wait_tx_done(UART_NUM_0, pdMS_TO_TICKS(1000)); esp_restart(); }
uint32_t Esp32Platform::config_read(const char* key, uint32_t fallback) const {
    nvs_handle_t handle; if (nvs_open("miku", NVS_READONLY, &handle) != ESP_OK) return fallback;
    uint32_t value = fallback; nvs_get_u32(handle, key, &value); nvs_close(handle); return value;
}
bool Esp32Platform::config_write(const char* key, uint32_t value) {
    nvs_handle_t handle; if (nvs_open("miku", NVS_READWRITE, &handle) != ESP_OK) return false;
    auto result = nvs_set_u32(handle, key, value); if (result == ESP_OK) result = nvs_commit(handle); nvs_close(handle); return result == ESP_OK;
}
}
