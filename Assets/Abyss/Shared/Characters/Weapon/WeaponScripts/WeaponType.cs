public enum WeaponType
{
    None,
    Sword,
    Bow,
    // 나중에 추가 가능: Axe, Dagger, Staff 등
}

/// <summary>
/// 무기 슬롯 타입 — 슬롯 인덱스와 1:1 대응 (Main=0, Sub=1)
/// </summary>
public enum WeaponSlotType
{
    Main = 0,
    Sub  = 1,
}
