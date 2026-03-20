using UnityEngine;

/// <summary>
/// 골렘 포효 특수 상태 데이터 SO.
/// Create > Lee/Monster/Special/GolemRoarData 로 .asset 생성 후
/// GolemConfigSO 의 roar 슬롯에 드래그하여 참조한다.
/// </summary>
[CreateAssetMenu(fileName = "GolemRoarData", menuName = "Lee/Monster/Special/GolemRoarData")]
public class GolemRoarData : SpecialStateDataBase
{
    [Tooltip("HP 비율이 이 이하로 떨어지면 포효 상태 진입 (0~1)")]
    [Range(0f, 1f)]
    public float hpThreshold = 0.5f;

    [Tooltip("포효 지속 시간 (초)")]
    public float roarDuration = 2.0f;

    [Tooltip("포효 애니메이션 상태 이름 (Animator State 이름과 일치)")]
    public string roarStateName = "Roar";

    public override LeeSpecialStateBase CreateState() => new GolemRoarState(this);
}
