using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 식물 몬스터 독 분사 특수 상태 데이터 SO.
/// HP 70% 이하 도달 시 1회 발동 — 제자리에서 독 포자 분사.
/// </summary>
[CreateAssetMenu(fileName = "MonsterPlantSprayData", menuName = "Lee/Monster/Special/MonsterPlantSprayData")]
public class MonsterPlantSprayData : SpecialStateDataBase
{
    [Tooltip("발동 HP 비율 (0~1). 이 비율 이하로 떨어지면 1회 발동.")]
    [Range(0f, 1f)]
    public float hpThreshold = 0.7f;

    [Tooltip("독 분사 애니메이션 상태 이름")]
    public string sprayStateName = "Attack02";

    [Tooltip("독 분사 지속 시간 (초)")]
    public float sprayDuration = 2.0f;

    [Tooltip("독 분사 반경 (m)")]
    public float sprayRadius = 3.5f;

    [Tooltip("독 분사 데미지")]
    public float sprayDamage = 8f;

    public override SpecialStateBase CreateState() => new MonsterPlantPoisonSprayState(this);
}
