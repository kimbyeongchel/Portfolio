// 서버 HitEvent → 데미지 숫자 + 피격 이펙트. 모든 화면이 같은 이벤트로 같은 위치에 그린다 (F3).
using LostRuins.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.UI;

namespace LostRuins.Client
{
    public sealed class DamageNumber : MonoBehaviour
    {
        [SerializeField] TMP_Text label;
        [SerializeField] float lifetime = 0.6f, rise = 1.2f;
        ObjectPool<DamageNumber> owner;
        Vector3 origin;
        float age;

        public void Show(int damage, bool crit, Vector3 pos, ObjectPool<DamageNumber> pool) { Show(damage, crit ? "{0}!" : "{0}", Color.white, pos, pool); }

        // format 예: "{0}", "+{0}" (회복), "({0})" (실드). 문자열 조합 대신 포맷 + 숫자 → 할당 없음 (F9)
        public void Show(int value, string format, Color color, Vector3 pos, ObjectPool<DamageNumber> pool)
        {
            owner = pool; origin = pos; age = 0f;
            label.color = color;
            label.SetText(format, value);
            transform.position = pos;
        }

        void Update()
        {
            age += Time.deltaTime;
            transform.position = origin + Vector3.up * (rise * age / lifetime);
            if (age >= lifetime) owner.Release(this);
        }
    }
}
