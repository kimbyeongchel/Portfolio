#include "game_server.h"

#include <algorithm>
#include <cstdio>
#include <thread>

namespace lr {

GameServer::Conn* GameServer::Find(const Address& a) {
  for (auto& c : conns_) {
    if (c.active && c.addr == a) return &c;
  }
  return nullptr;
}

void GameServer::Run(const std::atomic<bool>& stop) {
  const auto tickDur = std::chrono::microseconds(1000000 / kTickRate);
  auto next = Clock::now() + tickDur;
  while (!stop.load(std::memory_order_relaxed)) {
    Poll();
    const auto now = Clock::now();
    if (now >= next) {
      const auto t0 = Clock::now();
      TickOnce();
      stats_.tickMicros.push_back(std::chrono::duration<float, std::micro>(Clock::now() - t0).count());
      next += tickDur;
      if (now - next > std::chrono::seconds(1)) next = now + tickDur;  // 오래 멈췄다 깨어나도 몰아서 돌지 않음
    } else {
      std::this_thread::sleep_for(std::chrono::microseconds(500));
    }
  }
}

void GameServer::Poll() {
  uint8_t buf[1500];
  Address from;
  for (int n = 0; (n = sock_.RecvFrom(from, buf, sizeof(buf))) > 0;) {
    stats_.bytesIn += static_cast<uint64_t>(n);
    stats_.packetsIn++;
    Handle(from, buf, static_cast<size_t>(n));
  }
}

void GameServer::Handle(const Address& from, const uint8_t* data, size_t len) {
  BitReader r(data, len);
  const Header h = ReadHeader(r);
  if (!r.Ok()) {
    stats_.rejected++;
    return;
  }
  Conn* c = Find(from);

  if (h.type == PacketType::kConnect) {
    const ClassId cls = ReadConnect(r);
    if (!r.Ok()) {
      stats_.rejected++;
      return;
    }
    if (c == nullptr) {
      for (auto& slot : conns_) {
        if (slot.active) continue;
        slot = Conn{};
        slot.active = true;
        slot.addr = from;
        slot.playerId = world_.AddPlayer(cls);
        c = &slot;
        if (cfg_.log) std::fprintf(stderr, "[server] player %u joined\n", c->playerId);
        break;
      }
      if (c == nullptr) {  // 방이 가득 참
        stats_.rejected++;
        return;
      }
    }
    c->lastRecv = Clock::now();
    SendAccept(*c);  // Connect가 재전송돼도 같은 응답
    return;
  }
  if (c == nullptr) {  // 모르는 주소의 입력은 무시
    stats_.rejected++;
    return;
  }
  c->lastRecv = Clock::now();
  c->acks.OnReceived(h.seq);
  c->events.OnAck(h.ack, h.ackBits);

  if (h.type == PacketType::kInput) {
    ReadInputs(r, inputs_);
    if (!r.Ok()) {
      stats_.rejected++;
      return;
    }
    for (const auto& in : inputs_) world_.PushInput(c->playerId, in);
  }
}

void GameServer::Disconnect(Conn& c, const char* reason) {
  if (cfg_.log) std::fprintf(stderr, "[server] player %u left (%s)\n", c.playerId, reason);
  world_.RemovePlayer(c.playerId);
  c.active = false;
}

void GameServer::TickOnce() {
  world_.Step();
  stats_.ticks++;
  world_.TakeEvents(events_);
  world_.CollectEntities(entities_);
  const auto now = Clock::now();

  for (auto& c : conns_) {
    if (!c.active) continue;
    if (now - c.lastRecv > std::chrono::seconds(5)) {
      Disconnect(c, "timeout");
      continue;
    }
    for (const auto& e : events_) c.events.Push(e);
    if (c.events.Overflowed()) {  // 이벤트를 수백 개째 확인하지 못함 → 연결이 사실상 끊긴 상태
      Disconnect(c, "reliable backlog");
      continue;
    }
    const Player* p = world_.FindPlayer(c.playerId);
    if (p == nullptr) continue;
    BuildSnapshot(c, *p);
    Send(c);
  }
}

// 스냅샷 한 개를 1200바이트 안에 맞춘다. 넘치면 먼 엔티티부터 뺀다 (관심 영역 관리의 가장 단순한 형태).
void GameServer::BuildSnapshot(Conn& c, const Player& p) {
  Header h;
  h.type = PacketType::kSnapshot;
  h.seq = c.sendSeq++;
  h.ack = c.acks.Ack();
  h.ackBits = c.acks.Bits();

  snap_.serverTick = static_cast<uint16_t>(world_.Tick());
  snap_.lastInputSeq = p.lastSeq;
  snap_.local = p.mv;
  snap_.ability = p.ab;
  snap_.localHp = static_cast<uint16_t>(p.hp);
  snap_.localShield = static_cast<uint8_t>(std::clamp(p.shield, 0, 255));
  snap_.localFlags = static_cast<uint8_t>((p.buffTicks > 0 ? 1 : 0) | (world_.PassiveReady(p) ? 2 : 0));
  c.events.Collect(h.seq, kMaxEventsPerPacket, snap_.events);

  const size_t budgetBits = kMaxPacketBytes * 8 - kHeaderBits - kSnapshotFixedBits - snap_.events.size() * kEventBits;
  const size_t maxEntities = std::min<size_t>(255, budgetBits / kEntityBits);

  priority_.clear();
  for (uint32_t i = 0; i < entities_.size(); ++i) {
    const auto& e = entities_[i];
    if (e.id == c.playerId) continue;
    const float dx = e.x - p.mv.px;
    const float dz = e.z - p.mv.pz;
    // 다른 플레이어는 거리와 무관하게 항상 먼저 (협동 게임에서 동료 위치는 가장 중요)
    const float key = e.kind == EntityKind::kPlayer ? -1.0F : dx * dx + dz * dz;
    priority_.emplace_back(key, i);
  }
  snap_.entities.clear();
  if (priority_.size() > maxEntities) {
    std::nth_element(priority_.begin(), priority_.begin() + static_cast<long>(maxEntities), priority_.end());
    stats_.entitiesCulled += priority_.size() - maxEntities;
    priority_.resize(maxEntities);
  }
  for (const auto& [key, idx] : priority_) snap_.entities.push_back(entities_[idx]);

  writer_.Reset();
  WriteHeader(writer_, h);
  WriteSnapshot(writer_, snap_);
}

void GameServer::SendAccept(Conn& c) {
  writer_.Reset();
  Header h;
  h.type = PacketType::kAccept;
  h.seq = c.sendSeq++;
  h.ack = c.acks.Ack();
  h.ackBits = c.acks.Bits();
  WriteHeader(writer_, h);
  Accept a;
  a.playerId = c.playerId;
  a.serverTick = static_cast<uint16_t>(world_.Tick());
  if (const Player* p = world_.FindPlayer(c.playerId)) {
    a.cls = p->cls;
    a.spawn = p->mv;  // 스폰 위치를 알려줘야 클라가 같은 출발점에서 예측
  }
  WriteAccept(writer_, a);
  Send(c);
}

void GameServer::Send(Conn& c) {
  const auto& d = writer_.Data();
  sock_.SendTo(c.addr, d.data(), d.size());
  stats_.bytesOut += d.size();
  stats_.packetsOut++;
  stats_.maxPacketBytes = std::max(stats_.maxPacketBytes, d.size());
}

}  // namespace lr
