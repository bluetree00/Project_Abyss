using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 모드레드의 서약 — 배신으로 맺어진 파멸의 계약
///
/// [Basic]    공격력 +50%, 방어력 -30%
/// [Enhanced] 공격력 +70%, 방어력 -15%
/// [Evolved]  누적 처치 20마리 달성 시 방어력 패널티 제거, 공격력 +20% 추가 고정
/// </summary>
public sealed class MordredCovenant : CovenantBase
{
    private const int V_ATK_BONUS        = 0;
    private const int V_DEF_PENALTY      = 1;
    private const int V_EVOLVED_ATK_BONUS = 2;
    private const int V_KILL_THRESHOLD   = 3;

    public override string CovenantId => CovenantFactory.Mordred;

    // ── 런타임 상태 ──────────────────────────────────────
    private int  _killCount;
    private bool _penaltyRemoved;

    private float AtkBonus       => V(V_ATK_BONUS,         50f);
    private float DefPenalty     => V(V_DEF_PENALTY,       -30f);
    private float EvolvedAtkBonus => V(V_EVOLVED_ATK_BONUS, 0f);
    private int   KillThreshold  => VI(V_KILL_THRESHOLD,   20);

    // ── 스탯 레이어 기여 ─────────────────────────────────
    public override IEnumerable<StatModifier> GetStatModifiers()
    {
        float atkTotal = AtkBonus;
        if (Stage == CovenantStage.Evolved && _penaltyRemoved)
            atkTotal += EvolvedAtkBonus;

        yield return new StatModifier(StatType.AttackPower, atkTotal);

        if (!_penaltyRemoved)
            yield return new StatModifier(StatType.Defense, DefPenalty);
    }

    // ── 이벤트 ──────────────────────────────────────────
    public override void OnKill(GameObject target)
    {
        if (_penaltyRemoved) return;

        _killCount++;
        if (Stage == CovenantStage.Evolved && _killCount >= KillThreshold)
        {
            _penaltyRemoved = true;
            RefreshStats();
        }
    }
}
