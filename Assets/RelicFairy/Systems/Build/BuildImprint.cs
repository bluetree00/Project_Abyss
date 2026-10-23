using System;
using System.Collections.Generic;

/// <summary>
/// 발동 계열 <b>각인</b> — 이번 런에 같은 계열을 몇 개 쥐었나(판에 놓인 룬 + 맺은 서약 원인). 런 스코프 계산 값이라 저장하지 않는다.
/// <para>각인 2 / 4 / 6에서 단계가 오르고, 그 계열 룬의 효과가 단계마다 +20%씩 커진다(<see cref="EffectMultiplier"/>) —
/// 같은 동작을 모을수록 그 동작이 더 세지는 것이 빌드의 뼈대다(빌드 컨셉 「발동 계열」 §2-2).</para>
/// </summary>
public static class BuildImprint
{
    // ── Static ───────────────────────────────────────────
    private static readonly int[] s_counts = new int[8];
    private static readonly int[] s_halves = new int[8];   // 반 단위 셈 — 룬 · 서약 · 무기 · 조각 2, 스탯 룬 1(10-14 R0)
    private static readonly int[] s_stages = new int[8];
    private static bool s_primed;   // 런 첫 셈은 기준만 잡는다(불러오기 · 시작 때 알림이 쏟아지지 않게)

    /// <summary>각인이 다시 셈해졌다(판 · 서약이 바뀜) — 추적 패널이 듣는다.</summary>
    public static event Action Changed;

    // ── Public Methods ───────────────────────────────────

    public static int Count(BuildFamily f) => f == BuildFamily.None ? 0 : s_counts[(int)f];

    public static int Stage(BuildFamily f) => BuildFamilyRules.StageOf(Count(f));

    /// <summary>그 계열 룬 효과 배율 — 1, 1.2, 1.4, 1.6.</summary>
    public static float EffectMultiplier(BuildFamily f) => 1f + BuildFamilyRules.BonusPerStage * Stage(f);

    /// <summary>각인 반 단위 수(스탯 룬 하나 = 1 · 그 밖 = 2) — 룬 선택 카드가 「+0.5」를 보일 때 쓴다.</summary>
    public static int Halves(BuildFamily f) => f == BuildFamily.None ? 0 : s_halves[(int)f];

    /// <summary>
    /// 다시 센다 — 판에 놓인 룬(룬 하나 = 계열 하나 · 스탯 룬은 절반) + 맺은 서약의 원인 계열 + 무기 두 자루(승급 · 진화).
    /// 효과 재구성(Rebuild) 바로 앞에서 부른다.
    /// </summary>
    public static void Recount(GameRunSession run)
    {
        Array.Clear(s_halves, 0, s_halves.Length);
        if (run != null)
        {
            var placed = run.ItemInventory?.PlacedItems;
            if (placed != null)
                foreach (var item in placed)
                {
                    var f = BuildFamilyRules.OfItem(item, out bool statOnly);
                    if (f != BuildFamily.None) s_halves[(int)f] += statOnly ? 1 : 2;
                }

            var held = run.CovenantHandler?.Covenants;
            if (held != null)
                foreach (var c in held)
                {
                    // 조건(원인) 형상 = 계열 1칸 — 서약서(문장)는 조건절 하나(설계서 §5)
                    string causeId = c is AssembledCovenant a ? a.CauseId : c is CovenantSentence s ? s.CauseId : null;
                    if (causeId == null || !CovenantPalette.TryGetCause(causeId, out var def)) continue;
                    Add(BuildFamilyRules.FromCause(def.cls));
                }

            var wm = run.Player != null ? run.Player.WeaponManager : null;
            if (wm != null)
            {
                Add(BuildFamilyRules.OfWeapon(wm.Weapon0Data));
                Add(BuildFamilyRules.OfWeapon(wm.Weapon1Data));
            }

            // 유물 조각 · 진화는 더 이상 세지 않는다(10-22 E1 D4 — 유물 창에 계열 낱말 0 · 진화 「각인 +2」 삭제)

            // 근접 강화 증강 「각인 공명」(10-15) — 지금 든 무기에 있으면 가장 높은 계열에 반 칸 단위로 더한다
            float echo = MeleeAugments.ValueOnCurrent("aug_imprint_echo");
            if (echo > 0f)
            {
                int top = -1;
                for (int i = 0; i < s_halves.Length; i++)
                    if ((BuildFamily)i != BuildFamily.None && (top < 0 || s_halves[i] > s_halves[top])) top = i;
                if (top >= 0 && s_halves[top] > 0) s_halves[top] += UnityEngine.Mathf.RoundToInt(echo * 2f);
            }
        }
        for (int i = 0; i < s_counts.Length; i++) s_counts[i] = s_halves[i] / 2;

        // 단계가 오른 계열은 한 줄 알림 — 「이걸 모았더니 세졌다」가 그 순간 보여야 빌드가 된다.
        foreach (var f in BuildFamilyRules.All)
        {
            int i = (int)f, stage = BuildFamilyRules.StageOf(s_counts[i]);
            if (s_primed && stage > s_stages[i])
                ItemEffectVfxHelper.ShowBuildNotice(BuildNotice.Imprint(f, stage));   // 계열 칩 + 단계 눈금(83 UI · 10-14) — 글은 같다
            s_stages[i] = stage;
        }
        s_primed = run != null;
        Changed?.Invoke();
    }

    private static void Add(BuildFamily f)
    {
        if (f != BuildFamily.None) s_halves[(int)f] += 2;
    }

    /// <summary>새 런 · 런 끝 — 비운다.</summary>
    public static void Clear()
    {
        Array.Clear(s_counts, 0, s_counts.Length);
        Array.Clear(s_halves, 0, s_halves.Length);
        Array.Clear(s_stages, 0, s_stages.Length);
        s_primed = false;
        Changed?.Invoke();
    }

    /// <summary>이룬 단계의 전용 스탯을 누적기에 더한다 — ItemEffectManager.OnTick이 효과 기여 뒤에 부른다.</summary>
    public static void ContributeStats(ItemEffectContext ctx, ref ItemDynamicStats dyn)
    {
        foreach (var f in BuildFamilyRules.All)
            BuildFamilyRules.AddStageStats(f, s_stages[(int)f], ctx, ref dyn);
    }

    /// <summary>HUD 버프 줄 — 단계를 이룬 계열만 한 칸씩(「연격 ◆2」 + 보너스 글).</summary>
    public static void CollectBuffViews(List<BuffViewItem> into)
    {
        foreach (var f in BuildFamilyRules.All)
        {
            int stage = s_stages[(int)f];
            if (stage <= 0) continue;
            into.Add(new BuffViewItem(BuildFamilyRules.IconKey(f),
                $"{BuildFamilyRules.Label(f)} 각인 {stage}단계 · {BuildFamilyRules.StageBonusText(f, stage)}",
                stage, -1f, "", BuffSource.Item, isDebuff: false));
        }
    }

    /// <summary>
    /// 보유 각인이 가장 많은 계열 위 <paramref name="n"/>개(각인 1 이상만). 제시 가중이 쓴다.
    /// </summary>
    public static void TopFamilies(int n, List<BuildFamily> into)
    {
        into.Clear();
        for (int k = 0; k < n; k++)
        {
            BuildFamily best = BuildFamily.None; int bestCount = 0;
            foreach (var f in BuildFamilyRules.All)
            {
                if (into.Contains(f)) continue;
                int c = Count(f);
                if (c > bestCount) { best = f; bestCount = c; }
            }
            if (best == BuildFamily.None) break;
            into.Add(best);
        }
    }
}
