// 서버 규칙 단위 테스트. 외부 테스트 프레임워크 없이 CTest로 돌린다 (ctest --output-on-failure).
// 각 테스트 이름이 곧 "지켜야 하는 규칙"이다. 규칙을 바꾸면 여기가 먼저 실패해야 한다.
#include <cmath>
#include <cstdio>
#include <vector>

#include "../src/game/geometry.h"
#include "../src/game/world.h"
#include "../src/net/reliability.h"

namespace {
int g_checks = 0;
int g_failed = 0;
#define CHECK(cond)                                                 \
  do {                                                              \
    ++g_checks;                                                     \
    if (!(cond)) {                                                  \
      ++g_failed;                                                   \
      std::printf("  FAIL %s:%d  %s\n", __FILE__, __LINE__, #cond); \
    }                                                               \
  } while (0)

struct TestCase {
  const char* name;
  void (*fn)();
};
std::vector<TestCase>& Registry() {
  static std::vector<TestCase> r;
  return r;
}
struct Reg {
  Reg(const char* n, void (*f)()) { Registry().push_back({n, f}); }
};
#define TEST(name)                   \
  void name();                       \
  const Reg reg_##name(#name, name); \
  void name()

using namespace lr;

InputCmd In(uint16_t seq, int8_t mx = 0, int8_t mz = 0, uint8_t buttons = 0) {
  InputCmd in;
  in.seq = seq;
  in.moveX = mx;
  in.moveZ = mz;
  in.buttons = buttons;
  return in;
}

MoveState Grounded(float x = 0, float y = 0, float z = 0) {
  MoveState s;
  s.px = x;
  s.py = y;
  s.pz = z;
  Level lv = DefaultLevel();
  Step(s, In(0), lv);  // 한 틱 서 있게 해서 grounded = 1
  return s;
}

// ---------- 이동 (shared/movement.h) ----------

TEST(Movement_JumpLiftsOffOnTheSameTick) {  // 1편 F4: 0.446초 → 같은 틱
  const Level lv = DefaultLevel();
  MoveState s = Grounded();
  CHECK(s.grounded == 1);
  Step(s, In(1, 0, 0, kJump), lv);
  CHECK(s.py > 0.2F);
  CHECK(s.vy > 0.0F);
}

TEST(Movement_CoyoteTimeAllowsLateJump) {
  const Level lv = DefaultLevel();
  MoveState s = Grounded(7.3F, 1.0F, 5.5F);  // 높이 1m 상자 위 가장자리
  CHECK(s.grounded == 1 && s.py == 1.0F);
  Step(s, In(1, 127, 0), lv);  // 상자 밖으로 한 걸음 → 떨어지기 시작
  CHECK(s.grounded == 0);
  Step(s, In(2, 127, 0), lv);  // 공중 1틱 더
  Step(s, In(3, 127, 0, kJump), lv);
  CHECK(s.vy > 0.0F);  // 이미 발판을 벗어났지만 0.1초 안이라 점프 인정

  MoveState late = Grounded(7.3F, 1.0F, 5.5F);
  for (uint16_t i = 1; i <= 5; ++i) Step(late, In(i, 127, 0), lv);
  Step(late, In(6, 127, 0, kJump), lv);
  CHECK(late.vy < 0.0F);  // 0.1초가 지나면 점프 불가
}

TEST(Movement_JumpBufferRemembersEarlyPress) {
  const Level lv = DefaultLevel();
  MoveState s;
  s.py = 0.05F;
  s.vy = -1.0F;                     // 착지 직전
  Step(s, In(1, 0, 0, kJump), lv);  // 공중에서 미리 누름 → 아직 점프 아님
  CHECK(s.grounded == 1);
  Step(s, In(2), lv);  // 착지한 다음 틱에 저장해 둔 점프가 실행됨
  CHECK(s.vy > 0.0F);
}

TEST(Movement_DodgeRespectsCooldown) {
  const Level lv = DefaultLevel();
  MoveState s = Grounded();
  Step(s, In(1, 127, 0, kDodge), lv);
  CHECK(s.dodgeT == kDodgeTicks - 1);
  CHECK(s.dodgeCd == kDodgeCooldownTicks);
  for (uint16_t i = 2; i < 12; ++i) Step(s, In(i, 127, 0), lv);  // 구르기 끝날 때까지
  CHECK(s.dodgeT == 0);
  Step(s, In(12, 127, 0, kDodge), lv);  // 쿨타임 중 다시 누름
  CHECK(s.dodgeT == 0);
}

TEST(Movement_WallStopsPlayer) {
  const Level lv = DefaultLevel();
  MoveState s = Grounded(3.0F, 0.0F, 5.5F);  // 상자 (x 4..7) 왼쪽
  for (uint16_t i = 1; i < 60; ++i) Step(s, In(i, 127, 0), lv);
  CHECK(s.px <= 4.0F - kRadius + 1e-5F);  // 상자 안으로 들어가지 않음
}

TEST(Movement_IsDeterministic) {  // 같은 입력 → 비트까지 같은 결과
  const Level lv = DefaultLevel();
  MoveState a = Grounded();
  MoveState b = Grounded();
  Rng rng(9);
  for (uint16_t i = 1; i < 2000; ++i) {
    InputCmd in = In(i, static_cast<int8_t>(rng.Range(-127, 127)), static_cast<int8_t>(rng.Range(-127, 127)),
                     static_cast<uint8_t>(rng.Next() & 0x17U));
    Step(a, in, lv);
    Step(b, in, lv);
  }
  CHECK(BitwiseEqual(a, b));
}

// ---------- 프로토콜 (shared/protocol.h) ----------

TEST(Protocol_SnapshotRoundTrip) {
  Snapshot s;
  s.serverTick = 4321;
  s.lastInputSeq = 777;
  s.local.px = 1.25F;
  s.local.vy = kJumpVelocity;
  s.local.dodgeX = -90;
  s.localHp = 87;
  s.entities.push_back({12, EntityKind::kMonster, 10.0F, 0.0F, -6.5F, 64, 1, 120});
  GameEvent e;
  e.id = 65000;
  e.damage = 34;
  e.crit = 1;
  s.events.push_back(e);
  BitWriter w;
  WriteSnapshot(w, s);
  BitReader r(w.Data().data(), w.Data().size());
  Snapshot back;
  ReadSnapshot(r, back);
  CHECK(r.Ok());
  CHECK(back.serverTick == 4321 && back.lastInputSeq == 777 && back.localHp == 87);
  CHECK(BitwiseEqual(back.local, s.local));  // 내 상태는 float 원본 그대로
  CHECK(back.entities.size() == 1 && std::fabs(back.entities[0].x - 10.0F) < 0.01F);  // 양자화 오차 1cm 미만
  CHECK(back.events.size() == 1 && back.events[0].id == 65000 && back.events[0].crit == 1);
}

TEST(Protocol_RejectsTruncatedPacket) {
  Snapshot s;
  s.entities.resize(5);
  BitWriter w;
  WriteSnapshot(w, s);
  BitReader r(w.Data().data(), w.Data().size() / 2);  // 절반만 도착
  Snapshot back;
  ReadSnapshot(r, back);
  CHECK(!r.Ok());
}

TEST(Protocol_RejectsUnknownPacketType) {
  BitWriter w;
  w.WriteU8(9);  // 존재하지 않는 타입
  w.WriteU16(0);
  w.WriteU16(0);
  w.WriteU32(0);
  BitReader r(w.Data().data(), w.Data().size());
  (void)ReadHeader(r);
  CHECK(!r.Ok());
}

TEST(Protocol_RejectsTooManyInputs) {
  BitWriter w;
  w.WriteBits(7, 3);  // 상한 4개를 넘는 개수
  BitReader r(w.Data().data(), w.Data().size());
  std::vector<InputCmd> out;
  ReadInputs(r, out);
  CHECK(!r.Ok() && out.empty());
}

TEST(Protocol_SanitizesInput) {
  std::vector<InputCmd> in{In(1, -128, -128)};
  BitWriter w;
  WriteInputs(w, in);
  BitReader r(w.Data().data(), w.Data().size());
  std::vector<InputCmd> out;
  ReadInputs(r, out);
  CHECK(r.Ok() && out.size() == 1 && out[0].moveX == -127 && out[0].moveZ == -127);
}

// ---------- 신뢰성 (net/reliability.h) ----------

TEST(Ack_TracksOutOfOrderAndWrap) {
  AckTracker a;
  a.OnReceived(65534);
  a.OnReceived(65535);
  a.OnReceived(0);  // 65535 다음은 0
  a.OnReceived(2);  // 1은 아직 안 옴
  CHECK(a.Ack() == 2);
  CHECK(a.Bits() == 0b1110U);  // 0(2칸 전), 65535(3칸 전), 65534(4칸 전)는 받음, 1(1칸 전)은 못 받음
  a.OnReceived(1);             // 늦게 도착
  CHECK(a.Bits() == 0b1111U);
  a.OnReceived(2);  // 중복
  CHECK(a.Ack() == 2 && a.Bits() == 0b1111U);
}

TEST(Reliable_ResendsUntilAcked) {
  ReliableEventQueue q;
  GameEvent e1;
  e1.id = 1;
  GameEvent e2;
  e2.id = 2;
  q.Push(e1);
  q.Push(e2);
  std::vector<GameEvent> out;
  q.Collect(10, 32, out);
  CHECK(out.size() == 2);
  q.Collect(11, 32, out);  // ACK가 없으니 다음 패킷에도 다시 실림
  CHECK(out.size() == 2);
  q.OnAck(11, 0);  // 패킷 11을 받았다는 확인
  q.Collect(12, 32, out);
  CHECK(out.empty() && q.Pending() == 0);
}

TEST(Reliable_DedupIgnoresRepeats) {
  EventDedup d;
  CHECK(d.FirstTime(5));
  CHECK(!d.FirstTime(5));
  CHECK(d.FirstTime(5 + 1024));  // 같은 칸이지만 다른 번호
}

// ---------- 게임 규칙 (game/world.cpp, geometry.h) ----------

TEST(Geometry_FastArrowDoesNotTunnel) {
  // 한 틱에 1m 이동하는 화살, 반지름 0.3m 몬스터가 경로 한가운데 → 양 끝점은 모두 밖이지만 닿아야 함
  CHECK(SegmentHitsSphere(0, 1, 0, 0, 1, 1, 0, 1, 0.5F, 0.3F));
  CHECK(!SegmentHitsSphere(0, 1, 0, 0, 1, 1, 1, 1, 0.5F, 0.3F));
}

TEST(World_PassiveCritOnEveryThirdBasicShot) {  // 1편 F7 버그 수정 확인 (원본은 4번째에 적용)
  World w(1, 0, 0);
  const uint16_t id = w.AddPlayer();
  int shots = 0;
  uint16_t seq = 0;
  for (int t = 0; t < 600 && shots < 9; ++t) {
    const bool press = (t % 20 == 0);
    w.PushInput(id, In(++seq, 0, 0, press ? kAttack : 0));
    const size_t before = w.Projectiles().size();
    w.Step();
    if (w.Projectiles().size() > before) {
      ++shots;
      if (shots % 3 == 0) CHECK(w.Projectiles().back().crit == 1);
    }
  }
  CHECK(shots == 9);
}

TEST(World_InputBufferDelaysFirstStep) {
  World w(1, 0, 1);
  const uint16_t id = w.AddPlayer();
  w.PushInput(id, In(1));
  w.Step();
  CHECK(w.FindPlayer(id)->lastSeq == 0);  // 버퍼 1틱: 아직 시작 안 함
  w.PushInput(id, In(2));
  w.Step();
  CHECK(w.FindPlayer(id)->lastSeq == 1);
}

TEST(World_RepeatsLastInputWhenLate) {
  World w(1, 0, 0);
  const uint16_t id = w.AddPlayer();
  w.PushInput(id, In(1, 127, 0));
  w.Step();
  const float x1 = w.FindPlayer(id)->mv.px;
  w.Step();  // 입력 2가 아직 안 옴 → 입력 1 반복
  CHECK(w.stats.repeatedInputs == 1);
  CHECK(w.FindPlayer(id)->lastSeq == 1);
  CHECK(w.FindPlayer(id)->mv.px > x1);  // 멈추지 않고 직전 방향으로 계속 이동
}

TEST(World_IgnoresDuplicateAndOldInputs) {
  World w(1, 0, 0);
  const uint16_t id = w.AddPlayer();
  for (int i = 0; i < 4; ++i) w.PushInput(id, In(1));  // 같은 입력이 4번 도착 (중복 전송)
  w.Step();
  w.PushInput(id, In(1));  // 이미 처리한 번호
  w.Step();
  CHECK(w.stats.repeatedInputs == 1);  // 큐에 남은 게 없어서 반복 1회 → 중복이 쌓이지 않았다는 뜻
}

TEST(World_DynamicIdsWrapInsideTheirRange) {
  CHECK(World::NextIdAfter(1024) == 1025);
  CHECK(World::NextIdAfter(65535) == kFirstDynamicId);  // 몬스터·플레이어 번호(1~1023)와 겹치지 않음
}

}  // namespace

int main() {
  for (const auto& t : Registry()) {
    const int before = g_failed;
    t.fn();
    std::printf("%s %s\n", g_failed == before ? "PASS" : "FAIL", t.name);
  }
  std::printf("%d checks, %d failed\n", g_checks, g_failed);
  return g_failed == 0 ? 0 : 1;
}
