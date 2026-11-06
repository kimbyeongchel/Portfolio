// 씬에 하나. 30Hz 고정 틱으로 입력 → 예측 → 전송, 매 프레임 스냅샷 수신 → 재조정 → 그리기.
// 예전 PlayerState(535줄)가 하던 일 중 "규칙"은 전부 서버로 갔고, 여기엔 순서 조율만 남는다.
using System.Collections.Generic;
using LostRuins.Shared;
using UnityEngine;

namespace LostRuins.Client
{
    public sealed class GameClient : MonoBehaviour
    {
        [SerializeField] string host = "127.0.0.1";
        [SerializeField] int port = 27015;
        [SerializeField] InputReader input;
        [SerializeField] ClientCamera cam;
        [SerializeField] LocalPlayerView localView;
        [SerializeField] EntityViewRegistry views;
        [SerializeField] CombatPresenter combat;
        [SerializeField] HealthView health;
        [SerializeField] SkillBarView skillBar;
        [SerializeField] ClassId playerClass = ClassId.Archer;   // 로비에서 고른 직업 (서버가 Accept로 확정)

        readonly Level level = Level.Default();
        readonly SnapshotInterpolator interp = new SnapshotInterpolator(3f);
        readonly List<EntityState> sampled = new List<EntityState>(64);
        readonly List<InputCmd> recent = new List<InputCmd>(Protocol.MaxInputsPerPacket);
        NetClient net;
        LocalPlayerPredictor predictor;
        EventScheduler events;
        System.Action<GameEvent> showEvent;
        MoveState prevTickState;          // 틱 사이 보간용 (렌더 60~144fps, 시뮬레이션 30Hz)
        Vector3 correctionOffset;         // 재조정으로 생긴 차이를 순간이동 대신 0.1초에 걸쳐 흡수
        float accumulator;
        int lastHp = -1, lastShield = -1, tickBase, lastRawTick = -1;

        void Awake()
        {
            // 인스펙터 연결이 빠지면 첫 프레임에 NullReference가 나며 원인을 찾기 어렵다 → 시작할 때 바로 알린다
            if (input == null || cam == null || localView == null || views == null || combat == null || health == null || skillBar == null)
            {
                Debug.LogError("GameClient: 인스펙터에서 참조가 빠졌습니다 (input, cam, localView, views, combat, health, skillBar)", this);
                enabled = false;
                return;
            }
            net = new NetClient(host, port, playerClass);
            net.SnapshotReceived += OnSnapshot;
            showEvent = combat.Show;                  // 데미지 숫자·피격 이펙트는 서버 이벤트로만 (F3)
            net.EventReceived += OnEvent;
        }

        void OnDestroy() { if (net != null) net.Dispose(); }

        void Update()
        {
            net.Poll(Time.deltaTime);
            if (!net.Connected) return;
            if (predictor == null)
            {
                predictor = new LocalPlayerPredictor(level, net.SpawnState, net.Class);
                prevTickState = net.SpawnState;
                skillBar.Bind(Abilities.Timing(net.Class));
                events = new EventScheduler(net.PlayerId);
            }

            accumulator += Time.deltaTime;
            while (accumulator >= Rules.Dt) { accumulator -= Rules.Dt; FixedTick(); }

            correctionOffset = Vector3.Lerp(correctionOffset, Vector3.zero, 1f - Mathf.Exp(-Time.deltaTime / 0.1f));
            float alpha = accumulator / Rules.Dt;
            Vector3 pos = Vector3.Lerp(ToVec(prevTickState), ToVec(predictor.State), alpha) + correctionOffset;
            localView.Render(pos, predictor.State);

            interp.Sample(Time.deltaTime, sampled);
            views.Apply(sampled, net.PlayerId);
            events.Release(interp.RenderTick, tickBase + lastRawTick, showEvent);   // 보이는 모습과 같은 시각에 숫자
        }

        void FixedTick()
        {
            prevTickState = predictor.State;
            Vector2 move = input.Move;
            Vector3 world = cam.YawRotation * new Vector3(move.x, 0f, move.y);   // 카메라 기준 → 월드 방향
            var cmd = predictor.Predict(Quantize(world.x), Quantize(world.z), cam.YawU16, input.ConsumeButtons());
            predictor.RecentInputs(Protocol.MaxInputsPerPacket, recent);
            net.SendInputs(recent);
            localView.OnTick(predictor.State, cmd, predictor.LastResult);
            skillBar.Set(predictor.Ability);          // 쿨타임도 예측값 → 누르는 순간 바로 회색
        }

        void OnSnapshot(Snapshot s)
        {
            Vector3 before = ToVec(predictor != null ? predictor.State : s.Local);
            if (predictor != null)
            {
                predictor.Reconcile(s.LastInputSeq, s.Local, s.Ability, s.LocalHp);
                correctionOffset += before - ToVec(predictor.State);
            }
            interp.Add(Unwrap(s.ServerTick), s.Entities);
            if (s.LocalHp != lastHp) { lastHp = s.LocalHp; health.Set(s.LocalHp); }   // 바뀔 때만 UI 갱신 (F9)
            if (s.LocalShield != lastShield) { lastShield = s.LocalShield; health.SetShield(s.LocalShield); }
        }

        void OnEvent(GameEvent e)
        {
            if (events == null) { showEvent(e); return; }
            events.Enqueue(e, Unwrap(net.LastSnapshotTick), showEvent);
        }

        int Unwrap(ushort raw)   // 16비트 틱(36분마다 순환)을 계속 증가하는 int로
        {
            if (lastRawTick >= 0 && raw < lastRawTick && lastRawTick - raw > 32768) tickBase += 65536;
            lastRawTick = raw;
            return tickBase + raw;
        }

        static sbyte Quantize(float v) { return (sbyte)Mathf.Clamp(Mathf.RoundToInt(v * 127f), -127, 127); }
        static Vector3 ToVec(MoveState s) { return new Vector3(s.Px, s.Py, s.Pz); }
    }
}
