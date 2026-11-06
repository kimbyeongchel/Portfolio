// UDP 위의 최소 신뢰성 계층.
//  - AckTracker: 받은 패킷 번호를 32비트 비트필드로 요약해 상대에게 돌려준다 (출석부).
//  - ReliableEventQueue: 이벤트를 ACK 받을 때까지 모든 패킷에 다시 싣는다 (등기우편).
//    재전송 타이머가 없으므로 TCP처럼 "앞 패킷을 기다리며 멈추는" 일이 없다 (1편 F2).
#pragma once
#include <array>
#include <cstdint>
#include <deque>
#include <vector>

#include "../shared/protocol.h"

namespace lr {

class AckTracker {
 public:
  void OnReceived(uint16_t seq) {
    if (!any_) {
      any_ = true;
      latest_ = seq;
      bits_ = 0;
      return;
    }
    if (seq == latest_) return;  // 중복 수신
    if (SeqNewer(seq, latest_)) {
      const uint32_t shift = static_cast<uint16_t>(seq - latest_);  // 1 이상 (같은 번호는 위에서 걸러짐)
      if (shift >= 1 && shift <= 31) {
        bits_ = (bits_ << shift) | (1U << (shift - 1U));  // 기록을 밀고, 직전 최신 번호도 기록
      } else if (shift == 32) {
        bits_ = 1U << 31U;  // 직전 최신 번호만 맨 끝 칸에 남음
      } else {
        bits_ = 0;  // 32칸 밖으로 전부 밀려남
      }
      latest_ = seq;
    } else {
      const auto back = static_cast<uint16_t>(latest_ - seq);
      if (back >= 1 && back <= 32) bits_ |= (1U << (back - 1U));  // 늦게 온 옛 패킷
    }
  }
  uint16_t Ack() const { return latest_; }
  uint32_t Bits() const { return bits_; }
  bool Any() const { return any_; }

 private:
  bool any_ = false;
  uint16_t latest_ = 0;
  uint32_t bits_ = 0;
};

class ReliableEventQueue {
 public:
  // 이 이상 쌓이면 상대가 사실상 응답하지 않는 것 → 서버가 연결을 끊는다
  static constexpr size_t kMaxPending = 512;

  void Push(const GameEvent& e) { pending_.push_back(e); }
  bool Overflowed() const { return pending_.size() > kMaxPending; }
  size_t Pending() const { return pending_.size(); }

  // 아직 ACK 안 된 이벤트를 최대 max개 out에 싣고, 어떤 패킷에 실었는지 기록
  void Collect(uint16_t packetSeq, size_t max, std::vector<GameEvent>& out) {
    out.clear();
    auto& slot = sent_[packetSeq % sent_.size()];
    slot.seq = packetSeq;
    slot.ids.clear();
    for (const auto& e : pending_) {
      if (out.size() >= max) break;
      out.push_back(e);
      slot.ids.push_back(e.id);
    }
  }

  void OnAck(uint16_t ack, uint32_t bits) {
    Acked(ack);
    for (uint32_t i = 0; i < 32; ++i) {
      if ((bits & (1U << i)) != 0) Acked(static_cast<uint16_t>(ack - 1U - i));
    }
  }

 private:
  struct Sent {
    uint16_t seq = 0;
    std::vector<uint16_t> ids;
  };
  void Acked(uint16_t seq) {
    auto& slot = sent_[seq % sent_.size()];
    if (slot.seq != seq || slot.ids.empty()) return;
    for (const uint16_t id : slot.ids) {
      for (auto it = pending_.begin(); it != pending_.end(); ++it) {
        if (it->id == id) {
          pending_.erase(it);
          break;
        }
      }
    }
    slot.ids.clear();
  }
  std::deque<GameEvent> pending_;
  std::array<Sent, 256> sent_{};
};

// 받는 쪽 중복 제거 (같은 이벤트가 ACK 전까지 여러 패킷에 실려 오므로)
class EventDedup {
 public:
  bool FirstTime(uint16_t id) {
    auto& slot = seen_[id % seen_.size()];
    if (slot.valid && slot.id == id) return false;
    slot = {id, true};
    return true;
  }

 private:
  struct Slot {
    uint16_t id = 0;
    bool valid = false;
  };
  std::array<Slot, 1024> seen_{};
};

}  // namespace lr
