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
        Vector3 last;
        public EntityKind Kind { get; set; }

        public void Snap(EntityState s) { transform.position = Pos(s); last = transform.position; }

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
            }
        }

        static Vector3 Pos(EntityState s) { return new Vector3(s.X, s.Y, s.Z); }
    }
}
