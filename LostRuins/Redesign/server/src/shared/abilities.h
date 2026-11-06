// 스킬 "타이밍"의 공용 규칙. 서버(C++)와 클라(C#: Shared/Abilities.cs)가 똑같이 돌려서 예측한다.
//
// 여기에 두는 것 (= 입력만으로 결정되고, 클라가 미리 알아도 되는 것)
//   쿨타임, 선딜(windup) → 판정(active) → 후딜(recovery), 콤보 입력 예약, 충전, 이동 잠금·감속·돌진
// 두지 않는 것 (= 서버만 결정)
//   데미지, 명중, 회복량, 실드, 버프, 장판, 패시브 → game/classes.h, game/world.cpp
//
// 원본은 AnimationState 에셋의 비율(impactStartTime 0.15 같은 값)을 "애니메이션 길이 × 비율"로 바꿔
// 코루틴에서 기다렸다. 그래서 fps·애니메이터 전이에 따라 판정 시점이 흔들렸고, 서버가 검증할 수도 없었다.
// 재설계는 같은 비율을 30Hz 틱 수로 바꿔 표에 적는다. 애니메이션은 이 표를 "따라가는" 연출이 된다.
#pragma once
#include <array>
#include <cstdint>

#include "movement.h"

namespace lr {

enum class ClassId : uint8_t { kWarrior = 0, kArcher = 1, kLancer = 2, kHealer = 3 };
constexpr int kClassCount = 4;

enum Slot : uint8_t { kSlotBasic = 0, kSlotAlt = 1, kSlotQ = 2, kSlotE = 3, kSlotF = 4 };
constexpr uint8_t kSlotCount = 5;
constexpr uint8_t kNoSlot = 7;

enum Phase : uint8_t { kPhaseNone = 0, kPhaseCharge = 1, kPhaseWindup = 2, kPhaseActive = 3, kPhaseRecovery = 4 };
enum MoveRule : uint8_t { kMoveFree = 0, kMoveLocked = 1, kMoveSlowed = 2 };

// 한 동작의 프레임 데이터 (단위: 틱, 1틱 = 33ms)
struct Frames {
  uint8_t windup;  // 선딜: 누른 틱부터 판정 직전까지
  uint8_t active;  // 판정: 이 동안 맞을 수 있음 (다단 히트는 classes.h의 HitDef가 이 안을 나눠 씀)
  uint8_t recovery;  // 후딜: 판정이 끝나고 다음 행동이 가능해질 때까지
  uint8_t cancelAfter;  // 후딜이 이만큼 지난 뒤부터 이동·점프·구르기로 끊을 수 있음 (원본 breakAnimTime)
};

struct SkillTiming {
  uint8_t button;     // 0이면 이 슬롯에 스킬 없음
  uint16_t cooldown;  // 틱 (10비트로 전송하므로 1023 이하)
  uint8_t stepCount;  // 콤보 단계 수 (전사 기본 공격 3, 나머지 1)
  std::array<Frames, 3> steps;
  uint8_t chargeMin;  // 0이 아니면 "누르고 있다가 떼면" 발동. 이만큼 못 모으면 취소 (쿨타임 소모 없음)
  uint8_t chargeMax;
  float chargeSpeed;  // 충전 중 이동 속도 (m/s)
  MoveRule move;      // 선딜·판정·후딜 동안의 이동 규칙
  float castSpeed;    // kMoveSlowed일 때 속도
  float dashSpeed;    // 0이 아니면 판정 동안 "누른 순간의 이동 방향"으로 돌진
  bool needGround;
};

struct ClassTiming {
  std::array<SkillTiming, kSlotCount> skills;
  bool rightClickIsSkill;  // 창술사: 우클릭 = 방패 치기 (그 외 직업은 우클릭 = 조준 감속)
};

// 서버·클라가 매 틱 똑같이 갱신하는 스킬 상태. 스냅샷에 실려 재조정 때 비교된다.
struct AbilityState {
  uint8_t slot = kNoSlot;
  uint8_t phase = kPhaseNone;
  uint8_t tick = 0;
  uint8_t step = 0;
  uint8_t queued = 0;  // 콤보 입력 예약 (판정·후딜 중에 다시 누름)
  uint8_t charge = 0;
  int8_t lastX = 0, lastZ = 127;  // 마지막 이동 방향 → 돌진 방향
  int8_t dashX = 0, dashZ = 0;
  std::array<uint16_t, kSlotCount> cooldown{};
};

// 이번 틱의 결과: 이동 규칙 + "지금 판정 몇 번째 틱인가" (서버가 효과를 적용하는 기준)
struct AbilityResult {
  MoveMods mods;
  uint8_t slot = kNoSlot;
  uint8_t phase = kPhaseNone;
  uint8_t tick = 0;
  uint8_t step = 0;
  uint8_t charge = 0;
};

namespace detail {
inline void End(AbilityState& a) {
  a.slot = kNoSlot;
  a.phase = kPhaseNone;
  a.tick = 0;
  a.step = 0;
  a.queued = 0;
  a.charge = 0;
}
inline void Fire(AbilityState& a, const SkillTiming& s) {
  a.phase = kPhaseActive;
  a.tick = 1;
  if (s.chargeMax > 0) a.cooldown[a.slot] = s.cooldown;  // 충전 스킬은 실제로 쐈을 때 쿨타임 시작
}
inline void BeginWindup(AbilityState& a, const SkillTiming& s) {
  a.phase = kPhaseWindup;
  a.tick = 0;
  if (s.steps[a.step].windup == 0) Fire(a, s);
}
}  // namespace detail

// 한 틱 진행. mv는 이번 틱 이동(Step) "전" 상태여야 한다 (prevButtons로 새로 눌린 버튼을 계산).
inline AbilityResult StepAbility(AbilityState& a, const ClassTiming& c, const InputCmd& in, const MoveState& mv) {
  AbilityResult r;
  r.mods.noAim = c.rightClickIsSkill ? 1 : 0;
  const auto pressed = static_cast<uint8_t>(in.buttons & ~mv.prevButtons);
  const bool moving = in.moveX != 0 || in.moveZ != 0;
  if (moving) {
    a.lastX = in.moveX;
    a.lastZ = in.moveZ;
  }
  for (auto& cd : a.cooldown) {
    if (cd > 0) --cd;
  }

  if (a.slot != kNoSlot) {  // 진행 중인 동작
    const SkillTiming& s = c.skills[a.slot];
    const Frames& f = s.steps[a.step];
    const bool comboPress = (pressed & s.button) != 0 && a.step + 1 < s.stepCount;
    switch (a.phase) {
      case kPhaseCharge: {
        const bool dodged = (pressed & kDodge) != 0;
        if (!dodged && (in.buttons & s.button) != 0) {
          if (a.charge < s.chargeMax) ++a.charge;  // 누르고 있는 동안 충전
        } else if (!dodged && a.charge >= s.chargeMin) {
          detail::BeginWindup(a, s);  // 떼는 순간 발동
        } else {
          detail::End(a);  // 구르기를 눌렀거나 너무 일찍 뗌 → 취소 (구르기는 이 틱에 바로 나감)
        }
        break;
      }
      case kPhaseWindup:
        ++a.tick;
        if (a.tick >= f.windup) detail::Fire(a, s);
        break;
      case kPhaseActive:
        if (comboPress) a.queued = 1;
        if (a.tick >= f.active) {
          a.phase = kPhaseRecovery;
          a.tick = 0;
        } else {
          ++a.tick;
        }
        break;
      case kPhaseRecovery:
        if (comboPress) a.queued = 1;
        ++a.tick;
        if (a.queued != 0) {
          a.queued = 0;
          ++a.step;
          detail::BeginWindup(a, s);  // 다음 콤보 단계로 (후딜을 끝까지 기다리지 않음)
        } else if (a.tick > f.recovery || (a.tick > f.cancelAfter && (moving || (pressed & (kJump | kDodge)) != 0))) {
          detail::End(a);  // 후딜이 끝났거나, 캔슬 구간에서 이동·점프·구르기 → 이 틱에 바로 움직임
        }
        break;
      default:
        detail::End(a);
        break;
    }
  }

  if (a.slot == kNoSlot && mv.dodgeT == 0) {  // 새 동작 시작. 같은 틱에 여러 개 눌렸으면 F > E > Q > 우클릭 > 기본
    static constexpr std::array<uint8_t, kSlotCount> kOrder = {kSlotF, kSlotE, kSlotQ, kSlotAlt, kSlotBasic};
    for (const uint8_t slot : kOrder) {
      const SkillTiming& s = c.skills[slot];
      if (s.button == 0 || (pressed & s.button) == 0 || a.cooldown[slot] != 0) continue;
      if (s.needGround && mv.grounded == 0) continue;
      a.slot = slot;
      a.step = 0;
      a.queued = 0;
      a.charge = 0;
      a.tick = 0;
      a.dashX = a.lastX;
      a.dashZ = a.lastZ;
      if (s.chargeMax > 0) {
        a.phase = kPhaseCharge;
        a.charge = 1;  // 누른 틱도 1로 셈 → "N틱 누름 = 충전 N"
      } else {
        a.cooldown[slot] = s.cooldown;
        detail::BeginWindup(a, s);
      }
      break;
    }
  }

  if (a.slot != kNoSlot) {  // 지금 단계에 맞는 이동 규칙
    const SkillTiming& s = c.skills[a.slot];
    if (a.phase == kPhaseCharge) {
      r.mods.speed = s.chargeSpeed;
    } else if (s.dashSpeed > 0.0F && a.phase == kPhaseActive) {
      r.mods.dashSpeed = s.dashSpeed;
      r.mods.dashX = a.dashX;
      r.mods.dashZ = a.dashZ;
    } else if (s.move == kMoveLocked) {
      r.mods.lock = 1;
    } else if (s.move == kMoveSlowed) {
      r.mods.speed = s.castSpeed;
    }
  }
  r.slot = a.slot;
  r.phase = a.phase;
  r.tick = a.tick;
  r.step = a.step;
  r.charge = a.charge;
  return r;
}

// 클라 예측과 서버가 같이 부르는 "한 틱": 스킬 규칙 → 이동 규칙 순서
inline AbilityResult SimStep(MoveState& mv, AbilityState& ab, const ClassTiming& c, const InputCmd& in,
                             const Level& lv) {
  const AbilityResult r = StepAbility(ab, c, in, mv);
  Step(mv, in, lv, r.mods);
  return r;
}

inline bool AbilityEqual(const AbilityState& a, const AbilityState& b) {
  return a.slot == b.slot && a.phase == b.phase && a.tick == b.tick && a.step == b.step && a.queued == b.queued &&
         a.charge == b.charge && a.lastX == b.lastX && a.lastZ == b.lastZ && a.dashX == b.dashX && a.dashZ == b.dashZ &&
         a.cooldown == b.cooldown;
}

// ---------------- 직업별 타이밍 표 ----------------
// 쿨타임: 원본 State 에셋(초) × 30.
// 프레임: 원본 AnimationState 비율 × 30틱. 클립 길이를 알 수 없어 "1초 클립"으로 가정한 설계값이다.
//   선딜 = round(시작×30), 판정 = round(끝×30) − 선딜, 후딜 = 30 − round(끝×30), 캔슬 = round(break×30) − round(끝×30)
//   예) 전사 attack3 (0.10 / 0.26 / 0.75) → 선딜 3, 판정 5, 후딜 22, 캔슬 15
// 궁수는 원본에 AnimationState가 없어 전부 설계값.
constexpr Frames F(uint8_t w, uint8_t a, uint8_t r, uint8_t c) {
  return Frames{w, a, r, c};
}
constexpr SkillTiming None() {
  return SkillTiming{0, 0, 1, {}, 0, 0, 0.0F, kMoveFree, 0.0F, 0.0F, false};
}
constexpr SkillTiming Act(uint8_t button, uint16_t cd, Frames f, MoveRule move = kMoveLocked, bool ground = false) {
  return SkillTiming{button, cd, 1, {f, f, f}, 0, 0, 0.0F, move, 2.5F, 0.0F, ground};
}
constexpr SkillTiming Charged(uint8_t button, uint16_t cd, Frames f, uint8_t min, uint8_t max, float moveSpeed,
                              bool ground) {
  return SkillTiming{button, cd, 1, {f, f, f}, min, max, moveSpeed, kMoveFree, 0.0F, 0.0F, ground};
}

inline const ClassTiming& TimingOf(ClassId id) {
  static const std::array<ClassTiming, kClassCount> kTable = {{
      // 전사: 3단 콤보 / Q 번개검 / E 회전베기 / F 대검 내려치기. 쿨 7·5·15초
      {{{
           SkillTiming{kAttack,
                       0,
                       3,
                       {F(5, 1, 24, 15), F(5, 1, 24, 14), F(3, 5, 22, 15)},
                       0,
                       0,
                       0.0F,
                       kMoveLocked,
                       0.0F,
                       0.0F,
                       false},
           None(),
           Act(kSkillQ, 210, F(7, 1, 22, 15)),
           Act(kSkillE, 150, F(9, 3, 18, 14), kMoveLocked, true),
           Act(kSkillF, 450, F(14, 1, 15, 11), kMoveLocked, true),
       }},
       false},
      // 궁수: 기본 공격 = 누르고 있다가 떼서 발사 (0.4~0.8초 충전, 원본 30→70 at 50/s, 50 이상일 때 발사)
      //       Q 유도 화살 4발 / E 3갈래 / F 충전 폭발 화살 (0.5~1.4초 충전, 원본 30→100, 55 이상). 쿨 1·10·5·13초
      {{{
           Charged(kAttack, 30, F(0, 1, 6, 0), 12, 24, 2.0F, false),
           None(),
           Act(kSkillQ, 300, F(6, 1, 9, 3), kMoveSlowed),
           Act(kSkillE, 150, F(6, 1, 9, 3), kMoveSlowed),
           Charged(kSkillF, 390, F(0, 1, 10, 0), 15, 42, 1.0F, true),
       }},
       false},
      // 창술사: 찌르기 / 우클릭 방패 치기 / Q 아군 실드 / E 4연속 찌르기 / F 방패 돌진 4타. 쿨 10·5·13초
      {{{
           Act(kAttack, 0, F(11, 4, 15, 9), kMoveLocked, true),
           Act(kAim, 0, F(11, 4, 15, 11), kMoveLocked, true),
           Act(kSkillQ, 300, F(12, 3, 15, 15)),
           Act(kSkillE, 150, F(5, 14, 11, 6), kMoveLocked, true),
           SkillTiming{kSkillF, 390, 1, {F(6, 18, 6, 4), {}, {}}, 0, 0, 0.0F, kMoveLocked, 0.0F, 8.0F, true},
       }},
       true},
      // 힐러: 화염구 / Q 범위 회복 / E 저주 구슬 / F 범위 버프. 시전 중에는 천천히 걸을 수 있음. 쿨 13·5·15초
      {{{
           Act(kAttack, 0, F(15, 1, 14, 14), kMoveSlowed),
           None(),
           Act(kSkillQ, 390, F(15, 1, 14, 14), kMoveSlowed),
           Act(kSkillE, 150, F(19, 1, 10, 10), kMoveSlowed),
           Act(kSkillF, 450, F(23, 1, 6, 6), kMoveSlowed),
       }},
       false},
  }};
  return kTable[static_cast<size_t>(id)];
}

}  // namespace lr
