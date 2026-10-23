using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>증강 등급 — 화면마다 굴린다(사용자 10-15 「등급도 랜덤 — 고정이면 기대를 하기 힘들다」).</summary>
public enum AugmentTier { Silver = 0, Gold = 1, Prismatic = 2 }

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

    public const int MaxRank = 3;

    public MeleeAugmentDef(string id, string name, AugmentTier tier, BuildFamily family, MeleeWeaponKind weapon,
                           float[] values, string descFormat, Func<MeleeAugmentDef, int, MeleeAugmentEffect> create, float[] values2 = null,
                           string phantomDesc = null)
    {
        Id = id; Name = name; Tier = tier; Family = family; Weapon = weapon;
        Values = values; DescFormat = descFormat; Create = create; Values2 = values2; PhantomDescFormat = phantomDesc;
    }

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
    public static string TierName(AugmentTier t) => t switch { AugmentTier.Gold => "금", AugmentTier.Prismatic => "프리즘", _ => "은" };
}

/// <summary>근접 강화 증강 목록 — 공용 + 무기 갈래 전용. 수치는 시작값(실측으로 맞춘다).</summary>
public static class MeleeAugmentCatalog
{
    // 다른 코드(스킬 · 형 · 진화)가 읽는 id
    public const string HeavyHand = "aug_heavy_hand", SwordWave = "aug_sword_wave", TwinWave = "aug_twin_wave",
                        KatanaDraw = "aug_katana_draw", KatanaPhantom = "aug_katana_phantom",
                        GreatSplit = "aug_great_split", GreatPerimeter = "aug_great_perimeter", GreatFinal = "aug_great_final",
                        NamelessSheath = "aug_nameless_sheath";

    private static readonly List<MeleeAugmentDef> s_all = new()
    {
        // ── 공용 · 은 ── 모두 「규칙」 — 스탯 한 줄짜리는 두지 않는다(사용자 10-15 「의미 있는 증강 — 그냥 스탯 증가만이 아닌」)
        new("aug_ricochet",    "튕기는 칼끝", AugmentTier.Silver, BuildFamily.Crit,     MeleeWeaponKind.None, new[] { 1f, 2f, 3f },    "평타 치명타가 둘레 5 m의 다른 적 {0}명에게 튄다(치명 피해의 40%)",
            (d, r) => new RicochetAugment(d, r)),
        new(HeavyHand,         "묵직한 손",   AugmentTier.Silver, BuildFamily.Crit,     MeleeWeaponKind.None, new[] { 4f, 3f, 2f },    "평타 {0}번째 휘두름마다 강타",
            (d, r) => new HeavySwingAugment(d, r)),
        new("aug_blade_scar",  "칼자국",      AugmentTier.Silver, BuildFamily.Combo,    MeleeWeaponKind.None, new[] { 6f, 9f, 12f },   "평타 휘두름마다 앞 바닥에 칼자국이 1.5초 남아 0.5초마다 벤다(공격력 {0}%)",
            (d, r) => new BladeScarAugment(d, r)),
        new("aug_ambush",      "기습",        AugmentTier.Silver, BuildFamily.Mobility, MeleeWeaponKind.None, new[] { 0.5f, 0.75f, 1f }, "평타를 2초 쉬었다 친 첫 타는 반드시 치명 · 일반 몹 {0}초 경직",
            (d, r) => new AmbushAugment(d, r)),
        new("aug_hunter_breath","사냥꾼의 숨", AugmentTier.Silver, BuildFamily.Kill,     MeleeWeaponKind.None, new[] { 15f, 25f, 35f },  "처치하면 둘레 3 m 적이 「사냥감」 — 2초 동안 받는 피해 +{0}%",
            (d, r) => new HuntMarkAugment(d, r)),
        new("aug_dodge_riposte","회피 베기",  AugmentTier.Silver, BuildFamily.Mobility, MeleeWeaponKind.None, new[] { 40f, 60f, 80f },  "회피로 지나간 길의 적을 벤다(공격력 {0}%)",
            (d, r) => new DodgeCutAugment(d, r)),
        new("aug_spell_blade", "주문의 날",   AugmentTier.Silver, BuildFamily.Spell,    MeleeWeaponKind.None, new[] { 30f, 45f, 60f },  "E · R을 쓰면 다음 평타 3번이 앞 3 m 칼날 파동을 낸다(공격력 {0}%)",
            (d, r) => new SpellBladeAugment(d, r)),
        new("aug_last_will",   "마지막 의지", AugmentTier.Silver, BuildFamily.Danger,   MeleeWeaponKind.None, new[] { 1f, 1.5f, 2f },   "체력이 40% 아래로 떨어지면 1.5초 무적 · 둘레 3 m 적 {0}초 경직(방마다 한 번)",
            (d, r) => new LastWillAugment(d, r)),

        // ── 공용 · 금 ──
        new(SwordWave,         "검기",        AugmentTier.Gold,   BuildFamily.Combo,    MeleeWeaponKind.None, new[] { 60f, 90f, 120f }, "콤보 마지막 타가 앞으로 6 m 관통 검기(공격력 {0}%)",
            (d, r) => new SwordWaveAugment(d, r)),
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
        new(TwinWave,          "쌍검기",      AugmentTier.Prismatic, BuildFamily.Combo, MeleeWeaponKind.None, new[] { 60f, 80f, 100f }, "검기 양옆 ±20°로 두 갈래 더(갈래 피해 = 검기의 {0}%)",
            (d, r) => new MarkerAugment(d, r)),   // 검기 증강이 읽는다
        new("aug_thunder",     "천둥 같은 일격", AugmentTier.Prismatic, BuildFamily.Crit, MeleeWeaponKind.None, new[] { 50f, 75f, 100f }, "강타 적중 때 대상 뒤 4 m까지 관통 충격(공격력 {0}% · 휘두름당 1번)",
            (d, r) => new ThunderAugment(d, r)),
        new("aug_slaughter_chain","학살의 연쇄", AugmentTier.Prismatic, BuildFamily.Kill, MeleeWeaponKind.None, new[] { 50f, 75f, 100f }, "처치한 자리에서 칼바람이 일어 둘레 2.5 m를 벤다(공격력 {0}% · 칼바람의 처치도 다시 일으킨다)",
            (d, r) => new SlaughterChainAugment(d, r)),
        new("aug_imprint_echo","각인 공명",   AugmentTier.Prismatic, BuildFamily.None,  MeleeWeaponKind.None, new[] { 1f, 1.5f, 2f },  "가장 높은 발동 계열 각인 +{0}",
            (d, r) => new MarkerAugment(d, r)),   // BuildImprint.Recount가 읽는다

        // ── 카타나 전용(빠른 연타 · 돌진 · 칼바람) ──
        new("aug_katana_third","세 번째 칼",  AugmentTier.Silver, BuildFamily.Combo,    MeleeWeaponKind.Katana, new[] { 35f, 55f, 75f }, "콤보 3타에 맞은 적마다 0.6초 뒤 X자 베기(공격력 {0}%)",
            (d, r) => new ThirdBladeAugment(d, r)),
        new(KatanaDraw,        "발도 연속",   AugmentTier.Gold,   BuildFamily.Mobility, MeleeWeaponKind.Katana, new[] { 25f, 45f, 65f }, "성광베기 돌진 거리 +{0}% · 2단계부터 추가 타격 +{1}",
            (d, r) => new MarkerAugment(d, r), new[] { 1f, 1f, 2f }),   // 성광베기가 읽는다
        new("aug_katana_flow", "물 흐르듯",   AugmentTier.Gold,   BuildFamily.Mobility, MeleeWeaponKind.Katana, new[] { 5f, 4f, 3f },    "회피 뒤 1초 안 첫 적중이 회피 1칸을 돌려준다({0}초에 한 번)",
            (d, r) => new FlowAugment(d, r)),
        new(KatanaPhantom,     "환영 베기 강화", AugmentTier.Gold, BuildFamily.Spell,   MeleeWeaponKind.Katana, new[] { 20f, 35f, 50f }, "환영베기 피해 +{0}% · 2단계부터 이어 갈라짐 +{1}",
            (d, r) => new MarkerAugment(d, r), new[] { 1f, 1f, 2f }),   // 환영베기 · 스킬 피해 계산이 읽는다
        new("aug_katana_gale", "바람 가르기", AugmentTier.Prismatic, BuildFamily.Combo, MeleeWeaponKind.Katana, new[] { 35f, 70f, 100f }, "칼바람이 휘두름마다 {0}% 확률로 1칸 더 쌓인다",
            (d, r) => new GaleAugment(d, r)),

        // ── 대검 전용(무게 · 버팀 · 땅) ──
        new("aug_great_bulwark","철벽 자세",  AugmentTier.Silver, BuildFamily.Danger,   MeleeWeaponKind.Greatsword, new[] { 10f, 18f, 25f }, "평타 동작 중 맞아도 경직되지 않고 받는 피해 −{0}%",
            (d, r) => new BulwarkAugment(d, r)),
        new(GreatSplit,        "땅 가르기",   AugmentTier.Gold,   BuildFamily.Crit,     MeleeWeaponKind.Greatsword, new[] { 25f, 45f, 65f }, "내려찍기 범위 +{0}% · 균열 변주면 균열 +{1}초",
            (d, r) => new MarkerAugment(d, r), new[] { 1f, 2f, 3f }),   // 무게 형이 읽는다
        new(GreatPerimeter,    "주변경계 연장", AugmentTier.Gold, BuildFamily.Spell,    MeleeWeaponKind.Greatsword, new[] { 1f, 2f, 3f },    "주변경계 충격 고리 +{0}",
            (d, r) => new MarkerAugment(d, r)),   // 주변경계가 읽는다
        new(GreatFinal,        "최후의 일격 충격", AugmentTier.Gold, BuildFamily.Spell, MeleeWeaponKind.Greatsword, new[] { 20f, 35f, 50f }, "최후의 일격 착지 · 여진 반경 +{0}% · III은 여진 +1",
            (d, r) => new MarkerAugment(d, r)),   // 최후의 일격이 읽는다
        new("aug_great_quake", "지진",        AugmentTier.Gold,   BuildFamily.Combo,    MeleeWeaponKind.Greatsword, new[] { 30f, 45f, 60f }, "콤보 마지막 타 1초 뒤 반경 3 m 땅울림(공격력 {0}%)",
            (d, r) => new QuakeAugment(d, r)),
        new("aug_great_giant", "거인의 걸음", AugmentTier.Prismatic, BuildFamily.Crit,  MeleeWeaponKind.Greatsword, new[] { 30f, 45f, 60f }, "평타 휘두름마다 발밑 2 m 진동파(공격력 {0}% · 보스 외 0.3초 경직)",
            (d, r) => new GiantStepAugment(d, r)),

        // ── 무형검 전용(형 없는 칼 · 진화 전) ──
        new("aug_nameless_mist","안개 칼날",  AugmentTier.Silver, BuildFamily.Spell,    MeleeWeaponKind.Nameless,   new[] { 15f, 25f, 35f }, "무형참 칼날이 맞힌 자리에 안개가 1.5초 남아 0.5초마다 벤다(공격력 {0}%)",
            (d, r) => new MistAugment(d, r), phantomDesc: "환영이 E가 맞힌 자리를 한 번 더 그어 안개를 남긴다 — 1.5초 동안 0.5초마다 벤다(공격력 {0}%)"),
        new("aug_nameless_formless","형 없는 연타", AugmentTier.Silver, BuildFamily.Combo, MeleeWeaponKind.Nameless, new[] { 30f, 45f, 60f }, "콤보 마지막 타 0.2초 뒤 앞 2.5 m를 한 번 더 벤다(공격력 {0}%)",
            (d, r) => new FormlessFollowAugment(d, r), phantomDesc: "콤보 마지막 타 0.2초 뒤 환영이 앞 2.5 m를 한 번 더 벤다(공격력 {0}%)"),
        new("aug_nameless_afterimage","무형일섬 잔상", AugmentTier.Gold, BuildFamily.Spell, MeleeWeaponKind.Nameless, new[] { 40f, 60f, 80f }, "무형일섬이 지나간 길을 0.4초 뒤 잔상이 벤다(공격력 {0}%)",
            (d, r) => new AfterimageAugment(d, r), phantomDesc: "R을 쓰고 0.5초 뒤 환영이 조준 방향 6 m를 일섬으로 가른다(공격력 {0}%)"),
        new(NamelessSheath,    "비어 있는 칼집", AugmentTier.Prismatic, BuildFamily.None, MeleeWeaponKind.Nameless, new[] { 1f, 2f, 3f },  "진화하면 새 무기 전용 증강 {0}개(I)로 바뀐다",
            (d, r) => null),   // 효과 없음 — 진화 때 ConvertForEvolution이 바꾼다
    };

    public static IReadOnlyList<MeleeAugmentDef> All => s_all;

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
    /// 비어 있는 칼집은 새 갈래 전용 증강 N개(I)가 된다(높은 등급부터). 공용 · 이미 새 갈래 것은 그대로.
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
            var pick = FirstExclusive(kind, kept, AugmentTier.Prismatic) ?? FirstExclusive(kind, kept, AugmentTier.Gold) ?? FirstExclusive(kind, kept, null);
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
