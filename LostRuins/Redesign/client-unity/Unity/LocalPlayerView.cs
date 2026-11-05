// 표현 계층: 상태를 받아 그리기만 한다. 판정·데미지·쿨타임 계산이 없다.
using System.Collections.Generic;
using LostRuins.Shared;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.Pool;

namespace LostRuins.Client
{
    public sealed class LocalPlayerView : MonoBehaviour
    {
        [SerializeField] Animator animator;
        [SerializeField] Rig aimRig;
        [SerializeField] float aimWeight = 0.8f, aimBlendSeconds = 0.15f;
        float rigWeight;
        bool aiming;

        public void OnTick(MoveState s, InputCmd cmd)
        {
            aiming = (cmd.Buttons & (byte)Buttons.Aim) != 0;
            animator.SetBool(AnimIds.Grounded, s.Grounded != 0);
            animator.SetBool(AnimIds.Dodging, s.DodgeT > 0);
        }

        public void Render(Vector3 position, MoveState s)
        {
            transform.position = position;
            float planar = Mathf.Sqrt(s.Vx * s.Vx + s.Vz * s.Vz);
            animator.SetFloat(AnimIds.MoveAmount, planar, 0.1f, Time.deltaTime);
            rigWeight = Mathf.MoveTowards(rigWeight, aiming ? aimWeight : 0f, Time.deltaTime / aimBlendSeconds);   // F10
            aimRig.weight = rigWeight;
        }
    }
}
