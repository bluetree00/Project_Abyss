using Abyss.Monster;
using UnityEngine;

/// <summary>
/// 페어리박쥐 도주 특수 상태 데이터 SO.
/// FairyBatConfig 의 specialStates 리스트에 추가한다.
/// </summary>
[CreateAssetMenu(fileName = "FairyBatFleeData", menuName = "Lee/Monster/Special/FairyBatFleeData")]
public class FairyBatFleeData : SpecialStateDataBase
{
    [Tooltip("도주 발동 HP 비율 (0~1). 이 비율 이하로 떨어지면 1회 발동.")]
    [Range(0f, 1f)]
    public float hpThreshold  = 0.4f;

    [Tooltip("도주 지속 시간 (초)")]
    public float fleeDuration  = 5f;

    [Tooltip("도주 중 이동 속도 배율 (기본 속도 기준)")]
    public float fleeSpeedMult = 1.5f;

    public override SpecialStateBase CreateState() => new FairyBatFleeState(this);
}
