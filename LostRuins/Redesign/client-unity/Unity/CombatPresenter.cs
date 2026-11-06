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
        [SerializeField] PooledVfx hitPrefab, critPrefab, healPrefab, shieldPrefab, buffPrefab, blockPrefab;
        [SerializeField] Color damageColor = Color.white, critColor = new Color(1f, 0.8f, 0.2f, 1f),
                               healColor = new Color(0.4f, 1f, 0.5f, 1f), shieldColor = new Color(0.5f, 0.8f, 1f, 1f);
        ObjectPool<DamageNumber> numbers;
        ObjectPool<PooledVfx> hits, crits, heals, shields, buffs, blocks;

        void Awake()
        {
            numbers = new ObjectPool<DamageNumber>(() => Instantiate(numberPrefab, transform),
                n => n.gameObject.SetActive(true), n => n.gameObject.SetActive(false), null, true, 32, 128);
            hits = MakeVfxPool(hitPrefab);
            crits = MakeVfxPool(critPrefab);
            heals = MakeVfxPool(healPrefab);
            shields = MakeVfxPool(shieldPrefab);
            buffs = MakeVfxPool(buffPrefab);
            blocks = MakeVfxPool(blockPrefab);
        }

        ObjectPool<PooledVfx> MakeVfxPool(PooledVfx prefab)
        {
            return new ObjectPool<PooledVfx>(() => Instantiate(prefab, transform),
                v => v.gameObject.SetActive(true), v => v.gameObject.SetActive(false), null, true, 16, 64);
        }

        // 서버 이벤트 종류마다 이펙트 + 숫자. 클라는 "맞았다"를 스스로 판단하지 않는다.
        public void Show(GameEvent e)
        {
            var pos = new Vector3(e.X, e.Y, e.Z);
            switch (e.Type)
            {
                case EventType.Hit:
                    Vfx(e.Crit != 0 ? crits : hits, pos);
                    numbers.Get().Show(e.Damage, e.Crit != 0 ? "{0}!" : "{0}", e.Crit != 0 ? critColor : damageColor, pos, numbers);
                    break;
                case EventType.Heal:
                    Vfx(heals, pos);
                    if (e.Damage > 0) numbers.Get().Show(e.Damage, "+{0}", healColor, pos, numbers);   // 실제로 회복된 양만
                    break;
                case EventType.Shield:
                    Vfx(shields, pos);
                    numbers.Get().Show(e.Damage, "({0})", shieldColor, pos, numbers);
                    break;
                case EventType.Buff:
                    Vfx(buffs, pos);
                    break;
                case EventType.Block:
                    Vfx(blocks, pos);                                   // 힐러 패시브: 숫자 대신 "막음" 이펙트
                    break;
            }
        }

        static void Vfx(ObjectPool<PooledVfx> pool, Vector3 pos) { pool.Get().Play(pos, pool); }
    }
}
