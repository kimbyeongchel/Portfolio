// 논블로킹 UDP 소켓. Linux/macOS는 BSD 소켓, Windows는 Winsock.
// 실서비스에서는 이 자리에 asio(Windows=IOCP, Linux=epoll)나 ENet/GameNetworkingSockets를 둔다.
#pragma once
#include <cstdint>
#include <cstring>

#ifdef _WIN32
#include <mstcpip.h>
#include <winsock2.h>
#include <ws2tcpip.h>
#pragma comment(lib, "ws2_32.lib")
using socket_t = SOCKET;
#else
#include <arpa/inet.h>
#include <fcntl.h>
#include <netinet/in.h>
#include <sys/socket.h>
#include <unistd.h>
using socket_t = int;
#endif

namespace lr {

struct Address {
  sockaddr_in sa{};
  bool operator==(const Address& o) const {
    return sa.sin_addr.s_addr == o.sa.sin_addr.s_addr && sa.sin_port == o.sa.sin_port;
  }
  static Address Loopback(uint16_t port) {
    Address a;
    a.sa.sin_family = AF_INET;
    a.sa.sin_port = htons(port);
    a.sa.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
    return a;
  }
};

#ifdef _WIN32
// Winsock은 프로세스에서 한 번만 켜고 끈다 (소켓마다 WSAStartup/WSACleanup 하지 않음)
inline bool EnsureWinsock() {
  struct Guard {
    bool ok;
    Guard() {
      WSADATA wsa;
      ok = WSAStartup(MAKEWORD(2, 2), &wsa) == 0;
    }
    ~Guard() {
      if (ok) WSACleanup();
    }
  };
  static Guard g;
  return g.ok;
}
#endif

class UdpSocket {
 public:
  UdpSocket() = default;
  ~UdpSocket() { Close(); }
  // 소켓 핸들은 하나의 객체만 소유한다. 복사하면 두 객체가 같은 핸들을 닫게 되므로 금지.
  UdpSocket(const UdpSocket&) = delete;
  UdpSocket& operator=(const UdpSocket&) = delete;
  UdpSocket(UdpSocket&& o) noexcept : s_(o.s_) { o.s_ = kInvalid; }
  UdpSocket& operator=(UdpSocket&& o) noexcept {
    if (this != &o) {
      Close();
      s_ = o.s_;
      o.s_ = kInvalid;
    }
    return *this;
  }

  bool Open(uint16_t port) {  // port 0 = 운영체제가 빈 포트 배정
#ifdef _WIN32
    if (!EnsureWinsock()) return false;
#endif
    s_ = ::socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
    if (!Valid()) return false;
    sockaddr_in addr{};
    addr.sin_family = AF_INET;
    addr.sin_port = htons(port);
    addr.sin_addr.s_addr = htonl(INADDR_ANY);
    if (::bind(s_, reinterpret_cast<sockaddr*>(&addr), sizeof(addr)) != 0) {
      Close();
      return false;
    }
#ifdef _WIN32
    u_long nb = 1;
    ioctlsocket(s_, FIONBIO, &nb);
    // Windows는 상대가 꺼져 ICMP "포트 없음"이 오면 다음 recvfrom을 WSAECONNRESET으로 실패시킨다.
    // 서버 입장에서는 클라 한 명이 나간 것뿐이므로 이 동작을 끈다.
    BOOL off = FALSE;
    DWORD bytes = 0;
    WSAIoctl(s_, SIO_UDP_CONNRESET, &off, sizeof(off), nullptr, 0, &bytes, nullptr, nullptr);
#else
    fcntl(s_, F_SETFL, fcntl(s_, F_GETFL, 0) | O_NONBLOCK);
#endif
    return true;
  }
  int SendTo(const Address& to, const uint8_t* data, size_t len) {
    return static_cast<int>(::sendto(s_, reinterpret_cast<const char*>(data), static_cast<int>(len), 0,
                                     reinterpret_cast<const sockaddr*>(&to.sa), sizeof(to.sa)));
  }
  // 받을 게 없으면 -1 (논블로킹)
  int RecvFrom(Address& from, uint8_t* buf, size_t cap) {
    socklen_t fl = sizeof(from.sa);
    return static_cast<int>(::recvfrom(s_, reinterpret_cast<char*>(buf), static_cast<int>(cap), 0,
                                       reinterpret_cast<sockaddr*>(&from.sa), &fl));
  }
  uint16_t LocalPort() const {
    sockaddr_in a{};
    socklen_t l = sizeof(a);
    getsockname(s_, reinterpret_cast<sockaddr*>(&a), &l);
    return ntohs(a.sin_port);
  }
  void Close() {
    if (!Valid()) return;
#ifdef _WIN32
    closesocket(s_);
#else
    ::close(s_);
#endif
    s_ = kInvalid;
  }

 private:
#ifdef _WIN32
  static constexpr socket_t kInvalid = INVALID_SOCKET;
#else
  static constexpr socket_t kInvalid = -1;
#endif
  bool Valid() const { return s_ != kInvalid; }
  socket_t s_ = kInvalid;
};

}  // namespace lr
