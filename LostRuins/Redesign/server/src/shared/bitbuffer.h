// 비트 단위 직렬화. C# 쪽 Shared/Protocol.cs의 BitWriter/BitReader와 같은 순서(LSB 먼저)로 읽고 쓴다.
//
// 왜 바이트가 아니라 비트인가: hp는 0..1023이면 10비트로 충분하다. 16비트(2바이트)로 쓰면
// 엔티티마다 6비트씩 낭비되고, 엔티티 20개 × 30Hz면 초당 450바이트가 버려진다.
#pragma once
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <vector>

namespace lr {

class BitWriter {
 public:
  BitWriter() { buf_.reserve(1500); }
  void Reset() {  // 버퍼를 비우되 용량은 유지 → 틱마다 새로 할당하지 않음
    buf_.clear();
    bitPos_ = 0;
  }
  void WriteBits(uint32_t value, int bits) {
    for (int i = 0; i < bits; ++i) {
      if ((bitPos_ & 7U) == 0) buf_.push_back(0);
      if (((value >> i) & 1U) != 0) buf_.back() |= static_cast<uint8_t>(1U << (bitPos_ & 7U));
      ++bitPos_;
    }
  }
  void WriteU8(uint8_t v) { WriteBits(v, 8); }
  void WriteU16(uint16_t v) { WriteBits(v, 16); }
  void WriteU32(uint32_t v) { WriteBits(v, 32); }
  void WriteI8(int8_t v) { WriteBits(static_cast<uint8_t>(v), 8); }
  void WriteFloat(float f) {
    uint32_t u = 0;
    std::memcpy(&u, &f, 4);
    WriteU32(u);
  }
  // [min, max] 범위의 실수를 bits 비트 정수로 양자화 (예: 16비트로 ±256m → 0.78cm 단위)
  void WriteQuantized(float v, float min, float max, int bits) {
    const uint32_t steps = (1U << bits) - 1U;
    const float t = std::clamp((v - min) / (max - min), 0.0F, 1.0F);
    WriteBits(static_cast<uint32_t>(std::lround(t * static_cast<float>(steps))),
              bits);  // +0.5 후 버림은 경계값에서 틀림
  }
  const std::vector<uint8_t>& Data() const { return buf_; }
  size_t BitCount() const { return bitPos_; }

 private:
  std::vector<uint8_t> buf_;
  size_t bitPos_ = 0;
};

class BitReader {
 public:
  BitReader(const uint8_t* data, size_t size) : data_(data), size_(size) {}
  uint32_t ReadBits(int bits) {
    uint32_t v = 0;
    for (int i = 0; i < bits; ++i) {
      if ((bitPos_ >> 3U) >= size_) {  // 잘린 패킷: 실패로 표시하고 0을 돌려준다
        ok_ = false;
        return 0;
      }
      if (((data_[bitPos_ >> 3U] >> (bitPos_ & 7U)) & 1U) != 0) v |= (1U << i);
      ++bitPos_;
    }
    return v;
  }
  uint8_t ReadU8() { return static_cast<uint8_t>(ReadBits(8)); }
  uint16_t ReadU16() { return static_cast<uint16_t>(ReadBits(16)); }
  uint32_t ReadU32() { return ReadBits(32); }
  int8_t ReadI8() { return static_cast<int8_t>(ReadBits(8)); }
  float ReadFloat() {
    const uint32_t u = ReadU32();
    float f = 0;
    std::memcpy(&f, &u, 4);
    return f;
  }
  float ReadQuantized(float min, float max, int bits) {
    const uint32_t steps = (1U << bits) - 1U;
    return min + (max - min) * (static_cast<float>(ReadBits(bits)) / static_cast<float>(steps));
  }
  void Fail() { ok_ = false; }  // 값은 읽혔지만 의미상 잘못된 경우 (범위 밖 enum 등)
  bool Ok() const { return ok_; }

 private:
  const uint8_t* data_;
  size_t size_;
  size_t bitPos_ = 0;
  bool ok_ = true;
};

}  // namespace lr
