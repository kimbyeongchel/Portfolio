// 서버 이벤트(데미지·회복·실드…)를 "언제" 화면에 띄울지 정한다. Unity 의존성 없음.
//
// 다른 사람과 몬스터는 보간 때문에 100ms(3틱) 과거 모습으로 그려진다. 이벤트를 받자마자 띄우면
// 동료 화면에서는 칼이 닿기 전에 숫자가 먼저 뜬다. 그래서 이벤트에 서버 틱을 붙여 두었다가
// 보간 시계가 그 틱에 닿을 때 내보낸다. 내가 친 공격의 결과는 기다리지 않고 바로 보여 준다
// (내 휘두르기는 예측으로 이미 RTT만큼 앞서 화면에 나왔으므로, 더 늦추면 손맛이 나빠진다).
using System;
using System.Collections.Generic;
using LostRuins.Shared;

namespace LostRuins.Client
{
    public sealed class EventScheduler
    {
        struct Pending { public GameEvent Event; public int Tick; }

        const int MaxWaitTicks = 15;                     // 시계가 멈춰도 0.5초 넘게 붙잡지 않음
        readonly List<Pending> pending = new List<Pending>(64);
        readonly ushort localId;

        public EventScheduler(ushort localPlayerId) { localId = localPlayerId; }

        public int Count { get { return pending.Count; } }

        // serverTick: 이 이벤트를 처음 실어 온 스냅샷의 틱 (이벤트가 생긴 틱과 거의 같음)
        public void Enqueue(GameEvent e, int serverTick, Action<GameEvent> show)
        {
            if (e.Source == localId) { show(e); return; }              // 내 공격 결과는 즉시
            pending.Add(new Pending { Event = e, Tick = serverTick });
        }

        // 매 프레임: 보간 시계(renderTick)가 지난 이벤트만 순서대로 내보낸다. 리스트를 앞으로 당겨 재사용 (할당 없음)
        public void Release(double renderTick, int newestServerTick, Action<GameEvent> show)
        {
            int keep = 0;
            for (int i = 0; i < pending.Count; ++i)
            {
                var p = pending[i];
                if (p.Tick <= renderTick || newestServerTick - p.Tick > MaxWaitTicks) show(p.Event);
                else pending[keep++] = p;
            }
            pending.RemoveRange(keep, pending.Count - keep);
        }
    }
}
