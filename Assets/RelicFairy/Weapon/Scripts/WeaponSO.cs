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

    [Header("등급/티어")]
    [SerializeField, Tooltip("아이템 추첨 시스템과 통합된 등급. 기본 매핑: T1=Common, T2=Rare, T3=Epic, T4=Legendary")]
    private ItemRarity rarity = ItemRarity.Common;
    public ItemRarity Rarity => rarity;

    public float baseAttack;
    public float baseDefense;

    [Header("전투 스탯")]
    public float attackSpeed = 1f;    // 초당 공격 횟수
    public float attackRange = 1f;    // 공격 사거리 (m)
    public float areaOfEffect = 1f;   // 공격 범위 (m)

    [Header("치명타")]
    public float critChance = 25f;    // % (0~100)
    public float critDamage = 1.25f;  // 배율 (1.25 = +25%)

    [Header("콤보 정보")]
    public int groundEndCount;
    public int airEndCount;

    [Header("애니메이션")]
    public WeaponAnimationSetSO animationSet;

    [Header("Ability Set")]
    public WeaponAbilitySetSO abilitySet;

    [Header("트레일 VFX")]
    [Tooltip("공격 시 칼날에 표시할 트레일 VFX 프리팹(INab Weapon Trail). 비우면 트레일 스킵. PlayerWeaponTrailVfx가 읽음.")]
    public GameObject trailVfxPrefab;

    [Header("타입 & 정책")]
    public WeaponType weaponType = WeaponType.Katana;

    [Header("진화 (파생 분기)")]
    [Tooltip("이 무기가 진화할 수 있는 분기 목록. 비우면 최종 형태(더 이상 진화 불가). " +
             "진화하면 대상 WeaponSO로 통째 교체된다 — 외형·무브셋·스킬·아이콘·이름·타입·스탯 전부.")]
    public WeaponEvolutionSO evolution;

    [Header("스킬")]
    public SkillSO skillQ;
    public SkillSO skillE;

    [Header("차지/강화 공격 설정")]
    public float holdThreshold;
    public PromoteMode promoteMode = PromoteMode.None;    // None, Stage, ChargeFull
    public int chargeStages = 1;                          // 차지 공격 단계 수

}
