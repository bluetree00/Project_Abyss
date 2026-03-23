/// <summary>
/// 보스 공격 노드 간 공유 블랙보드.
/// 쿨다운은 시간 기반 패턴(ChargeAttack, OverheadSlash)에만 사용.
/// SpinSlash 는 HP 임계값 기반이므로 쿨다운 없음 — BKSpinSlashState._nextThresholdIndex 로 관리.
/// NormalModeTimer: 비특수 상태(평타 모드)에서 경과한 시간.
///   특수 상태 진입 시 0으로 리셋. OverheadSlash 발동 최소 평타 시간 보장에 사용.
/// </summary>
public class BossAttackBlackboard
{
    public float ChargeCooldown;
    public float OverheadCooldown;

    /// <summary>마지막 특수 상태 종료 후 평타 모드로 경과한 시간 (초).</summary>
    public float NormalModeTimer;

    public void TickCooldowns(float deltaTime)
    {
        if (ChargeCooldown   > 0f) ChargeCooldown   -= deltaTime;
        if (OverheadCooldown > 0f) OverheadCooldown -= deltaTime;
    }

    public void Reset()
    {
        ChargeCooldown   = 0f;
        OverheadCooldown = 0f;
        NormalModeTimer  = 0f;
    }
}
