/// <summary>
/// 무기 원소 속성.
/// 상성: Water > Fire > Grass > Earth > Lightning > Water
/// </summary>
public enum WeaponElement
{
    None,       // 무속성
    Water,      // 물
    Fire,       // 불
    Grass,      // 풀
    Earth,      // 땅
    Lightning,  // 번개
}

/// <summary>
/// 원소 상성 판정 유틸리티.
/// </summary>
public static class ElementRelation
{
    /// <summary>attacker 원소가 defender 원소에 유리한지.</summary>
    public static bool IsStrong(WeaponElement attacker, WeaponElement defender)
    {
        if (attacker == WeaponElement.None || defender == WeaponElement.None) return false;

        return attacker switch
        {
            WeaponElement.Water     => defender == WeaponElement.Fire,
            WeaponElement.Fire      => defender == WeaponElement.Grass,
            WeaponElement.Grass     => defender == WeaponElement.Earth,
            WeaponElement.Earth     => defender == WeaponElement.Lightning,
            WeaponElement.Lightning => defender == WeaponElement.Water,
            _                       => false,
        };
    }

    /// <summary>attacker 원소가 defender 원소에 불리한지.</summary>
    public static bool IsWeak(WeaponElement attacker, WeaponElement defender)
    {
        return IsStrong(defender, attacker);
    }

    /// <summary>
    /// trigger 문자열 → 요구 원소 반환. 원소 조건이 아니면 None.
    /// </summary>
    public static WeaponElement TriggerToElement(string trigger) => trigger switch
    {
        "WithWaterWeapon"     => WeaponElement.Water,
        "WithFireWeapon"      => WeaponElement.Fire,
        "WithGrassWeapon"     => WeaponElement.Grass,
        "WithEarthWeapon"     => WeaponElement.Earth,
        "WithLightningWeapon" => WeaponElement.Lightning,
        // WithMagicWeapon은 원소가 아니라 무기 종류(Staff) 조건 — 여기서 제외
        _                     => WeaponElement.None,
    };

    /// <summary>trigger가 원소 조건인지 여부.</summary>
    public static bool IsElementTrigger(string trigger) => TriggerToElement(trigger) != WeaponElement.None;

    /// <summary>WeaponElement → ElementType 변환.</summary>
    public static ElementType ToElementType(this WeaponElement we) => we switch
    {
        WeaponElement.Water     => ElementType.Water,
        WeaponElement.Fire      => ElementType.Fire,
        WeaponElement.Grass     => ElementType.Grass,
        WeaponElement.Earth     => ElementType.Earth,
        WeaponElement.Lightning => ElementType.Lightning,
        _                       => ElementType.None,
    };
}
