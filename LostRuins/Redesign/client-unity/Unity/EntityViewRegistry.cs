// 표현 계층: 상태를 받아 그리기만 한다. 판정·데미지·쿨타임 계산이 없다.
using System.Collections.Generic;
using LostRuins.Shared;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.Pool;

namespace LostRuins.Client
{
    public sealed class EntityViewRegistry : MonoBehaviour
    {
        [SerializeField] EntityView playerPrefab, monsterPrefab, projectilePrefab;
        readonly Dictionary<ushort, EntityView> live = new Dictionary<ushort, EntityView>();
        readonly HashSet<ushort> seen = new HashSet<ushort>();
        readonly List<ushort> gone = new List<ushort>();
        ObjectPool<EntityView>[] pools;

        void Awake()
        {
            pools = new[] { MakePool(playerPrefab), MakePool(monsterPrefab), MakePool(projectilePrefab) };
        }

        ObjectPool<EntityView> MakePool(EntityView prefab)
        {
            return new ObjectPool<EntityView>(() => Instantiate(prefab, transform),
                v => v.gameObject.SetActive(true), v => v.gameObject.SetActive(false), null, true, 16, 256);
        }

        public void Apply(List<EntityState> states, ushort localId)
        {
            seen.Clear();
            foreach (var s in states)
            {
                if (s.Id == localId) continue;
                seen.Add(s.Id);
                EntityView v;
                if (!live.TryGetValue(s.Id, out v))
                {
                    v = pools[(int)s.Kind].Get(); v.Kind = s.Kind; live.Add(s.Id, v); v.Snap(s);
                }
                v.Apply(s);
            }
            gone.Clear();
            foreach (var kv in live) if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var id in gone) { var v = live[id]; live.Remove(id); pools[(int)v.Kind].Release(v); }
        }
    }
}
