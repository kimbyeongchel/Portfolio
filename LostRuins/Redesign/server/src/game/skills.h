// 스킬은 클래스가 아니라 데이터 (F8). 기획자가 Unity ScriptableObject로 편집한 값을
// 빌드 때 JSON/CSV로 내보내 서버와 클라가 같은 표를 읽는 구조를 가정한다.
#pragma once
#include <cstdint>

#include "../shared/movement.h"

namespace lr {

struct SkillDef {  // 필드 순서는 패딩이 없도록 큰 것부터
  const char* name;
  float damageMul;
  float spreadDeg;         // 여러 발일 때 양쪽으로 벌어지는 각도
  float speed;             // m/s
  uint16_t cooldownTicks;  // 30틱 = 1초
  uint8_t button;          // 어떤 입력 비트가 눌렸을 때 발동하나
  uint8_t projectiles;
};

// 궁수 (Dealer.cs의 기본 / Q / E / F를 데이터로 옮긴 것)
inline constexpr SkillDef kArcherSkills[] = {
    // 이름, 데미지 배율, 퍼짐(도), 속도, 쿨타임(틱), 버튼, 화살 수
    {"Basic", 1.0F, 0.0F, 30.0F, 15, kAttack, 1},
    {"Q", 0.8F, 12.0F, 20.0F, 300, kSkillQ, 4},
    {"E", 0.9F, 15.0F, 30.0F, 150, kSkillE, 3},
    {"F", 3.0F, 0.0F, 40.0F, 600, kSkillF, 1},
};

struct ClassStats {
  int maxHp;
  float atk;
  float critChance;
  uint8_t passiveEvery;  // N번째 기본 공격은 확정 치명타 (0 = 없음)
};

inline constexpr ClassStats kArcherStats = {100, 20.0f, 0.1f, 3};

}  // namespace lr
