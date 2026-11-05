#include "world.h"

#include <algorithm>
#include <cmath>
#include <iterator>

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
constexpr uint16_t kProjectileTtl = 90;
constexpr size_t kMaxQueuedInputs = 6;

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

uint16_t World::AddPlayer() {
  Player p;
  p.id = nextStaticId_++;
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

// 같은 입력이 최대 4번 중복 도착하므로 번호로 거르고, 번호 순서대로 끼워 넣는다
void World::PushInput(uint16_t playerId, const InputCmd& in) {
  Player* p = FindPlayerMut(playerId);
  if (p == nullptr) return;
  if (p->started && !SeqNewer(in.seq, p->lastSeq)) return;  // 이미 처리한 번호
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
  StepMonsters();
  ++tick_;
}

void World::StepPlayer(Player& p) {
  if (!p.alive) {  // 죽어 있는 동안에도 입력 번호는 소비 (재조정 기준점 유지)
    while (!p.queue.empty()) {
      p.lastInput = p.queue.front();
      p.lastSeq = p.lastInput.seq;
      p.queue.pop_front();
    }
    if (p.respawnTimer > 0) {
      --p.respawnTimer;
      if (p.respawnTimer == 0) {
        p.alive = true;
        p.hp = kArcherStats.maxHp;
        p.mv = MoveState{};
      }
    }
    return;
  }
  // 입력 지터 버퍼: 처음 시작할 때 입력이 (1 + 버퍼) 개 모일 때까지 기다린다.
  // 이후로는 네트워크가 잠깐 늦어도 큐에 여유분이 있어 "직전 입력 반복"이 줄어든다.
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
  } else {
    in = p.lastInput;  // 입력이 아직 안 왔으면 직전 입력 반복 (클라가 재조정으로 맞춤)
    in.seq = p.lastSeq;
    stats.repeatedInputs++;
  }
  const auto pressed = static_cast<uint8_t>(in.buttons & ~p.mv.prevButtons);
  lr::Step(p.mv, in, level_);  // 클라 예측과 같은 함수

  for (auto& cd : p.cooldown) {
    if (cd > 0) --cd;
  }
  if (p.attackAnim > 0) --p.attackAnim;
  for (size_t i = 0; i < std::size(kArcherSkills); ++i) {
    const SkillDef& def = kArcherSkills[i];
    if ((pressed & def.button) != 0 && p.cooldown[i] == 0 && p.mv.dodgeT == 0) {
      Fire(p, def, in.yaw);
      p.cooldown[i] = def.cooldownTicks;
      p.attackAnim = 8;
    }
  }
}

int World::RollDamage(float mul, bool forceCrit, uint8_t& crit) {
  int dmg = static_cast<int>(std::floor(kArcherStats.atk * mul)) + rng_.Range(-5, 5);
  crit = (forceCrit || rng_.Uniform() < kArcherStats.critChance) ? 1 : 0;
  if (crit != 0) dmg = static_cast<int>(std::floor(static_cast<float>(dmg) * 1.7F));
  return std::max(dmg, 1);
}

void World::Fire(Player& p, const SkillDef& def, uint16_t yaw) {
  bool forceCrit = false;
  if (def.button == kAttack && kArcherStats.passiveEvery > 0) {
    p.basicCount = static_cast<uint8_t>((p.basicCount + 1) % kArcherStats.passiveEvery);
    forceCrit = (p.basicCount == 0);  // 3번째 화살 "그 자체"가 치명타 (원본은 4번째에 적용되던 버그)
  }
  const float base = static_cast<float>(yaw) * (2.0F * kPi / 65536.0F);
  for (int i = 0; i < def.projectiles; ++i) {
    const float off =
        (static_cast<float>(i) - static_cast<float>(def.projectiles - 1) * 0.5F) * def.spreadDeg * (kPi / 180.0F);
    const float a = base + off;
    Projectile pr;
    pr.id = NextDynamicId();
    pr.owner = p.id;
    pr.vx = std::sin(a) * def.speed;
    pr.vz = std::cos(a) * def.speed;
    pr.vy = 0.0F;
    pr.x = p.mv.px + std::sin(a) * 0.6F;
    pr.y = p.mv.py + 1.4F;
    pr.z = p.mv.pz + std::cos(a) * 0.6F;
    pr.ttl = kProjectileTtl;
    pr.damage = RollDamage(def.damageMul, forceCrit, pr.crit);
    projectiles_.push_back(pr);
    stats.shots++;
  }
}

void World::StepProjectiles() {
  for (auto& pr : projectiles_) {
    const float ax = pr.x;
    const float ay = pr.y;
    const float az = pr.z;
    pr.vy -= 2.0F * kDt;  // 화살은 약하게만 떨어진다
    pr.x += pr.vx * kDt;
    pr.y += pr.vy * kDt;
    pr.z += pr.vz * kDt;
    if (pr.ttl > 0) --pr.ttl;
    for (auto& m : monsters_) {
      if (m.state == Monster::State::kDead) continue;
      if (!SegmentHitsSphere(ax, ay, az, pr.x, pr.y, pr.z, m.x, m.y + 1.0F, m.z, kMonsterRadius)) continue;
      m.hp -= pr.damage;
      stats.hits++;
      Emit(EventType::kHit, pr.owner, m.id, pr.damage, pr.crit, m.x, m.y + 1.2F, m.z);
      if (m.hp <= 0) {
        m.hp = 0;
        m.state = Monster::State::kDead;
        m.timer = kMonsterRespawnTicks;
        stats.kills++;
        Emit(EventType::kDeath, pr.owner, m.id, 0, 0, m.x, m.y, m.z);
      }
      pr.ttl = 0;
      break;
    }
    if (pr.y < 0.0F) pr.ttl = 0;
    for (const Box& b : level_.boxes) {
      if (pr.x > b.minX && pr.x < b.maxX && pr.z > b.minZ && pr.z < b.maxZ && pr.y > b.minY && pr.y < b.maxY) {
        pr.ttl = 0;
      }
    }
  }
  projectiles_.erase(
      std::remove_if(projectiles_.begin(), projectiles_.end(), [](const Projectile& p) { return p.ttl == 0; }),
      projectiles_.end());
}

void World::StepMonsters() {
  using S = Monster::State;
  for (auto& m : monsters_) {
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

void World::DamagePlayer(Player& p, int dmg, uint16_t source) {
  p.hp = std::max(0, p.hp - dmg);
  Emit(EventType::kHit, source, p.id, dmg, 0, p.mv.px, p.mv.py + 1.2F, p.mv.pz);
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
    } else if (p.mv.grounded == 0) {
      a = Action::kJump;
    } else if (p.attackAnim > 0) {
      a = Action::kAttack;
    } else if (p.mv.vx != 0.0F || p.mv.vz != 0.0F) {
      a = Action::kMove;
    }
    e.action = static_cast<uint8_t>(a);
    e.hp = static_cast<uint16_t>(p.hp);
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
    out.push_back(e);
  }
}

}  // namespace lr
