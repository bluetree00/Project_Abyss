using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 모리건의 서약 — 전쟁의 여신과 맺은 처치 강화 계약
///
/// [Basic]    처치마다 공격속도 +2% 중첩 (최대 10중첩, 방 이동 시 초기화)
/// [Enhanced] 중첩당 +3%, 최대 15중첩
/// [Evolved]  10중첩 달성 시 초기화 없이 영구 유지, 중첩마다 처치 시 폭발 발생
/// </summary>
public sealed class MorriganCovenant : CovenantBase
{
    private const int V_STACK_BONUS            = 0;
    private const int V_MAX_STACKS             = 1;
    private const int V_EVOLVED_LOCK_THRESHOLD = 2;

    public override string CovenantId => CovenantFactory.Morrigan;

    // ── 런타임 상태 ──────────────────────────────────────
    private int  _stacks;
    private bool _permanentLocked;

    private float StackBonus           => V(V_STACK_BONUS,            0.02f);
    private int   MaxStacks            => VI(V_MAX_STACKS,            10);
    private int   EvolvedLockThreshold => VI(V_EVOLVED_LOCK_THRESHOLD, 10);
    private bool  IsEvolved            => Stage == CovenantStage.Evolved;

    // ── 스탯 레이어 기여 ─────────────────────────────────
    public override IEnumerable<StatModifier> GetStatModifiers()
    {
        if (_stacks <= 0) yield break;
        yield return new StatModifier(StatType.AttackSpeed, _stacks * StackBonus);
    }

    // ── 이벤트 ──────────────────────────────────────────
    public override void OnKill(GameObject target)
    {
        if (_stacks < MaxStacks)
        {
            _stacks++;

            if (IsEvolved && _stacks >= EvolvedLockThreshold && !_permanentLocked)
                _permanentLocked = true;

            Ctx.Stats.RefreshCovenants(Ctx.Session.CovenantHandler);
        }

        if (IsEvolved && _stacks > 0)
        {
            // TODO: 처치 위치에 폭발 이펙트 발동
        }
    }

    public override void OnRoomClear()
    {
        if (_permanentLocked) return;

        _stacks = 0;
        Ctx.Stats.RefreshCovenants(Ctx.Session.CovenantHandler);
    }
}
