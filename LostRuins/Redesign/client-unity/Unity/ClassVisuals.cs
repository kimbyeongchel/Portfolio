// 직업별 "연출" 표 (ScriptableObject). 규칙(Shared/Abilities.cs, 서버 classes.h)과 완전히 분리돼 있다.
// 애니메이션·이펙트를 바꿔도 서버와 예측 코드는 그대로이고, 수치를 바꿔도 이 에셋은 그대로다.
//
// 원본과 방향이 반대다.
//   원본: 로직이 애니메이션을 기다림 (animator.GetCurrentAnimatorStateInfo().normalizedTime >= impactStartTime)
//         → fps·전이 시간에 따라 판정 시점이 달라지고 서버는 검증할 수 없음
//   재설계: 판정 틱은 표(abilities.h)가 정하고, 애니메이션 재생 속도를 그 틱에 맞춘다 (리타이밍)
//         → 클립의 타격 지점(impactNormalized)이 정확히 판정 틱에 화면에 나온다
using LostRuins.Shared;
using UnityEngine;

namespace LostRuins.Client
{
    [CreateAssetMenu(fileName = "ClassVisuals", menuName = "LostRuins/Class Visuals")]
    public sealed class ClassVisuals : ScriptableObject
    {
        [System.Serializable]
        public struct SkillAnim
        {
            public string trigger;           // 원본 AnimationState.triggerName (attack1, skill_E …)
            public float clipSeconds;        // 클립 길이 (에셋에서 읽어 기입)
            public float impactNormalized;   // 원본 impactStartTime. 이 지점이 판정 틱에 오도록 재생 속도를 맞춘다
            public PooledVfx castVfx;        // 시전 순간 이펙트 (선택)
        }

        public ClassId cls;
        [SerializeField] SkillAnim[] skills = new SkillAnim[16];   // 인덱스 = 슬롯×3 + 콤보 단계 (서버 EntityState.Skill − 1)
        int[] triggerIds;

        public bool TryGet(int slot, int step, out SkillAnim anim, out int triggerId)
        {
            int i = slot * 3 + step;
            anim = default(SkillAnim); triggerId = 0;
            if (i < 0 || i >= skills.Length || string.IsNullOrEmpty(skills[i].trigger)) return false;
            if (triggerIds == null)                                 // 문자열 → 해시는 처음 한 번만 (F9)
            {
                triggerIds = new int[skills.Length];
                for (int k = 0; k < skills.Length; ++k)
                    triggerIds[k] = string.IsNullOrEmpty(skills[k].trigger) ? 0 : Animator.StringToHash(skills[k].trigger);
            }
            anim = skills[i]; triggerId = triggerIds[i];
            return true;
        }

        // 클립의 타격 지점까지 걸리는 시간 ÷ 표의 선딜 시간 = 재생 배율
        // 예) 전사 1타: 클립 0.9초 × 0.15 = 0.135초 / 선딜 5틱(0.167초) → 0.81배속으로 재생
        public float PlaybackSpeed(int slot, int step, ClassId id)
        {
            SkillAnim a; int t;
            if (!TryGet(slot, step, out a, out t)) return 1f;
            byte windup = Abilities.Timing(id).Skills[slot].Steps[step].Windup;
            if (windup == 0 || a.clipSeconds <= 0f || a.impactNormalized <= 0f) return 1f;
            return Mathf.Clamp(a.clipSeconds * a.impactNormalized / (windup * Rules.Dt), 0.5f, 2f);   // 너무 느리거나 빠르면 어색하므로 제한
        }

        public void Play(Animator animator, int slot, int step)
        {
            SkillAnim a; int id;
            if (!TryGet(slot, step, out a, out id)) return;
            animator.SetFloat(AnimIds.SkillSpeed, PlaybackSpeed(slot, step, cls));   // 상태의 Speed Multiplier 파라미터
            animator.SetTrigger(id);
        }
    }
}
