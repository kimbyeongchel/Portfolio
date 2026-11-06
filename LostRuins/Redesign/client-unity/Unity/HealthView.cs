// 서버 HitEvent → 데미지 숫자 + 피격 이펙트. 모든 화면이 같은 이벤트로 같은 위치에 그린다 (F3).
using LostRuins.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.UI;

namespace LostRuins.Client
{
    public sealed class HealthView : MonoBehaviour
    {
        [SerializeField] TMP_Text label;
        [SerializeField] Image fill;
        [SerializeField] int maxHp = 100;

        [SerializeField] Image shieldFill;   // 체력 바 위에 겹쳐 그리는 실드 (창술사 Q)

        public void Set(int hp)
        {
            label.SetText("{0}/100", hp);
            fill.fillAmount = (float)hp / maxHp;
        }

        public void SetShield(int shield) { shieldFill.fillAmount = Mathf.Clamp(shield, 0, maxHp) / (float)maxHp; }
    }
}
