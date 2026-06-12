using System.Collections.Generic;
using UnityEngine;
using RelicFairy.Monster;

/// <summary>
/// 가웨인 고유 스킬 — 태양 강림. 정오 전용·구간당 1회(GawainZenithRelic 게이팅).
/// 전방 부채꼴 광역 ATK×skillMult 1회 타격 + 적중 적에게 태양 화상(DoT) 부여.
/// 수치는 RELIC_STAT_DATA(gawain) 슬롯 구동. 화상은 기존 MonsterBurnHandler 재사용.
/// (애니 클립 QSkill_01은 RelicClassSO.QSkillClipKey로 오버라이드 — 미설정 시 연출만 생략, 판정은 동작)
/// </summary>
public sealed class SolarDescentSkillRuntime : ISkillRuntime
{
    private const string RelicKey = "gawain";
    private const int V_FIRST_HIT = 10, V_SKILL_MULT = 11, V_BURN_DURATION = 12, V_BURN_TICK_RATIO = 13;

    private const string AnimName        = "QSkill_01";
    private const float  AnimDuration    = 1.0f;
    private const float  HitTime         = 0.3f;
    private const float  FanRadius       = 4.5f;
    private const float  FanHalfAngleDeg = 60f;   // 부채꼴 반각(전방 120도)
    private const float  BurnTickInterval = 0.5f;

    private readonly GawainZenithRelic _relic;
    private float _elapsed;
    private bool  _hitDone;

    public SolarDescentSkillRuntime(GawainZenithRelic relic) { _relic = relic; }

    public void OnEnter(SkillExecutionContext ctx)
    {
        _elapsed = 0f; _hitDone = false;
        _relic?.MarkSkillUsed(); // 구간당 1회 소비

        ctx.RotateToMouse();
        ctx.SetMoveScale(0f);
        ctx.Animator?.CrossFade(AnimName, 0.1f);
    }

    public void OnUpdate(SkillExecutionContext ctx)
    {
        _elapsed += Time.deltaTime;
        if (!_hitDone && _elapsed >= HitTime) { _hitDone = true; FanStrike(ctx); }
        if (_elapsed >= AnimDuration) ctx.RequestEnd?.Invoke();
    }

    public void OnExit(SkillExecutionContext ctx) => ctx.SetMoveScale(1f);

    private void FanStrike(SkillExecutionContext ctx)
    {
        var pt = ctx.PlayerTransform;
        Vector3 origin = pt.position;
        Vector3 fwd = pt.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.001f) return;
        fwd.Normalize();
        float cosHalf = Mathf.Cos(FanHalfAngleDeg * Mathf.Deg2Rad);

        // 각인 첫타(정오 첫 공격이 스킬이면 스킬이 소비) → +50% = 525%
        float markMul = (_relic != null && _relic.ConsumeMarkFirstHit()) ? (1f + V(V_FIRST_HIT, 0.5f)) : 1f;
        float dmg     = ctx.CalculateDamage(V(V_SKILL_MULT, 3.5f)) * markMul;
        float effAtk  = ctx.RuntimeStats.GetEffectiveAttack(AttackStatKind.Melee);
        float burnDps = effAtk * V(V_BURN_TICK_RATIO, 0.15f);
        float burnDur = V(V_BURN_DURATION, 6f);

        var owner = ctx.Controller.gameObject;
        var hit   = new HashSet<GameObject>();
        var cols  = Physics.OverlapSphere(origin, FanRadius);
        foreach (var col in cols)
        {
            if (col == null || col.gameObject == owner) continue;
            Vector3 to = col.transform.position - origin; to.y = 0f;
            if (to.sqrMagnitude < 0.001f) continue;
            if (Vector3.Dot(fwd, to.normalized) < cosHalf) continue; // 부채꼴 밖

            var d = col.GetComponent<IDamageable>() ?? col.GetComponentInParent<IDamageable>();
            if (d == null || d is not Component dc) continue;
            if (!hit.Add(dc.gameObject)) continue; // 같은 적 1회

            d.TakeDamage(dmg, owner, 0.3f);
            if (burnDps > 0f)
                MonsterBurnHandler.Apply(target: dc.gameObject, dps: burnDps, duration: burnDur,
                                         tickInterval: BurnTickInterval, instigator: owner);
        }
    }

    private static float V(int slot, float fallback)
        => Managers.RelicStatData != null ? Managers.RelicStatData.Get(RelicKey, slot, fallback) : fallback;
}
