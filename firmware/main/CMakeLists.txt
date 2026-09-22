idf_component_register(SRCS "app_main.cpp" "../kernel/kernel.cpp" "../shell/shell.cpp" "../protocol/protocol.cpp" INCLUDE_DIRS ".." REQUIRES driver esp_timer esp_system esp_hw_support)
target_compile_features(${COMPONENT_LIB} PUBLIC cxx_std_17)
