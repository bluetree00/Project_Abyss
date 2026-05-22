using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 비홀더 눈 빔 특수 상태 데이터 SO.
/// HP 60% 이하일 때 반복 발동 — 차지 후 범위 빔 공격.
/// </summary>
[CreateAssetMenu(fileName = "BeholderEyeBeamData", menuName = "Lee/Monster/Special/BeholderEyeBeamData")]
public class BeholderEyeBeamData : SpecialStateDataBase
{
    [Tooltip("발동 HP 비율 (0~1). 이 비율 이하일 때 반복 발동.")]
    [Range(0f, 1f)]
    public float hpThreshold = 0.6f;

    [Tooltip("차지 단계 지속 시간 (초)")]
    public float chargeDuration = 0.8f;

    [Tooltip("빔 발사 단계 지속 시간 (초)")]
    public float beamDuration = 1.5f;

    [Tooltip("차지 애니메이션 상태 이름")]
    public string chargeStateName = "Attack02ST";

    [Tooltip("빔 발사 애니메이션 상태 이름")]
    public string beamStateName = "Attack02RPT";

    [Header("빔 범위 공격")]
    [Tooltip("빔 피해 반경 (m)")]
    public float beamRadius = 5f;

    [Tooltip("빔 틱당 데미지")]
    public float beamDamage = 15f;

    [Tooltip("데미지 틱 간격 (초)")]
    public float damageTick = 0.3f;

    [Header("레이저 VFX")]
    [Tooltip("빔 발사 시 스폰할 이펙트 프리팹 (루트에 배치, beamDuration 후 자동 소멸).")]
    public GameObject beamVfxPrefab;

    public override SpecialStateBase CreateState() => new BeholderEyeBeamState(this);
}
