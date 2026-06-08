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
    private const int V_EXPLODE_RADIUS         = 3;
    private const int V_EXPLODE_MULT           = 4;

    public override string CovenantId => CovenantFactory.Morrigan;

    // ── 런타임 상태 ──────────────────────────────────────
    private int  _stacks;
    private bool _permanentLocked;
    private bool _exploding; // DealAoe 재진입(폭발→처치→OnKill) 가드

    private float StackBonus           => V(V_STACK_BONUS,            0.02f);
    private int   MaxStacks            => VI(V_MAX_STACKS,            10);
    private int   EvolvedLockThreshold => VI(V_EVOLVED_LOCK_THRESHOLD, 10);
    private float ExplodeRadius        => V(V_EXPLODE_RADIUS,         3f);
    private float ExplodeMult          => V(V_EXPLODE_MULT,           0.6f);
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

            RefreshStats();
        }

        if (IsEvolved && _stacks > 0 && !_exploding)
        {
            // 처치 위치 폭발 — 재진입 가드로 폭발 처치가 다시 폭발을 부르지 않게
            _exploding = true;
            Vector3 at = target != null ? target.transform.position : PlayerPos;
            DealAoe(at, ExplodeRadius, ExplodeMult, knockback: 0.3f);
            Vfx("VFX_FireExplosion", at); // 임시 VFX (전용 자산 대기)
            _exploding = false;
        }
    }

    public override void OnRoomClear()
    {
        if (_permanentLocked) return;

        _stacks = 0;
        RefreshStats();
    }
}
