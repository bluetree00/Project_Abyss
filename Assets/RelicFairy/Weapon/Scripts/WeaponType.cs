public enum WeaponType
{
    None,
    [System.Obsolete("Sword는 Katana/Greatsword로 분리됨. 신규 사용 금지.")]
    Sword = 1,   // 직렬화 호환용 유지
    Bow,         // 보우 — 원거리
    Staff,
    Katana,      // 카타나 — 빠른 근접
    Greatsword,  // 양날검 — 느린 강공격
    Crossbow,    // 석궁 — 원거리
}
