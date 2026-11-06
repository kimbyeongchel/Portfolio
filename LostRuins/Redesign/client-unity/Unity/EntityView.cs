// 표현 계층: 상태를 받아 그리기만 한다. 판정·데미지·쿨타임 계산이 없다.
using System.Collections.Generic;
using LostRuins.Shared;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.Pool;

namespace LostRuins.Client
{
    public class EntityView : MonoBehaviour
    {
        [SerializeField] Animator animator;
        [SerializeField] GameObject[] looks;   // 투사체·장판: 직업×스킬별 모양 (인덱스 = 직업×16 + Skill). 비어 있으면 기본 모양
        Vector3 last;
        byte lastSkill;
        ClassVisuals[] visuals;
        public EntityKind Kind { get; set; }

        public void Bind(ClassVisuals[] byClass) { visuals = byClass; }

        public void Snap(EntityState s)
        {
            transform.position = Pos(s); last = transform.position; lastSkill = 0;
            if (looks != null && looks.Length > 0)
            {
                int pick = s.Cls * 16 + s.Skill;
                for (int i = 0; i < looks.Length; ++i) if (looks[i] != null) looks[i].SetActive(i == pick);
            }
        }

        public void Apply(EntityState s)
        {
            Vector3 p = Pos(s);
            float speed = (p - last).magnitude / Mathf.Max(Time.deltaTime, 1e-4f);
            last = p;
            transform.SetPositionAndRotation(p, Quaternion.Euler(0f, s.Yaw * (360f / 256f), 0f));
            if (animator != null)
            {
                animator.SetFloat(AnimIds.MoveAmount, speed, 0.1f, Time.deltaTime);
                animator.SetInteger(AnimIds.Action, s.Action);
                // 원격 플레이어 스킬: 서버가 보낸 Skill 코드가 바뀐 순간 재생 (보간 지연 100ms만큼 늦게, 위치와 같은 시각)
                if (Kind == EntityKind.Player && s.Skill != 0 && s.Skill != lastSkill && visuals != null && s.Cls < visuals.Length && visuals[s.Cls] != null)
                    visuals[s.Cls].Play(animator, (s.Skill - 1) / 3, (s.Skill - 1) % 3);
            }
            lastSkill = s.Skill;
        }

        static Vector3 Pos(EntityState s) { return new Vector3(s.X, s.Y, s.Z); }
    }
}
