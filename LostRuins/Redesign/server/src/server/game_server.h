// 방 하나를 맡는 게임 서버. 30Hz 고정 틱: 입력 수신 → 시뮬레이션 1틱 → 클라마다 스냅샷 1개.
// 프로토타입은 스레드 1개. 실서비스 구조(I/O 스레드 + 방별 로직 스레드)는 재설계서 4장 참고.
#pragma once
#include <array>
#include <atomic>
#include <chrono>
#include <cstdint>
#include <vector>

#include "../game/world.h"
#include "../net/reliability.h"
#include "../net/udp_socket.h"

namespace lr {

constexpr int kMaxClients = 3;

struct ServerConfig {
  uint64_t seed = 20241118;
  int monsters = 12;
  int inputBufferTicks = 1;  // 입력 지터 버퍼 (0이면 끔)
  bool log = true;           // 접속·퇴장 로그
};

struct ServerStats {
  uint64_t ticks = 0, bytesIn = 0, bytesOut = 0, packetsIn = 0, packetsOut = 0, rejected = 0;
  uint64_t entitiesCulled = 0;  // 패킷 크기 예산 때문에 이번 스냅샷에서 뺀 엔티티 (먼 것부터)
  size_t maxPacketBytes = 0;
  std::vector<float> tickMicros;  // 틱 1회 처리 시간 (시뮬레이션 + 스냅샷 직렬화·전송)
};

class GameServer {
 public:
  explicit GameServer(const ServerConfig& cfg) : cfg_(cfg), world_(cfg.seed, cfg.monsters, cfg.inputBufferTicks) {}
  bool Start(uint16_t port) { return sock_.Open(port); }
  uint16_t Port() const { return sock_.LocalPort(); }
  void Run(const std::atomic<bool>& stop);
  const ServerStats& Stats() const { return stats_; }
  const World& GetWorld() const { return world_; }

 private:
  using Clock = std::chrono::steady_clock;
  struct Conn {
    bool active = false;
    Address addr;
    uint16_t playerId = 0, sendSeq = 0;
    AckTracker acks;
    ReliableEventQueue events;
    Clock::time_point lastRecv;
  };
  void Poll();
  void Handle(const Address& from, const uint8_t* data, size_t len);
  void TickOnce();
  void BuildSnapshot(Conn& c, const Player& p);
  void Disconnect(Conn& c, const char* reason);
  void Send(Conn& c);
  void SendAccept(Conn& c);
  Conn* Find(const Address& a);

  ServerConfig cfg_;
  UdpSocket sock_;
  World world_;
  std::array<Conn, kMaxClients> conns_{};
  ServerStats stats_;
  // 틱마다 재사용하는 버퍼들 (뜨거운 경로에서 할당하지 않음)
  std::vector<EntityState> entities_;
  std::vector<GameEvent> events_;
  std::vector<InputCmd> inputs_;
  std::vector<std::pair<float, uint32_t>> priority_;
  Snapshot snap_;
  BitWriter writer_;
};

}  // namespace lr
