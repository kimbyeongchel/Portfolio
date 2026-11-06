// 서버와 클라이언트(C#: Shared/Movement.cs)가 "비트 단위로 같은 결과"를 내야 하는 이동 규칙.
//
// 결정론(같은 입력 → 같은 결과)을 지키는 규칙
//   1. 삼각함수 금지: 기기·언어마다 결과가 다를 수 있다. 방향은 클라가 월드 좌표로 양자화해서 보낸다.
//   2. 타이머는 float 초가 아니라 정수 틱.
//   3. 연산 순서를 C#과 한 줄씩 맞춘다. (a*b)+c 와 a*(b+c)는 float에서 결과가 다르다.
//   4. 컴파일 옵션 -ffp-contract=off: a*b+c 를 FMA 한 번으로 합치면 반올림이 한 번 줄어 C#과 달라진다.
#pragma once
#include <cmath>
#include <cstdint>
#include <cstring>
#include <vector>

namespace lr {

constexpr int kTickRate = 30;
constexpr float kDt = 0.0333333351F;  // 1/30 을 float 로 반올림한 값 (C#과 같은 리터럴)
constexpr float kMoveSpeed = 5.0F;
constexpr float kSprintSpeed = 10.0F;
constexpr float kAimSpeed = 2.0F;
constexpr float kDodgeSpeed = 12.0F;
constexpr float kGravity = 20.0F;
constexpr float kJumpVelocity = 7.07106781F;  // sqrt(2 * g * 1.25m) → 1.25m 높이
constexpr uint8_t kCoyoteTicks = 3;           // 발판을 벗어난 뒤에도 0.1초간 점프 허용
constexpr uint8_t kJumpBufferTicks = 3;       // 착지 0.1초 전에 누른 점프도 인정
constexpr uint8_t kDodgeTicks = 10;
constexpr uint8_t kDodgeCooldownTicks = 30;
constexpr float kRadius = 0.4F;
constexpr float kHeight = 1.8F;

enum Button : uint8_t {
  kJump = 1,
  kDodge = 2,
  kSprint = 4,
  kAttack = 8,
  kAim = 16,
  kSkillQ = 32,
  kSkillE = 64,
  kSkillF = 128,
};

// 클라 → 서버로 가는 유일한 게임플레이 데이터. 판정 결과는 절대 보내지 않는다.
struct InputCmd {
  uint16_t seq = 0;
  int8_t moveX = 0;  // 카메라 기준으로 이미 회전된 월드 방향, -127..127
  int8_t moveZ = 0;
  uint16_t yaw = 0;     // 조준 방향 0..65535 (서버가 발사 방향 계산에 사용)
  uint8_t buttons = 0;  // 누르고 있는 버튼 비트. 눌림/뗌은 직전 입력과 비교해 계산
};

// 네트워크에서 온 입력은 믿지 않는다. -128은 정상 클라가 만들지 않는 값.
inline void Sanitize(InputCmd& in) {
  if (in.moveX < -127) in.moveX = -127;
  if (in.moveZ < -127) in.moveZ = -127;
}

struct Box {
  float minX, minY, minZ, maxX, maxY, maxZ;
};
struct Level {
  std::vector<Box> boxes;
};

// 스킬이 이번 틱 이동에 주는 영향. abilities.h의 StepAbility가 만들고 Step이 읽는다. 기본값 = 영향 없음.
struct MoveMods {
  uint8_t lock = 0;    // 선딜·후딜: 이동·점프·구르기 금지
  uint8_t noAim = 0;   // 우클릭이 조준(감속)이 아닌 직업 (창술사: 우클릭 = 방패 치기)
  float speed = 0.0F;  // 0보다 크면 이 속도로만 이동 (충전·시전 중 감속). 점프·구르기 금지
  float dashSpeed = 0.0F;  // 0보다 크면 dash 방향으로 강제 전진 (창술사 F)
  int8_t dashX = 0, dashZ = 0;
};

// bool 10개 대신: 동작은 타이머 값으로, 조건은 grounded 하나로 (1편 F7)
struct MoveState {
  float px = 0, py = 0, pz = 0;
  float vx = 0, vy = 0, vz = 0;
  uint8_t grounded = 0, coyote = 0, jumpBuf = 0, dodgeT = 0, dodgeCd = 0, prevButtons = 0;
  int8_t dodgeX = 0, dodgeZ = 0;
};

inline uint32_t FloatBits(float f) {
  uint32_t u = 0;
  std::memcpy(&u, &f, sizeof(u));
  return u;
}

// 재조정 판단용: 필드마다 비트 그대로 비교한다 (-0.0과 +0.0도 다르게 본다. 의도된 동작).
inline bool BitwiseEqual(const MoveState& a, const MoveState& b) {
  return FloatBits(a.px) == FloatBits(b.px) && FloatBits(a.py) == FloatBits(b.py) &&
         FloatBits(a.pz) == FloatBits(b.pz) && FloatBits(a.vx) == FloatBits(b.vx) &&
         FloatBits(a.vy) == FloatBits(b.vy) && FloatBits(a.vz) == FloatBits(b.vz) && a.grounded == b.grounded &&
         a.coyote == b.coyote && a.jumpBuf == b.jumpBuf && a.dodgeT == b.dodgeT && a.dodgeCd == b.dodgeCd &&
         a.prevButtons == b.prevButtons && a.dodgeX == b.dodgeX && a.dodgeZ == b.dodgeZ;
}

inline bool Overlaps(const Box& b, float x, float y, float z) {
  return x + kRadius > b.minX && x - kRadius < b.maxX && z + kRadius > b.minZ && z - kRadius < b.maxZ &&
         y + kHeight > b.minY && y < b.maxY;
}

// 한 틱 이동. Rigidbody를 쓰지 않고 여기 한 곳에서만 위치와 속도를 바꾼다 (1편 F4).
// m: 스킬 사용 중의 이동 제한. 스킬 코드가 velocity를 직접 건드리지 않고 "규칙"만 넘긴다.
inline void Step(MoveState& s, const InputCmd& in, const Level& lv, const MoveMods& m = MoveMods{}) {
  const auto pressed = static_cast<uint8_t>(in.buttons & ~s.prevButtons);
  s.prevButtons = in.buttons;
  const bool busy = m.lock != 0 || m.speed > 0.0F || m.dashSpeed > 0.0F;  // 스킬 중: 점프·구르기 시작 금지

  if ((pressed & kJump) != 0) {
    s.jumpBuf = kJumpBufferTicks;
  } else if (s.jumpBuf > 0) {
    s.jumpBuf--;
  }
  if (s.grounded != 0) {
    s.coyote = kCoyoteTicks;
  } else if (s.coyote > 0) {
    s.coyote--;
  }
  if (s.dodgeCd > 0) s.dodgeCd--;

  if ((pressed & kDodge) != 0 && !busy && s.grounded != 0 && s.dodgeT == 0 && s.dodgeCd == 0 &&
      (in.moveX != 0 || in.moveZ != 0)) {
    s.dodgeT = kDodgeTicks;
    s.dodgeCd = kDodgeCooldownTicks;
    s.dodgeX = in.moveX;
    s.dodgeZ = in.moveZ;
  }

  float wx = 0;
  float wz = 0;
  float speed = 0;
  if (s.dodgeT > 0) {
    wx = static_cast<float>(s.dodgeX) / 127.0F;
    wz = static_cast<float>(s.dodgeZ) / 127.0F;
    speed = kDodgeSpeed;
    s.dodgeT--;
  } else if (m.dashSpeed > 0.0F) {
    wx = static_cast<float>(m.dashX) / 127.0F;
    wz = static_cast<float>(m.dashZ) / 127.0F;
    speed = m.dashSpeed;
  } else if (m.lock != 0) {
    speed = 0.0F;  // 제자리 (공중이면 수직으로만 떨어짐)
  } else {
    wx = static_cast<float>(in.moveX) / 127.0F;
    wz = static_cast<float>(in.moveZ) / 127.0F;
    if (m.speed > 0.0F) {
      speed = m.speed;
    } else if ((in.buttons & kAim) != 0 && m.noAim == 0) {
      speed = kAimSpeed;
    } else {
      speed = (in.buttons & kSprint) != 0 ? kSprintSpeed : kMoveSpeed;
    }
  }
  const float len2 = wx * wx + wz * wz;
  if (len2 > 1.0F) {
    const float inv = 1.0F / std::sqrt(len2);
    wx *= inv;
    wz *= inv;
  }
  s.vx = wx * speed;
  s.vz = wz * speed;

  if (s.jumpBuf > 0 && s.coyote > 0 && s.dodgeT == 0 &&
      !busy) {  // 입력한 틱에 바로 이륙 (후딜 중 누른 점프는 버퍼에 남음)
    s.vy = kJumpVelocity;
    s.jumpBuf = 0;
    s.coyote = 0;
  }
  s.vy -= kGravity * kDt;

  // 축마다 따로 이동하고, 겹치면 상자 밖으로 밀어낸다 (벽을 따라 미끄러지는 효과)
  float nx = s.px + s.vx * kDt;
  for (const Box& b : lv.boxes) {
    if (Overlaps(b, nx, s.py, s.pz)) nx = s.vx > 0.0F ? b.minX - kRadius : b.maxX + kRadius;
  }
  s.px = nx;

  float nz = s.pz + s.vz * kDt;
  for (const Box& b : lv.boxes) {
    if (Overlaps(b, s.px, s.py, nz)) nz = s.vz > 0.0F ? b.minZ - kRadius : b.maxZ + kRadius;
  }
  s.pz = nz;

  float ny = s.py + s.vy * kDt;
  s.grounded = 0;
  if (ny <= 0.0F) {
    ny = 0.0F;
    s.vy = 0.0F;
    s.grounded = 1;
  }
  for (const Box& b : lv.boxes) {
    if (Overlaps(b, s.px, ny, s.pz)) {
      if (s.vy <= 0.0F) {
        ny = b.maxY;  // 위에서 떨어지면 착지
        s.grounded = 1;
      } else {
        ny = b.minY - kHeight;  // 아래에서 올라가면 머리 부딪힘
      }
      s.vy = 0.0F;
    }
  }
  s.py = ny;
}

inline Level DefaultLevel() {
  return Level{{
      {4.0F, 0.0F, 4.0F, 7.0F, 1.0F, 7.0F},
      {-8.0F, 0.0F, -2.0F, -5.0F, 2.0F, 6.0F},
      {10.0F, 0.0F, -10.0F, 14.0F, 0.5F, -6.0F},
      {-3.0F, 0.0F, -12.0F, 3.0F, 3.0F, -10.0F},
  }};
}

}  // namespace lr
