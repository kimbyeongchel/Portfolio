// 서버 HitEvent → 데미지 숫자 + 피격 이펙트. 모든 화면이 같은 이벤트로 같은 위치에 그린다 (F3).
using LostRuins.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.UI;

namespace LostRuins.Client
{
    public sealed class PooledVfx : MonoBehaviour
    {
        [SerializeField] ParticleSystem particles;   // Stop Action = Callback 으로 설정
        ObjectPool<PooledVfx> owner;

        public void Play(Vector3 pos, ObjectPool<PooledVfx> pool) { owner = pool; transform.position = pos; particles.Play(); }
        void OnParticleSystemStopped() { owner.Release(this); }
    }
}
