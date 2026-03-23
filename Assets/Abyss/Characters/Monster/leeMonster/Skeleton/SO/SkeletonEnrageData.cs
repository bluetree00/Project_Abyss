using UnityEngine;

/// <summary>
/// 스켈레톤 광폭화 특수 상태 데이터 SO.
/// SkeletonConfig 의 specialStates 리스트에 추가한다.
/// </summary>
[CreateAssetMenu(fileName = "SkeletonEnrageData", menuName = "Lee/Monster/Special/SkeletonEnrageData")]
public class SkeletonEnrageData : SpecialStateDataBase
{
    [Tooltip("광폭화 발동 HP 비율 (0~1). 이 비율 이하로 떨어지면 1회 발동.")]
    [Range(0f, 1f)]
    public float hpThreshold     = 0.5f;

    [Tooltip("광폭화 연출 잠금 시간 (초). 이 시간 동안 이동·피격 불가.")]
    public float lockDuration    = 1.5f;

    [Tooltip("광폭화 후 영구 이동 속도 배율")]
    public float speedMultiplier  = 1.4f;

    [Tooltip("광폭화 후 영구 공격력 배율")]
    public float attackMultiplier = 1.5f;

    public override LeeSpecialStateBase CreateState() => new SkeletonEnrageState(this);
}
