using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 샐러맨더 화염 브레스 특수 상태 데이터 SO.
/// HP 70% 이하일 때 반복 발동 — 정면 Cone 형태의 화염 브레스.
/// </summary>
[CreateAssetMenu(fileName = "SalamanderBreathData", menuName = "Lee/Monster/Special/SalamanderBreathData")]
public class SalamanderBreathData : SpecialStateDataBase
{
    [Tooltip("발동 HP 비율 (0~1). 이 비율 이하일 때 반복 발동.")]
    [Range(0f, 1f)]
    public float hpThreshold = 0.7f;

    [Tooltip("화염 브레스 지속 시간 (초)")]
    public float breathDuration = 2.0f;

    [Tooltip("브레스 애니메이션 상태 이름")]
    public string breathStateName = "Salamander_Attack03";

    [Header("Cone 화염 범위")]
    [Tooltip("브레스 도달 반경 (m)")]
    public float breathRadius = 4f;

    [Tooltip("브레스 원뿔 각도 (도). 정면 기준 좌우 각도.")]
    public float breathAngle = 60f;

    [Tooltip("틱당 데미지")]
    public float breathDamage = 12f;

    [Tooltip("데미지 틱 간격 (초)")]
    public float damageTick = 0.4f;

    public override SpecialStateBase CreateState() => new SalamanderFlameBreathState(this);
}
