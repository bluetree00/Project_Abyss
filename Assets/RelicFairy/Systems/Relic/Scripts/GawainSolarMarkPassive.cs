using UnityEngine;

/// <summary>
/// 가웨인 패시브1(각인) — 정오 첫 공격 +50%.
/// 정오 진입 시 적립된 각인을 정오 첫 일반공격이 소비해 대상에 추가 피해(가산 근사).
/// 스킬(태양 강림)이 먼저 쓰이면 스킬이 각인을 가져간다(ConsumeMarkFirstHit 단일 소비).
/// 수치: RELIC_STAT_DATA(gawain) 슬롯 10(첫타 추가 배율).
/// </summary>
public sealed class GawainSolarMarkPassive : CharacterPassiveBase
{
    private const string RelicKey = "gawain";
    private const int V_FIRST_HIT = 10;

    public override string         PassiveName => "태양의 각인";
    public override PassiveTrigger Trigger     => PassiveTrigger.OnAttackHit;

    public override bool CanApply(PlayerController ctrl, in PassiveContext ctx)
        => ctx.target != null && ctx.damage > 0f && ctrl.RelicBehavior is GawainZenithRelic;

    public override void Apply(PlayerController ctrl, in PassiveContext ctx)
    {
        if (ctrl.RelicBehavior is not GawainZenithRelic gz) return;
        if (!gz.ConsumeMarkFirstHit()) return;

        float bonusMul = Managers.RelicStatData != null ? Managers.RelicStatData.Get(RelicKey, V_FIRST_HIT, 0.5f) : 0.5f;
        float bonus = ctx.damage * bonusMul;
        if (bonus <= 0f) return;

        if (ctx.target.TryGetComponent<IDamageable>(out var d))
            d.TakeDamage(bonus, ctrl.gameObject, 0f);
    }
}
