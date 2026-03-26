using Abyss.Monster;
using UnityEngine;

/// <summary>
/// SpinSlash HP 페이즈 인터럽트 조건.
/// BKSpinSlashPatternSO 의 IsHpThresholdMet() 에 위임하므로
/// "아직 미발동된 HP 임계값 구간에 진입했는지" 를 정확히 판정한다.
///
/// forceExecute=true 엔트리에 이 조건을 달아 스핀슬래시 페이즈 인터럽트를 구성한다.
/// </summary>
[CreateAssetMenu(fileName = "BK_Cond_SpinPhase",
                 menuName  = "Abyss/Boss/BlackKnight/Conditions/SpinPhase")]
public class BKSpinPhaseConditionSO : BossConditionSO
{
    [Tooltip("SpinSlash 패턴 SO 참조 (BKSpinSlashPatternSO 할당)")]
    public BKSpinSlashPatternSO spinPattern;

    public override bool Evaluate(BossPatternContext ctx)
        => spinPattern != null && spinPattern.IsHpThresholdMet(ctx);
}
