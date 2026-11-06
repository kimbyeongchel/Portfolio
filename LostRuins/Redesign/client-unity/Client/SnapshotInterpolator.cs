// 다른 플레이어·몬스터·화살: 서버 틱 기준 "조금 과거(기본 3틱 = 100ms)"를 앞뒤 스냅샷 사이로 보간 (1편 F6).
//
// 비유: 동영상 스트리밍. 플레이어는 몇 초를 미리 받아 두고(버퍼) 조금 늦게 재생하기 때문에
// 네트워크가 잠깐 끊겨도 영상이 멈추지 않는다. 여기서는 100ms만큼 늦게 그리는 대신 움직임이 매끄럽다.
using System;
using System.Collections.Generic;
using LostRuins.Shared;

namespace LostRuins.Client
{
    public sealed class SnapshotInterpolator
    {
        sealed class Frame
        {
            public int Tick;
            public readonly Dictionary<ushort, EntityState> Entities = new Dictionary<ushort, EntityState>(64);
        }

        const int MaxFrames = 32;
        readonly List<Frame> frames = new List<Frame>(MaxFrames);
        readonly Stack<Frame> spare = new Stack<Frame>();    // 버린 프레임을 재사용 (딕셔너리 할당 없음)
        readonly float delayTicks;
        int newestTick = -1;
        double clockTick;          // 로컬에서 흘러가는 서버 틱 추정치
        bool started;

        public double RenderTick { get; private set; } = double.NegativeInfinity;   // 지금 화면에 그리는 서버 틱 (이벤트 표시 시각에 사용)
        public int NewestTick { get { return newestTick; } }

        public SnapshotInterpolator(float delayTicks = 3f) { this.delayTicks = delayTicks; }

        public void Add(int serverTick, List<EntityState> entities)
        {
            if (serverTick <= newestTick) return;                    // 늦게 온 스냅샷은 버림
            var f = spare.Count > 0 ? spare.Pop() : new Frame();
            f.Tick = serverTick;
            f.Entities.Clear();
            for (int i = 0; i < entities.Count; ++i) f.Entities[entities[i].Id] = entities[i];
            frames.Add(f);
            newestTick = serverTick;
            if (!started) { clockTick = serverTick; started = true; }
            if (frames.Count > MaxFrames) { spare.Push(frames[0]); frames.RemoveAt(0); }
            // 로컬 시계가 서버보다 너무 앞서거나 뒤처지면 살짝 당겨서 맞춤 (한 번에 확 바꾸면 화면이 튐)
            double drift = serverTick - clockTick;
            if (Math.Abs(drift) > 6) clockTick = serverTick; else clockTick += drift * 0.05;
        }

        // 매 프레임 호출. deltaSeconds 만큼 시계를 진행하고 보간된 상태를 output에 채운다.
        public void Sample(float deltaSeconds, List<EntityState> output)
        {
            output.Clear();
            if (frames.Count == 0) return;
            clockTick += deltaSeconds * Rules.TickRate;
            double renderTick = clockTick - delayTicks;
            RenderTick = renderTick;

            int i = frames.Count - 1;
            while (i > 0 && frames[i - 1].Tick > renderTick) --i;
            if (i == 0 || frames[i].Tick <= renderTick)
            {
                foreach (var e in frames[frames.Count - 1].Entities.Values) output.Add(e);
                return;
            }

            Frame a = frames[i - 1], b = frames[i];
            float u = (float)((renderTick - a.Tick) / (b.Tick - a.Tick));   // 0..1: a와 b 사이 어디쯤인가
            foreach (var eb in b.Entities.Values)
            {
                EntityState ea;
                if (!a.Entities.TryGetValue(eb.Id, out ea)) { output.Add(eb); continue; }   // 새로 생긴 엔티티
                var e = eb;
                e.X = ea.X + (eb.X - ea.X) * u;
                e.Y = ea.Y + (eb.Y - ea.Y) * u;
                e.Z = ea.Z + (eb.Z - ea.Z) * u;
                int dyaw = ((eb.Yaw - ea.Yaw + 384) % 256) - 128;                      // 짧은 쪽으로 회전 (350° → 10°는 +20°)
                e.Yaw = (byte)((ea.Yaw + (int)Math.Round(dyaw * u) + 256) % 256);
                output.Add(e);
            }
        }
    }
}
