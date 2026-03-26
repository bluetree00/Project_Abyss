using UnityEngine;


namespace Abyss.Monster
{
/// <summary>
/// 보스 패턴 조건 추상 ScriptableObject.
///
/// BossConfigSO 의 패턴 엔트리에 여러 개를 조합하여 할당한다.
/// 모든 조건이 참(AND)일 때 해당 패턴 엔트리가 실행 후보가 된다.
/// </summary>
public abstract class BossConditionSO : ScriptableObject
{
    /// <summary>현재 프레임에 조건이 충족되면 true를 반환한다.</summary>
    public abstract bool Evaluate(BossPatternContext ctx);
}
}
