using UnityEngine;

/// <summary>
/// 갈라하드 패시브 1 — 아버지의 죄
/// 피격 시 신성 게이지 +15 충전.
/// </summary>
public class GalahadFathersGuiltPassive : CharacterPassiveBase
{
    private const float GaugeOnHit = 15f;

    private readonly HolyGauge _gauge;

    public GalahadFathersGuiltPassive(HolyGauge gauge)
    {
        _gauge = gauge;
    }

    public override string         PassiveName => "아버지의 죄";
    public override PassiveTrigger Trigger     => PassiveTrigger.OnTakeDamage;

    public override bool CanApply(PlayerController ctrl, in PassiveContext ctx)
        => ctx.damage > 0 && _gauge != null;

    public override void Apply(PlayerController ctrl, in PassiveContext ctx)
        => _gauge.AddGauge(GaugeOnHit);
}
