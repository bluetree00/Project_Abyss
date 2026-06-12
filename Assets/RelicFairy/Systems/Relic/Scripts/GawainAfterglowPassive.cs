using UnityEngine;

/// <summary>
/// 가웨인 패시브2(잔열) — 황혼 처치 가속.
/// 쿨다운(황혼) 구간 중 적 처치 시 다음 정오 게이지 충전 속도 +가속.
/// (화상 쿨다운 잔존·감소는 후속 — burn 수명을 phase에 묶는 작업)
/// 수치: RELIC_STAT_DATA(gawain) 슬롯 15(황혼 처치 충전 가속).
/// </summary>
public sealed class GawainAfterglowPassive : CharacterPassiveBase
{
    private const string RelicKey = "gawain";
    private const int V_TWILIGHT_ACCEL = 15;

    public override string         PassiveName => "태양의 잔열";
    public override PassiveTrigger Trigger     => PassiveTrigger.OnKill;

    public override bool CanApply(PlayerController ctrl, in PassiveContext ctx)
        => ctrl.RelicBehavior is GawainZenithRelic gz && gz.Gauge != null
           && gz.Gauge.CurrentPhase == ZenithGauge.ZPhase.Cooldown;

    public override void Apply(PlayerController ctrl, in PassiveContext ctx)
    {
        if (ctrl.RelicBehavior is not GawainZenithRelic gz || gz.Gauge == null) return;
        float accel = Managers.RelicStatData != null ? Managers.RelicStatData.Get(RelicKey, V_TWILIGHT_ACCEL, 0.2f) : 0.2f;
        gz.Gauge.AddChargeAccel(accel);
    }
}
