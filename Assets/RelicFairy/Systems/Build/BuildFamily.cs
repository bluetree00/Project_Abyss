using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 빌드 계열 = <b>언제 터지나</b>(발동). 09-29 사용자 결정 — 「속성보단 트리거 기반」.
/// 속성은 무엇이 터지나(탄두)라 계열과 따로 간다 — 「기동 × 불」「주문 × 전기」처럼 두 축 조합이 빌드 이름이 된다.
/// </summary>
public enum BuildFamily
{
    None = 0,
    /// <summary>연격 — 몰아칠 때(연타 · 같은 대상 · 적중)</summary>
    Combo,
    /// <summary>처치 — 쓰러뜨릴 때</summary>
    Kill,
    /// <summary>기동 — 움직일 때(이동 · 대시 · 무기 전환)</summary>
    Mobility,
    /// <summary>주문 — 스킬을 쓸 때</summary>
    Spell,
    /// <summary>필살 — 치명이 터질 때</summary>
    Crit,
    /// <summary>사격 — 원거리로 칠 때</summary>
    Ranged,
    /// <summary>위기 — 몰렸을 때(체력 낮음 · 피격 · 포위 · 보스전)</summary>
    Danger,
}

/// <summary>
/// 발동 계열 규칙 — 룬의 발동(ITEM_DATA trigger)과 서약 원인(CauseClass)을 계열로 묶고, 각인 단계를 정한다.
/// <para>계열이 없는 것(항상 · 주기)은 「바탕」 — 빌드를 기울이지 않는다.</para>
/// </summary>
public static class BuildFamilyRules
{
    // ── Constants ────────────────────────────────────────
    /// <summary>각인(같은 계열 보유 수) 단계 역치 — 2 / 4 / 6.</summary>
    public static readonly int[] Thresholds = { 2, 4, 6 };

    /// <summary>각인 단계마다 그 계열 룬의 효과가 오르는 몫(+20% · +40% · +60%).</summary>
    public const float BonusPerStage = 0.20f;

    public static readonly BuildFamily[] All =
        { BuildFamily.Combo, BuildFamily.Kill, BuildFamily.Mobility, BuildFamily.Spell, BuildFamily.Crit, BuildFamily.Ranged, BuildFamily.Danger };

    // ── Static ───────────────────────────────────────────
    private static readonly Dictionary<string, BuildFamily> s_itemFamily = new();

    // ── Public Methods ───────────────────────────────────

    /// <summary>룬 효과 발동(ITEM_DATA trigger 열) → 계열. 새 발동(대시 · 치명 · 원거리 명중 …)도 여기서 묶는다.
    /// <para>OnActivate(전설 투사체 · 장판 — 전투 내내 도는 것)는 스킬과 무관해 「바탕」이다.</para></summary>
    public static BuildFamily FromTrigger(string trigger) => trigger switch
    {
        "OnHit" or "OnHitCount" or "SameTarget" or "SingleEnemy"                         => BuildFamily.Combo,
        "OnKill" or "OnRoomClear" or "OnExecute" or "OnMultiKill" or "AfterKill"         => BuildFamily.Kill,
        "WhileMoving" or "OnDash" or "OnPerfectDodge" or "AfterDashHit" or "AfterDash"   => BuildFamily.Mobility,
        "AfterSkill" or "OnSkillUse" or "OnSkillHit" or "OnSkillChain"                   => BuildFamily.Spell,
        "OnCritCount" or "NoHit" or "OnCrit" or "AfterCrit"                              => BuildFamily.Crit,
        "OnRangedHit" or "WithRangedWeapon" or "WithBowWeapon"                           => BuildFamily.Ranged,
        "HPBelow40" or "AfterHit" or "EnemiesNearby" or "DuringBoss" or "HPBelow50"      => BuildFamily.Danger,
        _                                                                                 => BuildFamily.None,
    };

    /// <summary>
    /// 효과 한 칸의 계열 — 효과 종류가 발동을 정하는 것(「Always」로 적혔어도 그 동작에서만 쓰이는 것)이 먼저, 그다음 발동 열.
    /// 관통 · 투사체 수는 쏠 때만, 구르기 직후 첫 공격은 대시에서, 반사 · 피격 방어는 맞을 때만 뜻이 있다.
    /// </summary>
    public static BuildFamily FromSlot(string effectType, string trigger) => effectType switch
    {
        "ProjectilePierce" or "ProjectileCount"  => BuildFamily.Ranged,
        "FirstAttackAfterRoll"                   => BuildFamily.Mobility,
        "DamageReflect" or "DefenseOnHit"        => BuildFamily.Danger,
        _                                        => FromTrigger(trigger),
    };

    /// <summary>각인 배율을 곱해도 되는 효과인가 — 개수(관통 · 투사체 수)는 소수로 키우면 뜻이 없다.</summary>
    public static bool IsScalable(string effectType) => effectType is not ("ProjectilePierce" or "ProjectileCount");

    /// <summary>서약 원인 분류 → 계열(심장박동 · 경계는 바탕).</summary>
    public static BuildFamily FromCause(CauseClass cls) => cls switch
    {
        CauseClass.Melee    => BuildFamily.Combo,
        CauseClass.Kill     => BuildFamily.Kill,
        CauseClass.Mobility => BuildFamily.Mobility,
        CauseClass.Skill    => BuildFamily.Spell,
        CauseClass.Danger   => BuildFamily.Danger,
        _                   => BuildFamily.None,
    };

    /// <summary>룬 하나의 계열 — 효과 슬롯 중 처음으로 계열이 있는 발동. 없으면 None(바탕 룬).</summary>
    public static BuildFamily OfItem(RuntimeItemData item)
    {
        if (item?.effects == null) return BuildFamily.None;
        foreach (var slot in item.effects)
        {
            if (slot == null) continue;
            var f = FromSlot(slot.effectType, slot.trigger);
            if (f != BuildFamily.None) return f;
        }
        return BuildFamily.None;
    }

    /// <summary>아이템 id의 계열(차트 기준, 캐시) — 제시 가중처럼 아직 룬을 만들기 전에 쓴다.</summary>
    public static BuildFamily OfItemId(string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return BuildFamily.None;
        if (s_itemFamily.TryGetValue(itemId, out var f)) return f;
        f = BuildFamily.None;
        var entries = Managers.ItemData?.GetItem(itemId);
        if (entries != null)
            foreach (var e in entries)
            {
                if (e == null) continue;
                f = FromSlot(e.effect_type, e.trigger);
                if (f != BuildFamily.None) break;
            }
        if (entries != null) s_itemFamily[itemId] = f;   // 차트가 아직 없으면 캐시하지 않는다
        return f;
    }

    /// <summary>
    /// 무기 한 자루의 계열 — 전설 승급이 먼저(엑스칼리버 = 필살 · 갈라틴 = 연격 · 아론다이트 = 처치),
    /// 아니면 진화 분기(카타나 = 연격 · 대검 = 필살). 무명의 형상(진화 전)은 카타나 타입이라 진화 단계로 가른다.
    /// <para>지금 전설 3종은 효과가 같다 — 계열이 고를 이유가 된다(빌드 컨셉 §4).</para>
    /// </summary>
    public static BuildFamily OfWeapon(WeaponData w)
    {
        if (w == null) return BuildFamily.None;
        switch (w.legendId?.ToLowerInvariant())
        {
            case "excalibur": return BuildFamily.Crit;
            case "galatine":  return BuildFamily.Combo;
            case "arondight": return BuildFamily.Kill;
        }
        if (w.evolutionStage <= 0) return BuildFamily.None;
        return w.weaponType switch
        {
            WeaponType.Katana     => BuildFamily.Combo,
            WeaponType.Greatsword => BuildFamily.Crit,
            _                     => BuildFamily.None,
        };
    }

    /// <summary>유물 파츠(RELIC_PARTS_DATA part_id)의 계열 — 파츠가 언제 움직이는가.</summary>
    public static BuildFamily OfRelicPart(string partId) => partId switch
    {
        "gawain_core_solar_calamity" or "gawain_core_judgment_brand" or "gawain_eff_burn_spread"
            or "lancelot_core_blood_feast" or "lancelot_core_betrayer_brand"
            or "lancelot_trg_blood_price" or "lancelot_beh_blood_thirst"                      => BuildFamily.Kill,
        "gawain_core_eternal_noon" or "gawain_beh_ember_trail" or "gawain_trg_noon_bloom"
            or "lancelot_eff_bleed_brand"                                                     => BuildFamily.Spell,
        "gawain_beh_burst_burn" or "lancelot_core_endless_frenzy" or "lancelot_beh_lasting_madness" => BuildFamily.Combo,
        _                                                                                      => BuildFamily.None,
    };

    // ── 단계 전용 스탯(T3) ─────────────────────────────────
    // 단계마다 그 계열이 「더 자주 · 더 세게」 되는 값 하나씩. 범용 공격력이 아니라 그 동작에만 뜻이 있는 것을 고른다.
    private const float ComboAtkSpeed   = 0.04f;   // 연격 — 공격속도
    private const float KillAttack      = 0.04f;   // 처치 — 공격력
    private const float MobilityMove    = 0.05f;   // 기동 — 이동속도
    private const float SpellSkill      = 0.08f;   // 주문 — 스킬 피해
    private const float CritChanceStep  = 0.03f;   // 필살 — 치명 확률(%p)
    private const float CritDamageStep  = 0.08f;   //        치명 피해
    private const float RangedDamage    = 0.05f;   // 사격 — 원거리 무기를 든 동안 모든 피해
    private const float DangerDefense   = 0.06f;   // 위기 — 방어력

    /// <summary>각인 <paramref name="stage"/>단계가 주는 스탯을 누적기에 더한다(ItemEffectManager.OnTick).</summary>
    public static void AddStageStats(BuildFamily f, int stage, ItemEffectContext ctx, ref ItemDynamicStats dyn)
    {
        if (stage <= 0) return;
        switch (f)
        {
            case BuildFamily.Combo:    dyn.attackSpeed   += ComboAtkSpeed * stage; break;
            case BuildFamily.Kill:     dyn.attackPercent += KillAttack * stage; break;
            case BuildFamily.Mobility: dyn.moveSpeed     += MobilityMove * stage; break;
            case BuildFamily.Spell:    dyn.skillDamage   += SpellSkill * stage; break;
            case BuildFamily.Crit:     dyn.critChance    += CritChanceStep * stage; dyn.critDamage += CritDamageStep * stage; break;
            case BuildFamily.Ranged:
                if (ctx != null && (ctx.WeaponType == WeaponType.Bow || ctx.WeaponType == WeaponType.Crossbow))
                    dyn.allDamage += RangedDamage * stage;
                break;
            case BuildFamily.Danger:   dyn.defensePercent += DangerDefense * stage; break;
        }
    }

    /// <summary>단계 보너스 한 줄 — 알림 · 버프 줄 · 추적 줄 설명.</summary>
    public static string StageBonusText(BuildFamily f, int stage)
    {
        int eff = UnityEngine.Mathf.RoundToInt(stage * BonusPerStage * 100f);
        string stat = f switch
        {
            BuildFamily.Combo    => $"공격속도 +{Pct(ComboAtkSpeed * stage)} · 연타 요구 −{stage}",
            BuildFamily.Kill     => $"공격력 +{Pct(KillAttack * stage)} · 처치 폭발 반경 +{Pct(0.15f * stage)}",
            BuildFamily.Mobility => $"이동속도 +{Pct(MobilityMove * stage)}",
            BuildFamily.Spell    => $"스킬 피해 +{Pct(SpellSkill * stage)}",
            BuildFamily.Crit     => $"치명 확률 +{Pct(CritChanceStep * stage)} · 치명 피해 +{Pct(CritDamageStep * stage)}",
            BuildFamily.Ranged   => $"원거리 무기일 때 피해 +{Pct(RangedDamage * stage)}",
            BuildFamily.Danger   => $"방어력 +{Pct(DangerDefense * stage)}",
            _                    => "",
        };
        return $"{Label(f)} 룬 효과 +{eff}% · {stat}";
    }

    private static string Pct(float v) => UnityEngine.Mathf.RoundToInt(v * 100f) + "%";

    /// <summary>계열 → 버프 줄 아이콘 키(EffectIconRegistry 어휘).</summary>
    public static string IconKey(BuildFamily f) => f switch
    {
        BuildFamily.Combo    => "atkspeed",
        BuildFamily.Kill     => "atk",
        BuildFamily.Mobility => "speed",
        BuildFamily.Spell    => "skill",
        BuildFamily.Crit     => "crit",
        BuildFamily.Ranged   => "projectile",
        BuildFamily.Danger   => "def",
        _                    => "unknown",
    };

    /// <summary>각인 수 → 단계(0~3).</summary>
    public static int StageOf(int count)
    {
        int s = 0;
        foreach (var t in Thresholds) if (count >= t) s++;
        return s;
    }

    /// <summary>다음 단계 역치(다 찼으면 -1).</summary>
    public static int NextThreshold(int count)
    {
        foreach (var t in Thresholds) if (count < t) return t;
        return -1;
    }

    public static string Label(BuildFamily f) => f switch
    {
        BuildFamily.Combo    => "연격",
        BuildFamily.Kill     => "처치",
        BuildFamily.Mobility => "기동",
        BuildFamily.Spell    => "주문",
        BuildFamily.Crit     => "필살",
        BuildFamily.Ranged   => "사격",
        BuildFamily.Danger   => "위기",
        _                    => "",
    };

    /// <summary>한 줄 — 언제 터지나.</summary>
    public static string When(BuildFamily f) => f switch
    {
        BuildFamily.Combo    => "몰아칠 때",
        BuildFamily.Kill     => "쓰러뜨릴 때",
        BuildFamily.Mobility => "움직일 때",
        BuildFamily.Spell    => "스킬을 쓸 때",
        BuildFamily.Crit     => "치명이 터질 때",
        BuildFamily.Ranged   => "원거리로 칠 때",
        BuildFamily.Danger   => "몰렸을 때",
        _                    => "",
    };

    /// <summary>계열 색 — 채도를 낮춘 색(원색 금지). 속성 색과 겹치지 않게 따뜻한 쪽 · 차가운 쪽을 섞었다.</summary>
    public static Color ColorOf(BuildFamily f) => f switch
    {
        BuildFamily.Combo    => new Color(0.93f, 0.62f, 0.42f),
        BuildFamily.Kill     => new Color(0.84f, 0.44f, 0.48f),
        BuildFamily.Mobility => new Color(0.52f, 0.82f, 0.74f),
        BuildFamily.Spell    => new Color(0.66f, 0.60f, 0.94f),
        BuildFamily.Crit     => new Color(0.96f, 0.82f, 0.45f),
        BuildFamily.Ranged   => new Color(0.56f, 0.74f, 0.95f),
        BuildFamily.Danger   => new Color(0.78f, 0.56f, 0.76f),
        _                    => new Color(0.6f, 0.6f, 0.64f),
    };

    public static string Hex(BuildFamily f) => "#" + ColorUtility.ToHtmlStringRGB(ColorOf(f));
}
