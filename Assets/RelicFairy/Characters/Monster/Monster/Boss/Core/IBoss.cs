namespace RelicFairy.Monster
{
/// <summary>
/// 보스 몬스터 계약 인터페이스.
///
/// 조건 구현체는 특정 보스 클래스에 직접 의존하지 않고
/// IBoss 를 통해 필요한 런타임 데이터에 접근한다.
///
/// 실제 보스 구현체가 MonsterBase, IBoss 로 구성되면
/// 동일한 조건 로직을 여러 보스에서 재사용할 수 있다.
/// </summary>
public interface IBoss
{
    /// <summary>
    /// 현재 HP 비율 (0~1).
    /// 보스 조건 평가 시 페이즈 판정 등에 사용한다.
    /// </summary>
    float HpRatio { get; }

    /// <summary>
    /// 보스 전용 블랙보드.
    /// 쿨다운, NormalModeTimer, LastPatternTag 등을 조건 평가에 사용한다.
    /// </summary>
    BossAttackBlackboard Blackboard { get; }
}
}
