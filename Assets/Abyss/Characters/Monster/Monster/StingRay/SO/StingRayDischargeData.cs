using Abyss.Monster;
using UnityEngine;

/// <summary>
/// 가오리 방전 특수 상태 데이터 SO.
/// 피격 3회 누적 시 발동 — 즉시 주변 방전 범위 공격.
/// </summary>
[CreateAssetMenu(fileName = "StingRayDischargeData", menuName = "Lee/Monster/Special/StingRayDischargeData")]
public class StingRayDischargeData : SpecialStateDataBase
{
    [Tooltip("방전 발동에 필요한 피격 횟수")]
    public int hitThreshold = 3;

    [Tooltip("방전 애니메이션 상태 이름")]
    public string dischargeStateName = "StingRay_Attack02";

    [Tooltip("방전 연출 지속 시간 (초)")]
    public float dischargeDuration = 1.2f;

    [Tooltip("방전 반경 (m)")]
    public float dischargeRadius = 3.5f;

    [Tooltip("방전 데미지")]
    public float dischargeDamage = 8f;

    public override SpecialStateBase CreateState() => new StingRayDischargeState(this);
}
