/// <summary>
/// 보스 공격 노드 간 공유 블랙보드.
/// 쿨다운은 시간 기반 패턴에 사용.
/// SpinSlash 는 HP 임계값 기반이므로 쿨다운 없음 — BKSpinSlashState._nextThresholdIndex 로 관리.
/// NormalModeTimer: 비특수 상태(평타 모드)에서 경과한 시간.
///   특수 상태 진입 시 0으로 리셋. OverheadSlash 발동 최소 평타 시간 보장에 사용.
/// LastPatternTag: 마지막 실행 패턴의 태그. BKLastPatternTagConditionSO 에서 참조.
/// AttackSpeedMult: 각성(Enrage) 후 이동·공격 속도 배율. 기본 1.0.
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

    /// <summary>HP 이정표(75%·50%)를 지날 때마다 증가하는 추격 속도 배율. 기본 1.0.</summary>
    public float ChaseSpeedMult = 1f;

    /// <summary>마지막 특수 상태 종료 후 평타 모드로 경과한 시간 (초).</summary>
    public float NormalModeTimer;

    /// <summary>운석 낙하 페이즈 (0=기본, 1=1차 스핀 후, 2=2차 스핀 후).</summary>
    public int RainPhase;

    /// <summary>마지막으로 실행된 패턴의 태그. BKLastPatternTagConditionSO 에서 패턴 연계에 사용.</summary>
    public string LastPatternTag = "";

    /// <summary>각성 후 이동/공격 속도 배율. 1.0 = 기본, EnragePattern 발동 시 증가.</summary>
    public float AttackSpeedMult = 1f;

    /// <summary>각성 상태 여부. BKEnragePatternSO.CanExecute 에서 중복 발동 방지에 사용.</summary>
    public bool HasEnraged;

    /// <summary>보스 전용 오디오 풀. BlackKnightBoss.OnInitialized 에서 주입.</summary>
    public BKAudioPool AudioPool;

    public void TickCooldowns(float deltaTime)
    {
        if (ChargeCooldown    > 0f) ChargeCooldown    -= deltaTime;
        if (OverheadCooldown  > 0f) OverheadCooldown  -= deltaTime;
        if (RainCooldown      > 0f) RainCooldown      -= deltaTime;
        if (ScatterCooldown   > 0f) ScatterCooldown   -= deltaTime;
        if (LeapCooldown      > 0f) LeapCooldown      -= deltaTime;
        if (BackstepCooldown  > 0f) BackstepCooldown  -= deltaTime;
        if (DashSlashCooldown > 0f) DashSlashCooldown -= deltaTime;
        if (PressureCooldown  > 0f) PressureCooldown  -= deltaTime;
    }

    public void Reset()
    {
        ChargeCooldown    = 0f;
        OverheadCooldown  = 0f;
        RainCooldown      = 0f;
        ScatterCooldown   = 0f;
        LeapCooldown      = 0f;
        BackstepCooldown  = 0f;
        DashSlashCooldown = 0f;
        PressureCooldown  = 0f;
        ChaseSpeedMult    = 1f;
        NormalModeTimer   = 0f;
        RainPhase         = 0;
        LastPatternTag    = "";
        AttackSpeedMult   = 1f;
        HasEnraged        = false;
    }
}
