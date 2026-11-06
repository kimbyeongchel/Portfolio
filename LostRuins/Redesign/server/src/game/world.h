// 게임 규칙의 유일한 원본. Unity도 소켓도 모르는 순수 시뮬레이션이라 단위 테스트와 벤치마크가 쉽다.
#pragma once
#include <array>
#include <cstdint>
#include <deque>
#include <vector>

#include "../shared/protocol.h"
#include "classes.h"

namespace lr {

class Rng {  // xorshift64*: 서버만 굴리는 난수 (데미지 편차, 치명타, 스폰 위치)
 public:
  explicit Rng(uint64_t seed) : s_(seed != 0 ? seed : 0x9E3779B97F4A7C15ULL) {}
  uint32_t Next() {
    s_ ^= s_ >> 12U;
    s_ ^= s_ << 25U;
    s_ ^= s_ >> 27U;
    return static_cast<uint32_t>((s_ * 0x2545F4914F6CDD1DULL) >> 32U);
  }
  float Uniform() { return static_cast<float>(Next() >> 8U) * (1.0F / 16777216.0F); }  // [0, 1)
  // 나머지 연산이라 아주 약간 치우침이 있지만(최대 2^-32 수준) 게임 난수로는 무시 가능
  int Range(int lo, int hi) { return lo + static_cast<int>(Next() % static_cast<uint32_t>(hi - lo + 1)); }

 private:
  uint64_t s_;
};

enum class Action : uint8_t { kIdle = 0, kMove = 1, kJump = 2, kDodge = 3, kAttack = 4, kDead = 5, kCharge = 6 };

struct Player {
  uint16_t id = 0;
  ClassId cls = ClassId::kArcher;
  MoveState mv;     // ┐ 클라도 똑같이 계산하는 부분 (예측 + 재조정 대상)
  AbilityState ab;  // ┘
  int hp = 100;
  bool alive = true;
  uint16_t respawnTimer = 0;
  bool started = false;   // 입력 버퍼가 찰 때까지 시뮬레이션하지 않음 (접속 직후, 부활 직후)
  bool anyInput = false;  // 입력을 하나라도 처리했나 → 이후로는 lastSeq보다 옛 번호를 거름
  uint16_t lastSeq = 0;   // 마지막으로 처리한 입력 번호 → 스냅샷에 실어 재조정 기준
  InputCmd lastInput;
  std::deque<InputCmd> queue;
  // ---- 서버만 아는 전투 상태 (클라는 이벤트·스냅샷으로 결과만 받음) ----
  int shield = 0;
  uint16_t shieldTicks = 0;
  uint16_t buffTicks = 0;       // 남은 시간만 저장 → 수치는 매번 기본값 + 버프로 다시 계산
  uint16_t passiveTimer = 0;    // 창술사: 강화 공격 충전, 힐러: 마지막 피격 이후 시간
  uint8_t basicCount = 0;       // 궁수: 기본 공격 횟수
  bool empoweredSwing = false;  // 창술사: 지금 휘두르는 공격이 강화 공격인가
  std::vector<uint32_t> struck;  // 이번 동작에서 이미 맞은 (판정 번호 << 16 | 대상) → 같은 대상 중복 히트 방지
};

struct Monster {
  enum class State : uint8_t { kIdle, kChase, kWindup, kRecover, kDead };
  uint16_t id = 0;
  float x = 0, y = 0, z = 0, yaw = 0;
  int hp = 0, maxHp = 120;
  State state = State::kIdle;
  uint16_t timer = 0;
  uint16_t target = 0;
  uint16_t defDownTicks = 0;  // 저주: 받는 피해 증가 (중첩 대신 시간 갱신)
  uint8_t burnLeft = 0;       // 화상: 남은 횟수 (중첩 대신 횟수 갱신)
  uint16_t burnTimer = 0;
  uint16_t burnOwner = 0;
};

struct Projectile {
  uint16_t id = 0, owner = 0;
  float x = 0, y = 0, z = 0, vx = 0, vy = 0, vz = 0;
  uint16_t ttl = 0, age = 0;
  int damage = 0;  // 쏘는 순간 굴려 둠 (맞는 순간의 버프가 아니라 쏜 순간의 공격력)
  uint8_t crit = 0;
  ClassId cls = ClassId::kArcher;
  uint8_t slot = 0;  // 클라가 어떤 모양(화살·번개검·화염구…)으로 그릴지 고르는 데 사용
  const ShotDef* def = nullptr;
};

struct Zone {  // 힐러 E 저주 장판
  uint16_t id = 0, owner = 0;
  float x = 0, y = 0, z = 0;
  uint16_t ttl = 0, timer = 0;
};

struct CombatStats {  // 버프·패시브를 반영한 "지금" 수치
  float atk, def, crit;
};

struct WorldStats {
  uint64_t shots = 0, hits = 0, kills = 0, playerDowns = 0, heals = 0, blocks = 0;
  uint64_t repeatedInputs = 0;  // 입력이 제때 안 와서 직전 입력을 반복한 횟수 (재조정의 원인)
  uint64_t droppedInputs = 0;   // 큐가 넘쳐서 버린 입력
};

// 엔티티 번호 구간. 플레이어·몬스터는 고정 번호, 화살은 1024부터 돌려 쓴다.
// (하나의 카운터를 같이 쓰면 화살 6만여 발 뒤에 번호가 한 바퀴 돌아 몬스터 번호와 겹친다)
constexpr uint16_t kFirstStaticId = 1;
constexpr uint16_t kFirstDynamicId = 1024;

class World {
 public:
  // inputBufferTicks: 서버가 처리 전에 모아 둘 입력 수. 0 = 즉시 처리, 1 = 33ms 여유
  World(uint64_t seed, int monsterCount, int inputBufferTicks = 1);
  uint16_t AddPlayer(ClassId cls = ClassId::kArcher);
  void RemovePlayer(uint16_t id);
  void PushInput(uint16_t playerId, const InputCmd& in);
  void Step();
  void TakeEvents(std::vector<GameEvent>& out);               // out을 비우고 이번 틱 이벤트를 채움
  void CollectEntities(std::vector<EntityState>& out) const;  // out을 재사용 (틱마다 할당하지 않음)
  const Player* FindPlayer(uint16_t id) const;
  uint32_t Tick() const { return tick_; }
  const Level& GetLevel() const { return level_; }
  size_t MonsterCount() const { return monsters_.size(); }
  // 읽기 전용 (테스트·디버그용)
  const std::vector<Projectile>& Projectiles() const { return projectiles_; }
  const std::vector<Monster>& Monsters() const { return monsters_; }
  const std::vector<Zone>& Zones() const { return zones_; }
  CombatStats StatsOf(const Player& p) const;
  static bool PassiveReady(const Player& p);  // HUD 표시용
  // 테스트용: 몬스터를 원하는 위치에 두고 AI를 멈춤 / 플레이어 체력 설정
  void PlaceMonster(size_t index, float x, float z, int hp);
  void SetPlayerHp(uint16_t id, int hp);
  void HurtPlayer(uint16_t id, int dmg) {
    if (Player* p = FindPlayerMut(id)) DamagePlayer(*p, dmg, 0);
  }
  static uint16_t NextIdAfter(uint16_t id);  // 화살 번호 순환 규칙
  WorldStats stats;

 private:
  Player* FindPlayerMut(uint16_t id);
  void StepPlayer(Player& p);
  void TickStatus(Player& p);
  void ApplyAction(Player& p, const AbilityResult& r, uint16_t yaw);
  void Activate(Player& p, const AbilityResult& r, const SkillEffect& e, uint16_t yaw);
  void ApplyHit(Player& p, const HitDef& h, uint8_t index, uint16_t yaw);
  void Shoot(Player& p, const AbilityResult& r, const ShotDef& s, uint16_t yaw);
  void ApplySupport(Player& p, const SupportDef& s);
  int RollDamage(const Player& p, uint8_t dmgIndex, bool forceCrit, uint8_t& crit);
  void DamageMonster(Monster& m, int dmg, uint16_t src, uint8_t crit);
  void ProjectileEnds(const Projectile& pr, bool hitSomething);
  void StepProjectiles();
  void StepZones();
  void StepMonsters();
  void DamagePlayer(Player& p, int dmg, uint16_t source);
  void SpawnMonster(Monster& m);
  void Emit(EventType type, uint16_t src, uint16_t dst, int dmg, uint8_t crit, float x, float y, float z);
  uint16_t NextDynamicId();

  Rng rng_;
  Level level_;
  int inputBuffer_;
  uint32_t tick_ = 0;
  uint16_t nextStaticId_ = kFirstStaticId;
  uint16_t nextDynamicId_ = kFirstDynamicId;
  uint16_t nextEventId_ = 1;
  std::vector<Player> players_;
  std::vector<Monster> monsters_;
  std::vector<Projectile> projectiles_;
  std::vector<Zone> zones_;
  bool frozenMonsters_ = false;
  std::vector<GameEvent> events_;
};

}  // namespace lr
