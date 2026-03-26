using Abyss.Monster;
using UnityEngine;

/// <summary>
/// 비특수 상태(평타 모드)로 경과한 시간이 minDuration 이상이면 참.
/// OverheadSlash 발동 전 최소 평타 시간을 보장하는 데 사용한다.
/// </summary>
[CreateAssetMenu(fileName = "BK_Cond_NormalModeTimer",
                 menuName  = "Abyss/Boss/BlackKnight/Conditions/NormalModeTimer")]
public class BKNormalModeTimerConditionSO : BossConditionSO
{
    [Tooltip("평타 모드로 최소 경과해야 하는 시간 (초)")]
    public float minDuration = 3f;

    public override bool Evaluate(BossPatternContext ctx)
        => ctx.Blackboard.NormalModeTimer >= minDuration;
}
