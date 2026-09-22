#include "protocol/protocol.hpp"
#include <limits>
namespace miku {
static const std::string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
std::string encode(const Frame& f) {
    std::string b; unsigned val = 0; int bits = -6;
    for (unsigned char c : f.payload) { val = (val << 8) | c; bits += 8; while (bits >= 0) { b += alphabet[(val >> bits) & 63]; bits -= 6; } }
    if (bits > -6) b += alphabet[((val << 8) >> (bits + 8)) & 63];
    while (b.size() % 4) b += '=';
    return "1|" + f.type + '|' + std::to_string(f.id) + '|' + b + '\n';
}
static bool parse(const std::string& s, Frame& f) {
    if (s.rfind("1|CMD|", 0) != 0) return false;
    auto end = s.find('|', 6); if (end == std::string::npos || end == 6) return false;
    uint64_t id = 0; for (size_t i = 6; i < end; ++i) { if (s[i] < '0' || s[i] > '9') return false; id = id * 10 + s[i] - '0'; if (id > std::numeric_limits<uint32_t>::max()) return false; }
    const auto b = s.substr(end + 1); if (b.size() % 4) return false;
    std::string payload;
    for (size_t i = 0; i < b.size(); i += 4) {
        unsigned value = 0; int padding = 0;
        for (size_t j = 0; j < 4; ++j) {
            const char c = b[i+j]; if (c == '=') { if (j < 2 || i + 4 != b.size()) return false; ++padding; value <<= 6; }
            else { auto index = alphabet.find(c); if (padding || index == std::string::npos) return false; value = (value << 6) | static_cast<unsigned>(index); }
        }
        payload += static_cast<char>(value >> 16); if (padding < 2) payload += static_cast<char>(value >> 8); if (!padding) payload += static_cast<char>(value);
    }
    f = {"CMD", static_cast<uint32_t>(id), payload}; return true;
}
void Decoder::feed(const std::string& bytes, const std::function<void(const Frame&)>& callback) {
    for (unsigned char c : bytes) {
        if (c == '\n') { if (!dropping_) { if (!buffer_.empty() && buffer_.back() == '\r') buffer_.pop_back(); Frame f; if (parse(buffer_, f)) callback(f); else ++rejected; } buffer_.clear(); dropping_ = false; }
        else if (!dropping_) { if (c > 127 || buffer_.size() >= 8191) { buffer_.clear(); dropping_ = true; ++rejected; } else buffer_ += static_cast<char>(c); }
    }
}
}
