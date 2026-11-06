// 서버 server/src/shared/abilities.h 와 한 줄씩 대응하는 C# 구현 (스킬 타이밍: 쿨타임, 선딜·판정·후딜, 콤보, 충전).
// 데미지·명중·회복은 여기 없다. 클라는 "언제 휘두르나"만 예측하고 "맞았나"는 서버 이벤트로 받는다.
//
// C# 주의점: AbilityState는 struct다. struct 안에 배열(ushort[])을 넣으면 복사해도 같은 배열을 가리켜서
// 예측 기록(after[])이 전부 같은 쿨타임을 공유하게 된다. 그래서 쿨타임을 필드 5개로 풀어 둔다.
namespace LostRuins.Shared
{
    public enum ClassId : byte { Warrior = 0, Archer = 1, Lancer = 2, Healer = 3 }

    public struct Frames
    {
        public byte Windup, Active, Recovery, CancelAfter;
        public Frames(byte w, byte a, byte r, byte c) { Windup = w; Active = a; Recovery = r; CancelAfter = c; }
    }

    public sealed class SkillTiming
    {
        public byte Button;
        public ushort Cooldown;
        public byte StepCount = 1;
        public Frames[] Steps = new Frames[3];
        public byte ChargeMin, ChargeMax;
        public float ChargeSpeed;
        public byte Move;            // Abilities.MoveFree / MoveLocked / MoveSlowed
        public float CastSpeed, DashSpeed;
        public bool NeedGround;
    }

    public sealed class ClassTiming
    {
        public SkillTiming[] Skills;
        public bool RightClickIsSkill;
    }

    public struct AbilityState
    {
        public byte Slot, Phase, Tick, Step, Queued, Charge;
        public sbyte LastX, LastZ, DashX, DashZ;
        public ushort Cd0, Cd1, Cd2, Cd3, Cd4;

        public static AbilityState Idle()
        {
            var a = new AbilityState();
            a.Slot = Abilities.NoSlot;
            a.LastZ = 127;
            return a;
        }

        public ushort GetCd(int i)
        {
            switch (i) { case 0: return Cd0; case 1: return Cd1; case 2: return Cd2; case 3: return Cd3; default: return Cd4; }
        }

        public void SetCd(int i, ushort v)
        {
            switch (i) { case 0: Cd0 = v; break; case 1: Cd1 = v; break; case 2: Cd2 = v; break; case 3: Cd3 = v; break; default: Cd4 = v; break; }
        }

        public bool SameAs(AbilityState o)
        {
            return Slot == o.Slot && Phase == o.Phase && Tick == o.Tick && Step == o.Step && Queued == o.Queued &&
                   Charge == o.Charge && LastX == o.LastX && LastZ == o.LastZ && DashX == o.DashX && DashZ == o.DashZ &&
                   Cd0 == o.Cd0 && Cd1 == o.Cd1 && Cd2 == o.Cd2 && Cd3 == o.Cd3 && Cd4 == o.Cd4;
        }
    }

    public struct AbilityResult
    {
        public MoveMods Mods;
        public byte Slot, Phase, Tick, Step, Charge;
    }

    public static class Abilities
    {
        public const byte SlotBasic = 0, SlotAlt = 1, SlotQ = 2, SlotE = 3, SlotF = 4, SlotCount = 5, NoSlot = 7;
        public const byte PhaseNone = 0, PhaseCharge = 1, PhaseWindup = 2, PhaseActive = 3, PhaseRecovery = 4;
        public const byte MoveFree = 0, MoveLocked = 1, MoveSlowed = 2;
        static readonly byte[] Order = { SlotF, SlotE, SlotQ, SlotAlt, SlotBasic };

        static void End(ref AbilityState a)
        {
            a.Slot = NoSlot; a.Phase = PhaseNone; a.Tick = 0; a.Step = 0; a.Queued = 0; a.Charge = 0;
        }

        static void Fire(ref AbilityState a, SkillTiming s)
        {
            a.Phase = PhaseActive;
            a.Tick = 1;
            if (s.ChargeMax > 0) a.SetCd(a.Slot, s.Cooldown);       // 충전 스킬은 실제로 쐈을 때 쿨타임 시작
        }

        static void BeginWindup(ref AbilityState a, SkillTiming s)
        {
            a.Phase = PhaseWindup;
            a.Tick = 0;
            if (s.Steps[a.Step].Windup == 0) Fire(ref a, s);
        }

        // 한 틱 진행. mv는 이번 틱 이동 "전" 상태 (PrevButtons로 새로 눌린 버튼 계산)
        public static AbilityResult StepAbility(ref AbilityState a, ClassTiming c, InputCmd input, MoveState mv)
        {
            var r = new AbilityResult();
            r.Mods.NoAim = (byte)(c.RightClickIsSkill ? 1 : 0);
            byte pressed = (byte)(input.Buttons & ~mv.PrevButtons);
            bool moving = input.MoveX != 0 || input.MoveZ != 0;
            if (moving) { a.LastX = input.MoveX; a.LastZ = input.MoveZ; }
            for (int i = 0; i < SlotCount; ++i)
            {
                ushort cd = a.GetCd(i);
                if (cd > 0) a.SetCd(i, (ushort)(cd - 1));
            }

            if (a.Slot != NoSlot)
            {
                SkillTiming s = c.Skills[a.Slot];
                Frames f = s.Steps[a.Step];
                bool comboPress = (pressed & s.Button) != 0 && a.Step + 1 < s.StepCount;
                switch (a.Phase)
                {
                    case PhaseCharge:
                    {
                        bool dodged = (pressed & (byte)Buttons.Dodge) != 0;
                        if (!dodged && (input.Buttons & s.Button) != 0) { if (a.Charge < s.ChargeMax) ++a.Charge; }
                        else if (!dodged && a.Charge >= s.ChargeMin) BeginWindup(ref a, s);
                        else End(ref a);                                // 구르기 또는 너무 일찍 뗌 → 취소
                        break;
                    }
                    case PhaseWindup:
                        ++a.Tick;
                        if (a.Tick >= f.Windup) Fire(ref a, s);
                        break;
                    case PhaseActive:
                        if (comboPress) a.Queued = 1;
                        if (a.Tick >= f.Active) { a.Phase = PhaseRecovery; a.Tick = 0; }
                        else ++a.Tick;
                        break;
                    case PhaseRecovery:
                        if (comboPress) a.Queued = 1;
                        ++a.Tick;
                        if (a.Queued != 0) { a.Queued = 0; ++a.Step; BeginWindup(ref a, s); }
                        else if (a.Tick > f.Recovery ||
                                 (a.Tick > f.CancelAfter && (moving || (pressed & (byte)(Buttons.Jump | Buttons.Dodge)) != 0)))
                            End(ref a);
                        break;
                    default:
                        End(ref a);
                        break;
                }
            }

            if (a.Slot == NoSlot && mv.DodgeT == 0)
            {
                foreach (byte slot in Order)
                {
                    SkillTiming s = c.Skills[slot];
                    if (s.Button == 0 || (pressed & s.Button) == 0 || a.GetCd(slot) != 0) continue;
                    if (s.NeedGround && mv.Grounded == 0) continue;
                    a.Slot = slot; a.Step = 0; a.Queued = 0; a.Charge = 0; a.Tick = 0;
                    a.DashX = a.LastX; a.DashZ = a.LastZ;
                    if (s.ChargeMax > 0) { a.Phase = PhaseCharge; a.Charge = 1; }
                    else { a.SetCd(slot, s.Cooldown); BeginWindup(ref a, s); }
                    break;
                }
            }

            if (a.Slot != NoSlot)
            {
                SkillTiming s = c.Skills[a.Slot];
                if (a.Phase == PhaseCharge) r.Mods.Speed = s.ChargeSpeed;
                else if (s.DashSpeed > 0.0f && a.Phase == PhaseActive)
                { r.Mods.DashSpeed = s.DashSpeed; r.Mods.DashX = a.DashX; r.Mods.DashZ = a.DashZ; }
                else if (s.Move == MoveLocked) r.Mods.Lock = 1;
                else if (s.Move == MoveSlowed) r.Mods.Speed = s.CastSpeed;
            }
            r.Slot = a.Slot; r.Phase = a.Phase; r.Tick = a.Tick; r.Step = a.Step; r.Charge = a.Charge;
            return r;
        }

        // 클라 예측과 서버가 같이 부르는 "한 틱": 스킬 규칙 → 이동 규칙
        public static AbilityResult SimStep(ref MoveState mv, ref AbilityState ab, ClassTiming c, InputCmd input, Level lv)
        {
            AbilityResult r = StepAbility(ref ab, c, input, mv);
            Movement.Step(ref mv, input, lv, r.Mods);
            return r;
        }

        // ---------------- 직업별 타이밍 표 (C++ TimingOf와 같은 값. ParityTest가 직업마다 3000틱으로 확인) ----------------
        static Frames F(byte w, byte a, byte r, byte c) { return new Frames(w, a, r, c); }
        static SkillTiming None() { return new SkillTiming(); }
        static SkillTiming Act(Buttons b, ushort cd, Frames f, byte move = MoveLocked, bool ground = false)
        {
            return new SkillTiming { Button = (byte)b, Cooldown = cd, Steps = new[] { f, f, f }, Move = move, CastSpeed = 2.5f, NeedGround = ground };
        }
        static SkillTiming Charged(Buttons b, ushort cd, Frames f, byte min, byte max, float moveSpeed, bool ground)
        {
            return new SkillTiming { Button = (byte)b, Cooldown = cd, Steps = new[] { f, f, f }, ChargeMin = min, ChargeMax = max, ChargeSpeed = moveSpeed, NeedGround = ground };
        }

        static readonly ClassTiming[] Table =
        {
            new ClassTiming { Skills = new[] {   // 전사
                new SkillTiming { Button = (byte)Buttons.Attack, StepCount = 3, Steps = new[] { F(5, 1, 24, 15), F(5, 1, 24, 14), F(3, 5, 22, 15) }, Move = MoveLocked },
                None(),
                Act(Buttons.SkillQ, 210, F(7, 1, 22, 15)),
                Act(Buttons.SkillE, 150, F(9, 3, 18, 14), MoveLocked, true),
                Act(Buttons.SkillF, 450, F(14, 1, 15, 11), MoveLocked, true) } },
            new ClassTiming { Skills = new[] {   // 궁수
                Charged(Buttons.Attack, 30, F(0, 1, 6, 0), 12, 24, 2.0f, false),
                None(),
                Act(Buttons.SkillQ, 300, F(6, 1, 9, 3), MoveSlowed),
                Act(Buttons.SkillE, 150, F(6, 1, 9, 3), MoveSlowed),
                Charged(Buttons.SkillF, 390, F(0, 1, 10, 0), 15, 42, 1.0f, true) } },
            new ClassTiming { RightClickIsSkill = true, Skills = new[] {   // 창술사
                Act(Buttons.Attack, 0, F(11, 4, 15, 9), MoveLocked, true),
                Act(Buttons.Aim, 0, F(11, 4, 15, 11), MoveLocked, true),
                Act(Buttons.SkillQ, 300, F(12, 3, 15, 15)),
                Act(Buttons.SkillE, 150, F(5, 14, 11, 6), MoveLocked, true),
                new SkillTiming { Button = (byte)Buttons.SkillF, Cooldown = 390, Steps = new[] { F(6, 18, 6, 4), F(0, 0, 0, 0), F(0, 0, 0, 0) }, Move = MoveLocked, DashSpeed = 8.0f, NeedGround = true } } },
            new ClassTiming { Skills = new[] {   // 힐러
                Act(Buttons.Attack, 0, F(15, 1, 14, 14), MoveSlowed),
                None(),
                Act(Buttons.SkillQ, 390, F(15, 1, 14, 14), MoveSlowed),
                Act(Buttons.SkillE, 150, F(19, 1, 10, 10), MoveSlowed),
                Act(Buttons.SkillF, 450, F(23, 1, 6, 6), MoveSlowed) } },
        };

        public static ClassTiming Timing(ClassId id) { return Table[(int)id]; }
    }
}
