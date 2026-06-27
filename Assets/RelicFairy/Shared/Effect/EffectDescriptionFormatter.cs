using System.Globalization;
using UnityEngine;

/// <summary>
/// 한 효과의 표시용 결과. 호출처는 필요한 조각만 골라 쓴다.
/// </summary>
public readonly struct EffectDisplay
{
    public readonly string Label;        // 한글 라벨 (예: "공격 속도")
    public readonly string ValueText;    // 수치+단위 (예: "+4%", "20% 확률", "2.0초", "")
    public readonly string TriggerText;  // 조건 한글 (예: "적중 시"). Always/없음이면 ""
    public readonly EffectCategory Category;
    public readonly EffectUnit Unit;
    public readonly string IconKey;
    public readonly Color Color;         // 기존 컨벤션(이로움=옅은 청백 / 해로움=적색)
    public readonly bool IsRisk;
    public readonly bool IsFallback;     // 미등록 effectType 폴백 여부

    public EffectDisplay(string label, string valueText, string triggerText, EffectCategory category,
                         EffectUnit unit, string iconKey, Color color, bool isRisk, bool isFallback)
    {
        Label = label;
        ValueText = valueText;
        TriggerText = triggerText;
        Category = category;
        Unit = unit;
        IconKey = iconKey;
        Color = color;
        IsRisk = isRisk;
        IsFallback = isFallback;
    }

    /// <summary>"라벨 +4%" 형태(트리거 제외). 수치 없으면 라벨만.</summary>
    public string LabelWithValue
        => string.IsNullOrEmpty(ValueText) ? Label : $"{Label} {ValueText}";

    /// <summary>"라벨 +4% (적중 시)" 형태. 가장 완전한 한 줄.</summary>
    public string Combined
    {
        get
        {
            string core = LabelWithValue;
            return string.IsNullOrEmpty(TriggerText) ? core : $"{core} ({TriggerText})";
        }
    }
}

/// <summary>
/// 효과 설명 공통 포맷터(표시 전용 레이어 A). 모든 정적 효과 설명 지점의 단일 진입점.
///
/// ■ 단위 진실 원천: <see cref="EffectMetaRegistry"/>의 Unit으로 "+4"(Flat) vs "+400%"(Ratio) 불일치 제거.
/// ■ 동작/밸런스 무변경 — value를 표기만 한다.
/// ■ 색은 기존 컨벤션(이로움/해로움 듀오톤) 유지. Category는 3단계 아이콘 매핑용으로 노출만.
/// </summary>
public static class EffectDescriptionFormatter
{
    // 기존 호출처들이 공통으로 쓰던 색(ItemInfoPanel/UI_ItemAcquisitionPopup과 동일 값).
    public static readonly Color NormalColor = new(0.85f, 0.92f, 1f, 1f);
    public static readonly Color RiskColor   = new(1f, 0.35f, 0.35f, 1f);

    // ── 아이템 효과 슬롯 ─────────────────────────────────────────

    /// <summary>아이템 효과 슬롯 1개 → 표시 결과.</summary>
    public static EffectDisplay Describe(ItemEffectSlot slot)
    {
        if (slot == null)
            return Describe(string.Empty, 0f, null);
        return Describe(slot.effectType, slot.value, slot.trigger);
    }

    /// <summary>raw 필드 → 표시 결과.</summary>
    public static EffectDisplay Describe(string effectType, float value, string trigger)
    {
        var meta = EffectMetaRegistry.Get(effectType);

        bool isRisk = DetectRisk(effectType, value, meta.Unit);
        string valueText = FormatValue(meta.Unit, value);
        string triggerText = TriggerLabel(trigger);
        Color color = isRisk ? RiskColor : NormalColor;

        return new EffectDisplay(meta.Label, valueText, triggerText, meta.Category, meta.Unit,
                                 meta.IconKey, color, isRisk, meta.IsFallback);
    }

    // ── 룸 버프(HUD) — StatType 기반 ─────────────────────────────

    /// <summary>
    /// 룸 버프 한 줄 포맷(CombatPanelView.FormatBuff 수렴용).
    /// 버프는 IsPercent 정보를 자체 보유하므로 그것으로 단위를 결정한다.
    /// </summary>
    public static string FormatStatBuff(StatType type, float value, bool isPercent, int roomsRemaining)
    {
        string label = StatTypeLabel(type);
        string valueStr = isPercent ? FormatValue(EffectUnit.Ratio, value) : FormatValue(EffectUnit.Flat, value);
        string remain = roomsRemaining > 0 ? $" [{roomsRemaining}방]" : "";
        return $"{label} {valueStr}{remain}";
    }

    /// <summary>룸 버프(StatType) → 아이콘 키. EffectMetaRegistry IconKey 어휘와 동일.</summary>
    public static string IconKeyForStat(StatType type)
    {
        switch (type)
        {
            case StatType.AttackPower:                 return "atk";
            case StatType.MeleeAttack:                 return "atk";
            case StatType.RangedAttack:                return "range";
            case StatType.Defense:                     return "def";
            case StatType.MaxHp:                        return "hp";
            case StatType.MoveSpeed:                    return "speed";
            case StatType.AttackSpeed:                  return "atkspeed";
            case StatType.SkillCooldownReduction:      return "cooldown";
            case StatType.ActiveItemCooldownReduction: return "cooldown";
            case StatType.Luck:                         return "luck";
            case StatType.Projectile:                   return "projectile";
            case StatType.InstantDamage:                return "dmg";
            case StatType.CritChance:                   return "crit";
            case StatType.CritDamage:                   return "critdmg";
            default:                                    return "unknown";
        }
    }

    // ── 수치 포맷(단위 일원화 핵심) ──────────────────────────────

    /// <summary>단위 종류에 맞춰 value를 문자열로. 부호(+) 포함.</summary>
    public static string FormatValue(EffectUnit unit, float value)
    {
        var ci = CultureInfo.InvariantCulture;
        string sign = value >= 0f ? "+" : "";

        switch (unit)
        {
            case EffectUnit.None:
                return string.Empty;

            case EffectUnit.Ratio:
                return $"{sign}{(value * 100f).ToString("0.#", ci)}%";

            case EffectUnit.Flat:
                return $"{sign}{value.ToString("0.#", ci)}";

            case EffectUnit.FlatInt:
                return $"{sign}{Mathf.RoundToInt(value).ToString(ci)}";

            case EffectUnit.Duration:
                return $"{value.ToString("0.#", ci)}초";

            case EffectUnit.Chance:
                return $"{(value * 100f).ToString("0.#", ci)}% 확률";

            case EffectUnit.Auto:
            default:
                // AllDamage 휴리스틱: |value|>=1 → 절댓값, 아니면 비율.
                return Mathf.Abs(value) >= 1f
                    ? $"{sign}{value.ToString("0.#", ci)}"
                    : $"{sign}{(value * 100f).ToString("0.#", ci)}%";
        }
    }

    // ── 색 헬퍼(리치텍스트용) ────────────────────────────────────

    public static string ToHex(Color c)
        => $"#{ColorUtility.ToHtmlStringRGB(c)}";

    // ── 내부 ─────────────────────────────────────────────────────

    private static bool DetectRisk(string effectType, float value, EffectUnit unit)
    {
        if (!string.IsNullOrEmpty(effectType))
        {
            string lower = effectType.ToLowerInvariant();
            if (lower.Contains("damage_taken") || lower.Contains("risk") || lower.Contains("penalty") ||
                lower.Contains("decrease"))
                return true;
        }
        // 확률/지속/표기없음은 음수여도 해로움으로 단정하지 않음(부호가 페널티를 뜻하지 않을 수 있음).
        if (unit == EffectUnit.Ratio || unit == EffectUnit.Flat || unit == EffectUnit.FlatInt || unit == EffectUnit.Auto)
            return value < 0f;
        return false;
    }

    private static string TriggerLabel(string trigger)
    {
        if (string.IsNullOrEmpty(trigger)) return string.Empty;
        switch (trigger)
        {
            case "Always":        return string.Empty;
            case "OnHit":         return "적중 시";
            case "OnKill":        return "처치 시";
            case "OnUse":         return "사용 시";
            case "OnLowHp":       return "저체력 시";
            case "OnClear":       return "방 클리어 시";
            case "OnRoll":        return "회피 시";
            case "OnDodge":       return "회피 시";
            case "OnCrit":        return "치명타 시";
            case "OnSkill":       return "스킬 시";
            case "OnDamaged":
            case "OnTakeDamage":  return "피격 시";
            case "OnRecipe":      return "시너지 시";
            case "OnDeath":       return "사망 시";
            case "OnRevive":      return "부활 시";
            case "OnBossEnter":   return "보스 진입 시";
            case "OnRoomEnter":   return "방 진입 시";
            default:              return trigger; // 미지 트리거는 원문 노출(정보 손실 방지)
        }
    }

    private static string StatTypeLabel(StatType type)
    {
        switch (type)
        {
            case StatType.AttackPower:                 return "공격력";
            case StatType.MeleeAttack:                 return "근접 공격";
            case StatType.RangedAttack:                return "원거리 공격";
            case StatType.Defense:                     return "방어력";
            case StatType.MaxHp:                        return "최대 체력";
            case StatType.MoveSpeed:                    return "이동 속도";
            case StatType.AttackSpeed:                  return "공격 속도";
            case StatType.SkillCooldownReduction:      return "스킬 쿨다운 감소";
            case StatType.ActiveItemCooldownReduction: return "액티브 쿨다운 감소";
            case StatType.Luck:                         return "행운";
            case StatType.Projectile:                   return "투사체";
            case StatType.InstantDamage:                return "즉시 피해";
            case StatType.CritChance:                   return "치명타 확률";
            case StatType.CritDamage:                   return "치명타 피해";
            default:                                    return type.ToString();
        }
    }
}
