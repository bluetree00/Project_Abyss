/// <summary>
/// Boss attack blackboard shared across boss behavior-tree patterns.
/// Stores cooldowns, state flags, and simple runtime values needed by pattern conditions.
/// </summary>
public class BossAttackBlackboard
{
    public float ChargeCooldown;
    public float OverheadCooldown;
    public float RainCooldown;
    public float ScatterCooldown;
    public float LeapCooldown;
    public float BackstepCooldown;
    public float DashSlashCooldown;
    public float PressureCooldown;

    public float ChaseSpeedMult = 1f;
    public float NormalModeTimer;
    public int RainPhase;
    public string LastPatternTag = "";
    public float AttackSpeedMult = 1f;
    public bool HasEnraged;

    public void TickCooldowns(float deltaTime)
    {
        if (ChargeCooldown > 0f) ChargeCooldown -= deltaTime;
        if (OverheadCooldown > 0f) OverheadCooldown -= deltaTime;
        if (RainCooldown > 0f) RainCooldown -= deltaTime;
        if (ScatterCooldown > 0f) ScatterCooldown -= deltaTime;
        if (LeapCooldown > 0f) LeapCooldown -= deltaTime;
        if (BackstepCooldown > 0f) BackstepCooldown -= deltaTime;
        if (DashSlashCooldown > 0f) DashSlashCooldown -= deltaTime;
        if (PressureCooldown > 0f) PressureCooldown -= deltaTime;
    }

    public void Reset()
    {
        ChargeCooldown = 0f;
        OverheadCooldown = 0f;
        RainCooldown = 0f;
        ScatterCooldown = 0f;
        LeapCooldown = 0f;
        BackstepCooldown = 0f;
        DashSlashCooldown = 0f;
        PressureCooldown = 0f;
        ChaseSpeedMult = 1f;
        NormalModeTimer = 0f;
        RainPhase = 0;
        LastPatternTag = "";
        AttackSpeedMult = 1f;
        HasEnraged = false;
    }
}
