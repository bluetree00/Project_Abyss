using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// 스탯 상세 창(<see cref="StatDetailView"/>)의 데이터 — 11-05 사용자 「스탯 상세보기 팝업을 잘 보이게 · 지금 적용된 모든 룬 기능 · 수치를 보기 좋게」.
/// 왼쪽 = 능력치(최종 값 · 놓인 룬이 보탠 상시 몫) · 오른쪽 = 속성 시너지 · 발동 계열 각인 · 놓인 룬(각인 배율을 곱한 실제 값) · 지금 켜진 조건부 효과.
/// 읽기 전용 — 값을 바꾸지 않는다. 각인 배율은 효과를 지을 때와 같은 셈(<see cref="ItemEffectManager.Rebuild"/>의 계열 · 단계 · 배율).
/// </summary>
public sealed class StatDetailReport
{
    // ── Constants ─────────────────────────────────────────────
    private const string SubHex = "#9A98A0", DimHex = "#7E7A88", MetHex = "#6BE08A", NumHex = "#FFD98A";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ── Types ─────────────────────────────────────────────────
    public readonly struct StatRow
    {
        public readonly string Label, Value, Note;
        public StatRow(string label, string value, string note) { Label = label; Value = value; Note = note; }
    }

    public sealed class StatGroup
    {
        public readonly string Title;
        public readonly List<StatRow> Rows = new();
        public StatGroup(string title) => Title = Loc.T(title);
        public void Add(string label, string value, string note = null) => Rows.Add(new StatRow(Loc.T(label), value, note));
        public void AddIf(bool show, string label, string value, string note = null) { if (show) Add(label, value, note); }
    }

    /// <summary>오른쪽 한 칸 — 그림(없으면 비움) · 머리 한 줄 · 본문 여러 줄(서식 글 · 없으면 null).</summary>
    public sealed class Item
    {
        public Sprite Icon;
        public string Head;
        public string Body;
    }

    public sealed class Section
    {
        public readonly string Title, Empty;
        public string Count;
        public readonly List<Item> Items = new();
        public Section(string title, string empty) { Title = Loc.T(title); Empty = Loc.T(empty); }
    }

    // ── Private fields ────────────────────────────────────────
    private readonly List<StatGroup> _stats = new();
    private readonly List<Section> _sections = new();

    // ── Properties ────────────────────────────────────────────
    public IReadOnlyList<StatGroup> Stats => _stats;
    public IReadOnlyList<Section> Sections => _sections;

    // ── Public Methods ────────────────────────────────────────
    public static StatDetailReport Build(GameRunSession run)
    {
        var r = new StatDetailReport();
        if (run == null) return r;
        r.BuildStats(run);
        r._sections.Add(Synergies());
        r._sections.Add(Imprints());
        r._sections.Add(Runes(run));
        r._sections.Add(ActiveNow(run));
        return r;
    }

    // ── Private Methods: 왼쪽 능력치 ──────────────────────────
    private void BuildStats(GameRunSession run)
    {
        var s = run.Player != null ? run.Player.RuntimeStats : null;
        if (s == null) return;
        var acc = run.EffectManager != null ? run.EffectManager.GetAccumulatedStats() : default;
        var w = run.Player.WeaponManager != null ? run.Player.WeaponManager.CurrentWeaponData : null;

        var atk = Group("공격");
        int runeFlat = (s.MeleeAttack >= s.RangedAttack ? acc.MeleeDamage : acc.RangedDamage) + (int)acc.AllDamageFlat;
        atk.Add("공격력", s.AttackPower.ToString(Inv), Rune(Pct(acc.AllDamagePercent + acc.AllStatsPercent), Flat(runeFlat)));
        atk.Add("치명 확률", Num(CharacterInfoPanelView.CritChancePercent(s), "0.#") + "%", Rune(Pp(acc.CritChancePercent * 100f)));
        float critBase = w != null && w.critDamage > 0f ? w.critDamage : 1.25f;   // CombatCalculator.RollCrit과 같은 바탕
        atk.Add("치명 피해", "×" + Num(critBase + s.CritDamageBonus, "0.00"), Rune(Pct(acc.CritDamagePercent)));
        atk.Add("공격 속도", Num(s.AttackSpeedMultiplier * 100f, "0") + "%", Rune(Pct(acc.AttackSpeed)));
        atk.AddIf(!Zero(s.SkillDamageBonus), "스킬 피해", Pct(s.SkillDamageBonus), Rune(Pct(acc.SkillDamagePercent)));
        atk.AddIf(!Zero(s.AllElementBonus), "모든 속성 피해", Pct(s.AllElementBonus), Rune(Pct(acc.AllElementBonus)));

        var live = Group("생존");
        live.Add("체력", $"{s.Hp} / {s.MaxHp}", Rune(Flat(acc.MaxHP), Pct(acc.MaxHPPercent)));
        live.Add("방어력", s.Defense.ToString(Inv), Rune(Flat(acc.Defense), Pct(acc.DefensePercent + acc.AllStatsPercent)));
        live.AddIf(s.DamageReduction > 0f, "받는 피해 감소", Num(s.DamageReduction * 100f, "0.#") + "%", Rune(Pct(acc.DamageReduction)));
        live.AddIf(s.Shield > 0, "보호막", $"{s.Shield} / {s.ShieldCap}");
        live.AddIf(!Zero(s.HealingReceivedBonus), "받는 회복", Pct(s.HealingReceivedBonus), Rune(Pct(acc.HealingReceived)));
        live.AddIf(!Zero(s.DebuffResistance), "상태이상 저항", Pct(s.DebuffResistance), Rune(Pct(acc.DebuffResistance)));

        var move = Group("이동 · 기술");
        move.Add("이동 속도", Num(s.MoveSpeedMultiplier * 100f, "0") + "%", Rune(Pct(acc.MoveSpeed)));
        move.Add("스킬 쿨감", s.SkillCooldownReduction > 0f ? "−" + Num(s.SkillCooldownReduction * 100f, "0") + "%" : "—", Rune(Pct(acc.SkillCooldownReduction)));
        move.AddIf(!Zero(s.RollCooldownBonus), "구르기 재사용", Pct(s.RollCooldownBonus));   // 재사용 시간 ×(1+값) — LocoDodgeState
        move.AddIf(!Zero(s.RollDistanceBonus), "구르기 거리", Signed(s.RollDistanceBonus, "0.#") + " m");
        move.AddIf(!Zero(s.RangedRangeBonus), "원거리 사거리", Signed(s.RangedRangeBonus, "0.#") + " m");
        move.AddIf(s.BonusProjectile > 0, "추가 투사체", "+" + s.BonusProjectile, Rune(Flat(acc.ProjectileCountBonus)));
        move.AddIf(s.ProjectilePierceBonus > 0, "관통", "+" + s.ProjectilePierceBonus, Rune(Flat(acc.ProjectilePierceBonus)));

        var etc = Group("그 밖");
        etc.AddIf(!Zero(acc.GoldGainRate), "골드 획득", Pct(acc.GoldGainRate), Rune(Pct(acc.GoldGainRate)));
        etc.AddIf(!Zero(s.SpecialRoomChance), "특수방 확률", Pct(s.SpecialRoomChance));
        etc.AddIf(!Zero(s.HighGradeItemChance), "고등급 룬 확률", Pct(s.HighGradeItemChance));
        etc.AddIf(s.ConsumableSlotBonus != 0, "소모품 칸", "+" + s.ConsumableSlotBonus);

        _stats.RemoveAll(g => g.Rows.Count == 0);
    }

    private StatGroup Group(string title)
    {
        var g = new StatGroup(title);
        _stats.Add(g);
        return g;
    }

    // ── Private Methods: 오른쪽 룬 효과 ───────────────────────
    /// <summary>속성 시너지 — 채운 존마다 켜진 단계(◆)와 다음 단계(◇ · 흐리게). 존 칸 수 = 룬판 속성 시너지 줄과 같은 값.</summary>
    private static Section Synergies()
    {
        var sec = new Section("속성 시너지", "켜진 속성 시너지가 없다 — 같은 속성 룬을 그 속성 구역에 모으면 단계가 켜진다");
        var counts = MerlinRuneBridge.Instance != null ? MerlinRuneBridge.Instance.GetZoneOccupiedCounts() : null;
        if (counts == null || Managers.RuneData == null) return sec;
        foreach (var zone in ElementDef.Order)
        {
            if (!counts.TryGetValue(zone, out int count) || count <= 0) continue;
            var entries = Managers.RuneData.GetZoneSynergies(zone);
            if (entries == null || entries.Count == 0) continue;
            var sorted = new List<RuneSynergyEntry>(entries);
            sorted.Sort((a, b) => a.threshold.CompareTo(b.threshold));

            int met = 0;
            var body = new System.Text.StringBuilder();
            foreach (var e in sorted)
            {
                string desc = string.IsNullOrEmpty(e.description) ? e.effect_type : Loc.T(e.description);
                if (count >= e.threshold) { met++; Line(body, $"<color={MetHex}>◆</color> {desc}"); continue; }
                Line(body, $"<color={DimHex}>◇ {Loc.F("{0}칸", e.threshold)} — {desc}</color>");   // 다음 단계 하나만
                break;
            }
            var el = ElementDef.GetById(zone);
            string name = el != null ? $"<color=#{ColorUtility.ToHtmlStringRGB(el.Color)}>{Loc.T(el.Name)}</color>" : zone;
            sec.Items.Add(new Item
            {
                Icon = RuneArt.GetBlockTile(zone),
                Head = $"{name}   {Loc.F("{0}칸", count)}   <size=80%><color={SubHex}>{(met > 0 ? Loc.F("{0}단계", met) : Loc.T("단계 없음"))}</color></size>",
                Body = body.ToString(),
            });
        }
        sec.Count = sec.Items.Count > 0 ? sec.Items.Count.ToString(Inv) : null;
        return sec;
    }

    /// <summary>발동 계열 각인 — 같은 계열을 몇 개 쥐었나(판 룬 · 서약 원인 · 무기) · 단계 보너스 · 다음 단계까지.</summary>
    private static Section Imprints()
    {
        var sec = new Section("발동 계열 각인", "모은 발동 계열이 없다 — 같은 때 터지는 룬을 2 · 4 · 6개 모으면 그 계열 룬 효과가 커진다");
        foreach (var f in BuildFamilyRules.All)
        {
            int halves = BuildImprint.Halves(f);
            if (halves <= 0) continue;
            int count = BuildImprint.Count(f), stage = BuildImprint.Stage(f), next = BuildFamilyRules.NextThreshold(count);
            var marks = new System.Text.StringBuilder();
            for (int i = 0; i < BuildFamilyRules.Thresholds.Length; i++) marks.Append(i < stage ? "◆" : "◇");
            string body = stage > 0 ? BuildFamilyRules.StageBonusText(f, stage) : $"<color={DimHex}>{Loc.T("아직 단계 없음")}</color>";
            if (next > 0) body += $"\n<color={DimHex}>{Loc.F("다음 단계까지 {0} · {1}", next - count, Loc.T(BuildFamilyRules.When(f)))}</color>";
            sec.Items.Add(new Item
            {
                Head = $"<color={BuildFamilyRules.Hex(f)}>{Loc.T(BuildFamilyRules.Label(f))}  {marks}</color>   {Loc.F("각인 {0}", Num(halves * 0.5f, "0.#"))}",
                Body = body,
            });
        }
        sec.Count = sec.Items.Count > 0 ? sec.Items.Count.ToString(Inv) : null;
        return sec;
    }

    /// <summary>놓인 룬 — 같은 룬은 한 장으로 묶어 ×N · 등급 높은 순 → 속성 → 이름. 효과 줄마다 각인 배율이 붙으면 실제 값을 뒤에 적는다.</summary>
    private static Section Runes(GameRunSession run)
    {
        var sec = new Section("놓인 룬", "룬판에 놓인 룬이 없다 — 보관함의 룬을 판에 놓으면 여기 효과가 쌓인다");
        var placed = run.ItemInventory != null ? run.ItemInventory.PlacedItems : null;
        if (placed == null) return sec;

        var groups = new List<(RuntimeItemData d, int n)>();
        foreach (var it in placed)
        {
            if (it == null) continue;
            int at = groups.FindIndex(g => g.d.itemId == it.itemId);
            if (at >= 0) groups[at] = (groups[at].d, groups[at].n + 1);
            else groups.Add((it, 1));
        }
        groups.Sort((a, b) =>
        {
            int c = ((int)b.d.rarity).CompareTo((int)a.d.rarity);
            if (c != 0) return c;
            c = ElementIndex(a.d.element).CompareTo(ElementIndex(b.d.element));
            return c != 0 ? c : string.CompareOrdinal(a.d.displayName ?? "", b.d.displayName ?? "");
        });

        foreach (var (d, n) in groups)
        {
            var rc = d.rarity == ItemRarity.Common ? UICodeOverlay.Ink : RewardPresentation.FrameColor(d.rarity);
            var el = ElementDef.GetById(d.element);
            string sub = Loc.T(RewardPresentation.RarityLabel(d.rarity));
            if (el != null) sub += $"  ·  <color=#{ColorUtility.ToHtmlStringRGB(el.Color)}>●</color> {Loc.T(el.Name)}";
            var body = new System.Text.StringBuilder();
            if (d.effects != null)
                foreach (var slot in d.effects)
                {
                    string line = SlotLine(slot);
                    if (!string.IsNullOrEmpty(line)) Line(body, line);
                }
            sec.Items.Add(new Item
            {
                Icon = RuneArt.ResolveRuneIcon(d),
                Head = $"<color=#{ColorUtility.ToHtmlStringRGB(rc)}>{Loc.T(d.displayName)}</color>{(n > 1 ? $" <color={NumHex}>×{n}</color>" : "")}   <size=80%><color={SubHex}>{sub}</color></size>",
                Body = body.Length > 0 ? body.ToString() : null,
            });
        }
        sec.Count = placed.Count.ToString(Inv);
        return sec;
    }

    /// <summary>효과 한 줄 — 차트 원문(없으면 「조건 이름 값」) · 각인으로 커졌으면 「연격 각인 ×1.2 → 실제 값」.</summary>
    private static string SlotLine(ItemEffectSlot slot)
    {
        if (slot == null || string.IsNullOrEmpty(slot.effectType)) return null;
        var e = EffectDescriptionFormatter.Describe(slot);
        string line = !string.IsNullOrEmpty(e.Description) ? e.Description
                    : $"{(string.IsNullOrEmpty(e.TriggerText) ? "" : e.TriggerText + " ")}{e.Label} {e.ValueText}".Trim();
        if (string.IsNullOrEmpty(line)) return null;

        var fam = BuildFamilyRules.FromSlot(slot.effectType, slot.trigger);
        int stage = BuildFamilyRules.IsScalable(slot.effectType) ? BuildImprint.Stage(fam) : 0;
        if (stage > 0)
        {
            float mult = BuildImprint.EffectMultiplier(fam);
            string now = string.IsNullOrEmpty(e.ValueText) ? "" : " → " + EffectDescriptionFormatter.FormatValue(e.Unit, slot.value * mult);
            line += $"   <color={BuildFamilyRules.Hex(fam)}>{Loc.F("{0} 각인 ×{1}", Loc.T(BuildFamilyRules.Label(fam)), Num(mult, "0.0#"))}{now}</color>";
        }
        return "· " + line;
    }

    /// <summary>지금 조건이 충족돼 켜진 효과(조건부 · 쌓이는 것) — HUD 버프 줄과 같은 출처(<see cref="ItemEffectManager.CollectActiveBuffViews"/>) · 상시 스탯 룬은 뺀다(「놓인 룬」에 있다).</summary>
    private static Section ActiveNow(GameRunSession run)
    {
        var sec = new Section("지금 켜진 조건부 효과", "지금 조건이 충족된 효과가 없다 — 조건(체력 · 연타 · 처치 …)이 맞으면 여기 뜬다");
        if (run.EffectManager == null) return sec;
        var views = new List<BuffViewItem>();
        run.EffectManager.CollectActiveBuffViews(views, skipPassive: true);
        foreach (var v in views)
            sec.Items.Add(new Item { Head = v.Stacks > 1 ? $"{v.DisplayText}  <color={NumHex}>×{v.Stacks}</color>" : v.DisplayText });
        sec.Count = sec.Items.Count > 0 ? sec.Items.Count.ToString(Inv) : null;
        return sec;
    }

    // ── Private Methods: 글 조각 ──────────────────────────────
    private static void Line(System.Text.StringBuilder sb, string s)
    {
        if (sb.Length > 0) sb.Append('\n');
        sb.Append(s);
    }

    /// <summary>「룬 +8% · +5」 — 빈 조각은 뺀다 · 다 비면 null(줄에 아무것도 안 붙는다).</summary>
    private static string Rune(params string[] parts)
    {
        string joined = null;
        foreach (var p in parts)
            if (!string.IsNullOrEmpty(p)) joined = joined == null ? p : joined + " · " + p;
        return joined == null ? null : Loc.F("룬 {0}", joined);
    }

    private static bool Zero(float v) => Mathf.Abs(v) < 0.0001f;
    private static string Num(float v, string fmt) => v.ToString(fmt, Inv);
    private static string Signed(float v, string fmt) => (v >= 0f ? "+" : "−") + Mathf.Abs(v).ToString(fmt, Inv);
    private static string Pct(float ratio) => Zero(ratio) ? null : Signed(ratio * 100f, "0.#") + "%";
    private static string Pp(float points) => Zero(points) ? null : Signed(points, "0.#") + "%p";
    private static string Flat(int v) => v == 0 ? null : (v > 0 ? "+" : "−") + Mathf.Abs(v);

    private static int ElementIndex(string id)
    {
        var order = ElementDef.Order;
        for (int i = 0; i < order.Count; i++) if (order[i] == id) return i;
        return order.Count;
    }
}
