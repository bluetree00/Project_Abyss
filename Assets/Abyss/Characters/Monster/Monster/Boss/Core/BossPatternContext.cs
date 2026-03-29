namespace Abyss.Monster
{
/// <summary>
/// 보스 패턴 평가 및 실행 시 조건/패턴 SO에 전달되는 런타임 컨텍스트.
/// BossConditionSO.Evaluate / BossPatternSO.CanExecute 에서 사용.
/// </summary>
public class BossPatternContext
{
    /// <summary>
    /// 보스 인터페이스 — HpRatio, Blackboard 등 조건 평가에 필요한 보스 상태 접근.
    /// BossHpConditionSO, BossLastTagConditionSO 등에서 사용.
    /// </summary>
    public IBoss Boss;

    /// <summary>몬스터 공용 컨텍스트 (Transform, Agent, Animator, Config, Runtime).</summary>
    public MonsterContext Ctx;

    /// <summary>보스 전용 블랙보드 (쿨다운, 페이즈, 오디오 풀).</summary>
    public BossAttackBlackboard Blackboard;

    /// <summary>
    /// 현재 패턴 브레이크 쿨다운 (초).
    /// 0 이하이면 일반 패턴 선택 가능.
    /// 조건 SO에서 필요한 경우 참조한다.
    /// </summary>
    public float PatternBreakCooldown;
}
}
