using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 효과 단위 종류 — 수치를 어떻게 표기할지 결정하는 "진실 원천".
/// (동작 레이어 ItemEffectRegistry의 effectType 클래스 구현이 실제로 value를 어떻게 해석하는지를 반영)
/// </summary>
public enum EffectUnit
{
    /// <summary>비율(×100 + "%"). value 0.04 → "+4%". 대부분의 % 스탯.</summary>
    Ratio,
    /// <summary>절댓값(소수 그대로). value 4 → "+4".</summary>
    Flat,
    /// <summary>절댓값(정수). (int)value 적용 스탯(Defense/MaxHP/Luck/Projectile 등).</summary>
    FlatInt,
    /// <summary>초 단위. value 2 → "2.0초".</summary>
    Duration,
    /// <summary>발동 확률(×100 + "% 확률"). value 0.2 → "20% 확률". (Random.value>=_value 패턴)</summary>
    Chance,
    /// <summary>
    /// 자동 판정 — abs(value)>=1 이면 Flat, 아니면 Ratio.
    /// AllDamage 효과가 실제로 쓰는 휴리스틱이자, 미지/모호 effectType의 안전 기본값
    /// (flat 값을 %로 부풀리는 +400% 버그를 원천 차단).
    /// </summary>
    Auto,
    /// <summary>수치 표기 없음(존재만 표시).</summary>
    None,
}

/// <summary>효과 분류 — 색/아이콘/정렬 힌트(3단계 아이콘 매핑에서 활용).</summary>
public enum EffectCategory
{
    Attack,    // 공격 강화
    Defense,   // 방어/생존
    Utility,   // 유틸(이동/쿨다운/획득률 등)
    Proc,      // 조건/확률 발동(적중·처치·피격 시)
    Resource,  // 자원(골드/회복/행운)
    Special,   // 특수/복합(T3·T4 잔첨/광폭/타이밍/형태)
    Unknown,
}

/// <summary>effectType 1개의 표시용 메타.</summary>
public readonly struct EffectMeta
{
    public readonly string Label;          // 한글 라벨
    public readonly EffectCategory Category;
    public readonly EffectUnit Unit;
    public readonly string IconKey;        // 3단계에서 스프라이트로 매핑될 키 문자열
    public readonly bool IsFallback;       // 미등록 폴백 여부

    public EffectMeta(string label, EffectCategory category, EffectUnit unit, string iconKey, bool isFallback = false)
    {
        Label = label;
        Category = category;
        Unit = unit;
        IconKey = iconKey;
        IsFallback = isFallback;
    }
}

/// <summary>
/// 표시 전용 메타 레지스트리. effectType(string) → <see cref="EffectMeta"/>.
///
/// ■ ItemEffectRegistry(동작: effectType→IItemEffect)와 분리된 "표시용" 레이어.
/// ■ 누락 effectType → enum명 라벨 + Auto 단위로 폴백(경고 1회).
/// ■ 새 effectType 추가 시 RegisterAll()에 한 줄.
/// </summary>
public static class EffectMetaRegistry
{
    private static readonly Dictionary<string, EffectMeta> _table = new();
    private static readonly HashSet<string> _warned = new();
    private static bool _initialized;

    /// <summary>등록된 effectType 수(폴백 제외).</summary>
    public static int Count
    {
        get { EnsureInitialized(); return _table.Count; }
    }

    /// <summary>effectType 메타 조회. 미등록이면 폴백 메타(enum명+Auto)를 반환하고 1회 경고.</summary>
    public static EffectMeta Get(string effectType)
    {
        EnsureInitialized();

        if (!string.IsNullOrEmpty(effectType) && _table.TryGetValue(effectType, out var meta))
            return meta;

        string label = string.IsNullOrEmpty(effectType) ? "효과" : effectType;
        if (!string.IsNullOrEmpty(effectType) && _warned.Add(effectType))
            Debug.LogWarning($"[EffectMetaRegistry] 미등록 effectType: {effectType} → enum명 폴백(Auto 단위)");

        return new EffectMeta(label, EffectCategory.Unknown, EffectUnit.Auto, "unknown", isFallback: true);
    }

    /// <summary>등록 여부(폴백 없이).</summary>
    public static bool IsRegistered(string effectType)
    {
        EnsureInitialized();
        return !string.IsNullOrEmpty(effectType) && _table.ContainsKey(effectType);
    }

    private static void Register(string effectType, string label, EffectCategory cat, EffectUnit unit, string iconKey)
        => _table[effectType] = new EffectMeta(label, cat, unit, iconKey);

    private static void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;
        RegisterAll();
    }

    /// <summary>전체 등록. effectType은 ItemEffectRegistry와 1:1로 맞춘다.</summary>
    private static void RegisterAll()
    {
        // ── 패시브 스탯(Ratio: % 스탯) ─────────────────────────────
        Register("MoveSpeed",                   "이동 속도",            EffectCategory.Utility, EffectUnit.Ratio,   "speed");
        Register("AllDamage",                   "모든 피해",            EffectCategory.Attack,  EffectUnit.Auto,    "dmg");
        Register("AttackDamage",                "공격 피해",            EffectCategory.Attack,  EffectUnit.Ratio,   "atk");
        Register("AttackSpeed",                 "공격 속도",            EffectCategory.Attack,  EffectUnit.Ratio,   "atkspeed");
        Register("RollCooldown",                "구르기 쿨다운",        EffectCategory.Utility, EffectUnit.Ratio,   "roll");
        Register("RollDistance",                "구르기 거리",          EffectCategory.Utility, EffectUnit.Ratio,   "roll");
        Register("RangedRange",                 "원거리 사거리",        EffectCategory.Attack,  EffectUnit.Ratio,   "range");
        Register("SkillCooldownReduction",      "스킬 쿨다운 감소",     EffectCategory.Utility, EffectUnit.Ratio,   "cooldown");
        Register("ActiveItemCooldownReduction", "액티브 쿨다운 감소",   EffectCategory.Utility, EffectUnit.Ratio,   "cooldown");
        Register("HealingReceived",             "받는 회복량",          EffectCategory.Resource,EffectUnit.Ratio,   "heal");
        Register("DebuffResistance",            "디버프 저항",          EffectCategory.Defense, EffectUnit.Ratio,   "shield");
        Register("DamageReduction",             "피해 감소",            EffectCategory.Defense, EffectUnit.Ratio,   "def");
        Register("AllStats",                    "모든 능력치",          EffectCategory.Attack,  EffectUnit.Ratio,   "allstats");
        Register("AllElementBonus",             "모든 속성 피해",       EffectCategory.Attack,  EffectUnit.Ratio,   "element");
        Register("SpecialRoomChance",           "특수방 확률",          EffectCategory.Utility, EffectUnit.Ratio,   "utility");
        Register("HighGradeItemChance",         "고급 아이템 확률",     EffectCategory.Utility, EffectUnit.Ratio,   "utility");
        Register("DebuffDuration",              "디버프 지속",          EffectCategory.Attack,  EffectUnit.Ratio,   "utility");
        Register("GoldGain",                    "골드 획득",            EffectCategory.Resource,EffectUnit.Ratio,   "gold");
        Register("ShopRoomChance",              "상점방 확률",          EffectCategory.Utility, EffectUnit.Ratio,   "gold");
        Register("SkillDamage",                 "스킬 피해",            EffectCategory.Attack,  EffectUnit.Ratio,   "skill");
        Register("CritChance",                  "치명타 확률",          EffectCategory.Attack,  EffectUnit.Ratio,   "crit");
        Register("CritDamage",                  "치명타 피해",          EffectCategory.Attack,  EffectUnit.Ratio,   "critdmg");
        Register("DefensePercent",              "방어력",               EffectCategory.Defense, EffectUnit.Ratio,   "def");
        Register("MaxHPPercent",                "최대 체력",            EffectCategory.Defense, EffectUnit.Ratio,   "hp");

        // ── 패시브 스탯(Flat/정수) ─────────────────────────────────
        Register("Defense",                     "방어력",               EffectCategory.Defense, EffectUnit.FlatInt, "def");
        Register("MaxHP",                       "최대 체력",            EffectCategory.Defense, EffectUnit.FlatInt, "hp");
        Register("Luck",                        "행운",                 EffectCategory.Resource,EffectUnit.FlatInt, "luck");
        Register("ConsumableSlot",              "소비 슬롯",            EffectCategory.Utility, EffectUnit.FlatInt, "utility");
        Register("ProjectilePierce",            "투사체 관통",          EffectCategory.Attack,  EffectUnit.FlatInt, "projectile");
        Register("ProjectileCount",             "투사체 수",            EffectCategory.Attack,  EffectUnit.FlatInt, "projectile");

        // ── 조건부/타임드 동적 스탯(value=버프량, trigger=조건) ────
        Register("CondAttackPercent",           "공격력↑(조건)",        EffectCategory.Proc,    EffectUnit.Ratio,   "atk");
        Register("CondDefensePercent",          "방어력↑(조건)",        EffectCategory.Proc,    EffectUnit.Ratio,   "def");
        Register("CondAttackSpeed",             "공격속도↑(조건)",      EffectCategory.Proc,    EffectUnit.Ratio,   "atkspeed");
        Register("CondMoveSpeed",               "이동속도↑(조건)",      EffectCategory.Proc,    EffectUnit.Ratio,   "speed");
        Register("CondCritChance",              "치명타확률↑(조건)",    EffectCategory.Proc,    EffectUnit.Ratio,   "crit");
        Register("CondCritDamage",              "치명타피해↑(조건)",    EffectCategory.Proc,    EffectUnit.Ratio,   "critdmg");
        Register("CondSkillDamage",             "스킬피해↑(조건)",      EffectCategory.Proc,    EffectUnit.Ratio,   "skill");
        Register("CondAllDamage",               "모든피해↑(조건)",      EffectCategory.Proc,    EffectUnit.Ratio,   "dmg");
        Register("CondMaxHpPercent",            "최대체력↑(조건)",      EffectCategory.Proc,    EffectUnit.Ratio,   "hp");
        Register("FirstHitBonus",               "첫 타격 보너스",       EffectCategory.Proc,    EffectUnit.Ratio,   "dmg");

        // ── OnHit(적중 시) — value=발동확률/회복비율/회복량 혼재 ──
        Register("Lifesteal",                   "흡혈",                 EffectCategory.Resource,EffectUnit.Ratio,   "lifesteal");
        Register("PoisonOnHit",                 "독 부여",              EffectCategory.Proc,    EffectUnit.Chance,  "poison");
        Register("Freeze",                      "빙결",                 EffectCategory.Proc,    EffectUnit.Chance,  "freeze");
        Register("ExtraAttack",                 "추가 타격",            EffectCategory.Proc,    EffectUnit.Chance,  "dmg");
        Register("TeleportSwap",                "위치 교체",            EffectCategory.Proc,    EffectUnit.Chance,  "utility");
        Register("HPRegenOnHit",                "적중 회복",            EffectCategory.Resource,EffectUnit.FlatInt, "heal");
        Register("Petrify",                     "석화",                 EffectCategory.Proc,    EffectUnit.Chance,  "stun");
        Register("Stun",                        "기절",                 EffectCategory.Proc,    EffectUnit.Chance,  "stun");

        // ── OnTakeDamage(피격 시) ─────────────────────────────────
        Register("DamageNegate",                "피해 무효",            EffectCategory.Defense, EffectUnit.Chance,  "shield");
        Register("DamageReflect",               "피해 반사",            EffectCategory.Defense, EffectUnit.Ratio,   "shield");
        Register("FireReflect",                 "화염 반사",            EffectCategory.Defense, EffectUnit.Ratio,   "fire");
        Register("DefenseOnHit",                "피격 시 방어",         EffectCategory.Defense, EffectUnit.Auto,    "def");
        Register("ExtraDamageOnHit",            "피격 시 추가 피해",    EffectCategory.Proc,    EffectUnit.Auto,    "dmg");

        // ── OnKill(처치 시) ───────────────────────────────────────
        Register("GoldOnKill",                  "처치 골드",            EffectCategory.Resource,EffectUnit.FlatInt, "gold");
        Register("FullHealOnKill",              "처치 시 완전 회복",    EffectCategory.Resource,EffectUnit.Chance,  "heal");

        // ── OnClear(방/보스 클리어) ───────────────────────────────
        Register("HPRegenOnClear",              "클리어 회복",          EffectCategory.Resource,EffectUnit.FlatInt, "heal");
        Register("BossDropItem",                "보스 추가 드랍",       EffectCategory.Utility, EffectUnit.None,    "utility");
        Register("BuffRefreshOnBoss",           "보스전 버프 갱신",     EffectCategory.Utility, EffectUnit.None,    "utility");

        // ── OnRoll(회피 시) ───────────────────────────────────────
        Register("FirstAttackAfterRoll",        "회피 후 첫 공격",      EffectCategory.Proc,    EffectUnit.Ratio,   "dmg");
        Register("RollLandingDamage",           "구르기 착지 피해",     EffectCategory.Proc,    EffectUnit.Auto,    "dmg");

        // ── OnDeath(사망 시) ──────────────────────────────────────
        Register("ReviveHeal",                  "부활 회복",            EffectCategory.Resource,EffectUnit.Ratio,   "heal");
        Register("DeathNegate",                 "죽음 무효",            EffectCategory.Defense, EffectUnit.None,    "shield");

        // ── OnRecipe(시너지 시) ───────────────────────────────────
        Register("HPRegenOnRecipe",             "시너지 회복",          EffectCategory.Resource,EffectUnit.FlatInt, "heal");
        Register("AllDamageOnRecipe",           "시너지 피해 증가",     EffectCategory.Proc,    EffectUnit.Ratio,   "dmg");
        Register("RecipeSynergyNextAttack",     "시너지 다음 공격",     EffectCategory.Proc,    EffectUnit.Ratio,   "dmg");
        Register("SkillCooldownFlat",           "스킬 쿨다운 감소",     EffectCategory.Utility, EffectUnit.Duration,"cooldown");
        Register("DefensePermStack",            "영구 방어 중첩",       EffectCategory.Defense, EffectUnit.FlatInt, "def");

        // ── OnRoomEnter / OnBossEnter(방 진입 시) ─────────────────
        Register("HPRegenOnBossEnter",          "보스 진입 회복",       EffectCategory.Resource,EffectUnit.FlatInt, "heal");
        Register("MaxHPDecreasePerRoom",        "방마다 최대체력 감소", EffectCategory.Defense, EffectUnit.Auto,    "hp");

        // ── OnSkill(스킬 시) ──────────────────────────────────────
        Register("FireExplosionOnSkill",        "스킬 화염 폭발",       EffectCategory.Proc,    EffectUnit.Auto,    "fire");
        Register("LightningOnSkill",            "스킬 번개",            EffectCategory.Proc,    EffectUnit.Auto,    "lightning");

        // ── 특수 ──────────────────────────────────────────────────
        Register("PoisonApple",                 "독사과",               EffectCategory.Special, EffectUnit.Auto,    "poison");
        Register("RandomElement",               "랜덤 속성",            EffectCategory.Special, EffectUnit.None,    "element");
        Register("ItemGradeUp",                 "아이템 등급 상승",     EffectCategory.Special, EffectUnit.None,    "special");
        Register("Heal",                        "회복",                 EffectCategory.Resource,EffectUnit.FlatInt, "heal");

        // ── T3/T4 잔첨형 ──────────────────────────────────────────
        Register("StackDamagePerTarget",        "대상별 피해 중첩",     EffectCategory.Special, EffectUnit.Auto,    "dmg");
        Register("EchoStrike",                  "메아리 타격",          EffectCategory.Special, EffectUnit.Auto,    "dmg");
        Register("TargetVulnStack",             "취약 중첩",            EffectCategory.Special, EffectUnit.Auto,    "dmg");
        Register("SplitStrike",                 "분열 타격",            EffectCategory.Special, EffectUnit.Auto,    "dmg");
        Register("EchoArrow",                   "메아리 화살",          EffectCategory.Special, EffectUnit.Auto,    "projectile");
        Register("MarkExplode",                 "표식 폭발",            EffectCategory.Special, EffectUnit.Auto,    "dmg");
        Register("LifestealStack",              "흡혈 중첩",            EffectCategory.Special, EffectUnit.Auto,    "lifesteal");
        Register("RepeatChance",                "반복 발동",            EffectCategory.Special, EffectUnit.Chance,  "dmg");
        Register("CritMomentum",                "치명 가속",            EffectCategory.Special, EffectUnit.Auto,    "crit");
        Register("BrandChain",                  "낙인 연쇄",            EffectCategory.Special, EffectUnit.Auto,    "dmg");

        // ── T3/T4 광폭형 ──────────────────────────────────────────
        Register("TimedEmpowerNext",            "시간 강화",            EffectCategory.Special, EffectUnit.Auto,    "dmg");
        Register("HpThresholdAoE",              "저체력 광역",          EffectCategory.Special, EffectUnit.Auto,    "dmg");
        Register("SkillReadyEmpower",           "스킬 준비 강화",       EffectCategory.Special, EffectUnit.Auto,    "skill");
        Register("DamageAccumEmpower",          "피해 누적 강화",       EffectCategory.Special, EffectUnit.Auto,    "dmg");
        Register("LastBreath",                  "최후의 일격",          EffectCategory.Special, EffectUnit.Auto,    "dmg");
        Register("GuaranteedCrit",              "확정 치명타",          EffectCategory.Special, EffectUnit.Auto,    "crit");
        Register("ChargeWhileIdle",             "대기 충전",            EffectCategory.Special, EffectUnit.Auto,    "dmg");
        Register("CounterShockwave",            "반격 충격파",          EffectCategory.Special, EffectUnit.Auto,    "dmg");
        Register("SkillIdleEmpower",            "스킬 대기 강화",       EffectCategory.Special, EffectUnit.Auto,    "skill");
        Register("DamageAccumPenetrate",        "피해 누적 관통",       EffectCategory.Special, EffectUnit.Auto,    "dmg");

        // ── T3/T4 타이밍형 ────────────────────────────────────────
        Register("JustGuard",                   "저스트 가드",          EffectCategory.Special, EffectUnit.Auto,    "shield");
        Register("AttackInterrupt",             "공격 차단",            EffectCategory.Special, EffectUnit.Auto,    "stun");
        Register("SkillCdReset",                "스킬 쿨 초기화",       EffectCategory.Special, EffectUnit.Chance,  "cooldown");
        Register("DoubleHitTiming",             "타이밍 더블히트",      EffectCategory.Special, EffectUnit.Auto,    "dmg");
        Register("RoomEntryWindow",             "방 진입 윈도우",       EffectCategory.Special, EffectUnit.Auto,    "dmg");
        Register("NoHitThenCrit",               "무피격 치명",          EffectCategory.Special, EffectUnit.Auto,    "crit");
        Register("ExecuteBonus",                "처형",                 EffectCategory.Special, EffectUnit.Auto,    "dmg");
        Register("CombatStartWindow",           "전투 시작 윈도우",     EffectCategory.Special, EffectUnit.Auto,    "dmg");
        Register("StationaryRangeBuff",         "정지 사거리 강화",     EffectCategory.Special, EffectUnit.Auto,    "range");
        Register("SkillCastGuard",              "시전 중 보호",         EffectCategory.Special, EffectUnit.Auto,    "shield");

        // ── T3/T4 형태변형 ────────────────────────────────────────
        Register("MeleeMultiHit",               "근접 다단 히트",       EffectCategory.Special, EffectUnit.FlatInt, "dmg");
        Register("MeleeRangeExtend",            "근접 사거리 증가",     EffectCategory.Special, EffectUnit.Auto,    "range");
        Register("SkillProjectileCount",        "스킬 투사체 수",       EffectCategory.Special, EffectUnit.FlatInt, "projectile");
        Register("MeleeShapeCircle",            "근접 원형 범위",       EffectCategory.Special, EffectUnit.None,    "dmg");

        Debug.Log($"[EffectMetaRegistry] {_table.Count}개 effectType 메타 등록 완료");
    }
}
