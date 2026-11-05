// 패킷 형식. C# 쪽 Shared/Protocol.cs와 필드 순서·비트 수가 같아야 한다.
//
// 모든 패킷 = [헤더 72비트] + [본문]
//   헤더: type 8 | seq 16 | ack 16 | ackBits 32   (상대에게서 받은 최근 33개 패킷의 수신 여부)
//
// 클라 → 서버: Connect, Input (최근 입력 최대 4개를 중복 전송 → 손실 내성)
// 서버 → 클라: Accept, Snapshot (상태 + 아직 ACK 안 된 이벤트)
//
// 받은 패킷은 전부 의심한다: 길이가 모자라거나, enum이 범위 밖이거나, 개수가 상한을 넘으면 Ok()=false.
#pragma once
#include <cstdint>
#include <vector>

#include "bitbuffer.h"
#include "movement.h"

namespace lr {

// UDP 한 패킷의 안전 크기. 이더넷 MTU 1500에서 IP·UDP 헤더와 VPN·터널 여유분을 빼면
// 1200바이트 안쪽이 "어느 경로에서도 쪼개지지 않는" 관례적인 상한이다.
constexpr size_t kMaxPacketBytes = 1200;
constexpr int kMaxInputsPerPacket = 4;
constexpr int kMaxEventsPerPacket = 32;

enum class PacketType : uint8_t { kConnect = 1, kAccept = 2, kInput = 3, kSnapshot = 4 };

struct Header {
  PacketType type = PacketType::kInput;
  uint16_t seq = 0;
  uint16_t ack = 0;
  uint32_t ackBits = 0;
};

enum class EntityKind : uint8_t { kPlayer = 0, kMonster = 1, kProjectile = 2 };

struct EntityState {
  uint16_t id = 0;
  EntityKind kind = EntityKind::kPlayer;
  float x = 0, y = 0, z = 0;
  uint8_t yaw = 0;     // 256단계 (1.4°)
  uint8_t action = 0;  // 0 Idle 1 Move 2 Jump 3 Dodge 4 Attack 5 Dead (3비트)
  uint16_t hp = 0;     // 0..1023
};

enum class EventType : uint8_t { kHit = 1, kDeath = 2 };

// 신뢰 전송 이벤트. 판정 결과는 서버만 만든다 (1편 F3).
struct GameEvent {
  uint16_t id = 0;
  EventType type = EventType::kHit;
  uint16_t source = 0, target = 0;
  uint16_t damage = 0;
  uint8_t crit = 0;
  float x = 0, y = 0, z = 0;
};

struct Snapshot {
  uint16_t serverTick = 0;
  uint16_t lastInputSeq = 0;  // 이 입력까지 반영한 결과 → 클라 재조정 기준점
  MoveState local;            // 내 캐릭터는 float 원본 그대로 (재조정은 비트 단위 비교)
  uint16_t localHp = 0;
  std::vector<EntityState> entities;
  std::vector<GameEvent> events;
};

constexpr float kWorldMin = -256.0F;
constexpr float kWorldMax = 256.0F;
constexpr float kHeightMax = 64.0F;

// 비트 크기 (스냅샷 예산 계산에 사용)
constexpr size_t kHeaderBits = 72;
constexpr size_t kMoveStateBits = 6 * 32 + 1 + 2 + 2 + 4 + 5 + 8 + 8 + 8;     // 230
constexpr size_t kSnapshotFixedBits = 16 + 16 + kMoveStateBits + 10 + 8 + 6;  // tick, seq, 내 상태, hp, 개수 2개
constexpr size_t kEntityBits = 16 + 2 + 16 + 16 + 12 + 8 + 3 + 10;            // 83
constexpr size_t kEventBits = 16 + 2 + 16 + 16 + 12 + 1 + 16 + 16 + 12;       // 107

inline void WriteHeader(BitWriter& w, const Header& h) {
  w.WriteU8(static_cast<uint8_t>(h.type));
  w.WriteU16(h.seq);
  w.WriteU16(h.ack);
  w.WriteU32(h.ackBits);
}
inline Header ReadHeader(BitReader& r) {
  Header h;
  const uint8_t t = r.ReadU8();
  if (t < 1 || t > 4) r.Fail();  // 범위 밖 값을 enum으로 바꾸지 않는다
  h.type = static_cast<PacketType>(t < 1 || t > 4 ? 1 : t);
  h.seq = r.ReadU16();
  h.ack = r.ReadU16();
  h.ackBits = r.ReadU32();
  return h;
}

inline void WriteInputs(BitWriter& w, const std::vector<InputCmd>& inputs) {
  w.WriteBits(static_cast<uint32_t>(inputs.size()), 3);
  for (const auto& in : inputs) {
    w.WriteU16(in.seq);
    w.WriteI8(in.moveX);
    w.WriteI8(in.moveZ);
    w.WriteU16(in.yaw);
    w.WriteU8(in.buttons);
  }
}
inline void ReadInputs(BitReader& r, std::vector<InputCmd>& out) {
  const uint32_t n = r.ReadBits(3);
  out.clear();
  if (n > static_cast<uint32_t>(kMaxInputsPerPacket)) {
    r.Fail();
    return;
  }
  for (uint32_t i = 0; i < n; ++i) {  // resize(n) 대신 push_back: GCC 13의 -Wstringop-overflow 오탐 회피
    InputCmd in;
    in.seq = r.ReadU16();
    in.moveX = r.ReadI8();
    in.moveZ = r.ReadI8();
    in.yaw = r.ReadU16();
    in.buttons = r.ReadU8();
    Sanitize(in);
    out.push_back(in);
  }
}

inline void WriteMoveState(BitWriter& w, const MoveState& s) {  // kMoveStateBits = 230비트
  w.WriteFloat(s.px);
  w.WriteFloat(s.py);
  w.WriteFloat(s.pz);
  w.WriteFloat(s.vx);
  w.WriteFloat(s.vy);
  w.WriteFloat(s.vz);
  w.WriteBits(s.grounded, 1);
  w.WriteBits(s.coyote, 2);
  w.WriteBits(s.jumpBuf, 2);
  w.WriteBits(s.dodgeT, 4);
  w.WriteBits(s.dodgeCd, 5);
  w.WriteU8(s.prevButtons);
  w.WriteI8(s.dodgeX);
  w.WriteI8(s.dodgeZ);
}
inline MoveState ReadMoveState(BitReader& r) {
  MoveState s;
  s.px = r.ReadFloat();
  s.py = r.ReadFloat();
  s.pz = r.ReadFloat();
  s.vx = r.ReadFloat();
  s.vy = r.ReadFloat();
  s.vz = r.ReadFloat();
  s.grounded = static_cast<uint8_t>(r.ReadBits(1));
  s.coyote = static_cast<uint8_t>(r.ReadBits(2));
  s.jumpBuf = static_cast<uint8_t>(r.ReadBits(2));
  s.dodgeT = static_cast<uint8_t>(r.ReadBits(4));
  s.dodgeCd = static_cast<uint8_t>(r.ReadBits(5));
  s.prevButtons = r.ReadU8();
  s.dodgeX = r.ReadI8();
  s.dodgeZ = r.ReadI8();
  return s;
}

inline void WriteSnapshot(BitWriter& w, const Snapshot& s) {
  w.WriteU16(s.serverTick);
  w.WriteU16(s.lastInputSeq);
  WriteMoveState(w, s.local);
  w.WriteBits(s.localHp, 10);
  w.WriteU8(static_cast<uint8_t>(s.entities.size()));  // 호출하는 쪽이 255개 이하를 보장
  for (const auto& e : s.entities) {                   // 엔티티 1개 = 83비트 ≈ 10.4바이트
    w.WriteU16(e.id);
    w.WriteBits(static_cast<uint8_t>(e.kind), 2);
    w.WriteQuantized(e.x, kWorldMin, kWorldMax, 16);  // 0.78cm 단위
    w.WriteQuantized(e.z, kWorldMin, kWorldMax, 16);
    w.WriteQuantized(e.y, 0.0F, kHeightMax, 12);  // 1.6cm 단위
    w.WriteU8(e.yaw);
    w.WriteBits(e.action, 3);
    w.WriteBits(e.hp, 10);
  }
  w.WriteBits(static_cast<uint32_t>(s.events.size()), 6);
  for (const auto& ev : s.events) {
    w.WriteU16(ev.id);
    w.WriteBits(static_cast<uint8_t>(ev.type), 2);
    w.WriteU16(ev.source);
    w.WriteU16(ev.target);
    w.WriteBits(ev.damage, 12);
    w.WriteBits(ev.crit, 1);
    w.WriteQuantized(ev.x, kWorldMin, kWorldMax, 16);
    w.WriteQuantized(ev.z, kWorldMin, kWorldMax, 16);
    w.WriteQuantized(ev.y, 0.0F, kHeightMax, 12);
  }
}

// out을 재사용한다 (매 패킷 새 벡터를 만들지 않음)
inline void ReadSnapshot(BitReader& r, Snapshot& s) {
  s.serverTick = r.ReadU16();
  s.lastInputSeq = r.ReadU16();
  s.local = ReadMoveState(r);
  s.localHp = static_cast<uint16_t>(r.ReadBits(10));
  s.entities.resize(r.ReadU8());
  for (auto& e : s.entities) {
    e.id = r.ReadU16();
    const uint32_t kind = r.ReadBits(2);
    if (kind > 2) r.Fail();
    e.kind = static_cast<EntityKind>(kind > 2 ? 0 : kind);
    e.x = r.ReadQuantized(kWorldMin, kWorldMax, 16);
    e.z = r.ReadQuantized(kWorldMin, kWorldMax, 16);
    e.y = r.ReadQuantized(0.0F, kHeightMax, 12);
    e.yaw = r.ReadU8();
    e.action = static_cast<uint8_t>(r.ReadBits(3));
    e.hp = static_cast<uint16_t>(r.ReadBits(10));
  }
  const uint32_t nev = r.ReadBits(6);
  if (nev > static_cast<uint32_t>(kMaxEventsPerPacket)) {
    r.Fail();
    s.events.clear();
    return;
  }
  s.events.resize(nev);
  for (auto& ev : s.events) {
    ev.id = r.ReadU16();
    const uint32_t type = r.ReadBits(2);
    if (type < 1 || type > 2) r.Fail();
    ev.type = static_cast<EventType>(type < 1 || type > 2 ? 1 : type);
    ev.source = r.ReadU16();
    ev.target = r.ReadU16();
    ev.damage = static_cast<uint16_t>(r.ReadBits(12));
    ev.crit = static_cast<uint8_t>(r.ReadBits(1));
    ev.x = r.ReadQuantized(kWorldMin, kWorldMax, 16);
    ev.z = r.ReadQuantized(kWorldMin, kWorldMax, 16);
    ev.y = r.ReadQuantized(0.0F, kHeightMax, 12);
  }
}

// 시퀀스 번호 비교 (16비트 순환: 65535 다음은 0)
inline bool SeqNewer(uint16_t a, uint16_t b) {
  return ((a > b) && (a - b <= 32768)) || ((a < b) && (b - a > 32768));
}

}  // namespace lr
