using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
[CreateAssetMenu(fileName = "BossConfig", menuName = "RelicFairy/Boss/BossConfig")]
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
    Dragon_Summon70       = 6,
    Dragon_Summon40       = 7,
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

    // ── ForestGuardian HP 범위 ────────────────────────────────────
    FG_Phase1 = 17,   // HpAboveCondition(condPhase2HpThreshold) — 1페이즈 (HP > 50%)
    FG_Phase2 = 18,   // HpBelowCondition(condPhase2HpThreshold) — 2페이즈 HP 범위 (HP ≤ 50%)

    // ── DeathKnight 전용 ─────────────────────────────────────────
    DK_IsPhase2  = 19,  // DKBlackboard.IsPhase2 — Phase2 전환 완료
    DK_IsEnraged = 20,  // Enrage 상태 (HP ≤ enrageHpThreshold, 공격속도/이동속도 증가)
    DK_IsPhase1  = 24,  // !DKBlackboard.IsPhase2 — Phase2 전환 전 (1페이즈 전용 조건)

    // ── Lich (리치) 전용 — 페이지 키는 모드 키(Sealed/Nightmare)와 함께 써서 두 전투의 풀을 가른다 ──
    Lich_Phase1        = 21,  // 1페이지 (첫 전환 전) — 두 모드 공통
    Lich_IsPhase2      = 22,  // 2페이지 — 봉인기 P2′ 「사슬에 묶인 낫」 / 악몽기 P2 「대마법+낫」
    Lich_Phase2Pending = 23,  // 1페이지 && HP ≤ 첫 임계 — 2페이지 전환 대기 (봉인기 T1 / 악몽기 T2)
    Lich_Sealed        = 25,  // 봉인기 전투 (봉인된 리치)
    Lich_Nightmare     = 26,  // 악몽기 전투 (해방된 리치)
    Lich_Phase3        = 27,  // 3페이지 「영혼 복제」 (악몽기만)
    Lich_Phase3Pending = 28,  // 2페이지 && HP ≤ 둘째 임계 — 3페이지 전환 대기 (T3 최후의 원)
    Lich_FinalMagicPending = 29,  // 3페이지 && HP ≤ 최후의 대마법 임계 && 아직 안 막음 (F4 강제)

    // ── 2페이지(해방) 공용 — 숲 · 화룡 · 기사 (BossPages, 09-28) ──
    Page_1             = 40,  // 2페이지가 아님(봉인기 전투 전부 포함)
    Page_2             = 41,  // 2페이지(악몽기 해방 페이지)
    Page_TransitionDue = 42,  // 1페이지 체력이 다 깎임 — 전환 패턴 강제
    Page_SignatureDue  = 43,  // 2페이지 체력 50% · 간판 아직 — 간판 패턴 강제
    Page_Opener        = 44,  // 2페이지 개막 — 아직 2페이지 패턴을 안 씀(§9 구성: 목표를 보여 주는 패턴 먼저)
    Page_Late          = 45,  // 2페이지 후반 — 간판을 쓴 뒤(연계기 · 쉬는 시간 −25%)
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
