// Input System 액션 → 버튼 비트. 틱 사이(33ms 안)에 눌렀다 뗀 입력도 놓치지 않도록 "눌림"을 래치한다.
// (멈춘 리팩터링의 PlayerInputManager는 bool 유지값만 노출해서 이 정보가 사라졌다)
using LostRuins.Shared;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LostRuins.Client
{
    public sealed class InputReader : MonoBehaviour
    {
        [SerializeField] InputActionReference move, look, jump, dodge, sprint, attack, aim, skillQ, skillE, skillF;
        Buttons latched;

        public Vector2 Move { get { return move.action.ReadValue<Vector2>(); } }
        public Vector2 LookDelta { get { return look.action.ReadValue<Vector2>(); } }   // 마우스 델타: deltaTime 곱하지 않음 (F5)

        void Update()
        {
            Latch(jump, Buttons.Jump); Latch(dodge, Buttons.Dodge); Latch(attack, Buttons.Attack);
            Latch(skillQ, Buttons.SkillQ); Latch(skillE, Buttons.SkillE); Latch(skillF, Buttons.SkillF);
        }

        void Latch(InputActionReference a, Buttons b) { if (a.action.WasPressedThisFrame()) latched |= b; }

        // 고정 틱이 한 번 가져가면 래치는 비운다
        public Buttons ConsumeButtons()
        {
            Buttons held = Buttons.None;
            if (jump.action.IsPressed()) held |= Buttons.Jump;
            if (dodge.action.IsPressed()) held |= Buttons.Dodge;
            if (sprint.action.IsPressed()) held |= Buttons.Sprint;
            if (attack.action.IsPressed()) held |= Buttons.Attack;
            if (aim.action.IsPressed()) held |= Buttons.Aim;
            if (skillQ.action.IsPressed()) held |= Buttons.SkillQ;
            if (skillE.action.IsPressed()) held |= Buttons.SkillE;
            if (skillF.action.IsPressed()) held |= Buttons.SkillF;
            Buttons result = held | latched;
            latched = Buttons.None;
            return result;
        }
    }
}
