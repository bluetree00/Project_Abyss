using Abyss.Monster;
using UnityEngine;

/// <summary>
/// 마지막으로 실행된 패턴의 태그가 expectedTag 와 일치하면 참.
///
/// 패턴 연계 예시:
///   BKBackstepPatternSO.patternTag = "Backstep"
///   → 후속 엔트리 조건에 이 SO (expectedTag="Backstep") 를 추가하면
///     Backstep 직후에만 발동하는 패턴 체인을 구성할 수 있다.
/// </summary>
[CreateAssetMenu(fileName = "BK_Cond_LastPatternTag",
                 menuName  = "Abyss/Boss/BlackKnight/Conditions/LastPatternTag")]
public class BKLastPatternTagConditionSO : BossConditionSO
{
    [Tooltip("Blackboard.LastPatternTag 와 비교할 태그 문자열.")]
    public string expectedTag = "";

    public override bool Evaluate(BossPatternContext ctx)
        => !string.IsNullOrEmpty(expectedTag)
        && ctx.Blackboard.LastPatternTag == expectedTag;
}
