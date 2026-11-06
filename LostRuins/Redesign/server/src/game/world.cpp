#include "world.h"

#include <algorithm>
#include <cmath>

#include "geometry.h"

namespace lr {
namespace {
constexpr float kPi = 3.14159265F;
constexpr float kMonsterSpeed = 3.5F;
constexpr float kMonsterRadius = 0.6F;
constexpr float kAggroRange = 20.0F;
constexpr float kLeashRange = 25.0F;
constexpr float kMonsterReach = 1.6F;
constexpr float kMonsterHitRange = 2.2F;
constexpr int kMonsterDamage = 8;
constexpr uint16_t kWindupTicks = 12;
constexpr uint16_t kRecoverTicks = 20;
constexpr uint16_t kMonsterRespawnTicks = 60;
constexpr uint16_t kPlayerRespawnTicks = 90;
constexpr size_t kMaxQueuedInputs = 6;

float YawRad(uint16_t yaw) {
  return static_cast<float>(yaw) * (2.0F * kPi / 65536.0F);
}

int RageStacks(int hp) {  // 원본 Warrior.passive(): 70 / 40 / 10 경계
  if (hp >= 70) return 0;
  if (hp >= 40) return 1;
  if (hp >= 10) return 2;
  return 3;
}
}  // namespace

World::World(uint64_t seed, int monsterCount, int inputBufferTicks)
    : rng_(seed), level_(DefaultLevel()), inputBuffer_(std::max(0, inputBufferTicks)) {
  monsters_.resize(static_cast<size_t>(monsterCount));
  for (auto& m : monsters_) {
    m.id = nextStaticId_++;
    SpawnMonster(m);
  }
}

uint16_t World::NextIdAfter(uint16_t id) {
  return id == 65535 ? kFirstDynamicId : static_cast<uint16_t>(id + 1);
}

uint16_t World::NextDynamicId() {
  const uint16_t id = nextDynamicId_;
  nextDynamicId_ = NextIdAfter(nextDynamicId_);
  return id;
}

uint16_t World::AddPlayer(ClassId cls) {
  Player p;
  p.id = nextStaticId_++;
  p.cls = cls;
  p.hp = ClassOf(cls).maxHp;
  p.mv.px = static_cast<float>(players_.size()) * 2.0F;
  players_.push_back(p);
  return p.id;
}

void World::RemovePlayer(uint16_t id) {
  players_.erase(std::remove_if(players_.begin(), players_.end(), [&](const Player& p) { return p.id == id; }),
                 players_.end());
}

Player* World::FindPlayerMut(uint16_t id) {
  for (auto& p : players_) {
    if (p.id == id) return &p;
  }
  return nullptr;
}
const Player* World::FindPlayer(uint16_t id) const {
  for (const auto& p : players_) {
    if (p.id == id) return &p;
  }
  return nullptr;
}

void World::PlaceMonster(size_t index, float x, float z, int hp) {
  Monster& m = monsters_.at(index);
  m.x = x;
  m.z = z;
  m.hp = hp;
  m.maxHp = hp;
  m.state = Monster::State::kIdle;
  frozenMonsters_ = true;  // 테스트에서 위치가 바뀌지 않도록 AI를 멈춘다
}

void World::SetPlayerHp(uint16_t id, int hp) {
  if (Player* p = FindPlayerMut(id)) p->hp = hp;
}

// 같은 입력이 최대 4번 중복 도착하므로 번호로 거르고, 번호 순서대로 끼워 넣는다
void World::PushInput(uint16_t playerId, const InputCmd& in) {
  Player* p = FindPlayerMut(playerId);
  if (p == nullptr) return;
  if (p->anyInput && !SeqNewer(in.seq, p->lastSeq)) return;  // 이미 처리한 번호
  for (const auto& q : p->queue) {
    if (q.seq == in.seq) return;
  }
  auto it = std::find_if(p->queue.begin(), p->queue.end(), [&](const InputCmd& q) { return SeqNewer(q.seq, in.seq); });
  p->queue.insert(it, in);
  while (p->queue.size() > kMaxQueuedInputs) {  // 클라가 너무 앞서가면 오래된 것부터 버림
    p->queue.pop_front();
    stats.droppedInputs++;
  }
}

void World::Step() {
  for (auto& p : players_) StepPlayer(p);
  StepProjectiles();
  StepZones();
  StepMonsters();
  ++tick_;
}

void World::StepPlayer(Player& p) {
  const ClassDef& def = ClassOf(p.cls);
  if (!p.alive) {  // 죽어 있는 동안에도 입력 번호는 소비 (재조정 기준점 유지)
    while (!p.queue.empty()) {
      p.lastInput = p.queue.front();
      p.lastSeq = p.lastInput.seq;
      p.anyInput = true;
      p.queue.pop_front();
    }
    if (p.respawnTimer > 0) {
      --p.respawnTimer;
      if (p.respawnTimer == 0) {  // 부활: 위치·스킬·버프를 모두 처음 상태로 (빼기로 되돌리지 않음)
        const uint16_t id = p.id;
        const ClassId cls = p.cls;
        const uint16_t seq = p.lastSeq;
        const InputCmd last = p.lastInput;
        p = Player{};
        p.id = id;
        p.cls = cls;
        p.hp = def.maxHp;
        p.started = false;  // 접속 직후처럼 입력 버퍼가 다시 찰 때까지 기다림 (옛 입력을 새 위치에 반복하지 않게)
        p.anyInput = true;
        p.lastSeq = seq;
        p.lastInput = last;
      }
    }
    return;
  }
  // 입력 지터 버퍼: 처음 시작할 때 입력이 (1 + 버퍼) 개 모일 때까지 기다린다.
  if (!p.started) {
    if (p.queue.size() < 1U + static_cast<size_t>(inputBuffer_)) return;
    p.started = true;
  }
  InputCmd in;
  if (!p.queue.empty()) {
    in = p.queue.front();
    p.queue.pop_front();
    p.lastSeq = in.seq;
    p.lastInput = in;
    p.anyInput = true;
  } else {
    in = p.lastInput;  // 입력이 아직 안 왔으면 직전 입력 반복 (클라가 재조정으로 맞춤)
    in.seq = p.lastSeq;
    stats.repeatedInputs++;
  }
  // 클라 예측과 같은 함수: 스킬 타이밍 → 이동
  const AbilityResult r = SimStep(p.mv, p.ab, TimingOf(p.cls), in, level_);
  TickStatus(p);
  if (r.phase == kPhaseActive) ApplyAction(p, r, in.yaw);
}

// 지속 효과는 "남은 틱"만 줄인다. 수치는 StatsOf가 매번 기본값에서 다시 계산한다.
void World::TickStatus(Player& p) {
  const ClassDef& def = ClassOf(p.cls);
  if (p.shieldTicks > 0) {
    --p.shieldTicks;
    if (p.shieldTicks == 0) p.shield = 0;  // 시간이 다 되면 남은 실드는 사라짐
  }
  if (p.buffTicks > 0) --p.buffTicks;
  if ((def.passive == Passive::kEmpowered || def.passive == Passive::kBarrier) && p.passiveTimer < def.passiveTicks) {
    ++p.passiveTimer;
  }
}

CombatStats World::StatsOf(const Player& p) const {
  const ClassDef& def = ClassOf(p.cls);
  CombatStats s{def.atk, def.def, def.crit};
  if (def.passive == Passive::kRage) {
    const auto stacks = static_cast<float>(RageStacks(p.hp));
    s.atk += stacks * def.ragePerStack;
    s.def += stacks * def.ragePerStack;
  }
  if (p.buffTicks > 0) {
    s.atk += kBuffAtk;
    s.crit += kBuffCrit;
    s.def += kBuffDef;
  }
  s.def = std::min(s.def, 0.9F);  // 방어력 상한: 버프가 겹쳐도 무적이 되지 않게
  return s;
}

bool World::PassiveReady(const Player& p) {
  const ClassDef& def = ClassOf(p.cls);
  switch (def.passive) {
    case Passive::kRage:
      return RageStacks(p.hp) > 0;
    case Passive::kThirdShot:
      return p.basicCount == 2;  // 다음 기본 공격이 강화 화살
    case Passive::kEmpowered:
    case Passive::kBarrier:
      return p.passiveTimer >= def.passiveTicks;
    case Passive::kNone:
      break;
  }
  return false;
}

// 원본 PlayerState.set_damage와 같은 공식: floor(damage[i] × atk) + [-5, +4], 치명타 ×1.7
int World::RollDamage(const Player& p, uint8_t dmgIndex, bool forceCrit, uint8_t& crit) {
  const ClassDef& def = ClassOf(p.cls);
  const CombatStats s = StatsOf(p);
  const auto base = static_cast<float>(def.damage[std::min<size_t>(dmgIndex, def.damage.size() - 1)]);
  int dmg = static_cast<int>(std::floor(base * s.atk)) + rng_.Range(-5, 4);  // 원본 Random.Range(a-5, a+5)는 끝 미포함
  crit = (forceCrit || rng_.Uniform() < s.crit) ? 1 : 0;
  if (crit != 0) dmg = static_cast<int>(std::floor(static_cast<float>(dmg) * 1.7F));
  return std::max(dmg, 1);
}

// 판정 단계의 매 틱 호출. tick 1 = 판정이 막 시작된 틱
void World::ApplyAction(Player& p, const AbilityResult& r, uint16_t yaw) {
  const SkillEffect& e = ClassOf(p.cls).skills[r.slot][r.step];
  if (r.tick == 1) Activate(p, r, e, yaw);
  for (size_t i = 0; i < e.hits.size(); ++i) {
    const HitDef& h = e.hits[i];
    if (h.from != 0 && r.tick >= h.from && r.tick <= h.to) ApplyHit(p, h, static_cast<uint8_t>(i), yaw);
  }
}

void World::Activate(Player& p, const AbilityResult& r, const SkillEffect& e, uint16_t yaw) {
  const ClassDef& def = ClassOf(p.cls);
  p.struck.clear();
  p.empoweredSwing = false;
  if (r.slot == kSlotBasic && def.passive == Passive::kEmpowered && p.passiveTimer >= def.passiveTicks) {
    p.empoweredSwing = true;  // 8초 충전된 상태에서 휘두른 기본 공격
    p.passiveTimer = 0;
  }
  if (e.shot.count > 0) Shoot(p, r, e.shot, yaw);
  if (e.support.kind != Support::kNone) ApplySupport(p, e.support);
}

void World::ApplyHit(Player& p, const HitDef& h, uint8_t index, uint16_t yaw) {
  const ClassDef& def = ClassOf(p.cls);
  const float a = YawRad(yaw);
  const float fx = std::sin(a);
  const float fz = std::cos(a);
  const float cosHalf = std::cos(h.halfAngleDeg * (kPi / 180.0F));
  const float cx = p.mv.px + fx * h.forward;
  const float cz = p.mv.pz + fz * h.forward;
  const uint8_t dmgIndex = p.empoweredSwing ? def.passiveDmgIndex : h.dmgIndex;
  for (auto& m : monsters_) {
    if (m.state == Monster::State::kDead) continue;
    const uint32_t key = (static_cast<uint32_t>(index) << 16U) | m.id;
    if (std::find(p.struck.begin(), p.struck.end(), key) != p.struck.end()) continue;  // 이 판정에 이미 맞음
    const float dx = m.x - cx;
    const float dz = m.z - cz;
    const float d = std::sqrt(dx * dx + dz * dz);
    if (d > h.range + kMonsterRadius) continue;
    if (h.shape == Shape::kArc && d > 0.001F && (dx * fx + dz * fz) / d < cosHalf) continue;  // 부채꼴 밖
    if (std::abs(m.y - p.mv.py) > 2.5F) continue;  // 높이 차이가 크면 안 맞음
    p.struck.push_back(key);
    uint8_t crit = 0;
    const int dmg = RollDamage(p, dmgIndex, false, crit);  // 대상마다, 타마다 따로 굴림
    DamageMonster(m, dmg, p.id, crit);
  }
}

void World::Shoot(Player& p, const AbilityResult& r, const ShotDef& s, uint16_t yaw) {
  const ClassDef& def = ClassOf(p.cls);
  bool forceCrit = false;
  uint8_t dmgIndex = s.dmgIndex;
  if (s.forceCritOnPassive && def.passive == Passive::kThirdShot) {
    p.basicCount = static_cast<uint8_t>((p.basicCount + 1) % 3);
    if (p.basicCount == 0) {  // 3번째 화살 "그 자체"가 강화 (원본은 4번째에 적용되던 버그)
      forceCrit = true;
      dmgIndex = 0;
    }
  }
  float speed = s.speed;
  const SkillTiming& t = TimingOf(p.cls).skills[r.slot];
  if (t.chargeMax > t.chargeMin) {  // 충전량에 비례해 빨라짐
    const float k = static_cast<float>(r.charge - t.chargeMin) / static_cast<float>(t.chargeMax - t.chargeMin);
    speed = s.speed + (s.speedMax - s.speed) * std::clamp(k, 0.0F, 1.0F);
  }
  const float base = YawRad(yaw);
  for (int i = 0; i < s.count; ++i) {
    const float off = s.count > 1 ? (static_cast<float>(i) / static_cast<float>(s.count - 1) * 2.0F - 1.0F) *
                                        s.spreadDeg * (kPi / 180.0F)
                                  : 0.0F;
    const float a = base + off;
    Projectile pr;
    pr.id = NextDynamicId();
    pr.owner = p.id;
    pr.cls = p.cls;
    pr.slot = r.slot;
    pr.def = &s;
    pr.vx = std::sin(a) * speed;
    pr.vz = std::cos(a) * speed;
    pr.x = p.mv.px + std::sin(a) * 0.6F;
    pr.y = p.mv.py + 1.4F;
    pr.z = p.mv.pz + std::cos(a) * 0.6F;
    pr.ttl = s.ttl;
    pr.damage = s.dmgIndex == kNoDamage ? 0 : RollDamage(p, dmgIndex, forceCrit, pr.crit);
    projectiles_.push_back(pr);
    stats.shots++;
  }
}

void World::ApplySupport(Player& p, const SupportDef& s) {
  for (auto& ally : players_) {
    if (!ally.alive || DistXZ(ally.mv.px, ally.mv.pz, p.mv.px, p.mv.pz) > s.radius) continue;
    switch (s.kind) {
      case Support::kShield:  // 덮어쓰기 (더하지 않음 → 연속 사용해도 상한이 지켜짐)
        ally.shield = s.amount;
        ally.shieldTicks = s.duration;
        Emit(EventType::kShield, p.id, ally.id, s.amount, 0, ally.mv.px, ally.mv.py + 1.0F, ally.mv.pz);
        break;
      case Support::kHeal: {
        const int before = ally.hp;
        ally.hp = std::min(ClassOf(ally.cls).maxHp, ally.hp + s.amount);
        stats.heals++;
        Emit(EventType::kHeal, p.id, ally.id, ally.hp - before, 0, ally.mv.px, ally.mv.py + 1.2F, ally.mv.pz);
        break;
      }
      case Support::kBuff:  // 남은 시간만 갱신 (겹쳐도 수치가 두 번 오르지 않음)
        ally.buffTicks = s.duration;
        Emit(EventType::kBuff, p.id, ally.id, 0, 0, ally.mv.px, ally.mv.py + 1.0F, ally.mv.pz);
        break;
      case Support::kNone:
        break;
    }
  }
}

void World::DamageMonster(Monster& m, int dmg, uint16_t src, uint8_t crit) {
  if (m.state == Monster::State::kDead) return;
  const float def = m.defDownTicks > 0 ? -kCurseDefDown : 0.0F;  // 저주: 받는 피해 1.2배
  const int taken = std::max(1, static_cast<int>(std::floor(static_cast<float>(dmg) * (1.0F - def))));
  m.hp -= taken;
  stats.hits++;
  Emit(EventType::kHit, src, m.id, taken, crit, m.x, m.y + 1.2F, m.z);
  if (m.hp <= 0) {
    m.hp = 0;
    m.state = Monster::State::kDead;
    m.timer = kMonsterRespawnTicks;
    m.burnLeft = 0;
    m.defDownTicks = 0;
    stats.kills++;
    Emit(EventType::kDeath, src, m.id, 0, 0, m.x, m.y, m.z);
  }
}

// 투사체가 사라질 때의 후속 효과 (폭발, 장판)
void World::ProjectileEnds(const Projectile& pr, bool hitSomething) {
  const ShotDef& s = *pr.def;
  if (s.onHit == OnHit::kExplodeBurn && hitSomething) {
    for (auto& m : monsters_) {
      if (m.state == Monster::State::kDead || DistXZ(m.x, m.z, pr.x, pr.z) > kExplodeRadius + kMonsterRadius) continue;
      DamageMonster(m, kExplodeDamage, pr.owner, 0);
      m.burnLeft = kBurnHits;  // 다시 맞으면 횟수만 갱신 (원본은 화상 오브젝트가 계속 쌓였음)
      m.burnTimer = 1;
      m.burnOwner = pr.owner;
    }
  }
  if (s.onHit == OnHit::kCurseZone) {  // 맞든 안 맞든 사라지는 자리에 장판 (원본: 2초 뒤 또는 충돌 시)
    Zone z;
    z.id = NextDynamicId();
    z.owner = pr.owner;
    z.x = pr.x;
    z.y = std::max(0.0F, pr.y - 1.4F);
    z.z = pr.z;
    z.ttl = kCurseZoneTicks;
    z.timer = 1;  // 생기자마자 첫 피해 (원본 damageCoolTime 초기값 0.5)
    zones_.push_back(z);
  }
}

void World::StepProjectiles() {
  for (size_t i = 0; i < projectiles_.size(); ++i) {  // 반복 중에 장판·폭발이 생길 수 있어 인덱스로 순회
    Projectile& pr = projectiles_[i];
    const ShotDef& s = *pr.def;
    ++pr.age;
    if (s.homingAfter > 0 && pr.age >= s.homingAfter) {  // 유도: 가까운 몬스터 쪽으로 속도를 조금씩 섞음
      const Monster* best = nullptr;
      float bestD = s.homingRange;
      for (const auto& m : monsters_) {
        if (m.state == Monster::State::kDead) continue;
        const float d = DistXZ(m.x, m.z, pr.x, pr.z);
        if (d < bestD) {
          bestD = d;
          best = &m;
        }
      }
      if (best != nullptr && bestD > 0.01F) {
        const float sp = std::sqrt(pr.vx * pr.vx + pr.vz * pr.vz);
        const float tx = (best->x - pr.x) / bestD * sp;
        const float tz = (best->z - pr.z) / bestD * sp;
        pr.vx += (tx - pr.vx) * s.turn;
        pr.vz += (tz - pr.vz) * s.turn;
        pr.vy += ((best->y + 1.0F - pr.y) * 2.0F - pr.vy) * s.turn;
      }
    }
    const float ax = pr.x;
    const float ay = pr.y;
    const float az = pr.z;
    pr.vy -= s.gravity * kDt;
    pr.x += pr.vx * kDt;
    pr.y += pr.vy * kDt;
    pr.z += pr.vz * kDt;
    if (pr.ttl > 0) --pr.ttl;
    bool hit = false;
    for (auto& m : monsters_) {  // 선분 대 구: 한 틱에 2m를 날아가도 사이에 있는 몬스터를 놓치지 않음
      if (m.state == Monster::State::kDead) continue;
      if (!SegmentHitsSphere(ax, ay, az, pr.x, pr.y, pr.z, m.x, m.y + 1.0F, m.z, kMonsterRadius)) continue;
      if (pr.damage > 0) DamageMonster(m, pr.damage, pr.owner, pr.crit);
      if (s.splashRadius > 0.0F) {
        for (auto& o : monsters_) {
          if (&o == &m || DistXZ(o.x, o.z, m.x, m.z) > s.splashRadius + kMonsterRadius) continue;
          DamageMonster(o, pr.damage / s.splashDiv, pr.owner, 0);
        }
      }
      if (s.knockback > 0.0F && m.state != Monster::State::kDead) {
        const float sp = std::sqrt(pr.vx * pr.vx + pr.vz * pr.vz);
        if (sp > 0.01F) {
          m.x += pr.vx / sp * s.knockback;
          m.z += pr.vz / sp * s.knockback;
        }
      }
      hit = true;
      break;
    }
    bool blocked = pr.y < 0.0F;
    for (const Box& b : level_.boxes) {
      if (pr.x > b.minX && pr.x < b.maxX && pr.z > b.minZ && pr.z < b.maxZ && pr.y > b.minY && pr.y < b.maxY) {
        blocked = true;
      }
    }
    if (hit || blocked || pr.ttl == 0) {
      const Projectile done = pr;  // ProjectileEnds가 projectiles_에 추가하지는 않지만, 복사해 두면 안전
      pr.ttl = 0;
      ProjectileEnds(done, hit || blocked);
    }
  }
  projectiles_.erase(
      std::remove_if(projectiles_.begin(), projectiles_.end(), [](const Projectile& p) { return p.ttl == 0; }),
      projectiles_.end());
}

void World::StepZones() {
  for (auto& z : zones_) {
    if (z.ttl > 0) --z.ttl;
    if (--z.timer > 0) continue;
    z.timer = kCursePeriod;
    for (auto& m : monsters_) {
      if (m.state == Monster::State::kDead || DistXZ(m.x, m.z, z.x, z.z) > kCurseRadius + kMonsterRadius) continue;
      m.defDownTicks = kCurseDefDownTicks;  // 시간만 갱신 (원본은 −0.2를 여러 번 빼고 3초 뒤 더해서 틀어짐)
      DamageMonster(m, kCurseDamage, z.owner, 0);
    }
  }
  zones_.erase(std::remove_if(zones_.begin(), zones_.end(), [](const Zone& z) { return z.ttl == 0; }), zones_.end());
}

void World::StepMonsters() {
  using S = Monster::State;
  for (auto& m : monsters_) {
    if (m.defDownTicks > 0) --m.defDownTicks;
    if (m.burnLeft > 0 && --m.burnTimer == 0) {  // 화상: 1초마다 30, 5번
      --m.burnLeft;
      m.burnTimer = kBurnPeriod;
      DamageMonster(m, kBurnDamage, m.burnOwner, 0);
    }
    if (frozenMonsters_ && m.state != S::kDead) continue;
    Player* target = FindPlayerMut(m.target);
    if (target != nullptr && !target->alive) target = nullptr;
    switch (m.state) {
      case S::kIdle: {  // 가장 가까운 살아 있는 플레이어를 찾는다
        float best = kAggroRange;
        for (auto& p : players_) {
          if (!p.alive) continue;
          const float d = DistXZ(m.x, m.z, p.mv.px, p.mv.pz);
          if (d < best) {
            best = d;
            m.target = p.id;
            m.state = S::kChase;
          }
        }
        break;
      }
      case S::kChase: {
        if (target == nullptr) {
          m.state = S::kIdle;
          break;
        }
        const float dx = target->mv.px - m.x;
        const float dz = target->mv.pz - m.z;
        const float d = std::sqrt(dx * dx + dz * dz);
        if (d > kLeashRange) {
          m.state = S::kIdle;
          break;
        }
        m.yaw = std::atan2(dx, dz);
        if (d < kMonsterReach) {
          m.state = S::kWindup;
          m.timer = kWindupTicks;
          break;
        }
        m.x += dx / d * kMonsterSpeed * kDt;
        m.z += dz / d * kMonsterSpeed * kDt;
        break;
      }
      case S::kWindup:  // 준비 동작 0.4초 → 그 사이 구르면 피할 수 있다
        --m.timer;
        if (m.timer == 0) {
          if (target != nullptr && target->mv.dodgeT == 0 &&
              DistXZ(m.x, m.z, target->mv.px, target->mv.pz) < kMonsterHitRange) {
            DamagePlayer(*target, kMonsterDamage, m.id);
          }
          m.state = S::kRecover;
          m.timer = kRecoverTicks;
        }
        break;
      case S::kRecover:
        --m.timer;
        if (m.timer == 0) m.state = S::kChase;
        break;
      case S::kDead:
        --m.timer;
        if (m.timer == 0) SpawnMonster(m);
        break;
    }
  }
}

// 피해 처리 순서: 힐러 보호막(1회 무효) → 방어력 → 실드 → 체력
void World::DamagePlayer(Player& p, int dmg, uint16_t source) {
  const ClassDef& def = ClassOf(p.cls);
  if (def.passive == Passive::kBarrier) {
    const bool ready = p.passiveTimer >= def.passiveTicks;
    p.passiveTimer = 0;  // 원본과 같이: 맞으면 8초를 처음부터 다시 셈
    if (ready) {
      stats.blocks++;
      Emit(EventType::kBlock, source, p.id, dmg, 0, p.mv.px, p.mv.py + 1.2F, p.mv.pz);
      return;
    }
  }
  int taken = static_cast<int>(std::floor(static_cast<float>(dmg) * (1.0F - StatsOf(p).def)));
  const int absorbed = std::min(p.shield, taken);
  p.shield -= absorbed;
  taken -= absorbed;
  p.hp = std::max(0, p.hp - taken);
  Emit(EventType::kHit, source, p.id, taken, 0, p.mv.px, p.mv.py + 1.2F, p.mv.pz);
  if (p.hp == 0) {
    p.alive = false;
    p.respawnTimer = kPlayerRespawnTicks;
    stats.playerDowns++;
    Emit(EventType::kDeath, source, p.id, 0, 0, p.mv.px, p.mv.py, p.mv.pz);
  }
}

void World::SpawnMonster(Monster& m) {
  const float a = rng_.Uniform() * 2.0F * kPi;
  const float r = 12.0F + rng_.Uniform() * 6.0F;
  m.x = std::sin(a) * r;
  m.z = std::cos(a) * r;
  m.y = 0.0F;
  m.hp = m.maxHp;
  m.state = Monster::State::kIdle;
  m.timer = 0;
  m.target = 0;
  m.burnLeft = 0;
  m.defDownTicks = 0;
}

void World::Emit(EventType type, uint16_t src, uint16_t dst, int dmg, uint8_t crit, float x, float y, float z) {
  GameEvent e;
  e.id = nextEventId_++;
  e.type = type;
  e.source = src;
  e.target = dst;
  e.damage = static_cast<uint16_t>(std::clamp(dmg, 0, 4095));
  e.crit = crit;
  e.x = x;
  e.y = y;
  e.z = z;
  events_.push_back(e);
}

void World::TakeEvents(std::vector<GameEvent>& out) {
  out.clear();
  out.swap(events_);  // 버퍼를 맞바꿔 재사용 (할당 없음)
}

// 원격 플레이어 애니메이션용: 0 = 없음, 1 + 슬롯×3 + 콤보 단계
static uint8_t SkillCode(uint8_t slot, uint8_t step) {
  return slot == kNoSlot ? 0 : static_cast<uint8_t>(1 + slot * 3 + step);
}

void World::CollectEntities(std::vector<EntityState>& out) const {
  out.clear();
  for (const auto& p : players_) {
    EntityState e;
    e.id = p.id;
    e.kind = EntityKind::kPlayer;
    e.x = p.mv.px;
    e.y = p.mv.py;
    e.z = p.mv.pz;
    e.yaw = static_cast<uint8_t>(p.lastInput.yaw >> 8U);
    Action a = Action::kIdle;
    if (!p.alive) {
      a = Action::kDead;
    } else if (p.mv.dodgeT > 0) {
      a = Action::kDodge;
    } else if (p.ab.phase == kPhaseCharge) {
      a = Action::kCharge;
    } else if (p.ab.slot != kNoSlot) {
      a = Action::kAttack;
    } else if (p.mv.grounded == 0) {
      a = Action::kJump;
    } else if (p.mv.vx != 0.0F || p.mv.vz != 0.0F) {
      a = Action::kMove;
    }
    e.action = static_cast<uint8_t>(a);
    e.hp = static_cast<uint16_t>(p.hp);
    e.cls = static_cast<uint8_t>(p.cls);
    e.skill = p.alive ? SkillCode(p.ab.slot, p.ab.step) : 0;
    out.push_back(e);
  }
  for (const auto& m : monsters_) {
    EntityState e;
    e.id = m.id;
    e.kind = EntityKind::kMonster;
    e.x = m.x;
    e.y = m.y;
    e.z = m.z;
    e.yaw = static_cast<uint8_t>(static_cast<int>((m.yaw / (2.0F * kPi)) * 256.0F) & 255);
    using S = Monster::State;
    Action a = Action::kIdle;
    if (m.state == S::kDead) {
      a = Action::kDead;
    } else if (m.state == S::kChase) {
      a = Action::kMove;
    } else if (m.state == S::kWindup) {
      a = Action::kAttack;
    }
    e.action = static_cast<uint8_t>(a);
    e.hp = static_cast<uint16_t>(m.hp);
    out.push_back(e);
  }
  for (const auto& pr : projectiles_) {
    EntityState e;
    e.id = pr.id;
    e.kind = EntityKind::kProjectile;
    e.x = pr.x;
    e.y = pr.y;
    e.z = pr.z;
    e.yaw = static_cast<uint8_t>(static_cast<int>((std::atan2(pr.vx, pr.vz) / (2.0F * kPi)) * 256.0F) & 255);
    e.cls = static_cast<uint8_t>(pr.cls);
    e.skill = SkillCode(pr.slot, 0);  // 어떤 직업의 어떤 스킬인지 → 클라가 모양을 고름
    out.push_back(e);
  }
  for (const auto& z : zones_) {
    EntityState e;
    e.id = z.id;
    e.kind = EntityKind::kZone;
    e.x = z.x;
    e.y = z.y;
    e.z = z.z;
    e.cls = static_cast<uint8_t>(ClassId::kHealer);
    e.skill = SkillCode(kSlotE, 0);
    e.hp = z.ttl;  // 남은 시간 (클라가 사라지는 연출에 사용)
    out.push_back(e);
  }
}

}  // namespace lr
