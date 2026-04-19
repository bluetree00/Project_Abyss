using System.Collections.Generic;
using UnityEngine;

namespace Abyss.Monster
{
[CreateAssetMenu(fileName = "BossConfig", menuName = "Abyss/Boss/BossConfig")]
public class BossConfigSO : MonsterConfigSO
{
    [Header("Pattern Break")]
    public float patternBreakDurationMin = 2.5f;
    public float patternBreakDurationMax = 5f;

    [Header("Repeat Penalty")]
    public float patternRepeatPenaltyDuration = 20f;
    public float patternRepeatPenaltyMult = 0.1f;

    [Header("Condition Params")]
    public float condPhase2HpThreshold = 0.4f;
    public float condDistClose = 3.5f;
    public float condDistFar = 5.0f;
    public float condTimePressureSecs = 8.0f;

    [Header("Pattern Entries")]
    public List<BossPatternEntry> patternEntries = new();
}

public enum BossConditionKey
{
    // ── 공용 (모든 보스) ──────────────────────────────────────────
    Phase2        = 0,   // HpBelowCondition(condPhase2HpThreshold)
    Dist_Close    = 1,   // MaxRangeCondition(condDistClose)
    Dist_Far      = 2,   // MinRangeCondition(condDistFar)
    AfterBackstep = 3,   // LastTagCondition("backstep")
    AfterSidestep = 4,   // LastTagCondition("sidestep")
    TimePressure  = 5,   // NormalModeTimerCondition(condTimePressureSecs)

    // ── DragonBoss 전용 ──────────────────────────────────────────
    Dragon_Summon80       = 6,
    Dragon_Summon50       = 7,
    Dragon_Summon10       = 8,
    Dragon_ElementIce     = 9,
    Dragon_ElementThunder = 10,
    Dragon_ElementFire    = 11,

    // ── ForestGuardian 전용 ──────────────────────────────────────
    FG_PhaseChangePending = 12,  // 페이즈 전환 대기 중 (HP ≤ 50% && !IsPhase2)
    FG_IsPhase2           = 13,  // 2페이즈 완전 진입 상태
    FG_IsGroggy           = 14,  // 그로기(경직) 상태 중

    // ── DragonBoss 바디 상태 ─────────────────────────────────────
    Dragon_Body_Grounded  = 15,  // 지상 상태 (BodyState == Grounded)
    Dragon_Body_Airborne  = 16,  // 공중 상태 (BodyState == Airborne)
}

[System.Serializable]
public class BossPatternEntry
{
    public List<BossConditionKey> conditions = new();
    [System.NonSerialized] public ICondition[] BuiltConditions;
    public List<BossPatternSO> patterns = new();
    public PatternSelectionMode selectionMode = PatternSelectionMode.WeightedRandom;
    public bool forceExecute = false;

    public bool EvaluateConditions(BossPatternContext ctx)
    {
        if (BuiltConditions == null || BuiltConditions.Length == 0) return true;
        foreach (var c in BuiltConditions)
        {
            if (!c.Evaluate(ctx))
                return false;
        }

        return true;
    }
}

public enum PatternSelectionMode
{
    WeightedRandom,
    Sequential,
    Random,
}
}
