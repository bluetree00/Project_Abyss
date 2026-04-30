using UnityEngine;


namespace Abyss.Monster
{
/// <summary>
/// ICondition 구현체 모음.
///
/// BlackKnightBoss.BuildConditions() 에서 conditionKey → ICondition 인스턴스로 조립한다.
/// 조건 수치(거리, HP 비율 등)는 BossConfigSO 의 cond* 필드에서 가져온다.
///
/// AllConditions : AND 합성 — 여러 조건을 하나의 ICondition 으로 묶는다.
/// </summary>

// ── AND 합성 ───────────────────────────────────────────────
public sealed class AllConditions : ICondition
{
    private readonly ICondition[] _conditions;
    public AllConditions(params ICondition[] conditions) => _conditions = conditions;

    public bool Evaluate(BossPatternContext ctx)
    {
        foreach (var c in _conditions)
            if (!c.Evaluate(ctx)) return false;
        return true;
    }
}

// ── 항상 참 ────────────────────────────────────────────────
public sealed class AlwaysTrue : ICondition
{
    public bool Evaluate(BossPatternContext ctx) => true;
}

// ── 거리 ───────────────────────────────────────────────────
/// <summary>플레이어까지 거리 ≤ maxDist 이면 참.</summary>
public sealed class MaxRangeCondition : ICondition
{
    private readonly float _maxDist;
    public MaxRangeCondition(float maxDist) => _maxDist = maxDist;

    public bool Evaluate(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        return Vector3.Distance(
            ctx.Ctx.Transform.position,
            ctx.Ctx.Runtime.PlayerTarget.position) <= _maxDist;
    }
}

/// <summary>플레이어까지 거리 ≥ minDist 이면 참.</summary>
public sealed class MinRangeCondition : ICondition
{
    private readonly float _minDist;
    public MinRangeCondition(float minDist) => _minDist = minDist;

    public bool Evaluate(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        return Vector3.Distance(
            ctx.Ctx.Transform.position,
            ctx.Ctx.Runtime.PlayerTarget.position) >= _minDist;
    }
}

// ── HP 비율 ────────────────────────────────────────────────
/// <summary>현재 HP 비율 ≤ threshold (0~1) 이면 참.</summary>
public sealed class HpBelowCondition : ICondition
{
    private readonly float _threshold;
    public HpBelowCondition(float threshold) => _threshold = threshold;

    public bool Evaluate(BossPatternContext ctx)
    {
        var rt  = ctx.Ctx.Runtime;
        var cfg = ctx.Ctx.Config;
        if (cfg?.stat == null || cfg.stat.maxHp == 0) return false;
        float ratio = (float)rt.CurrentHp / cfg.stat.maxHp;
        return ratio <= _threshold;
    }
}

/// <summary>현재 HP 비율 > threshold (0~1) 이면 참.</summary>
public sealed class HpAboveCondition : ICondition
{
    private readonly float _threshold;
    public HpAboveCondition(float threshold) => _threshold = threshold;

    public bool Evaluate(BossPatternContext ctx)
    {
        var rt  = ctx.Ctx.Runtime;
        var cfg = ctx.Ctx.Config;
        if (cfg?.stat == null || cfg.stat.maxHp == 0) return false;
        float ratio = (float)rt.CurrentHp / cfg.stat.maxHp;
        return ratio > _threshold;
    }
}

// ── 직전 패턴 태그 ─────────────────────────────────────────
/// <summary>Blackboard.LastPatternTag == tag 이면 참.</summary>
public sealed class LastTagCondition : ICondition
{
    private readonly string _tag;
    public LastTagCondition(string tag) => _tag = tag;

    public bool Evaluate(BossPatternContext ctx)
        => ctx.Blackboard.LastPatternTag == _tag;
}

// ── 일반 모드 타이머 ───────────────────────────────────────
/// <summary>패턴을 쓰지 않은 시간 ≥ minDuration 이면 참 (압박 타이머).</summary>
public sealed class NormalModeTimerCondition : ICondition
{
    private readonly float _minDuration;
    public NormalModeTimerCondition(float minDuration) => _minDuration = minDuration;

    public bool Evaluate(BossPatternContext ctx)
        => ctx.Blackboard.NormalModeTimer >= _minDuration;
}
}
