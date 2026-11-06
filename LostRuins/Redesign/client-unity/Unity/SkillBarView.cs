// 스킬 아이콘 쿨타임 표시. 원본은 스킬마다 ui_coolTime 코루틴을 돌렸다 (5개 × 매 프레임 + 시작·중단 관리).
// 재설계는 예측된 AbilityState의 남은 틱을 읽어 비율만 그린다 → 코루틴 없음, 서버와 같은 숫자.
using LostRuins.Shared;
using UnityEngine;
using UnityEngine.UI;

namespace LostRuins.Client
{
    public sealed class SkillBarView : MonoBehaviour
    {
        [SerializeField] Image[] cooldownFills = new Image[Abilities.SlotCount];   // 기본, 우클릭, Q, E, F
        ClassTiming timing;

        public void Bind(ClassTiming t) { timing = t; }

        public void Set(AbilityState a)
        {
            if (timing == null) return;
            for (int i = 0; i < Abilities.SlotCount && i < cooldownFills.Length; ++i)
            {
                if (cooldownFills[i] == null) continue;
                ushort max = timing.Skills[i].Cooldown;
                cooldownFills[i].fillAmount = max == 0 ? 0f : (float)a.GetCd(i) / max;
            }
        }
    }
}
