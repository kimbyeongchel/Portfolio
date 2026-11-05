// 표현 계층: 상태를 받아 그리기만 한다. 판정·데미지·쿨타임 계산이 없다.
using System.Collections.Generic;
using LostRuins.Shared;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.Pool;

namespace LostRuins.Client
{
    static class AnimIds   // 문자열 파라미터는 해시로 한 번만 (F9)
    {
        public static readonly int MoveAmount = Animator.StringToHash("moveAmount");
        public static readonly int Action = Animator.StringToHash("action");
        public static readonly int Grounded = Animator.StringToHash("isGrounded");
        public static readonly int Dodging = Animator.StringToHash("dodging");
    }
}
