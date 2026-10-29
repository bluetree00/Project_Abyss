using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>증강 등급 — 화면마다 굴린다(사용자 10-15 「등급도 랜덤 — 고정이면 기대를 하기 힘들다」).</summary>
/// <para>전설(10-24 W7) — 화면마다 따로 낮은 확률로 굴려 카드 한 장으로 나온다 · 단계 없음 · 싸우는 방식을 바꾸는 규칙(<see cref="MeleeAugmentDraft.LegendChance"/>).</para>
public enum AugmentTier { Silver = 0, Gold = 1, Prismatic = 2, Legend = 3 }

/// <summary>
/// 증강이 붙는 근접 무기 갈래 — 공용(None) 외엔 그 갈래 무기를 들 때만 나오고 켜진다(사용자 10-15 「새로운 근접 무기에만 맞는 효과」).
/// 무형검은 무기 종류가 카타나라 무기 키로 가른다. 새 근접 무기를 더하면 여기 한 줄 + 그 갈래 증강만 더한다.
/// </summary>
public enum MeleeWeaponKind { None = 0, Nameless = 1, Katana = 2, Greatsword = 3 }

/// <summary>
/// 근접 강화 증강 하나의 정의(설계 기획/RelicFairy_근접강화증강_설계_20261015.md §3).
/// 단계 I · II · III 값을 들고, 효과 객체는 <see cref="Create"/>가 만든다.
/// 계열은 「나오는 확률 가중」에만 쓴다 — 세트 보너스는 만들지 않는다(롤 아레나 26.12 세트 증강 폐지 교훈).
/// </summary>
public sealed class MeleeAugmentDef
{
    public readonly string Id;
    public readonly string Name;
    public readonly AugmentTier Tier;
    public readonly BuildFamily Family;
    public readonly MeleeWeaponKind Weapon;
    public readonly float[] Values;
    public readonly float[] Values2;      // 둘째 값(추가 타격 수 · 반경 · 초) — 없으면 null
    public readonly string DescFormat;   // {0} = 지금 단계 값 · {1} = 둘째 값(표시용으로 이미 다듬은 숫자)
    public readonly string PhantomDescFormat;   // 진화 뒤 환영이 낼 때의 설명(무형검 전용만 · 없으면 null)
    public readonly Func<MeleeAugmentDef, int, MeleeAugmentEffect> Create;
    /// <summary>재련 자람(10-26 무기 개편 D9) — 진화 뒤 재련 1회마다 값 · 둘째 값에 더한다. 둘 다 0이면 단계(I~III)로 자라는 보통 증강.</summary>
    public readonly float Growth, Growth2;

    public const int MaxRank = 3;

    public MeleeAugmentDef(string id, string name, AugmentTier tier, BuildFamily family, MeleeWeaponKind weapon,
                           float[] values, string descFormat, Func<MeleeAugmentDef, int, MeleeAugmentEffect> create, float[] values2 = null,
                           string phantomDesc = null, float growth = 0f, float growth2 = 0f)
    {
        Id = id; Name = name; Tier = tier; Family = family; Weapon = weapon;
        Values = values; DescFormat = descFormat; Create = create; Values2 = values2; PhantomDescFormat = phantomDesc;
        Growth = growth; Growth2 = growth2;
    }

    /// <summary>단계 대신 재련으로 자라는 증강인가(강화 카드가 안 나온다 · 카드에 「재련 n회」).</summary>
    public bool IsGrowth => Growth != 0f || Growth2 != 0f;

    /// <summary>이 무기 재련에 맞춘 값 — 자람 증강이면 첫 값 + 자람 × 진화 뒤 재련 횟수, 아니면 그 무기의 단계 값.</summary>
    public float ValueFor(WeaponData w)
        => IsGrowth ? ValueAt(1) + Growth * MeleeAugmentCatalog.ReforgesSinceEvolution(w) : ValueAt(Mathf.Max(1, w != null ? w.AugmentRank(Id) : 1));

    /// <summary>이 무기 재련에 맞춘 둘째 값.</summary>
    public float Value2For(WeaponData w)
        => IsGrowth ? Value2At(1) + Growth2 * MeleeAugmentCatalog.ReforgesSinceEvolution(w) : Value2At(Mathf.Max(1, w != null ? w.AugmentRank(Id) : 1));

    /// <summary>이 무기 재련에 맞춘 카드 · 무기 정보 글(자람 증강은 지금 재련 값으로).</summary>
    public string TextFor(WeaponData w) => string.Format(DescFormat, Shown(ValueFor(w)), Shown(Value2For(w)));

    /// <summary>단계(1~3)의 값.</summary>
    public float ValueAt(int rank) => Values[Mathf.Clamp(rank, 1, MaxRank) - 1];

    /// <summary>단계(1~3)의 둘째 값(없으면 0).</summary>
    public float Value2At(int rank) => Values2 != null ? Values2[Mathf.Clamp(rank, 1, MaxRank) - 1] : 0f;

    /// <summary>카드 · 무기 정보 글 — 「치명 확률 +8%p」처럼 지금 단계 값을 넣는다.</summary>
    public string Text(int rank) => string.Format(DescFormat, Shown(ValueAt(rank)), Shown(Value2At(rank)));

    /// <summary>진화 뒤 환영이 낼 때의 글(무형검 증강 · ⑧-기획 A) — 없으면 <see cref="Text"/>.</summary>
    public string PhantomText(int rank) => string.IsNullOrEmpty(PhantomDescFormat) ? Text(rank) : string.Format(PhantomDescFormat, Shown(ValueAt(rank)), Shown(Value2At(rank)));

    private static string Shown(float v) => Mathf.Approximately(v, Mathf.Round(v)) ? v.ToString("0") : v.ToString("0.##");

    public static string RankName(int rank) => rank switch { 1 => "I", 2 => "II", 3 => "III", _ => "" };
    public static string TierName(AugmentTier t) => t switch { AugmentTier.Gold => "금", AugmentTier.Prismatic => "프리즘", AugmentTier.Legend => "전설", _ => "은" };

    /// <summary>전설은 단계가 없다(처음부터 완성 · D4) — 카드 · 정보 글에 「I」을 붙이지 않는다.</summary>
    public bool IsLegend => Tier == AugmentTier.Legend;
}

/// <summary>근접 강화 증강 목록 — 공용 + 무기 갈래 전용. 수치는 시작값(실측으로 맞춘다).</summary>
public static class MeleeAugmentCatalog
{
    // 다른 코드(스킬 · 형 · 진화)가 읽는 id
    public const string HeavyHand = "aug_heavy_hand", SwordWave = "aug_sword_wave", TwinWave = "aug_twin_wave",
                        KatanaDraw = "aug_katana_draw", KatanaPhantom = "aug_katana_phantom",
                        KatanaShadow = "aug_katana_shadow", KatanaGust = "aug_katana_gust",
                        GreatSplit = "aug_great_split", GreatPerimeter = "aug_great_perimeter", GreatFinal = "aug_great_final", GreatReach = "aug_great_reach",
                        GreatTip = "aug_great_tip", GreatSweep = "aug_great_sweep",
                        NamelessSheath = "aug_nameless_sheath",
                        LegendStorm = "aug_legend_storm", LegendStoppedTime = "aug_legend_stopped_time", LegendEndlessEdge = "aug_legend_endless_edge",
                        LegendThousand = "aug_legend_thousand", LegendIssen = "aug_legend_issen", LegendRift = "aug_legend_rift";

    /// <summary>
    /// 걷은 증강 — 정의는 없고 id만 남긴다: 옛 세이브 정리(<see cref="WeaponData.SanitizeAugments"/>) · 옛 실측 도구.
    /// 「추가 검기」 10종(10-26 무기 개편 D1 · D2) + 레거시 정리 3종(10-26 사용자 「레거시 내용들은 겹치면 정리」 — 지진 · 거인의 걸음 · 칼자국).
    /// </summary>
    public static readonly HashSet<string> RemovedIds = new()
    {
        SwordWave, TwinWave, "aug_spell_blade", "aug_dodge_riposte", "aug_ricochet", "aug_katana_third",
        "aug_nameless_formless", "aug_nameless_afterimage", LegendStorm, LegendThousand,
        "aug_great_quake", "aug_great_giant", "aug_blade_scar",
    };

    /// <summary>
    /// 레거시 정리로 걷은 증강이 옛 세이브에 있으면 바꿔 줄 등급 · 무기(10-26 사용자 「옛 세이브는 같은 등급 · 같은 무기의 없는 증강 I」).
    /// 지진(대검 금) — 대지 가르기와 같은 「막타 뒤 땅」 · 3 m는 커진 평타 범위 안 / 거인의 걸음(대검 프리즘) — 휘두름마다 발밑 진동(평타 범위 안 · 휘두름 바닥 그림) ·
    /// 대검 프리즘 전용이 없어져 금으로 / 칼자국(공용 은) — 휘두름마다 바닥 장판(대검 휘두름 장판을 걷은 이유와 같다).
    /// </summary>
    private static readonly Dictionary<string, (AugmentTier tier, MeleeWeaponKind weapon)> s_removedReplace = new()
    {
        ["aug_great_quake"] = (AugmentTier.Gold, MeleeWeaponKind.Greatsword),
        ["aug_great_giant"] = (AugmentTier.Gold, MeleeWeaponKind.Greatsword),
        ["aug_blade_scar"]  = (AugmentTier.Silver, MeleeWeaponKind.None),
    };
    // 바꿔 줄 때 먼저 보는 새 증강(개편의 대표 — 정리안 「지진 → 휩쓸기 · 없으면 속성 검 연장」)
    private static readonly string[] s_replacePrefer = { GreatSweep, GreatFinal };

    private static readonly List<MeleeAugmentDef> s_all = new()
    {
        // ── 공용 · 은 ── 모두 「규칙」 — 스탯 한 줄짜리는 두지 않는다(사용자 10-15 「의미 있는 증강 — 그냥 스탯 증가만이 아닌」)
        new(HeavyHand,         "묵직한 손",   AugmentTier.Silver, BuildFamily.Crit,     MeleeWeaponKind.None, new[] { 4f, 3f, 2f },    "평타 {0}번째 휘두름마다 강타",
            (d, r) => new HeavySwingAugment(d, r)),
        new("aug_ambush",      "기습",        AugmentTier.Silver, BuildFamily.Mobility, MeleeWeaponKind.None, new[] { 0.5f, 0.75f, 1f }, "평타를 2초 쉬었다 친 첫 타는 반드시 치명 · 일반 몹 {0}초 경직",
            (d, r) => new AmbushAugment(d, r)),
        new("aug_hunter_breath","사냥꾼의 숨", AugmentTier.Silver, BuildFamily.Kill,     MeleeWeaponKind.None, new[] { 15f, 25f, 35f },  "처치하면 둘레 3 m 적이 「사냥감」 — 2초 동안 받는 피해 +{0}%",
            (d, r) => new HuntMarkAugment(d, r)),
        new("aug_last_will",   "마지막 의지", AugmentTier.Silver, BuildFamily.Danger,   MeleeWeaponKind.None, new[] { 1f, 1.5f, 2f },   "체력이 40% 아래로 떨어지면 1.5초 무적 · 둘레 3 m 적 {0}초 경직(방마다 한 번)",
            (d, r) => new LastWillAugment(d, r)),

        // ── 공용 · 금 ──
        new("aug_impact",      "충격파",      AugmentTier.Gold,   BuildFamily.Crit,     MeleeWeaponKind.None, new[] { 40f, 60f, 80f },  "강타 · 치명 적중 때 반경 {1} m 충격(공격력 {0}% · 휘두름당 1번)",
            (d, r) => new ImpactAugment(d, r), new[] { 2f, 2.5f, 3f }),
        new("aug_combo_mark",  "연격 표식",   AugmentTier.Gold,   BuildFamily.Combo,    MeleeWeaponKind.None, new[] { 80f, 120f, 160f }, "같은 적을 5번 이어 치면 표식이 터진다(공격력 {0}%)",
            (d, r) => new ComboMarkAugment(d, r)),
        new("aug_executioner", "처형인",      AugmentTier.Gold,   BuildFamily.Kill,     MeleeWeaponKind.None, new[] { 15f, 20f, 25f },  "체력 {0}% 아래 일반 몹은 한 번에 처형(정예 · 보스 제외)",
            (d, r) => new ExecutionerAugment(d, r)),
        new("aug_mark_spread", "표식 퍼뜨리기", AugmentTier.Gold, BuildFamily.None,     MeleeWeaponKind.None, new[] { 1f, 2f, 3f },    "콤보 마지막 타로 유물 표식이 있는 적을 치면 그 표식을 둘레 3 m 적 {0}명에게 옮긴다",
            (d, r) => new MarkSpreadAugment(d, r)),
        new("aug_element_edge","원소의 날",   AugmentTier.Gold,   BuildFamily.None,     MeleeWeaponKind.None, new[] { 50f, 75f, 100f },  "콤보 마지막 타가 룬 판에 가장 많은 속성으로 터진다(둘레 2 m · 공격력 {0}% · 그 속성 상태)",
            (d, r) => new ElementEdgeAugment(d, r)),
        new("aug_spin_slash",  "회전 베기",   AugmentTier.Gold,   BuildFamily.Mobility, MeleeWeaponKind.None, new[] { 50f, 80f, 110f }, "회피가 끝날 때 둘레 2.5 m 베기(공격력 {0}%)",
            (d, r) => new SpinSlashAugment(d, r)),
        new("aug_counter",     "피격 반격",   AugmentTier.Gold,   BuildFamily.Danger,   MeleeWeaponKind.None, new[] { 40f, 70f, 100f }, "맞은 뒤 2초 안 첫 적중 때 둘레 2.5 m 반격 베기(공격력 {0}%)",
            (d, r) => new CounterAugment(d, r)),

        // ── 공용 · 프리즘 ──
        new("aug_thunder",     "천둥 같은 일격", AugmentTier.Prismatic, BuildFamily.Crit, MeleeWeaponKind.None, new[] { 50f, 75f, 100f }, "강타 적중 때 대상 뒤 4 m까지 관통 충격(공격력 {0}% · 휘두름당 1번)",
            (d, r) => new ThunderAugment(d, r)),
        new("aug_slaughter_chain","학살의 연쇄", AugmentTier.Prismatic, BuildFamily.Kill, MeleeWeaponKind.None, new[] { 50f, 75f, 100f }, "처치한 자리에서 회오리 베기가 일어 둘레 2.5 m를 벤다(공격력 {0}% · 회오리 베기의 처치도 다시 일으킨다)",
            (d, r) => new SlaughterChainAugment(d, r)),
        new("aug_imprint_echo","각인 공명",   AugmentTier.Prismatic, BuildFamily.None,  MeleeWeaponKind.None, new[] { 1f, 1.5f, 2f },  "가장 높은 발동 계열 각인 +{0}",
            (d, r) => new MarkerAugment(d, r)),   // BuildImprint.Recount가 읽는다

        // ── 카타나 전용(빠른 연타 · 돌진 · 칼바람) ──
        new(KatanaDraw,        "발도 연속",   AugmentTier.Gold,   BuildFamily.Mobility, MeleeWeaponKind.Katana, new[] { 25f, 45f, 65f }, "성광베기 돌진 거리 +{0}% · 추가 타격 +{1}",
            (d, r) => new MarkerAugment(d, r), new[] { 1f, 1f, 2f }),   // 성광베기가 읽는다
        new("aug_katana_flow", "물 흐르듯",   AugmentTier.Gold,   BuildFamily.Mobility, MeleeWeaponKind.Katana, new[] { 5f, 4f, 3f },    "회피 뒤 1초 안 첫 적중이 회피 1칸을 돌려준다({0}초에 한 번)",
            (d, r) => new FlowAugment(d, r)),
        new(KatanaPhantom,     "환영 베기 강화", AugmentTier.Gold, BuildFamily.Spell,   MeleeWeaponKind.Katana, new[] { 20f, 35f, 50f }, "환영베기 피해 +{0}% · 이어 갈라짐 +{1}",
            (d, r) => new MarkerAugment(d, r), new[] { 1f, 1f, 2f }),   // 환영베기 · 스킬 피해 계산이 읽는다
        new("aug_katana_gale", "바람 가르기", AugmentTier.Prismatic, BuildFamily.Combo, MeleeWeaponKind.Katana, new[] { 35f, 70f, 100f }, "칼바람이 휘두름마다 {0}% 확률로 1칸 더 쌓인다",
            (d, r) => new GaleAugment(d, r)),

        // 10-26 무기 개편 — 재련할수록 자란다(단계 없음 · D9 · 「재련 n회」)
        new(KatanaShadow,      "그림자 파트너", AugmentTier.Gold, BuildFamily.Combo,    MeleeWeaponKind.Katana, new[] { 12f, 12f, 12f }, "곁의 그림자가 평타를 한 박자 늦게 따라 벤다 — 그림자마다 그 타격의 {0}% 피해(스킬도 따라 쓴다 · 절반) · 재련 2 · 4회째에 그림자가 하나씩 는다",
            (d, r) => new ShadowPartnerAugment(d, r), growth: 2f),   // 설계 30 + 6n → 실측 98로 15 + 4n → 10-26 분신 수가 재련으로 는다(1 → 3)라 분신당 12 + 2n
        new(KatanaGust,        "검풍",        AugmentTier.Silver, BuildFamily.Combo,    MeleeWeaponKind.Katana, new[] { 15f, 15f, 15f }, "적을 맞힌 평타 · 스킬마다 {0}% 확률로 앞 6 m를 꿰뚫는 검기(공격력 {1}%) · 재련할수록 확률 · 피해가 오른다",
            (d, r) => new SwordGustAugment(d, r), new[] { 60f, 60f, 60f }, growth: 4f, growth2: 5f),
        // ── 대검 전용(무게 · 버팀 · 땅) ──
        new("aug_great_bulwark","철벽 자세",  AugmentTier.Silver, BuildFamily.Danger,   MeleeWeaponKind.Greatsword, new[] { 10f, 18f, 25f }, "평타 동작 중 맞아도 경직되지 않고 받는 피해 −{0}%",
            (d, r) => new BulwarkAugment(d, r)),
        new(GreatReach,        "거대한 칼날", AugmentTier.Silver, BuildFamily.Crit,     MeleeWeaponKind.Greatsword, new[] { 15f, 25f, 35f }, "평타 · E · R 공격 범위 +{0}% — 칼 그림 · 속성 자국도 같이 커진다",
            (d, r) => new MarkerAugment(d, r)),   // GreatswordReach가 읽는다(10-26 무기 개편)
        new(GreatSplit,        "땅 가르기",   AugmentTier.Gold,   BuildFamily.Crit,     MeleeWeaponKind.Greatsword, new[] { 25f, 45f, 65f }, "무게 형 내려찍기 범위 +{0}% · 균열 변주면 균열 +{1}초",
            (d, r) => new MarkerAugment(d, r), new[] { 1f, 2f, 3f }),   // 무게 형이 읽는다
        new(GreatPerimeter,    "주변경계 연장", AugmentTier.Gold, BuildFamily.Spell,    MeleeWeaponKind.Greatsword, new[] { 1f, 2f, 3f },    "주변경계 충격 고리 +{0}",
            (d, r) => new MarkerAugment(d, r)),   // 주변경계가 읽는다
        new(GreatFinal,        "속성 검 연장", AugmentTier.Gold, BuildFamily.Spell,   MeleeWeaponKind.Greatsword, new[] { 0.5f, 1f, 1.5f }, "속성 검(R) 지속 +{0}초 · 속성 장판 반경 +{1}%",
            (d, r) => new MarkerAugment(d, r), new[] { 10f, 15f, 20f }),   // 속성 검(R · GiantSwordBuff)이 읽는다(10-26 — 옛 「최후의 일격 충격」 · 「거인의 검 연장」)
        // 10-26 대검 R 개편 — 범위 규칙(재련할수록 자람 · 사용자 「범위 증가와 연관된 추가 재련 · r까지 키면 더 좋은 범위에 효율 · 수치가 아닌 기획」)
        new(GreatTip,          "칼끝 베기",   AugmentTier.Silver, BuildFamily.Crit,     MeleeWeaponKind.Greatsword, new[] { 25f, 25f, 25f }, "평타 칼끝(공격 범위 바깥쪽 35%)에 맞은 적에게 그 타격의 {0}% 피해를 더 준다 — 범위가 넓을수록 칼끝도 넓다 · 재련할수록 세진다",
            (d, r) => new TipStrikeAugment(d, r), growth: 5f),
        new(GreatSweep,        "휩쓸기",      AugmentTier.Gold,   BuildFamily.Spell,    MeleeWeaponKind.Greatsword, new[] { 0.2f, 0.2f, 0.2f }, "한 번 휘둘러 여럿을 맞히면 둘째 적부터 한 명마다 R 재사용 대기 −{0}초(휘두름마다 최대 1초) · 속성 검(R) 중이면 지속 +{1}초(한 번에 최대 3초) · 재련할수록 늘어난다",
            (d, r) => new SweepAugment(d, r), new[] { 0.15f, 0.15f, 0.15f }, growth: 0.05f, growth2: 0.03f),

        // ── 무형검 전용(형 없는 칼 · 진화 전) ──
        new("aug_nameless_mist","안개 칼날",  AugmentTier.Silver, BuildFamily.Spell,    MeleeWeaponKind.Nameless,   new[] { 15f, 25f, 35f }, "E가 맞힌 자리에 안개가 1.5초 남아 0.5초마다 벤다(공격력 {0}%) · 진화해도 그대로",
            (d, r) => new MistAugment(d, r), phantomDesc: "E가 맞힌 자리를 등 뒤 환영이 그어 안개를 남긴다 — 1.5초 동안 0.5초마다 벤다(공격력 {0}%)"),
        new(NamelessSheath,    "비어 있는 칼집", AugmentTier.Prismatic, BuildFamily.None, MeleeWeaponKind.Nameless, new[] { 1f, 2f, 3f },  "진화하면 새 무기 전용 증강 {0}개(I)로 바뀐다",
            (d, r) => null),   // 효과 없음 — 진화 때 ConvertForEvolution이 바꾼다

        // ── 전설(10-24 W7) — 단계 없음(세 칸 같은 값) · 공용 3 · 무기별 1 ──
        new(LegendStoppedTime, "멈춘 시간",   AugmentTier.Legend, BuildFamily.Mobility, MeleeWeaponKind.None, new[] { 100f, 100f, 100f }, "아슬아슬하게 피하면 둘레 8 m 적이 2초 멈춘다(보스 · 정예는 50% 느려짐) · 그동안 평타가 두 번씩 벤다",
            (d, r) => new StoppedTimeLegend(d, r)),
        new(LegendEndlessEdge, "끝없는 칼날", AugmentTier.Legend, BuildFamily.Crit,     MeleeWeaponKind.None, new[] { 15f, 15f, 15f },  "치명이 난 휘두름마다 치명 피해 +{0}%가 쌓인다(최대 10번 · 스킬 치명도) · 치명이 하나도 없는 휘두름은 쌓인 것의 절반을 날린다",
            (d, r) => new EndlessEdgeLegend(d, r)),
        new(LegendIssen,       "일섬",        AugmentTier.Legend, BuildFamily.Mobility, MeleeWeaponKind.Katana, new[] { 200f, 200f, 200f }, "E 돌진이 지나간 길을 0.4초 뒤 한 번 더 벤다(공격력 {0}%) · 그 길에서 처치하면 E 재사용 대기 초기화",
            (d, r) => new IssenLegend(d, r)),
        new(LegendRift,        "대지 가르기", AugmentTier.Legend, BuildFamily.Combo,    MeleeWeaponKind.Greatsword, new[] { 150f, 150f, 150f }, "콤보 마지막 타가 앞 8 m 균열을 남기고 0.6초 뒤 솟구친다(공격력 {0}% · 일반 몹 0.8초 띄움)",
            (d, r) => new RiftLegend(d, r)),
    };

    public static IReadOnlyList<MeleeAugmentDef> All => s_all;

    /// <summary>무형검이 진화하는 강화 단계(+9 · 10-18 재련 개편) — 자람 증강의 n은 여기부터 센다.</summary>
    public const int EvolvedAt = 9;

    /// <summary>진화 뒤 재련 횟수 n(0~4) = (강화 − 9) ÷ 3 — 재련 자람 증강의 값을 정한다(10-26 무기 개편).</summary>
    public static int ReforgesSinceEvolution(WeaponData w)
        => w == null ? 0 : Mathf.Clamp((w.enhanceLevel - EvolvedAt) / MeleeAugmentDraft.LevelStep, 0, 4);

    /// <summary>
    /// 재련 자람 증강의 지금 값 · 다음 재련 값(ca 카드 「재련 n회 · 지금 X → 다음 Y」). 자람 증강이 아니면 false.
    /// now2 · next2 = 둘째 값(검풍 검기 피해). 진화 뒤 재련이 4회(+21)면 next = now.
    /// </summary>
    public static bool TryGrowth(string id, int enhanceLevel, out float now, out float next, out float now2, out float next2)
    {
        now = next = now2 = next2 = 0f;
        var d = Get(id);
        if (d == null || !d.IsGrowth) return false;
        int n  = Mathf.Clamp((enhanceLevel - EvolvedAt) / MeleeAugmentDraft.LevelStep, 0, 4);
        int n1 = Mathf.Min(4, n + 1);
        now  = d.ValueAt(1) + d.Growth * n;   next  = d.ValueAt(1) + d.Growth * n1;
        now2 = d.Value2At(1) + d.Growth2 * n; next2 = d.Value2At(1) + d.Growth2 * n1;
        return true;
    }

    public static MeleeAugmentDef Get(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        for (int i = 0; i < s_all.Count; i++) if (s_all[i].Id == id) return s_all[i];
        return null;
    }

    /// <summary>무기의 갈래 — 무형검은 무기 종류가 카타나라 키로 먼저 본다. 근접이 아니면 None.</summary>
    public static MeleeWeaponKind KindOf(WeaponData w)
    {
        if (w == null) return MeleeWeaponKind.None;
        if (!string.IsNullOrEmpty(w.weaponSOKey) && w.weaponSOKey.Contains("Nameless")) return MeleeWeaponKind.Nameless;
        return w.weaponType switch
        {
            WeaponType.Katana     => MeleeWeaponKind.Katana,
            WeaponType.Greatsword => MeleeWeaponKind.Greatsword,
            _                     => MeleeWeaponKind.None,
        };
    }

    /// <summary>
    /// 진화 뒤 무형검 증강을 등 뒤 환영이 대신 내나(⑧-기획 A · 10-18) — 무형검 전용(칼집 제외)이 진화한 갈래(카타나 · 대검) 무기에 남았을 때.
    /// 환영 몫은 증강 고르기 · 단계 올리기에 나오지 않는다(<see cref="FitsWeapon"/>은 그대로 false · D4).
    /// </summary>
    public static bool RunsAsPhantom(MeleeAugmentDef d, WeaponData w)
    {
        if (d == null || w == null || d.Weapon != MeleeWeaponKind.Nameless || d.Id == NamelessSheath || w.evolutionStage < 1) return false;
        var kind = KindOf(w);
        return kind == MeleeWeaponKind.Katana || kind == MeleeWeaponKind.Greatsword;
    }

    /// <summary>이 무기에 환영 몫 증강이 하나라도 있나(등 뒤 환영 · 화면 「무형검 환영」 칸).</summary>
    public static bool HasPhantom(WeaponData w)
    {
        if (w == null || string.IsNullOrEmpty(w.augments)) return false;
        foreach (var (id, _) in w.AugmentList())
            if (RunsAsPhantom(Get(id), w)) return true;
        return false;
    }

    /// <summary>이 증강이 이 무기에 나올 수 있나(공용 = 근접 무기 전부 · 전용 = 그 갈래만).</summary>
    public static bool FitsWeapon(MeleeAugmentDef d, WeaponData w)
    {
        var kind = KindOf(w);
        if (kind == MeleeWeaponKind.None) return false;
        return d.Weapon == MeleeWeaponKind.None || d.Weapon == kind;
    }

    /// <summary>
    /// 진화 때 증강을 옮긴다 — 무형검 전용은 그대로 남아 진화한 무기의 등 뒤 환영이 낸다(<see cref="RunsAsPhantom"/> · ⑧-기획 A · 10-18).
    /// 비어 있는 칼집은 새 갈래 전용 증강 N개(I)가 된다(그 무기 전용 중 없는 것을 무작위로 · 전설 제외 · 10-26 사용자 — 옛 「높은 등급 · 목록 첫 것」은 늘 옛 증강만 줬다). 공용 · 이미 새 갈래 것은 그대로.
    /// (10-15 ~ 10-18: 무형검 전용을 목록 맨 앞 같은 등급 새 증강으로 말없이 바꿨다 — 고른 것이 버려진 것처럼 읽혔다)
    /// </summary>
    public static string ConvertForEvolution(WeaponData from, WeaponData to)
    {
        if (from == null || string.IsNullOrEmpty(from.augments)) return from?.augments ?? "";
        var kind = KindOf(to);
        if (kind == MeleeWeaponKind.None) return from.augments;

        var kept = new List<(string id, int rank)>();
        var convert = new List<(AugmentTier tier, int rank)>();
        int sheathGrants = 0;
        foreach (var (id, rank) in from.AugmentList())
        {
            var d = Get(id);
            if (d == null || d.Weapon == MeleeWeaponKind.None || d.Weapon == kind) { kept.Add((id, rank)); continue; }
            if (d.Id == NamelessSheath) sheathGrants += Mathf.RoundToInt(d.ValueAt(rank));
            else if (d.Weapon == MeleeWeaponKind.Nameless) kept.Add((id, rank));   // 환영 이식 — 등 뒤 무형검 환영이 낸다
            else convert.Add((d.Tier, rank));
        }
        foreach (var (tier, rank) in convert)
        {
            var pick = FirstExclusive(kind, kept, tier) ?? FirstExclusive(kind, kept, null);
            if (pick != null) kept.Add((pick.Id, rank));
        }
        for (int i = 0; i < sheathGrants; i++)
        {
            var pick = RandomExclusive(kind, kept, to);
            if (pick != null) kept.Add((pick.Id, 1));
        }

        var sb = new StringBuilder();
        foreach (var (id, rank) in kept)
        {
            if (sb.Length > 0) sb.Append(',');
            sb.Append(id).Append(':').Append(rank);
        }
        return sb.ToString();
    }

    /// <summary>그 무기 전용 중 아직 없는 것 하나를 무작위로(전설 제외 · 지금 이 무기에 의미 있는 것만 — 형 없는 칼바람 강화 등은 뺀다).</summary>
    private static MeleeAugmentDef RandomExclusive(MeleeWeaponKind kind, List<(string id, int rank)> owned, WeaponData to)
    {
        var pool = new List<MeleeAugmentDef>();
        foreach (var d in s_all)
        {
            if (d.Weapon != kind || d.IsLegend || Owns(owned, d.Id) || !MeleeAugmentDraft.IsMeaningful(d, to)) continue;
            pool.Add(d);
        }
        return pool.Count > 0 ? pool[UnityEngine.Random.Range(0, pool.Count)] : null;
    }

    private static bool Owns(List<(string id, int rank)> owned, string id)
    {
        foreach (var (o, _) in owned) if (o == id) return true;
        return false;
    }

    /// <summary>
    /// 레거시 정리로 걷은 증강(<paramref name="removedId"/>)을 대신할 증강 — 같은 등급 · 같은 무기(공용이면 공용) · 아직 없는 것 · 이 무기에 의미 있는 것.
    /// 개편의 대표(휩쓸기 → 속성 검 연장)를 먼저 보고, 그 밖은 목록 순서. 바꿔 줄 몫이 아니거나 남은 게 없으면 null.
    /// </summary>
    public static string ReplacementFor(string removedId, WeaponData w, ICollection<string> owned)
    {
        if (w == null || !s_removedReplace.TryGetValue(removedId, out var want)) return null;
        bool Ok(MeleeAugmentDef d) => d != null && d.Tier == want.tier && d.Weapon == want.weapon && !d.IsLegend
                                      && !owned.Contains(d.Id) && FitsWeapon(d, w) && MeleeAugmentDraft.IsMeaningful(d, w);
        foreach (var id in s_replacePrefer) { var d = Get(id); if (Ok(d)) return d.Id; }
        foreach (var d in s_all) if (Ok(d)) return d.Id;
        return null;
    }

    private static MeleeAugmentDef FirstExclusive(MeleeWeaponKind kind, List<(string id, int rank)> owned, AugmentTier? tier)
    {
        foreach (var d in s_all)
        {
            if (d.Weapon != kind || (tier.HasValue && d.Tier != tier.Value)) continue;
            bool has = false;
            foreach (var (id, _) in owned) if (id == d.Id) { has = true; break; }
            if (!has) return d;
        }
        return null;
    }
}
