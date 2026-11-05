// 서버 HitEvent → 데미지 숫자 + 피격 이펙트. 모든 화면이 같은 이벤트로 같은 위치에 그린다 (F3).
using LostRuins.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.UI;

namespace LostRuins.Client
{
    public sealed class CombatPresenter : MonoBehaviour
    {
        [SerializeField] DamageNumber numberPrefab;
        [SerializeField] PooledVfx hitPrefab, critPrefab;
        ObjectPool<DamageNumber> numbers;
        ObjectPool<PooledVfx> hits, crits;

        void Awake()
        {
            numbers = new ObjectPool<DamageNumber>(() => Instantiate(numberPrefab, transform),
                n => n.gameObject.SetActive(true), n => n.gameObject.SetActive(false), null, true, 32, 128);
            hits = MakeVfxPool(hitPrefab);
            crits = MakeVfxPool(critPrefab);
        }

        ObjectPool<PooledVfx> MakeVfxPool(PooledVfx prefab)
        {
            return new ObjectPool<PooledVfx>(() => Instantiate(prefab, transform),
                v => v.gameObject.SetActive(true), v => v.gameObject.SetActive(false), null, true, 16, 64);
        }

        public void Show(GameEvent e)
        {
            if (e.Type != EventType.Hit) return;
            var pos = new Vector3(e.X, e.Y, e.Z);
            var pool = e.Crit != 0 ? crits : hits;
            pool.Get().Play(pos, pool);
            numbers.Get().Show(e.Damage, e.Crit != 0, pos, numbers);
        }
    }
}
