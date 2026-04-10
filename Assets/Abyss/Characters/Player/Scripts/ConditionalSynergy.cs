/// <summary>
/// 조건부 시너지 데이터 (OnHit 중첩, OnLowHp 등).
/// PlayerRuntimeStats에 등록되어 조건 충족 시 스탯에 반영.
/// </summary>
[System.Serializable]
public class ConditionalSynergy
{
    public string gridId;
    public string effectType;
    public string trigger;        // "OnHit", "OnLowHp"
    public float value;
    public float threshold;       // OnLowHp: HP 비율 (예: 0.3 = 30%)
    public int maxStack;          // OnHit: 최대 중첩 수
    public float duration;        // OnHit: 중첩 지속 시간 (초)

    // ── 런타임 상태 ──
    public int currentStacks;
    public float remainingDuration;
    public bool isActive;         // OnLowHp: 현재 활성 여부
}
