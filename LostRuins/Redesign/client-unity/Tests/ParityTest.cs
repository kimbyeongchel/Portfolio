// Unity 없이 mono로 실행하는 클라이언트 테스트. CI에서 C++ 서버 빌드 직후에 돌린다.
//  1) C++이 기록한 직업 4개 × 3000틱 입력을 C# Abilities.SimStep에 넣고, 이동 14 + 스킬 15 필드를 비트 단위로 비교
//  2) C++이 만든 스냅샷 패킷을 C# Protocol로 읽어 값 확인
//  3) C#이 입력 패킷을 써서 C++이 읽게 함
//  4) 망가진 패킷 거절, ACK 비트필드, 예측기·보간기 동작 확인
// 실행: mono ParityTest.exe <C++ lr_parity가 만든 파일이 있는 폴더>
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using LostRuins.Client;
using LostRuins.Shared;

static class ParityTest
{
    static int failures;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "  PASS " : "  FAIL ") + what); if (!ok) failures++; }

    static int Main(string[] args)
    {
        string dir = args.Length > 0 ? args[0] : ".";

        Console.WriteLine("[1] movement + ability parity C++ vs C# (4 classes)");
        var lv = Level.Default();
        string[] ins = File.ReadAllLines(Path.Combine(dir, "inputs.txt"));
        string[] sts = File.ReadAllLines(Path.Combine(dir, "states.txt"));
        var perClass = new int[4];
        var badClass = new int[4];
        int mismatch = 0, firstBad = -1, airborne = 0, dodges = 0, skillTicks = 0, charging = 0, combos = 0;
        var s = new MoveState();
        var ab = AbilityState.Idle();
        int curClass = -1;
        for (int i = 0; i < ins.Length; ++i)
        {
            string[] f = ins[i].Split(' ');
            int cls = int.Parse(f[0]);
            if (cls != curClass) { curClass = cls; s = new MoveState(); ab = AbilityState.Idle(); }   // 직업마다 처음부터
            var cmd = new InputCmd { Seq = ushort.Parse(f[1]), MoveX = sbyte.Parse(f[2]), MoveZ = sbyte.Parse(f[3]), Yaw = ushort.Parse(f[4]), Buttons = byte.Parse(f[5]) };
            Abilities.SimStep(ref s, ref ab, Abilities.Timing((ClassId)cls), cmd, lv);
            perClass[cls]++;
            if (s.Grounded == 0) airborne++;
            if (s.DodgeT > 0) dodges++;
            if (ab.Slot != Abilities.NoSlot) skillTicks++;
            if (ab.Phase == Abilities.PhaseCharge) charging++;
            if (ab.Step > 0) combos++;
            string[] g = sts[i].Split(' ');
            uint[] want = new uint[6];
            for (int k = 0; k < 6; ++k) want[k] = uint.Parse(g[k], NumberStyles.HexNumber);
            Func<int, int> I = k => int.Parse(g[k]);
            bool same = MoveState.Bits(s.Px) == want[0] && MoveState.Bits(s.Py) == want[1] && MoveState.Bits(s.Pz) == want[2] &&
                        MoveState.Bits(s.Vx) == want[3] && MoveState.Bits(s.Vy) == want[4] && MoveState.Bits(s.Vz) == want[5] &&
                        s.Grounded == I(6) && s.Coyote == I(7) && s.JumpBuf == I(8) && s.DodgeT == I(9) && s.DodgeCd == I(10) &&
                        s.PrevButtons == I(11) && s.DodgeX == I(12) && s.DodgeZ == I(13) &&
                        ab.Slot == I(14) && ab.Phase == I(15) && ab.Tick == I(16) && ab.Step == I(17) && ab.Queued == I(18) &&
                        ab.Charge == I(19) && ab.LastX == I(20) && ab.LastZ == I(21) && ab.DashX == I(22) && ab.DashZ == I(23) &&
                        ab.Cd0 == I(24) && ab.Cd1 == I(25) && ab.Cd2 == I(26) && ab.Cd3 == I(27) && ab.Cd4 == I(28);
            if (!same) { mismatch++; badClass[cls]++; if (firstBad < 0) firstBad = i; }
        }
        Console.WriteLine("  ticks={0} airborne={1} dodge={2} skill={3} charging={4} combo_steps={5} mismatches={6} first_bad={7}",
                          ins.Length, airborne, dodges, skillTicks, charging, combos, mismatch, firstBad);
        string[] names = { "warrior", "archer", "lancer", "healer" };
        for (int c = 0; c < 4; ++c) Check(perClass[c] == 3000 && badClass[c] == 0, names[c] + ": 3000 ticks bit-identical (move + ability)");
        Check(skillTicks > 1000 && charging > 50 && combos > 50, "sequence actually exercises skills, charging and combos");

        Console.WriteLine("[2] read C++ snapshot packet");
        byte[] snapBytes = File.ReadAllBytes(Path.Combine(dir, "snapshot.bin"));
        var r = new BitReader(snapBytes, snapBytes.Length);
        Header h = Protocol.ReadHeader(r);
        var sn = new Snapshot();
        Protocol.ReadSnapshot(r, sn);
        Check(r.Ok && h.Type == PacketType.Snapshot && h.Seq == 9 && h.Ack == 65535 && h.AckBits == 0xA5A5A5A5u, "header");
        Check(sn.ServerTick == 4321 && sn.LastInputSeq == 777 && sn.LocalHp == 87 && sn.LocalShield == 55 && sn.LocalFlags == 3, "tick / input seq / hp / shield / flags");
        Check(sn.Ability.Slot == Abilities.SlotE && sn.Ability.Phase == Abilities.PhaseActive && sn.Ability.Tick == 9 && sn.Ability.LastX == -64 &&
              sn.Ability.DashZ == 100 && sn.Ability.Cd2 == 287 && sn.Ability.Cd3 == 141 && sn.Ability.Cd4 == 1023, "local ability state (10-bit cooldowns)");
        Check(sn.Local.Px == 1.25f && sn.Local.Pz == -3.75f && sn.Local.Vy == 7.07106781f && sn.Local.DodgeT == 4 && sn.Local.DodgeX == -90, "local state (exact floats)");
        Check(sn.Entities.Count == 4 && sn.Entities[0].Id == 12 && sn.Entities[0].Kind == EntityKind.Monster && Math.Abs(sn.Entities[0].X - 10f) < 0.01f &&
              Math.Abs(sn.Entities[0].Z + 6.5f) < 0.01f && sn.Entities[0].Hp == 120 && sn.Entities[1].Kind == EntityKind.Projectile && Math.Abs(sn.Entities[1].Y - 1.4f) < 0.02f,
              "entities (quantized within 1cm)");
        Check(sn.Entities[1].Cls == 1 && sn.Entities[1].Skill == 7 && sn.Entities[2].Kind == EntityKind.Player && sn.Entities[2].Cls == 2 &&
              sn.Entities[2].Skill == 12 && sn.Entities[3].Kind == EntityKind.Zone && sn.Entities[3].Hp == 45, "class / skill code / zone entity");
        Check(sn.Events.Count == 2 && sn.Events[0].Id == 65000 && sn.Events[0].Damage == 34 && sn.Events[0].Crit == 1 && sn.Events[0].Target == 12, "reliable event");
        Check(sn.Events[1].Type == EventType.Block && sn.Events[1].Damage == 8, "3-bit event type (Block = 6)");
        byte[] accBytes = File.ReadAllBytes(Path.Combine(dir, "accept.bin"));
        var ar = new BitReader(accBytes, accBytes.Length);
        Header ah = Protocol.ReadHeader(ar);
        var acc = Protocol.ReadAccept(ar);
        Check(ar.Ok && ah.Type == PacketType.Accept && acc.PlayerId == 3 && acc.Cls == ClassId.Lancer && acc.Spawn.Px == 4.0f, "accept packet carries class + spawn");

        Console.WriteLine("[3] write input packet for C++");
        var w = new BitWriter();
        Protocol.WriteHeader(w, new Header { Type = PacketType.Input, Seq = 1234, Ack = 4321, AckBits = 0x0F0F0F0Fu });
        Protocol.WriteInputs(w, new List<InputCmd> {
            new InputCmd { Seq = 65535, MoveX = -127, MoveZ = 64, Yaw = 40000, Buttons = (byte)(Buttons.Jump | Buttons.Sprint) },
            new InputCmd { Seq = 0, MoveX = 5, MoveZ = 0, Yaw = 0, Buttons = (byte)Buttons.SkillF } });
        File.WriteAllBytes(Path.Combine(dir, "input.bin"), w.ToArray());
        byte[] first = w.ToArray();
        w.Reset();                                                   // 재사용해도 같은 바이트가 나와야 함
        Protocol.WriteHeader(w, new Header { Type = PacketType.Input, Seq = 1234, Ack = 4321, AckBits = 0x0F0F0F0Fu });
        Protocol.WriteInputs(w, new List<InputCmd> {
            new InputCmd { Seq = 65535, MoveX = -127, MoveZ = 64, Yaw = 40000, Buttons = (byte)(Buttons.Jump | Buttons.Sprint) },
            new InputCmd { Seq = 0, MoveX = 5, MoveZ = 0, Yaw = 0, Buttons = (byte)Buttons.SkillF } });
        Check(w.Length == first.Length && Convert.ToBase64String(w.ToArray()) == Convert.ToBase64String(first), "BitWriter.Reset() reuse gives identical bytes");
        var cw = new BitWriter();
        Protocol.WriteHeader(cw, new Header { Type = PacketType.Connect, Seq = 0 });
        Protocol.WriteConnect(cw, ClassId.Healer);
        File.WriteAllBytes(Path.Combine(dir, "connect.bin"), cw.ToArray());

        Console.WriteLine("[4] robustness");
        var bad = new BitReader(snapBytes, snapBytes.Length / 2);
        Protocol.ReadHeader(bad);
        Protocol.ReadSnapshot(bad, new Snapshot());
        Check(!bad.Ok, "truncated snapshot rejected");
        var bw = new BitWriter(); bw.WriteU8(9); bw.WriteU16(0); bw.WriteU16(0); bw.WriteU32(0);
        var br = new BitReader(bw.ToArray(), bw.Length);
        Protocol.ReadHeader(br);
        Check(!br.Ok, "unknown packet type rejected");
        var ack = new AckTracker();
        ack.OnReceived(65534); ack.OnReceived(65535); ack.OnReceived(0); ack.OnReceived(2);
        Check(ack.Ack == 2 && ack.Bits == 14u, "ack bits across wrap (same expectation as C++ test)");
        ack.OnReceived(1);
        Check(ack.Bits == 15u, "late packet fills its bit");

        Console.WriteLine("[5] predictor + interpolator");
        var p = new LocalPlayerPredictor(lv, new MoveState(), ClassId.Warrior);
        var shadow = new MoveState();
        var shadowAb = AbilityState.Idle();
        for (int t = 0; t < 10; ++t)
        {
            var c = p.Predict(127, 0, 0, t == 3 ? Buttons.Attack : Buttons.None);
            Abilities.SimStep(ref shadow, ref shadowAb, Abilities.Timing(ClassId.Warrior), c, lv);
        }
        p.Reconcile(10, shadow, shadowAb, 100);
        Check(p.Corrections == 0, "matching server state (move + ability) -> no correction");
        Check(p.Ability.Slot == Abilities.SlotBasic && p.LastResult.Mods.Lock == 1, "attack predicted instantly and locks movement (no server wait)");
        var wrongAb = shadowAb; wrongAb.Cd2 = 99;
        p.Reconcile(10, shadow, wrongAb, 100);
        Check(p.Corrections == 1 && p.Ability.Cd2 == 99, "ability-only mismatch -> corrected from server");
        var wrong = shadow; wrong.Px += 0.5f;
        p.Reconcile(10, wrong, wrongAb, 100);
        Check(p.Corrections == 2 && Math.Abs(p.LastCorrectionMeters - 0.5f) < 1e-4f, "position mismatch -> correction of 0.5m, replayed");
        p.Reconcile(10, wrong, wrongAb, 100);
        Check(p.Corrections == 2, "same snapshot again -> no double count (baseline overwritten on rewind)");
        var archer = new LocalPlayerPredictor(lv, new MoveState(), ClassId.Archer);
        for (int t = 0; t < 14; ++t) archer.Predict(0, 0, 0, Buttons.Attack);
        Check(archer.Ability.Phase == Abilities.PhaseCharge && archer.Ability.Charge == 14, "archer: holding = charging (14 ticks)");
        archer.Predict(0, 0, 0, Buttons.None);
        Check(archer.LastResult.Phase == Abilities.PhaseActive && archer.Ability.Cd0 == 30, "archer: release fires the same tick, cooldown 30");
        var recent = new List<InputCmd>();
        p.RecentInputs(4, recent);
        Check(recent.Count == 4 && recent[0].Seq == 7 && recent[3].Seq == 10, "recent inputs oldest-first");
        var interp = new SnapshotInterpolator(3f);
        interp.Add(100, new List<EntityState> { new EntityState { Id = 7, X = 0f, Yaw = 250 } });
        interp.Add(101, new List<EntityState> { new EntityState { Id = 7, X = 1f, Yaw = 6 } });
        var outList = new List<EntityState>();
        interp.Sample(3.5f / Rules.TickRate, outList);
        Check(outList.Count == 1 && outList[0].X > 0.3f && outList[0].X < 0.7f, "midpoint between snapshots");
        Check(outList[0].Yaw >= 250 || outList[0].Yaw <= 6, "yaw wraps the short way (250 -> 6)");

        Console.WriteLine("[6] event timing");
        var shown = new List<ushort>();
        Action<GameEvent> show = e => shown.Add(e.Id);
        var sched = new EventScheduler(7);
        sched.Enqueue(new GameEvent { Id = 1, Source = 7 }, 100, show);     // 내 공격 결과
        sched.Enqueue(new GameEvent { Id = 2, Source = 3 }, 100, show);     // 동료·몬스터 쪽 결과
        Check(shown.Count == 1 && shown[0] == 1, "my own hit shows immediately");
        sched.Release(97.5, 100, show);
        Check(shown.Count == 1, "others' event waits while render clock is behind (100ms interpolation)");
        sched.Release(100.2, 103, show);
        Check(shown.Count == 2 && shown[1] == 2 && sched.Count == 0, "released when render clock reaches its tick");
        sched.Enqueue(new GameEvent { Id = 3, Source = 3 }, 200, show);
        sched.Release(150, 216, show);
        Check(shown.Count == 3, "never held more than 15 ticks even if the clock stalls");

        Console.WriteLine(failures == 0 ? "ALL PASS" : failures + " FAILED");
        return failures == 0 ? 0 : 1;
    }
}
