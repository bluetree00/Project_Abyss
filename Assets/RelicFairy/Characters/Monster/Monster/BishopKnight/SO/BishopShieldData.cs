using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 비숍나이트 방패 방어 + 반격 특수 상태 데이터 SO.
/// HP 임계값 이하 도달 시 1회 발동 — guardDuration 동안 방어 자세 후 범위 반격.
/// BishopKnightConfig 의 specialStates 리스트에 추가한다.
/// </summary>
[CreateAssetMenu(fileName = "BishopShieldData", menuName = "Lee/Monster/Special/BishopShieldData")]
public class BishopShieldData : SpecialStateDataBase
{
    [Tooltip("방어 발동 HP 비율 (0~1). 이 비율 이하로 떨어지면 1회 발동.")]
    [Range(0f, 1f)]
    public float hpThreshold = 0.5f;

    [Tooltip("방어 자세 애니메이션 스테이트 이름")]
    public string guardStateName = "BishopKnight_Defend";

    [Tooltip("반격 애니메이션 스테이트 이름")]
    public string counterStateName = "BishopKnight_Attack02";

    [Tooltip("방어 자세 지속 시간 (초)")]
    public float guardDuration = 2.5f;

    [Tooltip("반격 데미지")]
    public float counterDamage = 30f;

    [Tooltip("반격 범위 반경 (m)")]
    public float counterRadius = 3.0f;

    public override SpecialStateBase CreateState() => new BishopShieldGuardState(this);
}
