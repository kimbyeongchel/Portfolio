// 서버 1개 + 봇 3개를 실제 UDP(루프백)로 연결하고, 봇 쪽에서 지연·지터·손실을 흉내 내 측정한다.
// 봇은 Unity 클라이언트와 같은 일을 한다: 입력 전송, 이동 예측, 스냅샷 재조정, 이벤트 수신.
// 실행: ./lr_loadtest [초=20]  → 시나리오별 JSON 한 줄씩 출력
#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <memory>
#include <thread>
#include <vector>

#include "../src/server/game_server.h"

using lr::Address;
using lr::BitReader;
using lr::BitWriter;
using lr::Header;
using lr::InputCmd;
using lr::MoveState;
using lr::PacketType;
using Clock = std::chrono::steady_clock;

namespace {

struct Scenario {
  const char* name;
  int oneWayMs;
  int jitterMs;
  float loss;
  int inputBuffer;
  int monsters;
};

struct BotResult {
  uint64_t bytesOut = 0, bytesIn = 0, packetsOut = 0, packetsIn = 0, droppedOut = 0, droppedIn = 0;
  uint64_t comparisons = 0, exact = 0, corrections = 0, events = 0, dupEvents = 0, ownHits = 0, respawns = 0;
  float maxCorrection = 0;
  std::vector<float> confirmMs;
};

class Emulator {  // 네트워크 상태 흉내: 단방향 지연 + 지터 + 무작위 손실
 public:
  Emulator(const Scenario& sc, uint64_t seed) : sc_(sc), rng_(seed) {}
  bool Drop() { return rng_.Uniform() < sc_.loss; }
  Clock::time_point DeliverAt(Clock::time_point now) {
    const int j = sc_.jitterMs > 0 ? rng_.Range(0, sc_.jitterMs) : 0;
    return now + std::chrono::milliseconds(sc_.oneWayMs + j);
  }

 private:
  Scenario sc_;
  lr::Rng rng_;
};

struct Delayed {
  Clock::time_point at;
  std::vector<uint8_t> data;
};

class Bot {
 public:
  Bot(const Scenario& sc, uint16_t serverPort, uint64_t seed, float phase)
      : emu_(sc, seed), server_(Address::Loopback(serverPort)), phase_(phase), level_(lr::DefaultLevel()) {
    sock_.Open(0);
  }

  void Run(const std::atomic<bool>& stop) {
    const auto tickDur = std::chrono::microseconds(1000000 / lr::kTickRate);
    auto next = Clock::now();
    while (!stop.load()) {
      PumpNetwork();
      if (Clock::now() < next) {
        std::this_thread::sleep_for(std::chrono::microseconds(300));
        continue;
      }
      next += tickDur;
      if (!accepted_) {
        if (tick_++ % 6 == 0) SendConnect();
        continue;
      }
      ClientTick();
    }
  }
  BotResult result;

 private:
  void SendRaw(const BitWriter& w) {
    if (emu_.Drop()) {
      result.droppedOut++;
      return;
    }
    outQ_.push_back({emu_.DeliverAt(Clock::now()), w.Data()});
  }
  Header NewHeader(PacketType t) {
    Header h;
    h.type = t;
    h.seq = sendSeq_++;
    h.ack = acks_.Ack();
    h.ackBits = acks_.Bits();
    return h;
  }
  void SendConnect() {
    BitWriter w;
    WriteHeader(w, NewHeader(PacketType::kConnect));
    SendRaw(w);
  }

  // Unity 쪽 GameClient.FixedTick()과 같은 순서: 입력 샘플 → 예측 → 기록 → 전송
  void ClientTick() {
    const uint32_t t = tick_++;
    InputCmd in;
    in.seq = ++seq_;
    const float ang = static_cast<float>(t) * lr::kDt * 0.5F + phase_;
    float wx = std::cos(ang);
    float wz = std::sin(ang);
    const float d = std::sqrt(pred_.px * pred_.px + pred_.pz * pred_.pz);
    if (d > 12.0F) {  // 맵 밖으로 나가지 않게 중앙으로
      wx = -pred_.px / d;
      wz = -pred_.pz / d;
    }
    in.moveX = static_cast<int8_t>(std::lround(wx * 127.0F));
    in.moveZ = static_cast<int8_t>(std::lround(wz * 127.0F));
    if ((t / 90) % 2 != 0) in.buttons |= lr::kSprint;
    if (t % 51 == 0) in.buttons |= lr::kJump;
    if (t % 93 == 5) in.buttons |= lr::kDodge;
    if (t % 18 == 0) in.buttons |= lr::kAttack;
    if (t % 330 == 7) in.buttons |= lr::kSkillQ;
    if (t % 180 == 11) in.buttons |= lr::kSkillE;
    in.yaw = AimAtNearestMonster();

    if (!dead_) lr::Step(pred_, in, level_);  // 서버를 기다리지 않고 즉시 이동 (입력 지연 0틱)
    const size_t k = in.seq % kHist;
    hist_[k] = in;
    after_[k] = pred_;
    sentAt_[k] = Clock::now();
    sent_++;

    recent_.clear();  // 최근 4개 입력을 매번 같이 보냄 → 3연속 손실까지 견딤
    const uint32_t count = std::min<uint32_t>(sent_, lr::kMaxInputsPerPacket);
    for (uint32_t back = count; back-- > 0;) recent_.push_back(hist_[static_cast<uint16_t>(seq_ - back) % kHist]);
    BitWriter w;
    WriteHeader(w, NewHeader(PacketType::kInput));
    WriteInputs(w, recent_);
    SendRaw(w);
  }

  uint16_t AimAtNearestMonster() const {
    float best = 1e9F;
    float yaw = 0.0F;
    for (const auto& e : snap_.entities) {
      if (e.kind != lr::EntityKind::kMonster || e.action == 5) continue;
      const float dx = e.x - pred_.px;
      const float dz = e.z - pred_.pz;
      const float dd = dx * dx + dz * dz;
      if (dd < best) {
        best = dd;
        yaw = std::atan2(dx, dz);
      }
    }
    if (yaw < 0) yaw += 6.28318531F;
    return static_cast<uint16_t>(yaw / 6.28318531F * 65535.0F);
  }

  void PumpNetwork() {
    uint8_t buf[1500];
    Address from;
    const auto now = Clock::now();
    for (int n = 0; (n = sock_.RecvFrom(from, buf, sizeof(buf))) > 0;) {
      if (emu_.Drop()) {
        result.droppedIn++;
        continue;
      }
      inQ_.push_back({emu_.DeliverAt(now), std::vector<uint8_t>(buf, buf + n)});
    }
    for (size_t i = 0; i < outQ_.size();) {
      if (outQ_[i].at <= now) {
        sock_.SendTo(server_, outQ_[i].data.data(), outQ_[i].data.size());
        result.bytesOut += outQ_[i].data.size();
        result.packetsOut++;
        outQ_.erase(outQ_.begin() + static_cast<long>(i));
      } else {
        ++i;
      }
    }
    std::sort(inQ_.begin(), inQ_.end(), [](const Delayed& a, const Delayed& b) { return a.at < b.at; });
    while (!inQ_.empty() && inQ_.front().at <= now) {
      const auto pkt = std::move(inQ_.front());
      inQ_.erase(inQ_.begin());
      result.bytesIn += pkt.data.size();
      result.packetsIn++;
      Handle(pkt.data);
    }
  }

  void Handle(const std::vector<uint8_t>& data) {
    BitReader r(data.data(), data.size());
    const Header h = ReadHeader(r);
    if (!r.Ok()) return;
    acks_.OnReceived(h.seq);
    if (h.type == PacketType::kAccept) {
      playerId_ = r.ReadU16();
      (void)r.ReadU16();
      pred_ = lr::ReadMoveState(r);
      accepted_ = true;
      return;
    }
    if (h.type != PacketType::kSnapshot) return;
    ReadSnapshot(r, incoming_);
    if (!r.Ok()) return;

    for (const auto& ev : incoming_.events) {  // 같은 이벤트가 여러 번 와도 한 번만 처리
      if (!dedup_.FirstTime(ev.id)) {
        result.dupEvents++;
        continue;
      }
      result.events++;
      if (ev.type == lr::EventType::kHit && ev.source == playerId_) result.ownHits++;
    }
    if (haveSnap_ && !lr::SeqNewer(incoming_.serverTick, snap_.serverTick)) return;  // 늦게 온 옛 스냅샷은 버림
    haveSnap_ = true;
    std::swap(snap_, incoming_);
    Reconcile(snap_);
  }

  // 서버가 입력 N까지 처리한 결과와, 내가 N까지 예측한 결과를 비트 단위로 비교
  void Reconcile(const lr::Snapshot& s) {
    const uint16_t n = s.lastInputSeq;
    if (n == 0 || static_cast<uint16_t>(seq_ - n) >= kHist - 1) return;
    if (!confirmedAny_ || lr::SeqNewer(n, lastConfirmed_)) {
      result.confirmMs.push_back(std::chrono::duration<float, std::milli>(Clock::now() - sentAt_[n % kHist]).count());
      lastConfirmed_ = n;
      confirmedAny_ = true;
    }
    const bool respawned = dead_ && s.localHp > 0;
    dead_ = (s.localHp == 0);
    if (!dead_ && !respawned) {
      const MoveState& mine = after_[n % kHist];
      result.comparisons++;
      if (lr::BitwiseEqual(mine, s.local)) {
        result.exact++;
        return;
      }
      const float dx = mine.px - s.local.px;
      const float dy = mine.py - s.local.py;
      const float dz = mine.pz - s.local.pz;
      result.corrections++;
      result.maxCorrection = std::max(result.maxCorrection, std::sqrt(dx * dx + dy * dy + dz * dz));
    } else if (respawned) {
      result.respawns++;  // 부활은 서버가 정하는 순간이동 → 보정으로 세지 않음
    }
    pred_ = s.local;  // 서버 상태로 되감고
    for (auto k = static_cast<uint16_t>(n + 1); k != static_cast<uint16_t>(seq_ + 1); ++k) {
      if (!dead_) lr::Step(pred_, hist_[k % kHist], level_);  // 아직 확인 안 된 입력을 다시 적용
      after_[k % kHist] = pred_;
    }
  }

  static constexpr size_t kHist = 256;
  Emulator emu_;
  lr::UdpSocket sock_;
  Address server_;
  float phase_;
  lr::Level level_;
  std::vector<Delayed> outQ_, inQ_;
  lr::AckTracker acks_;
  lr::EventDedup dedup_;
  lr::Snapshot snap_, incoming_;
  std::vector<InputCmd> recent_;
  uint16_t sendSeq_ = 0, seq_ = 0, playerId_ = 0, lastConfirmed_ = 0;
  uint32_t sent_ = 0;
  bool accepted_ = false, haveSnap_ = false, confirmedAny_ = false, dead_ = false;
  uint32_t tick_ = 0;
  MoveState pred_;
  InputCmd hist_[kHist];
  MoveState after_[kHist];
  Clock::time_point sentAt_[kHist];
};

float Pct(std::vector<float> v, float q) {
  if (v.empty()) return 0;
  std::sort(v.begin(), v.end());
  return v[std::min(v.size() - 1, static_cast<size_t>(q * static_cast<float>(v.size())))];
}

double Avg(const std::vector<float>& v) {
  double a = 0;
  for (const float x : v) a += x;
  return v.empty() ? 0.0 : a / static_cast<double>(v.size());
}

}  // namespace

int main(int argc, char** argv) {
  const double seconds = argc > 1 ? std::atof(argv[1]) : 20.0;
  const Scenario scenarios[] = {
      {"local", 0, 0, 0.0F, 0, 12},
      {"rtt100_loss2", 50, 5, 0.02F, 0, 12},
      {"rtt200_loss5", 100, 10, 0.05F, 0, 12},
      {"local", 0, 0, 0.0F, 1, 12},
      {"rtt100_loss2", 50, 5, 0.02F, 1, 12},
      {"rtt200_loss5", 100, 10, 0.05F, 1, 12},
      {"local_200mon", 0, 0, 0.0F, 1, 200},
  };
  for (const auto& sc : scenarios) {
    lr::ServerConfig cfg;
    cfg.monsters = sc.monsters;
    cfg.inputBufferTicks = sc.inputBuffer;
    cfg.log = false;
    lr::GameServer server(cfg);
    if (!server.Start(0)) {
      std::fprintf(stderr, "bind failed\n");
      return 1;
    }
    std::atomic<bool> stop{false};
    std::thread st([&] { server.Run(stop); });
    std::vector<std::unique_ptr<Bot>> bots;
    bots.reserve(lr::kMaxClients);
    for (int i = 0; i < lr::kMaxClients; ++i) {
      bots.push_back(std::make_unique<Bot>(sc, server.Port(), 1000 + i, static_cast<float>(i) * 2.1F));
    }
    std::vector<std::thread> bt;
    bt.reserve(bots.size());
    for (auto& b : bots) bt.emplace_back([&b, &stop] { b->Run(stop); });
    std::this_thread::sleep_for(std::chrono::duration<double>(seconds));
    stop = true;
    for (auto& t : bt) t.join();
    st.join();

    const auto& ss = server.Stats();
    BotResult sum;
    std::vector<float> confirm;
    for (auto& b : bots) {
      const auto& r = b->result;
      sum.bytesOut += r.bytesOut;
      sum.bytesIn += r.bytesIn;
      sum.packetsOut += r.packetsOut;
      sum.packetsIn += r.packetsIn;
      sum.droppedOut += r.droppedOut;
      sum.droppedIn += r.droppedIn;
      sum.comparisons += r.comparisons;
      sum.exact += r.exact;
      sum.corrections += r.corrections;
      sum.events += r.events;
      sum.dupEvents += r.dupEvents;
      sum.ownHits += r.ownHits;
      sum.respawns += r.respawns;
      sum.maxCorrection = std::max(sum.maxCorrection, r.maxCorrection);
      confirm.insert(confirm.end(), r.confirmMs.begin(), r.confirmMs.end());
    }
    const auto& ws = server.GetWorld().stats;
    const double n = lr::kMaxClients;
    const auto u = [](uint64_t v) { return static_cast<unsigned long long>(v); };
    const auto dv = [](uint64_t v) { return static_cast<double>(v); };
    std::printf(
        "{\"scenario\":\"%s\",\"input_buffer\":%d,\"monsters\":%d,\"seconds\":%.1f,\"ticks\":%llu,"
        "\"tick_us_avg\":%.1f,\"tick_us_p99\":%.1f,"
        "\"up_Bps_per_client\":%.0f,\"down_Bps_per_client\":%.0f,\"up_pps_per_client\":%.1f,\"down_pps_per_client\":%."
        "1f,"
        "\"avg_snapshot_bytes\":%.1f,\"avg_input_bytes\":%.1f,\"max_packet_bytes\":%zu,\"entities_culled\":%llu,"
        "\"room_packets_per_s\":%.0f,"
        "\"pred_comparisons\":%llu,\"pred_exact\":%llu,\"pred_corrections\":%llu,\"max_correction_m\":%.3f,"
        "\"confirm_ms_p50\":%.0f,\"confirm_ms_p95\":%.0f,"
        "\"events\":%llu,\"dup_events_ignored\":%llu,\"own_hits\":%llu,"
        "\"server_shots\":%llu,\"server_hits\":%llu,\"server_kills\":%llu,\"repeated_inputs\":%llu,"
        "\"player_downs\":%llu,\"respawns\":%llu,\"dropped_out\":%llu,\"dropped_in\":%llu}\n",
        sc.name, sc.inputBuffer, sc.monsters, seconds, u(ss.ticks), Avg(ss.tickMicros),
        static_cast<double>(Pct(ss.tickMicros, 0.99F)), dv(sum.bytesOut) / n / seconds, dv(sum.bytesIn) / n / seconds,
        dv(sum.packetsOut) / n / seconds, dv(sum.packetsIn) / n / seconds,
        sum.packetsIn != 0 ? dv(sum.bytesIn) / dv(sum.packetsIn) : 0.0,
        sum.packetsOut != 0 ? dv(sum.bytesOut) / dv(sum.packetsOut) : 0.0, ss.maxPacketBytes, u(ss.entitiesCulled),
        dv(ss.packetsIn + ss.packetsOut) / seconds, u(sum.comparisons), u(sum.exact), u(sum.corrections),
        static_cast<double>(sum.maxCorrection), static_cast<double>(Pct(confirm, 0.5F)),
        static_cast<double>(Pct(confirm, 0.95F)), u(sum.events), u(sum.dupEvents), u(sum.ownHits), u(ws.shots),
        u(ws.hits), u(ws.kills), u(ws.repeatedInputs), u(ws.playerDowns), u(sum.respawns), u(sum.droppedOut),
        u(sum.droppedIn));
    std::fflush(stdout);
  }
  return 0;
}
