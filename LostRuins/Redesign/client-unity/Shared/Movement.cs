// 서버 server/src/shared/movement.h 와 한 줄씩 대응하는 C# 구현.
// Unity API를 쓰지 않는다 → Unity 밖(테스트, 서버 도구)에서도 컴파일되고, C++과 비트 단위로 비교할 수 있다.
using System;
using System.Collections.Generic;

namespace LostRuins.Shared
{
    public static class Rules
    {
        public const int TickRate = 30;
        public const float Dt = 0.0333333351f;               // C++ kDt 와 같은 리터럴
        public const float MoveSpeed = 5.0f, SprintSpeed = 10.0f, AimSpeed = 2.0f, DodgeSpeed = 12.0f;
        public const float Gravity = 20.0f;
        public const float JumpVelocity = 7.07106781f;
        public const byte CoyoteTicks = 3, JumpBufferTicks = 3, DodgeTicks = 10, DodgeCooldownTicks = 30;
        public const float Radius = 0.4f, Height = 1.8f;
    }

    [Flags]
    public enum Buttons : byte
    {
        None = 0, Jump = 1, Dodge = 2, Sprint = 4, Attack = 8, Aim = 16, SkillQ = 32, SkillE = 64, SkillF = 128
    }

    public struct InputCmd
    {
        public ushort Seq;
        public sbyte MoveX, MoveZ;
        public ushort Yaw;
        public byte Buttons;
    }

    public struct Box
    {
        public float MinX, MinY, MinZ, MaxX, MaxY, MaxZ;
        public Box(float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
        { MinX = minX; MinY = minY; MinZ = minZ; MaxX = maxX; MaxY = maxY; MaxZ = maxZ; }
    }

    public sealed class Level
    {
        public readonly List<Box> Boxes = new List<Box>();

        public static Level Default()
        {
            var lv = new Level();
            lv.Boxes.Add(new Box(4.0f, 0.0f, 4.0f, 7.0f, 1.0f, 7.0f));
            lv.Boxes.Add(new Box(-8.0f, 0.0f, -2.0f, -5.0f, 2.0f, 6.0f));
            lv.Boxes.Add(new Box(10.0f, 0.0f, -10.0f, 14.0f, 0.5f, -6.0f));
            lv.Boxes.Add(new Box(-3.0f, 0.0f, -12.0f, 3.0f, 3.0f, -10.0f));
            return lv;
        }
    }

    public struct MoveState
    {
        public float Px, Py, Pz, Vx, Vy, Vz;
        public byte Grounded, Coyote, JumpBuf, DodgeT, DodgeCd, PrevButtons;
        public sbyte DodgeX, DodgeZ;

        public bool BitwiseEquals(MoveState o)
        {
            return Bits(Px) == Bits(o.Px) && Bits(Py) == Bits(o.Py) && Bits(Pz) == Bits(o.Pz) &&
                   Bits(Vx) == Bits(o.Vx) && Bits(Vy) == Bits(o.Vy) && Bits(Vz) == Bits(o.Vz) &&
                   Grounded == o.Grounded && Coyote == o.Coyote && JumpBuf == o.JumpBuf && DodgeT == o.DodgeT &&
                   DodgeCd == o.DodgeCd && PrevButtons == o.PrevButtons && DodgeX == o.DodgeX && DodgeZ == o.DodgeZ;
        }

        public static uint Bits(float f) { return FloatBits.ToUInt(f); }   // 할당 없는 변환
    }

    // 스킬이 이번 틱 이동에 주는 영향 (C++ MoveMods). Abilities.StepAbility가 만든다. default = 영향 없음
    public struct MoveMods
    {
        public byte Lock;          // 선딜·후딜: 이동·점프·구르기 금지
        public byte NoAim;         // 우클릭이 조준(감속)이 아닌 직업 (창술사)
        public float Speed;        // 0보다 크면 이 속도로만 이동 (충전·시전 중). 점프·구르기 금지
        public float DashSpeed;    // 0보다 크면 Dash 방향으로 강제 전진
        public sbyte DashX, DashZ;
    }

    public static class Movement
    {
        static bool Overlaps(Box b, float x, float y, float z)
        {
            return x + Rules.Radius > b.MinX && x - Rules.Radius < b.MaxX &&
                   z + Rules.Radius > b.MinZ && z - Rules.Radius < b.MaxZ &&
                   y + Rules.Height > b.MinY && y < b.MaxY;
        }

        public static void Step(ref MoveState s, InputCmd input, Level lv) { Step(ref s, input, lv, default(MoveMods)); }

        public static void Step(ref MoveState s, InputCmd input, Level lv, MoveMods m)
        {
            byte pressed = (byte)(input.Buttons & ~s.PrevButtons);
            s.PrevButtons = input.Buttons;
            bool busy = m.Lock != 0 || m.Speed > 0.0f || m.DashSpeed > 0.0f;

            if ((pressed & (byte)Buttons.Jump) != 0) s.JumpBuf = Rules.JumpBufferTicks;
            else if (s.JumpBuf > 0) s.JumpBuf--;
            if (s.Grounded != 0) s.Coyote = Rules.CoyoteTicks;
            else if (s.Coyote > 0) s.Coyote--;
            if (s.DodgeCd > 0) s.DodgeCd--;

            if ((pressed & (byte)Buttons.Dodge) != 0 && !busy && s.Grounded != 0 && s.DodgeT == 0 && s.DodgeCd == 0 &&
                (input.MoveX != 0 || input.MoveZ != 0))
            {
                s.DodgeT = Rules.DodgeTicks; s.DodgeCd = Rules.DodgeCooldownTicks;
                s.DodgeX = input.MoveX; s.DodgeZ = input.MoveZ;
            }

            float wx = 0, wz = 0, speed = 0;
            if (s.DodgeT > 0)
            {
                wx = (float)s.DodgeX / 127.0f; wz = (float)s.DodgeZ / 127.0f; speed = Rules.DodgeSpeed; s.DodgeT--;
            }
            else if (m.DashSpeed > 0.0f)
            {
                wx = (float)m.DashX / 127.0f; wz = (float)m.DashZ / 127.0f; speed = m.DashSpeed;
            }
            else if (m.Lock != 0)
            {
                speed = 0.0f;
            }
            else
            {
                wx = (float)input.MoveX / 127.0f; wz = (float)input.MoveZ / 127.0f;
                if (m.Speed > 0.0f) speed = m.Speed;
                else if ((input.Buttons & (byte)Buttons.Aim) != 0 && m.NoAim == 0) speed = Rules.AimSpeed;
                else speed = (input.Buttons & (byte)Buttons.Sprint) != 0 ? Rules.SprintSpeed : Rules.MoveSpeed;
            }
            float len2 = wx * wx + wz * wz;
            if (len2 > 1.0f) { float inv = 1.0f / (float)Math.Sqrt(len2); wx *= inv; wz *= inv; }
            s.Vx = wx * speed;
            s.Vz = wz * speed;

            if (s.JumpBuf > 0 && s.Coyote > 0 && s.DodgeT == 0 && !busy)
            {
                s.Vy = Rules.JumpVelocity; s.JumpBuf = 0; s.Coyote = 0;
            }
            s.Vy -= Rules.Gravity * Rules.Dt;

            float nx = s.Px + s.Vx * Rules.Dt;
            foreach (var b in lv.Boxes)
                if (Overlaps(b, nx, s.Py, s.Pz)) nx = s.Vx > 0.0f ? b.MinX - Rules.Radius : b.MaxX + Rules.Radius;
            s.Px = nx;

            float nz = s.Pz + s.Vz * Rules.Dt;
            foreach (var b in lv.Boxes)
                if (Overlaps(b, s.Px, s.Py, nz)) nz = s.Vz > 0.0f ? b.MinZ - Rules.Radius : b.MaxZ + Rules.Radius;
            s.Pz = nz;

            float ny = s.Py + s.Vy * Rules.Dt;
            s.Grounded = 0;
            if (ny <= 0.0f) { ny = 0.0f; s.Vy = 0.0f; s.Grounded = 1; }
            foreach (var b in lv.Boxes)
            {
                if (Overlaps(b, s.Px, ny, s.Pz))
                {
                    if (s.Vy <= 0.0f) { ny = b.MaxY; s.Grounded = 1; } else { ny = b.MinY - Rules.Height; }
                    s.Vy = 0.0f;
                }
            }
            s.Py = ny;
        }
    }
}
