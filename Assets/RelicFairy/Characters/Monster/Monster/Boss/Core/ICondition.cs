namespace RelicFairy.Monster
{
/// <summary>
/// 보스 패턴 발동 조건 인터페이스.
///
/// 구현체는 BossConditions.cs 에 모여 있으며 BlackKnightBoss.BuildConditions() 에서
/// conditionKey 문자열을 ICondition 인스턴스로 변환한다.
///
/// SOLID:
///  SRP  — 조건 평가만 담당 (패턴 실행·상태 변환 없음)
///  OCP  — 새 조건은 이 인터페이스를 구현하면 되며 기존 코드 수정 불필요
///  LSP  — 모든 구현체를 ICondition 으로 대체 가능
///  ISP  — 단일 메서드 인터페이스 (최소 의존)
///  DIP  — BossPatternEntry 는 구체 SO 가 아닌 ICondition 에 의존
/// </summary>
public interface ICondition
{
    bool Evaluate(BossPatternContext ctx);
}
}
