using UnityEngine;

/// <summary>
/// 유물 클래스 정의(정적 데이터). 몸은 CombatGirl 고정, 유물이 패시브 + 고유스킬 + 외형(오라)만 부여.
/// 스탯은 부여하지 않는다(베이스 스탯은 CombatGirl 공통).
/// 코드가 필요한 패시브/스킬 로직은 <see cref="RelicId"/> 기반 팩토리(RelicRegistry)에서 처리한다.
/// </summary>
[CreateAssetMenu(fileName = "RelicClass", menuName = "RelicFairy/Relic Class")]
public class RelicClassSO : ScriptableObject
{
    [Header("식별")]
    [SerializeField] private RelicId id;
    [SerializeField] private string displayName;
    [SerializeField, TextArea] private string loreDesc;

    [Header("선택 UI")]
    [SerializeField] private Sprite portrait;
    [SerializeField, Tooltip("유물 선택 팝업 전신 일러스트")] private Sprite rosterIllust;

    [Header("패시브 (순수 데이터). 코드형 패시브는 RelicId 팩토리에서 등록")]
    [SerializeField] private PassiveSO[] passives;

    [Header("고유 스킬 (Q)")]
    [SerializeField, Tooltip("Q스킬 'QSkill_01' state 클립으로 쓸 Addressables 키")]
    private string qSkillClipKey;
    [SerializeField, Tooltip("Q 입력 시 카메라 연출. 비우면 연출 생략")]
    private UltimateCinematicConfig qSkillCinematic;

    [Header("스탯 (공통 CombatGirl 베이스 위에 가산)")]
    [SerializeField] private StatModifier[] stats;

    [Header("외형 (초기엔 오라만)")]
    [SerializeField, Tooltip("상시 오라 VFX Addressables 키. 비우면 없음")]
    private string auraVfxKey;
    [SerializeField, Tooltip("오라를 붙일 본/소켓 이름 (비우면 루트)")]
    private string auraSocket;

    public RelicId Id => id;
    public string DisplayName => displayName;
    public string LoreDesc => loreDesc;
    public Sprite Portrait => portrait;
    public Sprite RosterIllust => rosterIllust;
    public PassiveSO[] Passives => passives;
    public StatModifier[] Stats => stats;
    public string QSkillClipKey => qSkillClipKey;
    public UltimateCinematicConfig QSkillCinematic => qSkillCinematic;
    public string AuraVfxKey => auraVfxKey;
    public string AuraSocket => auraSocket;
}
