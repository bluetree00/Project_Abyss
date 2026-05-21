using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 모르가나의 서약 — 금지된 힘을 거래한 어둠의 계약
///
/// [Basic]    처치마다 최대 체력의 3% 흡혈
/// [Enhanced] 흡혈 5%, 누적 최대 30%까지
/// [Evolved]  누적 흡혈량이 최대 체력 초과 시 초과분이 다음 공격의 추가 피해로 방출
/// </summary>
public sealed class MorganaCovenant : CovenantBase
{
    private const int V_HEAL_RATIO     = 0;
    private const int V_MAX_HEAL_RATIO = 1;

    public override string CovenantId => CovenantFactory.Morgana;

    // ── 런타임 상태 ──────────────────────────────────────
    private float _accumulatedHeal;
    private float _pendingBurst;

    private float HealRatio    => V(V_HEAL_RATIO,     0.03f);
    private float MaxHealRatio => V(V_MAX_HEAL_RATIO, 0f);    // 0 = 한도 없음
    private bool  IsEvolved    => Stage == CovenantStage.Evolved;

    // ── 이벤트 ──────────────────────────────────────────
    public override void OnKill(GameObject target)
    {
        // TODO: 처치 시 흡혈 처리
        // healAmount = Ctx.Stats.MaxHp * HealRatio
        // MaxHealRatio > 0 이면 _accumulatedHeal 누적, MaxHp * MaxHealRatio 이상이면 Evolved 초과분 계산
        // Ctx.RunState.Heal(healAmount)
    }

    // ── 피해 파이프라인 ──────────────────────────────────
    public override void ModifyOutgoingDamage(ref float damage, CombatContext ctx)
    {
        if (!IsEvolved || _pendingBurst <= 0f) return;

        // TODO: _pendingBurst 추가 피해 방출 후 초기화
    }
}
