using UnityEngine;

/// <summary>
/// 6속성 공용 열거형. 룬/상태이상/이펙트 매칭의 단일 키.
/// (효과 로직은 effect_type 문자열을 계속 쓰고, 이 enum은 VFX 매핑 전용이다.)
/// </summary>
public enum RuneElement
{
    Fire,      // 불 — 점화/화상
    Ice,       // 얼음 — 서리/빙결/분쇄
    Electric,  // 번개 — 감전/기절 (에셋 테마: Storm)
    Grass,     // 독 — 독안개/취약 (에셋 테마: Nature)
    Light,     // 빛 — 성역/치명
    Dark,      // 어둠 — 침식/낙인
}

/// <summary>
/// 6속성 색 팔레트 — 빔·데미지 텍스트·아이콘이 공유하는 단일 출처.
///
/// 속성 표기는 단색이 아니라 <b>세로 그라데이션</b>(Bright=밝은 심지 → Deep=짙은 가장자리)으로 준다.
/// DamagePopup이 치명타를 "색이 아니라 재질"로 구분하는 것과 같은 언어를 속성에도 적용한 것 —
/// 단색이면 그냥 "색 있는 숫자"로 읽히지만, 그라데이션은 원소가 타오르는 질감으로 읽힌다.
/// </summary>
public static class ElementPalette
{
    /// <summary>단색(빔/라인/아이콘용) — 그 속성의 대표 색.</summary>
    public static Color Core(RuneElement e) => e switch
    {
        RuneElement.Fire     => new Color(1.00f, 0.55f, 0.18f, 1f),
        RuneElement.Ice      => new Color(0.55f, 0.85f, 1.00f, 1f),
        RuneElement.Electric => new Color(1.00f, 0.95f, 0.50f, 1f),
        RuneElement.Grass    => new Color(0.55f, 0.95f, 0.40f, 1f),
        RuneElement.Light    => new Color(1.00f, 0.97f, 0.70f, 1f),
        RuneElement.Dark     => new Color(0.70f, 0.40f, 1.00f, 1f),
        _                    => Color.white,
    };

    /// <summary>그라데이션 위쪽 — 가장 뜨겁고 밝은 심지.</summary>
    public static Color Bright(RuneElement e) => e switch
    {
        RuneElement.Fire     => new Color(1.00f, 0.88f, 0.42f, 1f),   // 백열에 가까운 주황
        RuneElement.Ice      => new Color(0.88f, 0.99f, 1.00f, 1f),   // 서릿빛 흰
        RuneElement.Electric => new Color(1.00f, 1.00f, 0.80f, 1f),   // 방전 순간의 백색
        RuneElement.Grass    => new Color(0.88f, 1.00f, 0.58f, 1f),   // 독기 어린 연둣빛
        RuneElement.Light    => new Color(1.00f, 1.00f, 0.94f, 1f),   // 성광
        RuneElement.Dark     => new Color(0.88f, 0.74f, 1.00f, 1f),   // 보랏빛 잔광
        _                    => Color.white,
    };

    /// <summary>그라데이션 아래쪽 — 식어가는 짙은 가장자리.</summary>
    public static Color Deep(RuneElement e) => e switch
    {
        RuneElement.Fire     => new Color(1.00f, 0.26f, 0.04f, 1f),   // 적열
        RuneElement.Ice      => new Color(0.22f, 0.52f, 1.00f, 1f),   // 심해빛 파랑
        RuneElement.Electric => new Color(0.98f, 0.68f, 0.05f, 1f),   // 잔전류의 호박색
        RuneElement.Grass    => new Color(0.24f, 0.68f, 0.14f, 1f),   // 짙은 독초록
        RuneElement.Light    => new Color(1.00f, 0.80f, 0.34f, 1f),   // 금빛
        RuneElement.Dark     => new Color(0.40f, 0.10f, 0.72f, 1f),   // 심연 보라
        _                    => Color.gray,
    };
}

/// <summary>상태이상 id·효과 문자열을 6속성으로 해석하는 단일 지점.</summary>
public static class RuneElementMap
{
    /// <summary>상태이상 id → 속성. 미매칭이면 false.</summary>
    public static bool FromStatusId(string statusId, out RuneElement element)
    {
        switch (statusId)
        {
            case "ignite": case "burn":
                element = RuneElement.Fire; return true;
            case "frost": case "freeze": case "shatter":
                element = RuneElement.Ice; return true;
            case "shock": case "static": case "stun":
                element = RuneElement.Electric; return true;
            case "poison": case "item_poison": case "poison_atk":
                element = RuneElement.Grass; return true;
            // 주의: vulnerable/brand/item_mark 는 특정 속성이 아닌 범용 디버프(유물·아이템·다속성 공유)라
            //       속성 오라를 붙이지 않는다(잘못된 색 표시 방지).
            default:
                element = RuneElement.Fire; return false;
        }
    }

    /// <summary>effect_type 접두(Fire/Ice/Elec/Grass/Light/Dark) → 속성. 미매칭이면 false.</summary>
    public static bool FromEffectType(string effectType, out RuneElement element)
    {
        element = RuneElement.Fire;
        if (string.IsNullOrEmpty(effectType)) return false;
        if (effectType.StartsWith("Fire"))  { element = RuneElement.Fire;     return true; }
        if (effectType.StartsWith("Ice"))   { element = RuneElement.Ice;      return true; }
        if (effectType.StartsWith("Elec"))  { element = RuneElement.Electric; return true; }
        if (effectType.StartsWith("Grass")) { element = RuneElement.Grass;    return true; }
        if (effectType.StartsWith("Light")) { element = RuneElement.Light;    return true; }
        if (effectType.StartsWith("Dark"))  { element = RuneElement.Dark;     return true; }
        return false;
    }
}
