// Unity 없이 mono로 실행하는 클라이언트 테스트. CI에서 C++ 서버 빌드 직후에 돌린다.
//  1) C++이 기록한 3000틱 입력을 C# Movement.Step에 넣고, 상태 14개 필드를 비트 단위로 비교
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

        Console.WriteLine("[1] movement parity C++ vs C#");
        var lv = Level.Default();
        var s = new MoveState();
        string[] ins = File.ReadAllLines(Path.Combine(dir, "inputs.txt"));
        string[] sts = File.ReadAllLines(Path.Combine(dir, "states.txt"));
        int mismatch = 0, firstBad = -1, airborne = 0, dodges = 0;
        for (int i = 0; i < ins.Length; ++i)
        {
            string[] f = ins[i].Split(' ');
            var cmd = new InputCmd { Seq = ushort.Parse(f[0]), MoveX = sbyte.Parse(f[1]), MoveZ = sbyte.Parse(f[2]), Yaw = ushort.Parse(f[3]), Buttons = byte.Parse(f[4]) };
            Movement.Step(ref s, cmd, lv);
            if (s.Grounded == 0) airborne++;
            if (s.DodgeT > 0) dodges++;
            string[] g = sts[i].Split(' ');
            uint[] want = new uint[6];
            for (int k = 0; k < 6; ++k) want[k] = uint.Parse(g[k], NumberStyles.HexNumber);
            bool same = MoveState.Bits(s.Px) == want[0] && MoveState.Bits(s.Py) == want[1] && MoveState.Bits(s.Pz) == want[2] &&
                        MoveState.Bits(s.Vx) == want[3] && MoveState.Bits(s.Vy) == want[4] && MoveState.Bits(s.Vz) == want[5] &&
                        s.Grounded == int.Parse(g[6]) && s.Coyote == int.Parse(g[7]) && s.JumpBuf == int.Parse(g[8]) &&
                        s.DodgeT == int.Parse(g[9]) && s.DodgeCd == int.Parse(g[10]) && s.PrevButtons == int.Parse(g[11]) &&
                        s.DodgeX == int.Parse(g[12]) && s.DodgeZ == int.Parse(g[13]);
            if (!same) { mismatch++; if (firstBad < 0) firstBad = i; }
        }
        Console.WriteLine("  ticks={0} airborne_ticks={1} dodge_ticks={2} mismatches={3} first_bad={4}", ins.Length, airborne, dodges, mismatch, firstBad);
        Check(ins.Length == 3000 && mismatch == 0, "3000 ticks bit-identical");

        Console.WriteLine("[2] read C++ snapshot packet");
        byte[] snapBytes = File.ReadAllBytes(Path.Combine(dir, "snapshot.bin"));
        var r = new BitReader(snapBytes, snapBytes.Length);
        Header h = Protocol.ReadHeader(r);
        var sn = new Snapshot();
        Protocol.ReadSnapshot(r, sn);
        Check(r.Ok && h.Type == PacketType.Snapshot && h.Seq == 9 && h.Ack == 65535 && h.AckBits == 0xA5A5A5A5u, "header");
        Check(sn.ServerTick == 4321 && sn.LastInputSeq == 777 && sn.LocalHp == 87, "tick / input seq / hp");
        Check(sn.Local.Px == 1.25f && sn.Local.Pz == -3.75f && sn.Local.Vy == 7.07106781f && sn.Local.DodgeT == 4 && sn.Local.DodgeX == -90, "local state (exact floats)");
        Check(sn.Entities.Count == 2 && sn.Entities[0].Id == 12 && sn.Entities[0].Kind == EntityKind.Monster && Math.Abs(sn.Entities[0].X - 10f) < 0.01f &&
              Math.Abs(sn.Entities[0].Z + 6.5f) < 0.01f && sn.Entities[0].Hp == 120 && sn.Entities[1].Kind == EntityKind.Projectile && Math.Abs(sn.Entities[1].Y - 1.4f) < 0.02f,
              "entities (quantized within 1cm)");
        Check(sn.Events.Count == 1 && sn.Events[0].Id == 65000 && sn.Events[0].Damage == 34 && sn.Events[0].Crit == 1 && sn.Events[0].Target == 12, "reliable event");

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
        var p = new LocalPlayerPredictor(lv, new MoveState());
        var shadow = new MoveState();
        for (int t = 0; t < 10; ++t)
        {
            var c = p.Predict(127, 0, 0, Buttons.None);
            Movement.Step(ref shadow, c, lv);
        }
        p.Reconcile(10, shadow, 100);
        Check(p.Corrections == 0, "matching server state -> no correction");
        var wrong = shadow; wrong.Px += 0.5f;
        p.Reconcile(10, wrong, 100);
        Check(p.Corrections == 1 && Math.Abs(p.LastCorrectionMeters - 0.5f) < 1e-4f, "mismatch -> 1 correction of 0.5m, replayed");
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

        Console.WriteLine(failures == 0 ? "ALL PASS" : failures + " FAILED");
        return failures == 0 ? 0 : 1;
    }
}
