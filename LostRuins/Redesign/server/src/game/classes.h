// 직업 = 클래스 상속이 아니라 "표 한 줄" (1편 F8, 2편 4장).
// 원본: PlayerState(추상) → Warrior / Dealer / Lancer / Healer → MultiWarrior / MultiDealer / ... (상속 3단)
//       스킬마다 코루틴, 데미지는 프리팹마다 흩어진 int 필드.
// 재설계: 스킬이 하는 일을 몇 가지 "효과 부품"(근접 판정, 투사체, 지원)으로 나누고, 직업은 그 조합을 적은 표.
//         직업을 추가해도 world.cpp는 그대로이고 이 파일에 표 한 줄이 늘어난다.
//
// 숫자 출처 표기
//   [원본]  State·프리팹·스크립트에 있던 값 그대로
//   [설계]  원본에 없거나(궁수 프레임, 번개검 속도 등) 원본 값이 버그라서 새로 정한 값 → 기획 확인 대상
#pragma once
#include <array>
#include <cstdint>

#include "../shared/abilities.h"

namespace lr {

constexpr uint8_t kNoDamage = 255;

// 근접·범위 판정 하나. 판정 단계의 from~to 틱 동안 매 틱 검사하고, 같은 대상은 한 번만 맞는다.
// (원본은 칼 콜라이더를 켜 두고 OnTriggerEnter → 같은 몬스터가 두 번 맞거나 빈 프레임에 놓치는 일이 있었다)
enum class Shape : uint8_t { kArc, kCircle };
struct HitDef {
  uint8_t from = 0, to = 0;  // 판정 단계 안의 틱 번호 (1부터). from == 0이면 비어 있음
  Shape shape = Shape::kArc;
  float range = 0.0F;         // 반지름 (m)
  float halfAngleDeg = 0.0F;  // kArc: 정면 기준 좌우 각도
  float forward = 0.0F;       // kCircle: 중심을 바라보는 방향으로 이만큼 이동
  uint8_t dmgIndex = 0;       // 직업 damage[] 표의 몇 번째 값인가 (원본 s_damage[] 인덱스와 같음)
};

enum class OnHit : uint8_t { kNone, kExplodeBurn, kCurseZone };
struct ShotDef {      // 투사체
  uint8_t count = 0;  // 0이면 투사체 없음
  uint8_t dmgIndex = 0;
  float spreadDeg = 0.0F;  // 여러 발일 때 양 끝 사이 각도의 절반
  float speed = 0.0F;      // m/s (충전 스킬은 충전량에 따라 speed → speedMax)
  float speedMax = 0.0F;
  float gravity = 0.0F;
  uint16_t ttl = 0;          // 틱
  uint16_t homingAfter = 0;  // 이 틱 이후 가장 가까운 몬스터 쪽으로 휜다 (0 = 유도 없음)
  float homingRange = 0.0F;
  float turn = 0.0F;          // 유도: 매 틱 목표 방향을 섞는 비율 (0~1)
  float splashRadius = 0.0F;  // 명중 지점 주변 추가 피해 (데미지 / splashDiv)
  uint8_t splashDiv = 1;
  float knockback = 0.0F;
  OnHit onHit = OnHit::kNone;
  bool forceCritOnPassive = false;
};

enum class Support : uint8_t { kNone, kShield, kHeal, kBuff };
struct SupportDef {  // 아군 대상 (자기 포함)
  Support kind = Support::kNone;
  float radius = 0.0F;
  int amount = 0;         // 실드량·회복량
  uint16_t duration = 0;  // 틱
};

struct SkillEffect {
  std::array<HitDef, 4> hits{};
  ShotDef shot{};
  SupportDef support{};
};

enum class Passive : uint8_t {
  kNone,
  kRage,       // 전사: 체력이 낮을수록 공격력·방어력 상승
  kThirdShot,  // 궁수: 기본 공격 3발마다 강화 화살 (확정 치명타)
  kEmpowered,  // 창술사: 8초마다 다음 기본 공격 강화
  kBarrier,    // 힐러: 8초마다 다음 피격 1회 무효
};

struct ClassDef {
  const char* name;
  int maxHp;
  float atk, def, crit;       // 원본 State 에셋: 공격 배율, 받는 피해 감소율, 치명타 확률
  std::array<int, 5> damage;  // 원본 s_damage[]
  std::array<std::array<SkillEffect, 3>, kSlotCount> skills;  // [슬롯][콤보 단계]
  Passive passive;
  uint16_t passiveTicks;    // 강화·보호막 재충전 시간
  uint8_t passiveDmgIndex;  // 강화 공격이 쓰는 damage[] 인덱스
  float ragePerStack;       // 전사 패시브 한 단계당 atk·def 증가량
};

// ---- 표를 읽기 쉽게 만드는 작은 함수들 ----
constexpr HitDef Arc(uint8_t from, uint8_t to, float range, float halfAngle, uint8_t dmg) {
  return HitDef{from, to, Shape::kArc, range, halfAngle, 0.0F, dmg};
}
constexpr HitDef Circle(uint8_t from, uint8_t to, float radius, float forward, uint8_t dmg) {
  return HitDef{from, to, Shape::kCircle, radius, 0.0F, forward, dmg};
}
constexpr SkillEffect Hits(HitDef a, HitDef b = {}, HitDef c = {}, HitDef d = {}) {
  return SkillEffect{{a, b, c, d}, {}, {}};
}
constexpr SkillEffect Shot(ShotDef s) {
  return SkillEffect{{}, s, {}};
}
constexpr SkillEffect Help(Support kind, float radius, int amount, uint16_t duration) {
  return SkillEffect{{}, {}, SupportDef{kind, radius, amount, duration}};
}
constexpr std::array<SkillEffect, 3> One(SkillEffect e) {
  return {e, e, e};
}
constexpr std::array<SkillEffect, 3> Empty() {
  return {};
}

// 2편의 화살 상수와 같은 기본값을 가진 투사체
constexpr ShotDef Arrow(uint8_t dmg, uint8_t count, float spread, float speed) {
  ShotDef s;
  s.count = count;
  s.dmgIndex = dmg;
  s.spreadDeg = spread;
  s.speed = speed;
  s.speedMax = speed;
  s.gravity = 2.0F;
  s.ttl = 90;
  return s;
}

inline const ClassDef& ClassOf(ClassId id) {
  static const std::array<ClassDef, kClassCount> kTable = [] {
    std::array<ClassDef, kClassCount> t{};

    // ---------------- 전사 ----------------
    // [원본] atk 1.5 def 0.2 ctr 0.4, damage {30, 30, 50, 90}
    // [원본] 기본 3타 모두 damage[0], E = 내 주변 OverlapBox(반경 3), F = 칼 판정 + OverlapBox(반경 11)
    // [원본] 패시브 한 단계당 +0.07 (prefab/Class/Warrior.prefab)
    // [원본 버그] 멀티에서 쓰는 Resources/Warrior.prefab·WarriorVer2.prefab은 0 → 멀티에서는 패시브 무효.
    //             같은 직업의 수치가 프리팹마다 달랐다 → 재설계는 수치를 이 표 한 곳에만 둔다
    // [설계] 번개검: 원본은 범위 안 "무작위" 몬스터 → 가장 가까운 몬스터로 유도. 속도 15m/s
    ShotDef bolt = Arrow(1, 1, 0.0F, 15.0F);
    bolt.gravity = 0.0F;
    bolt.ttl = 30;  // [원본] 1초 뒤 사라짐
    bolt.homingAfter = 1;
    bolt.homingRange = 11.0F;
    bolt.turn = 0.35F;
    t[0] = ClassDef{
        .name = "Warrior",
        .maxHp = 100,
        .atk = 1.5F,
        .def = 0.2F,
        .crit = 0.4F,
        .damage = {30, 30, 50, 90, 0},
        .skills = {{
            /* 기본 3타 */ {Hits(Arc(1, 1, 2.5F, 60.0F, 0)), Hits(Arc(1, 1, 2.5F, 60.0F, 0)),
                            Hits(Arc(1, 5, 2.8F, 75.0F, 0))},
            /* 우클릭  */ Empty(),
            /* Q 번개검 */ One(Shot(bolt)),
            /* E 회전   */ One(Hits(Circle(1, 3, 3.0F, 0.0F, 2))),
            /* F 내려치기 */ One(Hits(Arc(1, 1, 3.0F, 45.0F, 3), Circle(1, 1, 11.0F, 0.0F, 3))),
        }},
        .passive = Passive::kRage,
        .passiveTicks = 0,
        .passiveDmgIndex = 0,
        .ragePerStack = 0.07F,
    };

    // ---------------- 궁수 ----------------
    // [원본] atk 1.7 def 0.1 ctr 0.4, damage {47, 35, 30, 42, 30}
    // [원본] 기본 damage[1]. 3번째 기본은 damage[0] + 확정 치명 (원본은 4번째 화살에 적용되던 버그)
    // [원본] Q 유도 4발 (0.5초 직진 후 유도), E 3갈래 damage[3]
    // [원본] F 충전 화살 damage[4] → 폭발 40 (반경 2.2) + 화상 30 × 5회
    ShotDef basic = Arrow(1, 1, 0.0F, 50.0F);
    basic.speedMax = 70.0F;  // [원본] 충전량 50~70을 화살 힘으로 사용
    basic.forceCritOnPassive = true;
    ShotDef homing = Arrow(2, 4, 12.0F, 20.0F);
    homing.homingAfter = 15;
    homing.homingRange = 15.0F;
    homing.turn = 0.25F;
    homing.gravity = 0.0F;
    ShotDef boom = Arrow(4, 1, 0.0F, 40.0F);
    boom.onHit = OnHit::kExplodeBurn;
    t[1] = ClassDef{
        .name = "Archer",
        .maxHp = 100,
        .atk = 1.7F,
        .def = 0.1F,
        .crit = 0.4F,
        .damage = {47, 35, 30, 42, 30},
        .skills = {{
            /* 기본 (충전) */ One(Shot(basic)),
            /* 우클릭 = 조준 */ Empty(),
            /* Q 유도 4발 */ One(Shot(homing)),
            /* E 3갈래   */ One(Shot(Arrow(3, 3, 15.0F, 30.0F))),
            /* F 폭발 화살 */ One(Shot(boom)),
        }},
        .passive = Passive::kThirdShot,
        .passiveTicks = 0,
        .passiveDmgIndex = 0,
        .ragePerStack = 0.0F,
    };

    // ---------------- 창술사 ----------------
    // [원본] atk 1.3 def 0.4 ctr 0.1, damage {65, 60, 70, 50}
    // [원본] Q 반경 5m 아군에게 실드 100 (5초), E 4연속 찌르기 damage[2], F·우클릭 방패 damage[3]
    // [원본 버그] E는 데미지를 시작할 때 한 번만 굴려 4타가 같은 숫자 → 재설계는 타마다 굴림
    // [원본 확인 필요] 8초마다 강화되는 기본 공격이 damage[1] = 60으로 일반(65)보다 약함.
    //                  기획 의도를 알 수 없어 값은 그대로 두고 표시만 함
    // [설계] E·F 다단 히트 시점 = 원본 비율(e .18/.28/.42/.54, f .20/.35/.55/.78)을 틱으로 옮긴 것
    t[2] = ClassDef{
        .name = "Lancer",
        .maxHp = 100,
        .atk = 1.3F,
        .def = 0.4F,
        .crit = 0.1F,
        .damage = {65, 60, 70, 50, 0},
        .skills = {{
            /* 기본 찌르기 */ One(Hits(Arc(1, 4, 3.2F, 25.0F, 0))),
            /* 우클릭 방패 */ One(Hits(Arc(1, 4, 2.2F, 70.0F, 3))),
            /* Q 실드   */ One(Help(Support::kShield, 5.0F, 100, 150)),
            /* E 4연타  */
            One(Hits(Arc(1, 2, 3.2F, 30.0F, 2), Arc(4, 5, 3.2F, 30.0F, 2), Arc(9, 10, 3.2F, 30.0F, 2),
                     Arc(12, 14, 3.4F, 35.0F, 2))),
            /* F 돌진 4타 */
            One(Hits(Arc(1, 1, 2.2F, 70.0F, 3), Arc(6, 6, 2.2F, 70.0F, 3), Arc(12, 12, 2.2F, 70.0F, 3),
                     Arc(18, 18, 2.6F, 80.0F, 3))),
        }},
        .passive = Passive::kEmpowered,
        .passiveTicks = 240,
        .passiveDmgIndex = 1,
        .ragePerStack = 0.0F,
    };

    // ---------------- 힐러 ----------------
    // [원본] atk 1.7 def 0 ctr 0.1, damage {35}
    // [원본] 화염구 damage[0] + 반경 1.5 스플래시(1/3) + 넉백, Q 반경 7 회복 30
    // [원본] E 저주 구슬: 2초 뒤(또는 충돌 시) 장판 → 0.5초마다 반경 1m에 15 + 방어력 −0.2
    // [원본] F 반경 5 버프 (atk +0.1, ctr +0.2, def +0.1, 8초), 패시브: 8초간 안 맞으면 다음 1회 무효
    // [원본 버그] 회복 함수가 인자를 무시하고 항상 30 → 재설계는 표의 값 사용
    // [원본 버그] 버프 해제를 "8초 뒤 빼기"로 해서 겹치거나 죽으면 수치가 틀어짐
    //             → 재설계는 남은 시간만 저장하고 수치는 매번 기본값에서 다시 계산
    ShotDef fire = Arrow(0, 1, 0.0F, 15.0F);
    fire.gravity = 0.0F;
    fire.splashRadius = 1.5F;
    fire.splashDiv = 3;
    fire.knockback = 1.0F;
    ShotDef curse = Arrow(kNoDamage, 1, 0.0F, 10.0F);
    curse.gravity = 0.0F;
    curse.ttl = 60;
    curse.onHit = OnHit::kCurseZone;
    t[3] = ClassDef{
        .name = "Healer",
        .maxHp = 100,
        .atk = 1.7F,
        .def = 0.0F,
        .crit = 0.1F,
        .damage = {35, 0, 0, 0, 0},
        .skills = {{
            /* 기본 화염구 */ One(Shot(fire)),
            /* 우클릭 = 조준 */ Empty(),
            /* Q 회복   */ One(Help(Support::kHeal, 7.0F, 30, 0)),
            /* E 저주 구슬 */ One(Shot(curse)),
            /* F 버프   */ One(Help(Support::kBuff, 5.0F, 0, 240)),
        }},
        .passive = Passive::kBarrier,
        .passiveTicks = 240,
        .passiveDmgIndex = 0,
        .ragePerStack = 0.0F,
    };
    return t;
  }();
  return kTable[static_cast<size_t>(id)];
}

// 효과 상수 (원본 스크립트에 하드코딩돼 있던 값)
constexpr int kExplodeDamage = 40;      // charge_explosion.cs
constexpr float kExplodeRadius = 2.2F;  // 프리팹 range
constexpr int kBurnDamage = 30;         // Burn.cs 프리팹 damage
constexpr uint8_t kBurnHits = 5;        // 1초마다 5번
constexpr uint16_t kBurnPeriod = 30;
constexpr int kCurseDamage = 15;  // DebuffCircle.cs
constexpr float kCurseRadius = 1.0F;
constexpr uint16_t kCursePeriod = 15;     // 0.5초
constexpr uint16_t kCurseZoneTicks = 90;  // [설계] 장판 지속 3초 (원본은 파티클 수명에 의존)
constexpr uint16_t kCurseDefDownTicks = 90;
constexpr float kCurseDefDown = 0.2F;
constexpr float kBuffAtk = 0.1F, kBuffCrit = 0.2F, kBuffDef = 0.1F;

}  // namespace lr
