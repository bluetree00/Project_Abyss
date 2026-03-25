/// <summary>
/// 보스 공격 노드 간 공유 블랙보드.
/// 쿨다운은 시간 기반 패턴에 사용.
/// SpinSlash 는 HP 임계값 기반이므로 쿨다운 없음 — BKSpinSlashState._nextThresholdIndex 로 관리.
/// NormalModeTimer: 비특수 상태(평타 모드)에서 경과한 시간.
///   특수 상태 진입 시 0으로 리셋. OverheadSlash 발동 최소 평타 시간 보장에 사용.
/// </summary>
public class BossAttackBlackboard
{
    public float ChargeCooldown;
    public float OverheadCooldown;
    public float RainCooldown;
    public float ScatterCooldown;
    public float LeapCooldown;

    /// <summary>마지막 특수 상태 종료 후 평타 모드로 경과한 시간 (초).</summary>
    public float NormalModeTimer;

    /// <summary>운석 낙하 페이즈 (0=기본, 1=1차 스핀 후, 2=2차 스핀 후).</summary>
    public int RainPhase;

    /// <summary>보스 전용 오디오 풀. BlackKnightBoss.OnInitialized 에서 주입.</summary>
    public BKAudioPool AudioPool;

    public void TickCooldowns(float deltaTime)
    {
        if (ChargeCooldown   > 0f) ChargeCooldown   -= deltaTime;
        if (OverheadCooldown > 0f) OverheadCooldown -= deltaTime;
        if (RainCooldown     > 0f) RainCooldown     -= deltaTime;
        if (ScatterCooldown  > 0f) ScatterCooldown  -= deltaTime;
        if (LeapCooldown     > 0f) LeapCooldown     -= deltaTime;
    }

    public void Reset()
    {
        ChargeCooldown   = 0f;
        OverheadCooldown = 0f;
        RainCooldown     = 0f;
        ScatterCooldown  = 0f;
        LeapCooldown     = 0f;
        NormalModeTimer  = 0f;
        RainPhase        = 0;
    }
}
