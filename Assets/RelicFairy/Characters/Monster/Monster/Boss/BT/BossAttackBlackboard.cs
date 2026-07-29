/// <summary>
/// Boss attack blackboard shared across boss behavior-tree patterns.
/// Stores cooldowns, state flags, and simple runtime values needed by pattern conditions.
/// </summary>
public class BossAttackBlackboard
{
    // [DESIGN GUIDE] LeapCooldown / DashSlashCooldown 은 Dragon 전용 쿨다운입니다.
    // DragonBossBlackboard 로 이동 후 이 필드들을 제거해야 합니다.
    // CanExecute 에서는 이미 DragonBossBlackboard 캐스팅을 통해 올바르게 접근 중이나,
    // DragonAirDashPatternSO.Exit() 와 DragonStormWingsPatternSO.Exit() 는
    // BossAttackBlackboard 베이스 타입으로 접근하는 잘못된 패턴이 남아있습니다.
    // 수정 방향:
    //   1. 이 두 필드를 DragonBossBlackboard 로 이동
    //   2. 위 두 Exit() 에서 (ctx.Monster as IBoss)?.Blackboard 를
    //      DragonBossBlackboard 로 캐스팅하여 접근
    public float LeapCooldown;
    public float DashSlashCooldown;

    public float NormalModeTimer;
    public string LastPatternTag = "";
    public float AttackSpeedMult = 1f;

    /// <summary>
    /// 런타임에 패턴 브레이크 딜레이를 덮어쓸 때 사용.
    /// -1 이하이면 BossConfigSO 기본값 사용.
    /// </summary>
    public float BreakDurationMinOverride = -1f;
    public float BreakDurationMaxOverride = -1f;

    public void TickCooldowns(float deltaTime)
    {
        if (LeapCooldown     > 0f) LeapCooldown     -= deltaTime;
        if (DashSlashCooldown > 0f) DashSlashCooldown -= deltaTime;
    }

    public void Reset()
    {
        LeapCooldown              = 0f;
        DashSlashCooldown         = 0f;
        NormalModeTimer           = 0f;
        LastPatternTag            = "";
        AttackSpeedMult           = 1f;
        BreakDurationMinOverride  = -1f;
        BreakDurationMaxOverride  = -1f;
    }
}
