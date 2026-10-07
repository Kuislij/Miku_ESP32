idf_component_register(SRCS "app_main.cpp" "../platform/esp32.cpp" "../kernel/kernel.cpp" "../shell/shell.cpp" "../protocol/protocol.cpp" "../storage/storage.cpp" "../storage/file_service.cpp" INCLUDE_DIRS ".." REQUIRES esp_driver_uart esp_timer esp_system esp_hw_support esp_psram spi_flash nvs_flash fatfs wear_levelling)
target_compile_features(${COMPONENT_LIB} PUBLIC cxx_std_17)
# Kept outside normal flash arguments: firmware upgrades must preserve user files.
fatfs_create_spiflash_image(storage ../fs-image)
