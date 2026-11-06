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
        [SerializeField] ClassVisuals visuals;
        float rigWeight;
        bool aiming;
        byte lastSlot = Abilities.NoSlot, lastStep, lastPhase;

        // 예측 틱마다 1번 (재조정 중의 재계산에서는 부르지 않음 → 같은 동작이 두 번 재생되지 않음)
        public void OnTick(MoveState s, InputCmd cmd, AbilityResult r)
        {
            aiming = (cmd.Buttons & (byte)Buttons.Aim) != 0 && r.Mods.NoAim == 0;
            animator.SetBool(AnimIds.Grounded, s.Grounded != 0);
            animator.SetBool(AnimIds.Dodging, s.DodgeT > 0);
            animator.SetBool(AnimIds.Charging, r.Phase == Abilities.PhaseCharge);
            // 새 동작(또는 다음 콤보 단계)이 시작된 틱에 한 번만 트리거. 충전 스킬은 떼서 발사되는 틱에.
            bool started = r.Slot != Abilities.NoSlot && r.Phase != Abilities.PhaseCharge &&
                           (r.Slot != lastSlot || r.Step != lastStep || lastPhase == Abilities.PhaseCharge || lastPhase == Abilities.PhaseNone);
            if (started && visuals != null) visuals.Play(animator, r.Slot, r.Step);
            lastSlot = r.Slot; lastStep = r.Step; lastPhase = r.Phase;
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
