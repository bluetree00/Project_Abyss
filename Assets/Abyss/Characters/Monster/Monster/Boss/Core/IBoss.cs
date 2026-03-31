namespace Abyss.Monster
{
/// <summary>
/// 보스 몬스터 계약 인터페이스.
///
/// ICondition 구현체(BossConditionSO 파생 클래스)는 특정 보스 클래스에 직접 의존하지 않고
/// IBoss를 통해 필요한 데이터에 접근한다.
///
/// IBoss → BossConditionSO (ICondition 구현) 흐름:
///   • 각 BossConditionSO는 Evaluate(BossPatternContext)에서 ctx.Boss 를 통해 보스 상태를 읽는다.
///   • BlackKnightBoss : MonsterBase, IBoss 로 구현하면
///     동일한 조건 SO를 다른 보스에도 재사용할 수 있다.
/// </summary>
public interface IBoss
{
    /// <summary>
    /// 현재 HP 비율 (0~1).
    /// BossHpConditionSO 에서 페이즈 판정에 사용.
    /// </summary>
    float HpRatio { get; }

    /// <summary>
    /// 보스 전용 블랙보드 — 쿨다운, NormalModeTimer, LastPatternTag 등.
    /// BossLastTagConditionSO, BossTimerConditionSO 에서 사용.
    /// </summary>
    BossAttackBlackboard Blackboard { get; }
}
}
