using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 쿠훌린의 서약 — 전사의 광기 서약 (게시, 戱詩)
///
/// [Basic]    HP 50% 이하 시 게시 발동 — 피해 +60%, 방어력 -40%
/// [Enhanced] 발동 조건 HP 65%, 피해 +80%
/// [Evolved]  게시 중 처치 시 HP 2% 회복 + 게시 지속시간 연장 (무한 유지 가능)
/// </summary>
public sealed class CuChulainnCovenant : CovenantBase
{
    private const int V_TRIGGER_THRESHOLD  = 0;
    private const int V_DMG_BONUS          = 1;
    private const int V_DEF_PENALTY        = 2;
    private const int V_EVOLVED_HEAL_RATIO = 3;

    public override string CovenantId => CovenantFactory.CuChulainn;

    // ── 런타임 상태 ──────────────────────────────────────
    private bool _warpSpasm;

    private float TriggerThreshold  => V(V_TRIGGER_THRESHOLD,  0.50f);
    private float DmgBonus          => V(V_DMG_BONUS,          60f);
    private float DefPenalty        => V(V_DEF_PENALTY,        -40f);
    private float EvolvedHealRatio  => V(V_EVOLVED_HEAL_RATIO, 0.02f);
    private bool  IsEvolved         => Stage == CovenantStage.Evolved;

    private bool ShouldActivate => Ctx != null &&
        (float)Ctx.RunState.Hp / Ctx.RunState.MaxHp <= TriggerThreshold;

    // ── 스탯 레이어 기여 ─────────────────────────────────
    public override IEnumerable<StatModifier> GetStatModifiers()
    {
        if (!_warpSpasm) yield break;

        yield return new StatModifier(StatType.AttackPower, DmgBonus);
        yield return new StatModifier(StatType.Defense,     DefPenalty);
    }

    // ── 이벤트 ──────────────────────────────────────────
    public override void OnTakeDamage(float damage)
    {
        bool shouldBeActive = ShouldActivate;
        if (shouldBeActive == _warpSpasm) return;

        _warpSpasm = shouldBeActive;
        Ctx.Stats.RefreshCovenants(Ctx.Session.CovenantHandler);
    }

    public override void OnKill(GameObject target)
    {
        if (!IsEvolved || !_warpSpasm) return;

        // TODO: HP EvolvedHealRatio% 회복
        // TODO: 게시 지속 연장 (HP 임계 해제 지연 or 별도 타이머)
    }
}
