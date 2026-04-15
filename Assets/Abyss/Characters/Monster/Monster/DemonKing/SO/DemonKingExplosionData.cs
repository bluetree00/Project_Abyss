using Abyss.Monster;
using UnityEngine;

/// <summary>
/// 데몬킹 폭발 특수 상태 데이터 SO.
/// HP 40% 이하 도달 시 1회 발동 — 무적 + 주변 대폭발 + 분노 추격 속도 부스트.
/// </summary>
[CreateAssetMenu(fileName = "DemonKingExplosionData", menuName = "Lee/Monster/Special/DemonKingExplosionData")]
public class DemonKingExplosionData : SpecialStateDataBase
{
    [Tooltip("발동 HP 비율 (0~1)")]
    [Range(0f, 1f)]
    public float hpThreshold = 0.4f;

    [Tooltip("폭발 연출 지속 시간 (초)")]
    public float explosionDuration = 2.0f;

    [Tooltip("폭발 애니메이션 상태 이름")]
    public string explosionStateName = "DemonKing_Taunting";

    [Header("폭발 범위 공격")]
    [Tooltip("폭발 반경 (m)")]
    public float explosionRadius = 8f;

    [Tooltip("폭발 데미지")]
    public float explosionDamage = 20f;

    [Tooltip("넉백 힘 배율")]
    public float knockbackForce = 8f;

    [Header("분노 추격")]
    [Tooltip("분노 추격 중 이동 속도 배율")]
    public float rageSpeedMultiplier = 1.7f;

    [Tooltip("분노 추격 지속 시간 (초)")]
    public float rageChaseDuration = 10f;

    public override SpecialStateBase CreateState() => new DemonKingExplosionState(this);
}
