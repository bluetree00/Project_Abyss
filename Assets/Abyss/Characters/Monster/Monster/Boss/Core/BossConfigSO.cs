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
    Phase2,
    Dist_Close,
    Dist_Far,
    AfterBackstep,
    AfterSidestep,
    TimePressure,
    Dragon_Summon80,
    Dragon_Summon50,
    Dragon_Summon10,
    Dragon_ElementIce,
    Dragon_ElementThunder,
    Dragon_ElementFire,
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
