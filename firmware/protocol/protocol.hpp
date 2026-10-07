#pragma once
#include <functional>
#include <string>
#include <cstdint>
namespace miku {
struct Frame { std::string type; uint32_t id; std::string payload; };
std::string encode(const Frame& frame);
std::string base64_encode(const std::string& bytes);
bool base64_decode(const std::string& encoded, std::string& bytes);
bool utf8_valid(const std::string& text);
class Decoder {
    std::string buffer_; bool dropping_ = false;
public:
    unsigned rejected = 0;
    void feed(const std::string& bytes, const std::function<void(const Frame&)>& callback);
};
}
