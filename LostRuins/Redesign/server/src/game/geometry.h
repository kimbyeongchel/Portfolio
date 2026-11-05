// 판정에 쓰는 기하 함수. world.cpp 안에 숨겨 두지 않고 꺼내 둔 이유: 단위 테스트에서 직접 검사하기 위해.
#pragma once
#include <algorithm>
#include <cmath>

namespace lr {

inline float DistXZ(float ax, float az, float bx, float bz) {
  const float dx = ax - bx;
  const float dz = az - bz;
  return std::sqrt(dx * dx + dz * dz);
}

// 선분 AB(이번 틱 화살 이동 경로)와 구(중심 C, 반지름 r)가 닿는지.
// 끝점만 검사하면 30m/s 화살은 틱마다 1m씩 건너뛰어 반지름 0.6m 몬스터를 뚫고 지나갈 수 있다(터널링).
//   1) 선분 위에서 C에 가장 가까운 점 P = A + t·(B−A)를 구한다. t = (C−A)·(B−A) / |B−A|², 0..1로 자름
//   2) |P − C| ≤ r 이면 닿음
inline bool SegmentHitsSphere(float ax, float ay, float az, float bx, float by, float bz, float cx, float cy, float cz,
                              float r) {
  const float dx = bx - ax;
  const float dy = by - ay;
  const float dz = bz - az;
  const float len2 = dx * dx + dy * dy + dz * dz;
  float t = len2 > 0.0F ? ((cx - ax) * dx + (cy - ay) * dy + (cz - az) * dz) / len2 : 0.0F;
  t = std::clamp(t, 0.0F, 1.0F);
  const float px = ax + dx * t - cx;
  const float py = ay + dy * t - cy;
  const float pz = az + dz * t - cz;
  return px * px + py * py + pz * pz <= r * r;
}

}  // namespace lr
