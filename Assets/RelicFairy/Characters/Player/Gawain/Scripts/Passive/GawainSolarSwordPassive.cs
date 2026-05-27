using UnityEngine;

/// <summary>
/// 가웨인 패시브 — 태양의 검.
/// 기본 공격이 적중하면 대상에 화상(DoT) 을 부여한다.
/// MonsterBurnHandler 가 부착되어 정해진 시간 동안 지속 피해를 가한다.
/// </summary>
public class GawainSolarSwordPassive : CharacterPassiveBase
{
    // ── Constants ─────────────────────────────────────────────
    private const float BurnDpsRatio   = 0.20f; // 기본 공격력의 20%/초
    private const float BurnDuration   = 3.0f;
    private const float BurnTickRate   = 0.5f;

    public override string         PassiveName => "태양의 검";
    public override PassiveTrigger Trigger     => PassiveTrigger.OnAttackHit;

    public override bool CanApply(PlayerController ctrl, in PassiveContext ctx)
        => ctx.target != null && ctx.damage > 0f;

    public override void Apply(PlayerController ctrl, in PassiveContext ctx)
    {
        float baseAttack = ctrl.RuntimeStats.GetEffectiveAttack(AttackStatKind.Melee);
        float dps        = baseAttack * BurnDpsRatio;
        if (dps <= 0f) return;

        MonsterBurnHandler.Apply(
            target:       ctx.target,
            dps:          dps,
            duration:     BurnDuration,
            tickInterval: BurnTickRate,
            instigator:   ctrl.gameObject);
    }
}
