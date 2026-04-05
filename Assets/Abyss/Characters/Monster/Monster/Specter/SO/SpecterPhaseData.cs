using Abyss.Monster;
using UnityEngine;

/// <summary>
/// 스펙터 위상 이동 특수 상태 데이터 SO.
/// HP 임계값 이하 도달 시 1회 발동 — 투명화 + 기습 이동 후 추격 재개.
/// SpecterConfig 의 specialStates 리스트에 추가한다.
/// </summary>
[CreateAssetMenu(fileName = "SpecterPhaseData", menuName = "Lee/Monster/Special/SpecterPhaseData")]
public class SpecterPhaseData : SpecialStateDataBase
{
    [Tooltip("위상 발동 HP 비율 (0~1). 이 비율 이하로 떨어지면 1회 발동.")]
    [Range(0f, 1f)]
    public float hpThreshold = 0.6f;

    [Tooltip("위상 지속 시간 (초). 이 시간 동안 투명(유령 색상) 상태로 이동.")]
    public float phaseDuration = 2.0f;

    [Tooltip("위상 중 재생할 애니메이션 스테이트 이름")]
    public string phaseStateName = "Run";

    public override SpecialStateBase CreateState() => new SpecterPhaseState(this);
}
