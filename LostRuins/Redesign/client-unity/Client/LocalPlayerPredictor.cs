// 내 캐릭터: 입력 즉시 움직이고(예측), 서버 결과가 오면 비교해서 다르면 되감아 다시 계산(재조정).
// Unity 의존성 없음 → 서버 프로토타입의 봇(loadtest.cpp)과 같은 알고리즘을 C#으로 옮긴 것.
//
// 비유: 내비게이션. GPS 신호(서버)가 1초마다 오더라도, 화면 속 차는 속도와 방향으로 미리 움직인다(예측).
// 신호가 오면 "그 시각에 내가 있다고 생각한 위치"와 비교하고, 다르면 그 시각부터 다시 계산한다(재조정).
using System.Collections.Generic;
using LostRuins.Shared;

namespace LostRuins.Client
{
    public sealed class LocalPlayerPredictor
    {
        const int History = 256;                 // 256틱 = 8.5초 분량. 65536이 256으로 나눠떨어져 번호 순환에도 안전
        readonly InputCmd[] inputs = new InputCmd[History];
        readonly MoveState[] after = new MoveState[History];   // 각 입력을 적용한 "직후" 내 상태
        readonly Level level;
        ushort seq;
        uint sent;
        bool dead;

        public MoveState State;                  // 지금 화면에 그릴 내 상태
        public int Corrections { get; private set; }
        public float LastCorrectionMeters { get; private set; }

        public LocalPlayerPredictor(Level level, MoveState spawn) { this.level = level; State = spawn; }

        // 고정 틱마다 1번. 반환값을 기록하고, RecentInputs로 서버에 보낸다.
        public InputCmd Predict(sbyte moveX, sbyte moveZ, ushort yaw, Buttons buttons)
        {
            var cmd = new InputCmd { Seq = ++seq, MoveX = moveX, MoveZ = moveZ, Yaw = yaw, Buttons = (byte)buttons };
            if (!dead) Movement.Step(ref State, cmd, level);
            inputs[cmd.Seq % History] = cmd;
            after[cmd.Seq % History] = State;
            sent++;
            return cmd;
        }

        // 최근 n개 입력을 into에 채운다 (패킷 하나가 사라져도 다음 패킷이 메워 줌). 리스트는 재사용.
        public void RecentInputs(int n, List<InputCmd> into)
        {
            into.Clear();
            int count = (int)System.Math.Min((uint)n, sent);
            for (int back = count - 1; back >= 0; --back) into.Add(inputs[(ushort)(seq - back) % History]);
        }

        public void Reconcile(ushort lastInputSeq, MoveState server, ushort hp)
        {
            if (lastInputSeq == 0 || (ushort)(seq - lastInputSeq) >= History - 1) return;   // 너무 옛날 결과
            bool respawned = dead && hp > 0;
            dead = hp == 0;
            if (!dead && !respawned && after[lastInputSeq % History].BitwiseEquals(server)) return;   // 대부분 여기서 끝

            if (!dead && !respawned)
            {
                var mine = after[lastInputSeq % History];
                float dx = mine.Px - server.Px, dy = mine.Py - server.Py, dz = mine.Pz - server.Pz;
                LastCorrectionMeters = (float)System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
                Corrections++;
            }
            State = server;                                                         // 서버 결과로 되감고
            for (ushort k = (ushort)(lastInputSeq + 1); k != (ushort)(seq + 1); ++k)
            {
                if (!dead) Movement.Step(ref State, inputs[k % History], level);   // 확인 안 된 입력을 다시 적용
                after[k % History] = State;
            }
        }
    }
}
