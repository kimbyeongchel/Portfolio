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
  const uint16_t id = w.AddPlayer(ClassId::kArcher);
  int shots = 0;
  uint16_t seq = 0;
  for (int t = 0; t < 900 && shots < 9; ++t) {
    const bool hold = (t % 40) < 14;  // 14틱(0.47초) 충전 후 뗌 → 발사. 쿨타임 1초라 40틱 간격
    w.PushInput(id, In(++seq, 0, 0, hold ? kAttack : 0));
    const size_t before = w.Projectiles().size();
    w.Step();
    if (w.Projectiles().size() > before) {
      ++shots;
      if (shots % 3 == 0) CHECK(w.Projectiles().back().crit == 1);
    }
  }
  CHECK(shots == 9);
}

// ---------- 스킬 타이밍 (shared/abilities.h: 클라도 같은 코드로 예측) ----------

// 직업 하나를 버튼 패턴대로 n틱 돌린다. 매 틱 상태를 넘겨 검사할 수 있게 콜백을 받는다.
struct Sim {
  explicit Sim(ClassId c) : timing(TimingOf(c)), lv(DefaultLevel()) { mv = Grounded(); }
  AbilityResult Tick(int8_t mx, int8_t mz, uint8_t buttons) {
    return SimStep(mv, ab, timing, In(++seq, mx, mz, buttons), lv);
  }
  const ClassTiming& timing;
  Level lv;
  MoveState mv;
  AbilityState ab;
  uint16_t seq = 0;
};

TEST(Ability_WarriorComboAdvancesOnlyWhenPressedAgain) {
  Sim s(ClassId::kWarrior);
  s.Tick(0, 0, kAttack);  // 1타 시작
  CHECK(s.ab.slot == kSlotBasic && s.ab.step == 0 && s.ab.phase == kPhaseWindup);
  for (int i = 0; i < 6; ++i) s.Tick(0, 0, 0);  // 선딜 5 + 판정 1 → 후딜 진입
  CHECK(s.ab.phase == kPhaseRecovery);
  s.Tick(0, 0, kAttack);  // 후딜 중에 다시 누름 → 바로 2타 선딜
  CHECK(s.ab.step == 1 && s.ab.phase == kPhaseWindup);
  for (int i = 0; i < 8; ++i) s.Tick(0, 0, 0);
  s.Tick(0, 0, kAttack);
  CHECK(s.ab.step == 2);  // 3타
  for (int i = 0; i < 10; ++i) s.Tick(0, 0, 0);
  s.Tick(0, 0, kAttack);  // 3타 뒤에는 4타가 없다
  CHECK(s.ab.step == 2);
  for (int i = 0; i < 30; ++i) s.Tick(0, 0, 0);
  CHECK(s.ab.slot == kNoSlot);  // 아무것도 안 누르면 후딜이 끝나고 자유
}

TEST(Ability_RecoveryLocksMovementUntilCancelWindow) {
  Sim s(ClassId::kWarrior);  // 1타: 선딜 5, 판정 1, 후딜 24, 캔슬 15
  s.Tick(0, 0, kAttack);
  for (int i = 0; i < 6; ++i) s.Tick(0, 0, 0);
  const float x0 = s.mv.px;
  for (int i = 0; i < 15; ++i) s.Tick(127, 0, 0);  // 후딜 1~15틱: 이동 키를 눌러도 제자리
  CHECK(s.mv.px == x0);
  CHECK(s.ab.slot == kSlotBasic);
  s.Tick(127, 0, 0);  // 16틱째: 캔슬 → 같은 틱에 바로 이동
  CHECK(s.ab.slot == kNoSlot);
  CHECK(s.mv.px > x0);
}

TEST(Ability_ArcherChargeReleasedTooEarlyCancelsWithoutCooldown) {
  Sim s(ClassId::kArcher);  // 기본 공격: 12틱 이상 충전해야 발사
  for (int i = 0; i < 6; ++i) s.Tick(0, 0, kAttack);
  const AbilityResult r = s.Tick(0, 0, 0);  // 0.2초 만에 뗌
  CHECK(r.phase == kPhaseNone);
  CHECK(s.ab.cooldown[kSlotBasic] == 0);  // 실패한 충전은 쿨타임을 쓰지 않음
  for (int i = 0; i < 14; ++i) s.Tick(0, 0, kAttack);
  const AbilityResult fired = s.Tick(0, 0, 0);
  CHECK(fired.phase == kPhaseActive && fired.tick == 1);  // 떼는 틱에 바로 발사
  CHECK(fired.charge == 14);
  CHECK(s.ab.cooldown[kSlotBasic] == 30);
}

TEST(Ability_DodgeCancelsCharge) {
  Sim s(ClassId::kArcher);
  for (int i = 0; i < 8; ++i) s.Tick(0, 0, kAttack);
  CHECK(s.ab.phase == kPhaseCharge);
  s.Tick(127, 0, kAttack | kDodge);
  CHECK(s.ab.slot == kNoSlot);
  CHECK(s.mv.dodgeT > 0);  // 충전을 버리고 같은 틱에 구름
}

TEST(Ability_LancerDashMovesOnlyDuringActive) {
  Sim s(ClassId::kLancer);  // F: 선딜 6, 판정 18 동안 8m/s 돌진
  s.Tick(127, 0, 0);        // 오른쪽(+x)으로 걷다가
  const float x0 = s.mv.px;
  s.Tick(0, 0, kSkillF);  // 손을 떼고 F
  for (int i = 0; i < 5; ++i) s.Tick(0, 0, 0);
  CHECK(s.mv.px == x0);  // 선딜 동안 제자리
  for (int i = 0; i < 18; ++i) s.Tick(0, 0, 0);
  const float dashed = s.mv.px - x0;
  CHECK(dashed > 4.7F && dashed < 4.9F);  // 8m/s × 18틱 × (1/30)초 = 4.8m, 키를 안 눌러도 마지막 방향으로
}

TEST(Ability_CooldownBlocksRecastAndCountsInTicks) {
  Sim s(ClassId::kWarrior);
  s.Tick(0, 0, kSkillE);
  CHECK(s.ab.slot == kSlotE && s.ab.cooldown[kSlotE] == 150);
  for (int i = 0; i < 40; ++i) s.Tick(0, 0, 0);
  s.Tick(0, 0, kSkillE);  // 쿨타임 중
  CHECK(s.ab.slot == kNoSlot);
  for (int i = 0; i < 150; ++i) s.Tick(0, 0, 0);
  s.Tick(0, 0, kSkillE);
  CHECK(s.ab.slot == kSlotE);
}

// ---------- 직업별 효과 (game/world.cpp: 서버만 계산) ----------

// 버튼 함수대로 n틱 진행하면서, 이번에 나온 이벤트를 모두 모은다
template <typename F>
std::vector<GameEvent> Drive(World& w, uint16_t id, int ticks, F buttonsAt, uint16_t yaw = 0) {
  std::vector<GameEvent> all;
  std::vector<GameEvent> ev;
  static uint16_t seq = 0;
  for (int t = 0; t < ticks; ++t) {
    InputCmd in = In(++seq, 0, 0, buttonsAt(t));
    in.yaw = yaw;
    w.PushInput(id, in);
    w.Step();
    w.TakeEvents(ev);
    all.insert(all.end(), ev.begin(), ev.end());
  }
  return all;
}
int CountOn(const std::vector<GameEvent>& ev, EventType type, uint16_t target) {
  int n = 0;
  for (const auto& e : ev) n += (e.type == type && e.target == target) ? 1 : 0;
  return n;
}

TEST(World_WarriorArcHitsFrontNotBack) {
  World w(1, 2, 0);
  const uint16_t id = w.AddPlayer(ClassId::kWarrior);
  w.PlaceMonster(0, 0.0F, 2.0F, 1000);   // 앞 (yaw 0 = +z 방향)
  w.PlaceMonster(1, 0.0F, -2.0F, 1000);  // 뒤
  const auto ev = Drive(w, id, 40, [](int t) { return t == 2 ? kAttack : 0; });
  CHECK(CountOn(ev, EventType::kHit, w.Monsters()[0].id) == 1);
  CHECK(CountOn(ev, EventType::kHit, w.Monsters()[1].id) == 0);
}

TEST(World_ActiveWindowHitsEachTargetOnce) {
  World w(1, 1, 0);
  const uint16_t id = w.AddPlayer(ClassId::kWarrior);
  w.PlaceMonster(0, 0.0F, 2.0F, 5000);
  // 3타 콤보. 3타는 판정이 5틱 열려 있지만 같은 몬스터는 한 번만 맞아야 한다 → 총 3회
  const auto ev = Drive(w, id, 90, [](int t) { return (t == 2 || t == 10 || t == 20) ? kAttack : 0; });
  CHECK(CountOn(ev, EventType::kHit, w.Monsters()[0].id) == 3);
}

TEST(World_LancerEHitsFourTimesWithSeparateRolls) {
  World w(3, 1, 0);
  const uint16_t id = w.AddPlayer(ClassId::kLancer);
  w.PlaceMonster(0, 0.0F, 2.5F, 5000);
  const auto ev = Drive(w, id, 40, [](int t) { return t == 2 ? kSkillE : 0; });
  std::vector<int> dmg;
  for (const auto& e : ev) {
    if (e.type == EventType::kHit) dmg.push_back(e.damage);
  }
  CHECK(dmg.size() == 4);  // 원본 비율 .18 / .28 / .42 / .54에 맞춘 4타
  bool varied = false;
  for (const int d : dmg) varied = varied || d != dmg[0];
  CHECK(varied);                                       // 원본은 한 번 굴린 숫자를 4번 사용
  for (const int d : dmg) CHECK(d >= 86 && d <= 162);  // floor(70×1.3)=91 ±5, 치명 ×1.7
}

TEST(World_WarriorRageScalesWithMissingHp) {
  World w(1, 0, 0);
  const uint16_t id = w.AddPlayer(ClassId::kWarrior);
  CHECK(std::fabs(w.StatsOf(*w.FindPlayer(id)).atk - 1.5F) < 1e-6F);
  w.SetPlayerHp(id, 30);  // 10~39 → 2단계 × 0.07 (원본 prefab/Class/Warrior.prefab 값)
  CHECK(std::fabs(w.StatsOf(*w.FindPlayer(id)).atk - 1.64F) < 1e-5F);
  CHECK(std::fabs(w.StatsOf(*w.FindPlayer(id)).def - 0.34F) < 1e-5F);
}

TEST(World_HealerBarrierBlocksOneHitAfterEightQuietSeconds) {
  World w(1, 0, 0);
  const uint16_t id = w.AddPlayer(ClassId::kHealer);
  Drive(w, id, 241, [](int) { return 0; });
  CHECK(World::PassiveReady(*w.FindPlayer(id)));
  w.HurtPlayer(id, 20);
  CHECK(w.FindPlayer(id)->hp == 100);  // 막음
  CHECK(w.stats.blocks == 1);
  w.HurtPlayer(id, 20);
  CHECK(w.FindPlayer(id)->hp == 80);  // 두 번째는 막지 못함 (다시 8초를 버텨야 함)
}

TEST(World_ShieldAbsorbsAndHealCapsAtMax) {
  World w(1, 0, 0);
  const uint16_t lancer = w.AddPlayer(ClassId::kLancer);
  const uint16_t archer = w.AddPlayer(ClassId::kArcher);  // 2m 옆 → 실드 범위(5m) 안
  w.PushInput(archer, In(1));
  Drive(w, lancer, 40, [](int t) { return t == 2 ? kSkillQ : 0; });
  CHECK(w.FindPlayer(archer)->shield == 100);
  CHECK(w.FindPlayer(lancer)->shield == 100);
  w.HurtPlayer(archer, 50);  // 방어 0.1 → 45를 실드가 흡수
  CHECK(w.FindPlayer(archer)->hp == 100);
  CHECK(w.FindPlayer(archer)->shield == 55);

  World h(1, 0, 0);
  const uint16_t healer = h.AddPlayer(ClassId::kHealer);
  h.SetPlayerHp(healer, 90);
  const auto ev = Drive(h, healer, 40, [](int t) { return t == 2 ? kSkillQ : 0; });
  CHECK(h.FindPlayer(healer)->hp == 100);  // 상한
  bool sawHeal10 = false;
  for (const auto& e : ev) sawHeal10 = sawHeal10 || (e.type == EventType::kHeal && e.damage == 10);
  CHECK(sawHeal10);  // 실제로 회복된 양만 표시
}

TEST(World_BuffRefreshesInsteadOfStacking) {
  World w(1, 0, 0);
  const uint16_t a = w.AddPlayer(ClassId::kHealer);
  const uint16_t b = w.AddPlayer(ClassId::kHealer);
  w.PushInput(b, In(1, 0, 0, 0));
  w.PushInput(b, In(2, 0, 0, kSkillF));
  for (uint16_t s = 3; s < 60; ++s) w.PushInput(b, In(s));
  Drive(w, a, 50, [](int t) { return t == 3 ? kSkillF : 0; });       // 두 힐러가 거의 동시에 버프
  CHECK(std::fabs(w.StatsOf(*w.FindPlayer(a)).atk - 1.8F) < 1e-6F);  // 1.7 + 0.1 (0.2가 아님)
  Drive(w, a, 245, [](int) { return 0; });
  CHECK(std::fabs(w.StatsOf(*w.FindPlayer(a)).atk - 1.7F) < 1e-6F);  // 끝나면 정확히 원래 값
}

TEST(World_WarriorRageAndHealerBuffStack) {
  // 원본: 전사 passive()가 매 프레임 atk·def를 기본값에서 다시 써서 힐러 버프(atk·def)가 다음 프레임에 지워졌다
  World w(1, 0, 0);
  const uint16_t warrior = w.AddPlayer(ClassId::kWarrior);
  const uint16_t healer = w.AddPlayer(ClassId::kHealer);  // 2m 옆 → 버프 범위(5m) 안
  w.SetPlayerHp(warrior, 30);                             // 분노 2단계
  for (uint16_t s = 1; s < 60; ++s) w.PushInput(warrior, In(s));
  Drive(w, healer, 40, [](int t) { return t == 2 ? kSkillF : 0; });
  const CombatStats st = w.StatsOf(*w.FindPlayer(warrior));
  CHECK(std::fabs(st.atk - (1.5F + 2 * 0.07F + 0.1F)) < 1e-5F);  // 기본 + 분노 + 버프가 모두 적용
  CHECK(std::fabs(st.def - (0.2F + 2 * 0.07F + 0.1F)) < 1e-5F);
}

TEST(World_ArcherExplosionBurnsFiveTimes) {
  World w(1, 1, 0);
  const uint16_t id = w.AddPlayer(ClassId::kArcher);
  w.PlaceMonster(0, 0.0F, 8.0F, 5000);
  const auto ev = Drive(w, id, 220, [](int t) { return (t >= 2 && t < 22) ? kSkillF : 0; });
  int burns = 0;
  int explode = 0;
  for (const auto& e : ev) {
    if (e.type != EventType::kHit) continue;
    burns += e.damage == kBurnDamage ? 1 : 0;
    explode += e.damage == kExplodeDamage ? 1 : 0;
  }
  CHECK(CountOn(ev, EventType::kHit, w.Monsters()[0].id) == 1 + 1 + 5);  // 화살 + 폭발 + 화상 5회
  CHECK(explode == 1);
  CHECK(burns == 5);
}

TEST(World_CurseZoneTicksEveryHalfSecondAndAmplifiesDamage) {
  World w(1, 1, 0);
  const uint16_t id = w.AddPlayer(ClassId::kHealer);
  w.PlaceMonster(0, 0.0F, 4.0F, 5000);
  const auto ev = Drive(w, id, 200, [](int t) { return t == 2 ? kSkillE : 0; });
  int ticks = 0;
  for (const auto& e : ev) ticks += (e.type == EventType::kHit && e.damage == 18) ? 1 : 0;  // 15 × 1.2
  CHECK(ticks == 6);                                                                        // 3초 동안 0.5초마다
  CHECK(w.Zones().empty());  // 시간이 지나면 사라짐
}

TEST(World_InputBufferDelaysFirstStep) {
  World w(1, 0, 1);
  const uint16_t id = w.AddPlayer(ClassId::kArcher);
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

TEST(World_RespawnWaitsForFreshInputInsteadOfRepeating) {
  World w(1, 0, 1);
  const uint16_t id = w.AddPlayer(ClassId::kWarrior);
  uint16_t seq = 0;
  for (int i = 0; i < 3; ++i) {
    w.PushInput(id, In(++seq, 127, 0));
    w.Step();
  }
  w.HurtPlayer(id, 1000);
  CHECK(!w.FindPlayer(id)->alive);
  for (int i = 0; i < 90; ++i) {  // 죽어 있는 동안에도 입력 번호는 소비
    w.PushInput(id, In(++seq, 127, 0));
    w.Step();
  }
  CHECK(w.FindPlayer(id)->alive);
  const uint64_t repeats = w.stats.repeatedInputs;
  w.Step();
  w.Step();
  CHECK(w.stats.repeatedInputs == repeats);  // 옛 입력을 부활 위치에 반복하지 않음 (전에는 4m 오보정의 원인)
  CHECK(w.FindPlayer(id)->mv.px == 0.0F);
  CHECK(w.FindPlayer(id)->lastSeq == seq);
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
