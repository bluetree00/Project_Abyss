// WeaponData.cs
using System;
using UnityEngine;

public enum PromoteMode
{
    None,       // 차지/강화 없음
    Stage,      // 단계별 강화
    ChargeFull  // 일정 시간 누르면 최대 강화
}

/// <summary>
/// 런타임 Weapon 데이터 (WeaponSO 기반 생성)
/// </summary>
[Serializable]
public class WeaponData
{
    public string weaponDisplayKey;
    public string displayName;
    public string weaponPrefabKey;
    /// <summary>WeaponSO 에셋 이름 = Addressable 주소. 이어하기 복원 시 WeaponSO 로드에 사용한다.</summary>
    public string weaponSOKey;
    public string iconKey;
    public Sprite icon;            // HUD 표시용 직접 참조
    public float baseAttack;
    public float baseDefense;
    public float attackSpeed;
    public float attackRange;
    public float areaOfEffect;
    public float holdThreshold;

    // ── 치명타 ───────────────────────────────────────────────────────
    public float critChance = 25f;   // % 단위 (0~100)
    public float critDamage = 1.25f; // 배율 (1.25 = +25%)

    public int tier = 1;
    // 아이템 추첨 시스템과 통합된 등급. tier와 병행 유지(점진적 마이그레이션).
    public ItemRarity rarity = ItemRarity.Common;

    public PromoteMode promoteMode;
    public int chargeStages;

    // ── 강화(재련소) ─────────────────────────────────────────────────
    // enhanceLevel: 재련소 강화 성공 누적(0 = 기본/무명). 실패 시 하락(하한 0).
    // legendId    : 승급 전설 분기 id(엑스칼리버/갈라틴/아론다이트). 빈값 = 미승급.
    // baseAttackRaw: 강화 이전 순수 공격력(마스터). baseAttack 은 강화가 반영된 "유효값"이며
    //               씬 전환마다 재적용되는 ApplyServerOverride 이후에도 RecomputeEnhancedStats()로 보존한다.
    public int    enhanceLevel;
    public string legendId;
    public float  baseAttackRaw;

    /// <summary>
    /// 진화를 몇 번 거쳤는지(0 = 무명/미진화). 진화할 때마다 +1.
    /// 강화 상한을 밀어올리는 값 — 진화가 강화의 끝이 아니라 "다음 구간의 문"이 되게 한다.
    /// rarity 는 서버 차트가 덮으므로 상한 확장을 rarity 로 표현할 수 없어 별도 필드로 둔다.
    /// </summary>
    public int evolutionStage;

    /// <summary>
    /// 고른 스킬 각인 id(쉼표 구분). 스킬 단계에 오를 때 둘 중 하나를 고른다 — 자동으로 열리던 단계 분기를 선택으로 바꾼다
    /// (기획 스킬구성_재련소연결 A안, 09-25 시범: 환영베기). 진화해도 이어진다(같은 스킬을 쓰는 진화가 대부분).
    /// </summary>
    public string engravings = "";

    public bool HasEngraving(string id)
    {
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(engravings)) return false;
        foreach (var e in engravings.Split(',')) if (e == id) return true;
        return false;
    }

    public void AddEngraving(string id)
    {
        if (string.IsNullOrEmpty(id) || HasEngraving(id)) return;
        engravings = string.IsNullOrEmpty(engravings) ? id : engravings + "," + id;
    }

    /// <summary>진화 무기의 고유 능력 「형」 — SO 소유 참조(서버 차트가 덮지 않는다). 없으면 null.</summary>
    public WeaponFormSO form;
    /// <summary>형 단련 단계: 0 = 형 없음 · 1 = 깨어남 · 2 · 3 = 단련. 진화 · 승급해도 이어진다.</summary>
    public int formTier;
    /// <summary>고른 단련 변주 id(쉼표 구분) — Ⅱ · Ⅲ에서 하나씩.</summary>
    public string formVariants = "";

    public bool HasForm => form != null && formTier > 0;

    // ── 근접 강화 증강(10-15) — 「id:단계」 쉼표 문자열(형 단련처럼 그대로 세이브에 싣는다) ──
    /// <summary>고른 증강과 단계(I~III) — 「aug_sharp_edge:2,aug_pursuit:1」.</summary>
    public string augments = "";
    /// <summary>처음 닿은 최고 강화 단계 — 이 단계를 처음 넘을 때만 증강을 준다(하락 뒤 다시 닿으면 안 준다).</summary>
    public int highestEnhanceReached;
    /// <summary>은 등급이 이어진 수 — 3이면 다음 화면은 금 이상(바닥 보장).</summary>
    public int augmentSilverStreak;
    /// <summary>진화 직후 첫 강화 — 그 화면은 금 이상.</summary>
    public bool augmentAfterEvolve;
    /// <summary>재련 기록(10-19) — 재련마다 고른 증강 「id:그때 단계」를 고른 순서대로 쌓는다(같은 증강을 다시 골라도 새 줄 · 진화해도 이어진다).</summary>
    public string reforgeLog = "";

    /// <summary>이 증강의 단계(없으면 0).</summary>
    public int AugmentRank(string id)
    {
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(augments)) return 0;
        foreach (var (aid, rank) in AugmentList()) if (aid == id) return rank;
        return 0;
    }

    /// <summary>가진 증강 목록(id · 단계).</summary>
    public System.Collections.Generic.IEnumerable<(string id, int rank)> AugmentList() => Pairs(augments);

    /// <summary>재련 기록(재련 1번째부터 · id · 그때 단계).</summary>
    public System.Collections.Generic.IEnumerable<(string id, int rank)> ReforgeLogList() => Pairs(reforgeLog);

    /// <summary>재련 기록에 한 줄 — 증강을 고를 때(<see cref="MeleeAugmentDraft.Choose"/>).</summary>
    public void AppendReforgeLog(string id, int rank)
    {
        if (string.IsNullOrEmpty(id)) return;
        reforgeLog = string.IsNullOrEmpty(reforgeLog) ? $"{id}:{rank}" : $"{reforgeLog},{id}:{rank}";
    }

    private static System.Collections.Generic.IEnumerable<(string id, int rank)> Pairs(string list)
    {
        if (string.IsNullOrEmpty(list)) yield break;
        foreach (var part in list.Split(','))
        {
            int c = part.IndexOf(':');
            string id = c > 0 ? part.Substring(0, c) : part;
            int rank = c > 0 && int.TryParse(part.Substring(c + 1), out int r) ? r : 1;
            if (!string.IsNullOrEmpty(id)) yield return (id, UnityEngine.Mathf.Clamp(rank, 1, 3));
        }
    }

    /// <summary>증강을 얻거나 한 단계 올린다(최대 III). 새 단계를 돌려준다.</summary>
    public int AddAugment(string id)
    {
        if (string.IsNullOrEmpty(id)) return 0;
        var sb = new System.Text.StringBuilder();
        int result = 1;
        bool found = false;
        foreach (var (aid, rank) in AugmentList())
        {
            int r = rank;
            if (aid == id) { r = UnityEngine.Mathf.Min(rank + 1, 3); result = r; found = true; }
            if (sb.Length > 0) sb.Append(',');
            sb.Append(aid).Append(':').Append(r);
        }
        if (!found) { if (sb.Length > 0) sb.Append(','); sb.Append(id).Append(":1"); }
        augments = sb.ToString();
        return result;
    }

    public bool HasFormVariant(string id)
    {
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(formVariants)) return false;
        foreach (var v in formVariants.Split(',')) if (v == id) return true;
        return false;
    }

    public void AddFormVariant(string id)
    {
        if (string.IsNullOrEmpty(id) || HasFormVariant(id)) return;
        formVariants = string.IsNullOrEmpty(formVariants) ? id : formVariants + "," + id;
    }

    /// <summary>세이브에서 온 변주를 이 형 · 단계에 맞게 거른다 — 없는 id · 단계보다 높은 변주 · 같은 단계 두 번째는 버린다(변조 세이브 · 검토 M9).</summary>
    public void SanitizeFormVariants()
    {
        if (form == null || string.IsNullOrEmpty(formVariants)) { formVariants = form == null ? "" : (formVariants ?? ""); return; }
        var kept = new System.Text.StringBuilder();
        int usedTiers = 0;
        foreach (var id in formVariants.Split(','))
        {
            if (!form.TryGetVariant(id, out var v) || v.tier < 2 || v.tier > formTier || (usedTiers & (1 << v.tier)) != 0) continue;
            usedTiers |= 1 << v.tier;
            if (kept.Length > 0) kept.Append(',');
            kept.Append(id);
        }
        formVariants = kept.ToString();
    }

    public int groundEndCount;
    public int airEndCount;

    public WeaponAnimationSetSO animationSet;
    public WeaponAbilitySetSO abilitySet;
    public WeaponType weaponType = WeaponType.None;

    /// <summary>
    /// 이 무기가 진화할 수 있는 분기 테이블(SO 참조 — 서버 차트는 미관여). null이면 최종 형태.
    /// 진화하면 분기의 target WeaponSO로 통째 교체된다.
    /// </summary>
    public WeaponEvolutionSO evolution;

    /// <summary>진화 가능한 분기가 하나라도 있는지.</summary>
    public bool CanEvolve => evolution != null && evolution.Branches.Count > 0;

    /// <summary>공격 시 칼날 트레일 VFX 프리팹(INab Weapon Trail). SO에서만 채워짐(서버 차트는 미관여). 없으면 트레일 스킵.</summary>
    public GameObject trailVfxPrefab;

    // ── 스킬 SO 참조 ─────────────────────────────────────────────────
    public SkillSO skillQ;
    public SkillSO skillE;
    public SkillSO skillR;

    // ── 하위 호환 편의 접근자 ─────────────────────────────────────────
    public string skillName        => skillQ?.skillName;
    public string skillDescription => skillQ?.description;
    // 아이콘은 <b>현재 스킬 단계</b>를 따라간다 — 강화(재련소)·진화로 스킬 기능이 바뀌면 그림도 바뀐다(09-21).
    // 단계는 이 무기의 강화 수치에서 나온다(SkillTierResolver: 근접 +3·+6, 원거리 파츠 2·10).
    public Sprite skillQIcon       => skillQ?.IconForTier(SkillTier);
    public Sprite skillEIcon       => skillE?.IconForTier(SkillTier);
    public Sprite skillRIcon       => skillR?.IconForTier(SkillTier);
    /// <summary>이 무기의 현재 스킬 단계(1~3).</summary>
    public int    SkillTier        => SkillTierResolver.Resolve(this);
    public float  skillQCooldown   => skillQ?.cooldown ?? 0f;
    public float  skillECooldown   => skillE?.cooldown ?? 0f;
    public float  skillRCooldown   => skillR?.cooldown ?? 0f;

    /// <summary>WeaponSO 기반 생성</summary>
    public WeaponData(WeaponSO so)
    {
        if (so == null) throw new ArgumentNullException(nameof(so));

        weaponDisplayKey = so.weaponDisplayKey;
        weaponPrefabKey  = so.weaponPrefabKey;
        weaponSOKey      = so.name;
        displayName      = so.displayName;
        iconKey          = so.iconKey;
        icon             = so.icon;
        baseAttack       = so.baseAttack;
        baseDefense      = so.baseDefense;
        attackSpeed      = so.attackSpeed;
        attackRange      = so.attackRange;
        areaOfEffect     = so.areaOfEffect;
        holdThreshold    = so.holdThreshold;
        critChance       = so.critChance;
        critDamage       = so.critDamage > 0f ? so.critDamage : 1.25f;
        tier             = so.tier;
        rarity           = so.Rarity;

        promoteMode  = so.promoteMode;
        chargeStages = so.chargeStages;

        weaponType = so.weaponType;

        skillQ = so.skillQ;
        skillE = so.skillE;

        groundEndCount = so.groundEndCount;
        airEndCount    = so.airEndCount;

        animationSet = so.animationSet;
        abilitySet   = so.abilitySet;
        trailVfxPrefab = so.trailVfxPrefab;
        evolution    = so.evolution;   // 진화 분기 — 서버 차트가 덮지 않는 SO 소유 참조
        form         = so.Form;
        formTier     = form != null ? 1 : 0;   // 형이 있는 무기 = 진화한 무기 — 깨어난 채로 쥔다(테스트 허브 직접 장착 · 세이브 복원도 같다)

        baseAttackRaw = baseAttack;   // 강화 재계산의 기준(마스터)
    }

    /// <summary>
    /// baseAttackRaw + enhanceLevel + legendId 로 유효 공격력(baseAttack)을 재계산한다.
    /// 곡선은 WeaponEnhanceCurve(정적 provider) — 기본 내장 곡선이며 재련소 데이터(EnhanceTableSO)가
    /// 로드되면 WeaponEnhanceService가 실제 곡선으로 교체한다.
    /// 획득/서버 오버라이드/복원 직후 반드시 호출해 강화가 씬 전환에도 보존되게 한다.
    /// </summary>
    public void RecomputeEnhancedStats()
    {
        if (baseAttackRaw <= 0f) baseAttackRaw = baseAttack; // raw 미설정 폴백
        baseAttack = WeaponEnhanceCurve.Evaluate(baseAttackRaw, enhanceLevel, legendId);
    }

    /// <summary>SO 타입을 자동 판별해 적절한 WeaponData를 생성하는 팩토리</summary>
    public static WeaponData FromSO(WeaponSO so) => new WeaponData(so);

    /// <summary>
    /// 차트(EquipmentEntry) stats로 SO 디폴트값을 덮어쓴다.
    /// 차트가 마스터이므로 무기 획득 시 SO 로드 후 반드시 호출.
    /// SO 참조(animationSet, abilitySet, skillQ/E, icon, weaponType, displayName, prefabKey 등)는 보존.
    /// animationSet 의 ClipMapping 중 GroundLight/AirLight 콤보의 lunge/aim 필드는
    /// CSV(attack_step_1/2/3, move_input_scale, aim_assist_radius, use_aim_assist) 로 덮어쓴다.
    /// 이를 위해 animationSet 을 런타임 클론으로 교체한다(원본 .asset 보호).
    /// </summary>
    public void ApplyServerOverride(EquipmentEntry entry)
    {
        if (entry == null) return;

        baseAttack         = entry.base_attack;
        baseDefense        = entry.base_defense;
        critChance         = entry.crit_chance;
        critDamage         = entry.crit_damage > 0f ? entry.crit_damage : 1.25f;
        attackSpeed        = entry.attack_speed;
        attackRange        = entry.attack_range;
        areaOfEffect       = entry.area_of_effect;
        holdThreshold      = entry.hold_threshold;
        groundEndCount     = entry.ground_combo_count;
        airEndCount        = entry.air_combo_count;
        promoteMode        = ParsePromoteMode(entry.promote_mode);
        chargeStages       = entry.charge_stages;
        tier               = entry.tier;
        rarity             = ParseRarity(entry.rarity, entry.tier);

        // animationSet 런타임 클론 + ClipMapping 의 lunge/aim 필드를 CSV 로 주입
        if (animationSet != null)
        {
            var clone = UnityEngine.Object.Instantiate(animationSet);
            clone.name = animationSet.name + " (Runtime)";
            InjectChartLungeAimInto(clone, entry);
            animationSet = clone;
        }

        // weaponType, displayName, weaponPrefabKey, weaponDisplayKey, iconKey,
        // abilitySet, skillQ/E, evolution 은 SO 값 유지

        // 차트 값이 강화의 새 기준(raw). enhanceLevel 이 보존된 채 매 씬 전환마다 여기로 들어오므로
        // 반드시 재계산하여 baseAttack 을 유효값으로 유지한다(강화 유실 방지).
        baseAttackRaw = baseAttack;
        RecomputeEnhancedStats();
    }

    /// <summary>
    /// CSV 의 attack_step_1/2/3, move_input_scale, aim_assist_radius, use_aim_assist 를
    /// 클론된 WeaponAnimationSetSO 의 GroundLight/AirLight ClipMapping 에 주입한다.
    /// step 값이 0 이면 SO 디폴트(또는 인스펙터 값) 유지.
    /// </summary>
    private static void InjectChartLungeAimInto(WeaponAnimationSetSO set, EquipmentEntry e)
    {
        if (set == null || set.animGroups == null) return;

        var stepByCombo = new[] { e.attack_step_1, e.attack_step_2, e.attack_step_3 };
        bool aimAssist  = e.use_aim_assist != 0;
        float aimRadius = e.aim_assist_radius;
        float moveScale = e.move_input_scale;

        foreach (var group in set.animGroups)
        {
            if (group?.clipMappings == null) continue;
            foreach (var m in group.clipMappings)
            {
                if (m == null) continue;
                if (m.actionType != WeaponActionType.GroundLight &&
                    m.actionType != WeaponActionType.AirLight)
                    continue;

                int idx = UnityEngine.Mathf.Clamp(m.comboIndex, 0, stepByCombo.Length - 1);
                if (stepByCombo[idx] > 0f)
                    m.attackStepDistance = stepByCombo[idx];

                m.moveInputScale = moveScale;
                m.useAimAssist   = aimAssist;
                if (aimRadius > 0f)
                    m.aimAssistRadius = aimRadius;
            }
        }
    }

    /// <summary>
    /// 서버 EquipmentEntry 기반 생성.
    /// SO 참조(animationSet, abilitySet, skillQ/E, icon)는 null — 별도 바인딩 필요.
    /// </summary>
    public static WeaponData FromServer(EquipmentEntry entry)
    {
        if (entry == null) throw new ArgumentNullException(nameof(entry));

        var data = new WeaponData
        {
            weaponDisplayKey = entry.weapon_display_key,
            weaponPrefabKey  = entry.weapon_prefab_key,
            displayName      = entry.weapon_name,
            iconKey          = entry.icon_key,
            icon             = null, // Addressables로 별도 로드
            baseAttack       = entry.base_attack,
            baseDefense      = entry.base_defense,
            attackSpeed      = entry.attack_speed,
            attackRange      = entry.attack_range,
            areaOfEffect     = entry.area_of_effect,
            holdThreshold    = entry.hold_threshold,
            critChance       = entry.crit_chance,
            critDamage       = entry.crit_damage > 0f ? entry.crit_damage : 1.25f,
            tier             = entry.tier,
            rarity           = ParseRarity(entry.rarity, entry.tier),
            promoteMode      = ParsePromoteMode(entry.promote_mode),
            chargeStages     = entry.charge_stages,
            groundEndCount   = entry.ground_combo_count,
            airEndCount      = entry.air_combo_count,
            weaponType           = ParseWeaponType(entry.weapon_type),
            // SO 참조는 null — WeaponSO에서 바인딩하거나 Addressables로 로드
            animationSet     = null,
            abilitySet       = null,
            skillQ           = null,
            skillE           = null,
        };
        data.baseAttackRaw = data.baseAttack;   // 강화 재계산 기준
        UnityEngine.Debug.Log($"[WeaponData.FromServer] {entry.weapon_id} ({entry.weapon_name})");
        return data;
    }

    /// <summary>SO 없이 빈 WeaponData 생성 (서버 팩토리용)</summary>
    private WeaponData() { }

    private static PromoteMode ParsePromoteMode(string s) => s switch
    {
        "Stage"      => PromoteMode.Stage,
        "ChargeFull" => PromoteMode.ChargeFull,
        _            => PromoteMode.None,
    };

    /// <summary>
    /// 서버 rarity 문자열을 ItemRarity로 파싱.
    /// 비어있거나 파싱 실패 시 tier 기반 폴백.
    /// </summary>
    private static ItemRarity ParseRarity(string s, int tier)
    {
        if (string.IsNullOrEmpty(s)) return MapTierToRarity(tier);
        if (Enum.TryParse<ItemRarity>(s, ignoreCase: true, out var r)) return r;
        return MapTierToRarity(tier);
    }

    /// <summary>tier(1~4) → ItemRarity 매핑. 잘못된 값은 Common으로 폴백.</summary>
    private static ItemRarity MapTierToRarity(int tier) => tier switch
    {
        1 => ItemRarity.Common,
        2 => ItemRarity.Rare,
        3 => ItemRarity.Epic,
        4 => ItemRarity.Legendary,
        _ => ItemRarity.Common,
    };

    private static WeaponType ParseWeaponType(string s) => s switch
    {
        "Katana"     => WeaponType.Katana,
        "Greatsword" => WeaponType.Greatsword,
        "Crossbow"   => WeaponType.Crossbow,
        "Bow"        => WeaponType.Bow,
        "Staff"      => WeaponType.Staff,
        _            => WeaponType.None,
    };
}
