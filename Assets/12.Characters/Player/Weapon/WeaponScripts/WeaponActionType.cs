// ActionType: 애니 이벤트가 어떤 공격 그룹을 가리키는지
public enum WeaponActionType {
    None = 0,
    Light,   // ground light attack (콤보 리스트)
    Heavy,   // heavy attack
    QSkill,  // Q
    ESkill,  // E
    Air,     // 공중 공격
    Dodge,
    // 필요시 확장
}
