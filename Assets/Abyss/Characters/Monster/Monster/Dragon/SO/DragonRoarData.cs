using Abyss.Monster;
using UnityEngine;

/// <summary>
/// 드래곤 포효 특수 상태 데이터 SO.
/// HP 50% 이하 도달 시 1회 발동 — 포효하며 무적 + 주변 넉백 후 분노 추격 속도 부스트.
/// </summary>
[CreateAssetMenu(fileName = "DragonRoarData", menuName = "Lee/Monster/Special/DragonRoarData")]
public class DragonRoarData : SpecialStateDataBase
{
    [Tooltip("발동 HP 비율 (0~1)")]
    [Range(0f, 1f)]
    public float hpThreshold = 0.5f;

    [Tooltip("포효 지속 시간 (초)")]
    public float roarDuration = 2.5f;

    [Tooltip("포효 애니메이션 상태 이름")]
    public string roarStateName = "Taunting";

    [Header("넉백 충격파")]
    [Tooltip("충격파 반경 (m)")]
    public float knockbackRadius = 7f;

    [Tooltip("날려버리는 힘 배율")]
    public float knockbackForce = 6f;

    [Tooltip("충격파 데미지")]
    public float knockbackDamage = 8f;

    [Header("포효 후 분노 추격")]
    [Tooltip("분노 추격 중 이동 속도 배율")]
    public float rageSpeedMultiplier = 1.6f;

    [Tooltip("포효 종료 후 빠른 추격 지속 시간 (초)")]
    public float rageChaseDuration = 8f;

    public override SpecialStateBase CreateState() => new DragonRoarState(this);
}
