// C++ ↔ C# 일치 검사용 데이터 생성/검증.
//   parity gen <inputs.txt> <states.txt>   : 무작위 입력 3000틱과 그 결과 상태(비트 그대로)를 기록
//   parity snap <snapshot.bin>             : 알려진 값으로 스냅샷 패킷을 기록 (C#이 읽어서 검증)
//   parity input <input.bin>               : C#이 쓴 입력 패킷을 읽어서 검증
#include <cstdio>
#include <cstring>
#include <fstream>
#include <string>

#include "../src/game/world.h"

using namespace lr;

static uint32_t Bits(float f) {
  uint32_t u;
  std::memcpy(&u, &f, 4);
  return u;
}

int main(int argc, char** argv) {
  if (argc < 3) return 2;
  const std::string mode = argv[1];
  if (mode == "gen" && argc >= 4) {
    std::ofstream fin(argv[2]), fst(argv[3]);
    Rng rng(42);
    Level lv = DefaultLevel();
    MoveState s;
    uint8_t held = 0;
    for (int t = 1; t <= 3000; ++t) {
      InputCmd in;
      in.seq = static_cast<uint16_t>(t);
      in.moveX = static_cast<int8_t>(rng.Range(-127, 127));
      in.moveZ = static_cast<int8_t>(rng.Range(-127, 127));
      if (rng.Uniform() < 0.15f) in.moveX = in.moveZ = 0;
      if (rng.Uniform() < 0.05f) held ^= kSprint;
      if (rng.Uniform() < 0.03f) held ^= kAim;
      in.buttons = held;
      if (rng.Uniform() < 0.06f) in.buttons |= kJump;
      if (rng.Uniform() < 0.03f) in.buttons |= kDodge;
      in.yaw = static_cast<uint16_t>(rng.Next());
      Step(s, in, lv);
      fin << in.seq << ' ' << int(in.moveX) << ' ' << int(in.moveZ) << ' ' << in.yaw << ' ' << int(in.buttons) << '\n';
      char line[256];
      std::snprintf(line, sizeof(line), "%08x %08x %08x %08x %08x %08x %d %d %d %d %d %d %d %d\n", Bits(s.px),
                    Bits(s.py), Bits(s.pz), Bits(s.vx), Bits(s.vy), Bits(s.vz), s.grounded, s.coyote, s.jumpBuf,
                    s.dodgeT, s.dodgeCd, s.prevButtons, s.dodgeX, s.dodgeZ);
      fst << line;
    }
    std::printf("wrote 3000 ticks\n");
    return 0;
  }
  if (mode == "snap") {
    Snapshot s;
    s.serverTick = 4321;
    s.lastInputSeq = 777;
    s.local.px = 1.25f;
    s.local.py = 0.5f;
    s.local.pz = -3.75f;
    s.local.vy = 7.07106781f;
    s.local.grounded = 0;
    s.local.dodgeT = 4;
    s.local.dodgeX = -90;
    s.localHp = 87;
    s.entities.push_back({12, EntityKind::kMonster, 10.0f, 0.0f, -6.5f, 64, 1, 120});
    s.entities.push_back({40, EntityKind::kProjectile, -2.0f, 1.4f, 3.0f, 200, 0, 0});
    GameEvent e;
    e.id = 65000;
    e.type = EventType::kHit;
    e.source = 3;
    e.target = 12;
    e.damage = 34;
    e.crit = 1;
    e.x = 10;
    e.y = 1.2f;
    e.z = -6.5f;
    s.events.push_back(e);
    BitWriter w;
    Header h;
    h.type = PacketType::kSnapshot;
    h.seq = 9;
    h.ack = 65535;
    h.ackBits = 0xA5A5A5A5u;
    WriteHeader(w, h);
    WriteSnapshot(w, s);
    std::ofstream(argv[2], std::ios::binary)
        .write(reinterpret_cast<const char*>(w.Data().data()), static_cast<std::streamsize>(w.Data().size()));
    std::printf("snapshot bytes=%zu\n", w.Data().size());
    return 0;
  }
  if (mode == "input") {
    std::ifstream f(argv[2], std::ios::binary);
    std::string d((std::istreambuf_iterator<char>(f)), std::istreambuf_iterator<char>());
    BitReader r(reinterpret_cast<const uint8_t*>(d.data()), d.size());
    const Header h = ReadHeader(r);
    std::vector<InputCmd> in;
    ReadInputs(r, in);
    const bool ok = r.Ok() && h.type == PacketType::kInput && h.seq == 1234 && h.ack == 4321 &&
                    h.ackBits == 0x0F0F0F0Fu && in.size() == 2 && in[0].seq == 65535 && in[0].moveX == -127 &&
                    in[0].moveZ == 64 && in[0].yaw == 40000 && in[0].buttons == (kJump | kSprint) && in[1].seq == 0 &&
                    in[1].moveX == 5 && in[1].buttons == kSkillF;
    std::printf("input packet from C#: %zu bytes, %s\n", d.size(), ok ? "OK" : "MISMATCH");
    return ok ? 0 : 1;
  }
  return 2;
}
