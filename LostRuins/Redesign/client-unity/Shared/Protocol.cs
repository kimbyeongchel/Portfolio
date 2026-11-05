// 서버 bitbuffer.h / protocol.h / reliability.h 와 같은 비트 배치. 필드 하나라도 어긋나면
// ParityTest가 C++이 만든 패킷을 읽다가 실패한다.
//
// GC 원칙 (1편 F9): 패킷마다 new를 하지 않는다. BitWriter·Snapshot·리스트는 만들어 두고 재사용한다.
using System;
using System.Collections.Generic;

namespace LostRuins.Shared
{
    public sealed class BitWriter
    {
        readonly byte[] buf;
        int bitPos;

        public BitWriter(int capacity = 1500) { buf = new byte[capacity]; }

        public byte[] Buffer { get { return buf; } }       // 앞에서 Length 바이트만 유효
        public int Length { get { return (bitPos + 7) >> 3; } }

        public void Reset() { Array.Clear(buf, 0, Length); bitPos = 0; }

        public void WriteBits(uint value, int bits)
        {
            for (int i = 0; i < bits; ++i)
            {
                if (((value >> i) & 1u) != 0) buf[bitPos >> 3] |= (byte)(1 << (bitPos & 7));
                ++bitPos;
            }
        }
        public void WriteU8(byte v) { WriteBits(v, 8); }
        public void WriteU16(ushort v) { WriteBits(v, 16); }
        public void WriteU32(uint v) { WriteBits(v, 32); }
        public void WriteI8(sbyte v) { WriteBits((byte)v, 8); }
        public void WriteFloat(float f) { WriteU32(FloatBits.ToUInt(f)); }

        public byte[] ToArray()   // 테스트·디버그용 (할당함)
        {
            var copy = new byte[Length];
            Array.Copy(buf, copy, Length);
            return copy;
        }
    }

    public sealed class BitReader
    {
        byte[] data;
        int size;
        int bitPos;
        public bool Ok = true;

        public BitReader(byte[] data, int size) { Reset(data, size); }

        public void Reset(byte[] newData, int newSize) { data = newData; size = newSize; bitPos = 0; Ok = true; }

        public uint ReadBits(int bits)
        {
            uint v = 0;
            for (int i = 0; i < bits; ++i)
            {
                if ((bitPos >> 3) >= size) { Ok = false; return 0; }   // 잘린 패킷
                if (((data[bitPos >> 3] >> (bitPos & 7)) & 1) != 0) v |= 1u << i;
                ++bitPos;
            }
            return v;
        }
        public byte ReadU8() { return (byte)ReadBits(8); }
        public ushort ReadU16() { return (ushort)ReadBits(16); }
        public uint ReadU32() { return ReadBits(32); }
        public sbyte ReadI8() { return unchecked((sbyte)(byte)ReadBits(8)); }
        public float ReadFloat() { return FloatBits.ToFloat(ReadU32()); }
        public float ReadQuantized(float min, float max, int bits)
        {
            uint steps = (1u << bits) - 1u;
            return min + (max - min) * ((float)ReadBits(bits) / steps);
        }
    }

    // float ↔ 비트 변환 (BitConverter.GetBytes는 매번 배열을 할당하므로 쓰지 않음)
    public static class FloatBits
    {
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
        struct Union
        {
            [System.Runtime.InteropServices.FieldOffset(0)] public float F;
            [System.Runtime.InteropServices.FieldOffset(0)] public uint U;
        }
        public static uint ToUInt(float f) { var u = new Union(); u.F = f; return u.U; }
        public static float ToFloat(uint v) { var u = new Union(); u.U = v; return u.F; }
    }

    public enum PacketType : byte { Connect = 1, Accept = 2, Input = 3, Snapshot = 4 }
    public enum EntityKind : byte { Player = 0, Monster = 1, Projectile = 2 }
    public enum EventType : byte { Hit = 1, Death = 2 }

    public struct Header { public PacketType Type; public ushort Seq, Ack; public uint AckBits; }

    public struct EntityState
    {
        public ushort Id; public EntityKind Kind; public float X, Y, Z; public byte Yaw, Action; public ushort Hp;
    }

    public struct GameEvent
    {
        public ushort Id; public EventType Type; public ushort Source, Target, Damage; public byte Crit; public float X, Y, Z;
    }

    public sealed class Snapshot
    {
        public ushort ServerTick, LastInputSeq;
        public MoveState Local;
        public ushort LocalHp;
        public readonly List<EntityState> Entities = new List<EntityState>(64);
        public readonly List<GameEvent> Events = new List<GameEvent>(32);
    }

    public static class Protocol
    {
        public const int MaxPacketBytes = 1200;
        public const int MaxInputsPerPacket = 4;
        public const int MaxEventsPerPacket = 32;
        const float WorldMin = -256.0f, WorldMax = 256.0f, HeightMax = 64.0f;

        public static void WriteHeader(BitWriter w, Header h)
        { w.WriteU8((byte)h.Type); w.WriteU16(h.Seq); w.WriteU16(h.Ack); w.WriteU32(h.AckBits); }

        public static Header ReadHeader(BitReader r)
        {
            Header h;
            byte t = r.ReadU8();
            if (t < 1 || t > 4) r.Ok = false;               // 범위 밖 값은 enum으로 바꾸지 않는다
            h.Type = (t < 1 || t > 4) ? PacketType.Connect : (PacketType)t;
            h.Seq = r.ReadU16(); h.Ack = r.ReadU16(); h.AckBits = r.ReadU32();
            return h;
        }

        public static void WriteInputs(BitWriter w, IList<InputCmd> inputs)
        {
            w.WriteBits((uint)inputs.Count, 3);
            for (int i = 0; i < inputs.Count; ++i)
            {
                var c = inputs[i];
                w.WriteU16(c.Seq); w.WriteI8(c.MoveX); w.WriteI8(c.MoveZ); w.WriteU16(c.Yaw); w.WriteU8(c.Buttons);
            }
        }

        public static MoveState ReadMoveState(BitReader r)
        {
            var s = new MoveState();
            s.Px = r.ReadFloat(); s.Py = r.ReadFloat(); s.Pz = r.ReadFloat();
            s.Vx = r.ReadFloat(); s.Vy = r.ReadFloat(); s.Vz = r.ReadFloat();
            s.Grounded = (byte)r.ReadBits(1); s.Coyote = (byte)r.ReadBits(2); s.JumpBuf = (byte)r.ReadBits(2);
            s.DodgeT = (byte)r.ReadBits(4); s.DodgeCd = (byte)r.ReadBits(5); s.PrevButtons = r.ReadU8();
            s.DodgeX = r.ReadI8(); s.DodgeZ = r.ReadI8();
            return s;
        }

        // into를 재사용한다 (리스트를 비우고 다시 채움)
        public static void ReadSnapshot(BitReader r, Snapshot into)
        {
            into.Entities.Clear();
            into.Events.Clear();
            into.ServerTick = r.ReadU16(); into.LastInputSeq = r.ReadU16();
            into.Local = ReadMoveState(r); into.LocalHp = (ushort)r.ReadBits(10);
            int n = r.ReadU8();
            for (int i = 0; i < n && r.Ok; ++i)
            {
                EntityState e;
                e.Id = r.ReadU16();
                uint kind = r.ReadBits(2);
                if (kind > 2) r.Ok = false;
                e.Kind = (EntityKind)(kind > 2 ? 0 : kind);
                e.X = r.ReadQuantized(WorldMin, WorldMax, 16);
                e.Z = r.ReadQuantized(WorldMin, WorldMax, 16);
                e.Y = r.ReadQuantized(0.0f, HeightMax, 12);
                e.Yaw = r.ReadU8(); e.Action = (byte)r.ReadBits(3); e.Hp = (ushort)r.ReadBits(10);
                into.Entities.Add(e);
            }
            int m = (int)r.ReadBits(6);
            if (m > MaxEventsPerPacket) { r.Ok = false; return; }
            for (int i = 0; i < m && r.Ok; ++i)
            {
                GameEvent ev;
                ev.Id = r.ReadU16();
                uint type = r.ReadBits(2);
                if (type < 1 || type > 2) r.Ok = false;
                ev.Type = (type < 1 || type > 2) ? EventType.Hit : (EventType)type;
                ev.Source = r.ReadU16(); ev.Target = r.ReadU16(); ev.Damage = (ushort)r.ReadBits(12); ev.Crit = (byte)r.ReadBits(1);
                ev.X = r.ReadQuantized(WorldMin, WorldMax, 16);
                ev.Z = r.ReadQuantized(WorldMin, WorldMax, 16);
                ev.Y = r.ReadQuantized(0.0f, HeightMax, 12);
                into.Events.Add(ev);
            }
        }

        public static bool SeqNewer(ushort a, ushort b)
        {
            return (a > b && a - b <= 32768) || (a < b && b - a > 32768);
        }
    }

    // 받은 패킷 번호 요약 (reliability.h AckTracker와 같은 규칙)
    public sealed class AckTracker
    {
        bool any; public ushort Ack; public uint Bits;

        public void OnReceived(ushort seq)
        {
            if (!any) { any = true; Ack = seq; Bits = 0; return; }
            if (seq == Ack) return;
            if (Protocol.SeqNewer(seq, Ack))
            {
                int shift = (ushort)(seq - Ack);
                if (shift >= 1 && shift <= 31) Bits = (Bits << shift) | (1u << (shift - 1));
                else if (shift == 32) Bits = 1u << 31;
                else Bits = 0;
                Ack = seq;
            }
            else
            {
                int back = (ushort)(Ack - seq);
                if (back >= 1 && back <= 32) Bits |= 1u << (back - 1);
            }
        }
    }

    // 같은 이벤트가 ACK 전까지 여러 패킷에 실려 오므로 한 번만 처리
    public sealed class EventDedup
    {
        readonly ushort[] ids = new ushort[1024];
        readonly bool[] valid = new bool[1024];

        public bool FirstTime(ushort id)
        {
            int i = id % 1024;
            if (valid[i] && ids[i] == id) return false;
            ids[i] = id; valid[i] = true;
            return true;
        }
    }
}
