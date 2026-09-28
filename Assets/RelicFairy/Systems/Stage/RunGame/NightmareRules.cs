using System.Collections.Generic;
using UnityEngine;

/// <summary>악몽 규칙 id. 공통 1 + 챕터별 1.</summary>
public enum NightmareRuleId
{
    None,
    DimLight,      // 공통 — 흐려진 빛: 포션 최대 2
    RootBind,      // Ch1  — 뿌리의 속박: 회피 스태미나 소모 ×1.4
    MeltedShield,  // Ch2  — 녹아내린 방패: 보호막 상한 최대체력 15%
    BrokenOath,    // Ch3  — 부서진 맹세: 새 서약을 맺을 수 없다
    BackflowSpell, // Ch4  — 역류하는 술식: 무기 스킬(E·R) 재사용 대기 ×1.25
}

/// <summary>
/// 악몽 규칙 — 붕괴 뒤(악몽기) 모든 챕터가 <b>플레이어를 묶는 규칙</b>을 건다(적 수치는 올리지 않는다).
/// 규칙 2개만 켠다: 공통 「흐려진 빛」 + 그 챕터의 규칙 1개. 리치까지 성장한 플레이어가 버틸 수 있는 수준.
///
/// <para>저장하지 않는다 — 세계 단계(<see cref="StoryProgress.IsNightmare"/>)와 현재 챕터에서 매번 유도한다.
/// 플레이어가 새로 바인딩될 때(챕터 시작·이어하기) <see cref="ApplyOnBind"/>로 값형 규칙을 다시 건다 —
/// 포션 최대치는 세이브 보정이 3으로 되돌리고, 보호막 상한은 플레이어 인스턴스마다 새로 생기기 때문이다.</para>
///
/// 설계: 기획 「최종장이후_사이클시나리오」 v2 §5.
/// </summary>
public static class NightmareRules
{
    // ── 수치 ───────────────────────────────────────────
    private const int   DimLightPotionCapacity     = 2;
    private const float RootBindStaminaMultiplier  = 1.4f;   // 25 → 35
    private const float MeltedShieldCapRatio       = 0.15f;  // 0.30 → 0.15
    private const float BackflowCooldownMultiplier = 1.25f;
    private const float NightmareEssenceMultiplier = 1.2f;   // 필수 진행 구간이라 벌칙만 두지 않는다

    public readonly struct RuleInfo
    {
        public readonly NightmareRuleId Id;
        public readonly string Name;
        public readonly string Description;
        public readonly string IconKey;
        /// <summary>버프창 툴팁 본문(이름 + 설명) — 폴링마다 문자열을 만들지 않게 미리 둔다.</summary>
        public readonly string BuffLabel;

        public RuleInfo(NightmareRuleId id, string name, string description, string iconKey)
        {
            Id = id; Name = name; Description = description; IconKey = iconKey;
            BuffLabel = $"악몽 · {name}\n<size=85%>{description}</size>";
        }
    }

    // 아이콘은 기존 IconKey 어휘(EffectIconSetSO)를 쓴다 — 전용 아트가 오면 그 키로 교체.
    private static readonly RuleInfo[] Catalog =
    {
        new(NightmareRuleId.DimLight,      "흐려진 빛",       "포션을 2개까지만 지닐 수 있다",          "heal"),
        new(NightmareRuleId.RootBind,      "뿌리의 속박",     "회피가 스태미나를 더 먹는다 (25 → 35)",  "roll"),
        new(NightmareRuleId.MeltedShield,  "녹아내린 방패",   "보호막 상한이 최대 체력의 15%",          "shield"),
        new(NightmareRuleId.BrokenOath,    "부서진 맹세",     "이 성채에선 새 서약을 맺을 수 없다",     "special"),
        new(NightmareRuleId.BackflowSpell, "역류하는 술식",   "무기 스킬 재사용 대기 +25%",             "cooldown"),
    };

    // ── 상태 ───────────────────────────────────────────

    /// <summary>지금 런이 악몽 규칙 아래에 있는가.</summary>
    public static bool IsActive => StoryProgress.IsNightmare && CurrentRun != null;

    private static GameRunSession CurrentRun => GameRunBootstrapper.Instance?.Run;

    /// <summary>챕터 고유 규칙.</summary>
    public static NightmareRuleId ChapterRule(ChapterId chapter) => chapter switch
    {
        ChapterId.Chapter1 => NightmareRuleId.RootBind,
        ChapterId.Chapter2 => NightmareRuleId.MeltedShield,
        ChapterId.Chapter3 => NightmareRuleId.BrokenOath,
        ChapterId.Chapter4 => NightmareRuleId.BackflowSpell,
        _                  => NightmareRuleId.None,
    };

    public static bool Has(NightmareRuleId id)
    {
        if (id == NightmareRuleId.None || !IsActive) return false;
        return id == NightmareRuleId.DimLight || id == ChapterRule(CurrentRun.CurrentChapter);
    }

    /// <summary>지금 켜진 규칙(공통 → 챕터 순). 없으면 빈 목록.</summary>
    public static void CollectActive(List<RuleInfo> into)
    {
        if (!IsActive) return;
        foreach (var info in Catalog)
            if (Has(info.Id)) into.Add(info);
    }

    // ── 값형 규칙 (호출부가 곱한다) ─────────────────────

    public static float DodgeStaminaMultiplier     => Has(NightmareRuleId.RootBind)      ? RootBindStaminaMultiplier  : 1f;
    public static float WeaponSkillCooldownMultiplier => Has(NightmareRuleId.BackflowSpell) ? BackflowCooldownMultiplier : 1f;
    public static bool  BlocksCovenantAltar        => Has(NightmareRuleId.BrokenOath);
    public static float EssenceMultiplier          => IsActive ? NightmareEssenceMultiplier : 1f;

    // ── 적용 ───────────────────────────────────────────

    /// <summary>플레이어 바인딩 직후(챕터 시작·이어하기) — 값형 규칙을 걸고 켜진 규칙을 알린다.</summary>
    public static void ApplyOnBind(GameRunSession run)
    {
        if (run == null || !StoryProgress.IsNightmare) return;

        if (Has(NightmareRuleId.DimLight))
            run.PlayerState?.SetPotionCapacity(DimLightPotionCapacity);

        var stats = run.Player != null ? run.Player.RuntimeStats : null;
        if (stats != null && Has(NightmareRuleId.MeltedShield))
            stats.SynergyMechanics.ShieldCapRatio = MeltedShieldCapRatio;

        var active = new List<RuleInfo>(2);
        CollectActive(active);
        if (active.Count == 0) return;

        var names = new System.Text.StringBuilder("악몽 — ");
        for (int i = 0; i < active.Count; i++)
        {
            if (i > 0) names.Append(" · ");
            names.Append(active[i].Name);
        }
        Debug.Log($"[Nightmare] 규칙 적용 — {run.CurrentChapter}: {names}");
        Object.FindFirstObjectByType<HudPresenter>(FindObjectsInactive.Include)?.ShowBuffNotice(names.ToString());
    }
}

/// <summary>켜진 악몽 규칙을 버프창에 띄운다(해제 불가 디버프, 게이지 없음).</summary>
public sealed class NightmareRuleBuffViewSource : IBuffViewSource
{
    private readonly List<NightmareRules.RuleInfo> _buffer = new(2);

    public void Contribute(List<BuffViewItem> into)
    {
        _buffer.Clear();
        NightmareRules.CollectActive(_buffer);
        foreach (var rule in _buffer)
            into.Add(new BuffViewItem(rule.IconKey, rule.BuffLabel, 0, -1f, string.Empty, BuffSource.Status, isDebuff: true));
    }
}
