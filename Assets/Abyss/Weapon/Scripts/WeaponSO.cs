using UnityEngine;

[CreateAssetMenu(menuName = "Game/WeaponSO")]
public class WeaponSO : ScriptableObject
{
    [Header("기본 정보")]
    public string weaponDisplayKey;
    public string weaponPrefabKey;  // Addressables 무기 프리팹 키
    public string displayName;
    public string iconKey;          // Addressables 비동기 로드용 키
    public Sprite icon;             // HUD 즉시 표시용 직접 참조
    [Tooltip("장비 티어 (1~3). 스킬 해금 단계에 사용")]
    public int tier = 1;
    public float baseAttack;
    public float baseDefense;

    [Header("전투 스탯")]
    public float attackSpeed = 1f;    // 초당 공격 횟수
    public float attackRange = 1f;    // 공격 사거리 (m)
    public float areaOfEffect = 1f;   // 공격 범위 (m)

    [Header("콤보 정보")]
    public int groundEndCount;
    public int airEndCount;

    [Header("애니메이션")]
    public WeaponAnimationSetSO animationSet;

    [Header("Ability Set")]
    public WeaponAbilitySetSO abilitySet;

    [Header("타입 & 정책")]
    public WeaponType weaponType = WeaponType.Katana;

    [Header("스킬")]
    public SkillSO skillQ;
    public SkillSO skillE;

    [Header("차지/강화 공격 설정")]
    public float holdThreshold;
    public PromoteMode promoteMode = PromoteMode.None;    // None, Stage, ChargeFull
    public int chargeStages = 1;                          // 차지 공격 단계 수

}
