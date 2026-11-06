// 실행: ./lr_server [port=27015] [monsters=12] [inputBufferTicks=1]
#include <atomic>
#include <csignal>
#include <cstdio>
#include <cstdlib>

#include "game_server.h"

#ifdef _WIN32
#include <timeapi.h>
#pragma comment(lib, "winmm.lib")
#endif

namespace {
std::atomic<bool> g_stop{false};
static_assert(std::atomic<bool>::is_always_lock_free, "signal handler needs a lock-free flag");
extern "C" void OnSignal(int /*sig*/) {
  g_stop.store(true);
}

int ArgInt(int argc, char** argv, int i, int def, int lo, int hi) {
  if (argc <= i) return def;
  char* end = nullptr;
  const long v = std::strtol(argv[i], &end, 10);
  if (end == argv[i] || *end != '\0' || v < lo || v > hi) {
    std::fprintf(stderr, "invalid argument %d: %s (expected %d..%d)\n", i, argv[i], lo, hi);
    std::exit(2);
  }
  return static_cast<int>(v);
}
}  // namespace

int main(int argc, char** argv) {
  const auto port = static_cast<uint16_t>(ArgInt(argc, argv, 1, 27015, 1, 65535));
  lr::ServerConfig cfg;
  cfg.monsters = ArgInt(argc, argv, 2, 12, 0, 1000);
  cfg.inputBufferTicks = ArgInt(argc, argv, 3, 1, 0, 4);
  std::signal(SIGINT, OnSignal);
  std::signal(SIGTERM, OnSignal);
#ifdef _WIN32
  timeBeginPeriod(1);  // Windows 기본 Sleep 해상도는 15.6ms라 30Hz 틱이 흔들린다 → 1ms로
#endif

  lr::GameServer server(cfg);
  if (!server.Start(port)) {
    std::fprintf(stderr, "bind failed on udp/%u\n", port);
    return 1;
  }
  std::printf("LostRuins room server on udp/%u, %d monsters, %d Hz, input buffer %d tick(s)\n", server.Port(),
              cfg.monsters, lr::kTickRate, cfg.inputBufferTicks);
  server.Run(g_stop);

  const auto& s = server.Stats();
  std::printf("ticks=%llu in=%lluB out=%lluB rejected=%llu\n", static_cast<unsigned long long>(s.ticks),
              static_cast<unsigned long long>(s.bytesIn), static_cast<unsigned long long>(s.bytesOut),
              static_cast<unsigned long long>(s.rejected));
#ifdef _WIN32
  timeEndPeriod(1);
#endif
  return 0;
}
