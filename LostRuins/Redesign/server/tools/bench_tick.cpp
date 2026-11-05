// 네트워크 없이 시뮬레이션 1틱 비용만 잰다. 플레이어 3명 + 몬스터 수를 늘려 가며 9000틱(5분) 실행.
#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <vector>

#include "../src/game/world.h"

int main() {
  for (const int monsters : {12, 50, 200, 1000}) {
    lr::World w(7, monsters, /*inputBufferTicks=*/0);
    uint16_t ids[3];
    for (auto& id : ids) id = w.AddPlayer();
    std::vector<float> us;
    us.reserve(9000);
    std::vector<lr::EntityState> entities;
    std::vector<lr::GameEvent> events;
    uint16_t seq = 0;
    for (uint32_t t = 0; t < 9000; ++t) {
      ++seq;
      for (int i = 0; i < 3; ++i) {
        lr::InputCmd in;
        in.seq = seq;
        const float a = static_cast<float>(t) * lr::kDt * 0.5F + static_cast<float>(i) * 2.1F;
        in.moveX = static_cast<int8_t>(std::lround(std::cos(a) * 127));
        in.moveZ = static_cast<int8_t>(std::lround(std::sin(a) * 127));
        in.yaw = static_cast<uint16_t>(t * 97 + static_cast<uint32_t>(i) * 20000);
        if (t % 18 == 0) in.buttons |= lr::kAttack;
        if (t % 180 == 11) in.buttons |= lr::kSkillE;
        if (t % 330 == 7) in.buttons |= lr::kSkillQ;
        w.PushInput(ids[i], in);
      }
      const auto t0 = std::chrono::steady_clock::now();
      w.Step();
      w.CollectEntities(entities);
      w.TakeEvents(events);
      us.push_back(std::chrono::duration<float, std::micro>(std::chrono::steady_clock::now() - t0).count());
    }
    double avg = 0;
    for (const float x : us) avg += x;
    avg /= static_cast<double>(us.size());
    std::sort(us.begin(), us.end());
    std::printf("{\"monsters\":%d,\"tick_us_avg\":%.2f,\"tick_us_p99\":%.2f,\"budget_share_pct\":%.3f}\n", monsters,
                avg, static_cast<double>(us[us.size() * 99 / 100]), avg / (1e6 / lr::kTickRate) * 100.0);
  }
  return 0;
}
