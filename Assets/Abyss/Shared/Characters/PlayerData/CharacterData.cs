using UnityEngine;

[CreateAssetMenu(fileName = "NewCharacterData", menuName = "Characters/Character Data")]
public class CharacterData : ScriptableObject
{
    // 캐릭터 기본 정보
    [Header("캐릭터 기본 정보")]
    public string characterName;

    // 캐릭터 기본 스탯
    [Header("캐릭터 기본 스탯")]
    public float baseMoveSpeed;
    public float baseRunSpeed;
    public int maxHealth;

    [Header("공격력 (근거리/원거리)")]
    public int baseMeleeAttack;
    public int baseRangedAttack;

    [Header("방어/행운")]
    public int baseDefense;
    public int baseLuck;

    // 공격, 콤보 관련 수치
    [Header("공격, 콤보 관련 수치")]
    public float comboDuration = 3.0f;

    [Header("강공격 관련 수치")]
    public float heavyAttackChargeThreshold = 1.5f;
    public float heavyAttackReleaseTime = 0.4f;

    // 대시 관련 수치
    [Header("대시 관련 수치")]
    public float dashSpeed = 10f;
    public float dashDuration = 0.2f;
    public float dodgeCooldown = 2f;

    [Header("점프 및 중력 설정")]
    public float jumpForce = 5f;
    public float gravity = -30f;
    public float fallMultiplier = 2f;

    [Header("지면 체크 및 착지 관련")]
    public float groundCheckDistance = 1f;
    public float hardLandingTimeThreshold = 0.8f;
    public LayerMask groundLayer;

    [Header("물리 이동 관련")]
    public float airControlMultiplier = 0.5f;
    public float groundDrag = 4f;
    public float airDrag = 0.5f;

    //캐릭터 클래스
    [Header("캐릭터 클래스")]
    public Define.CharacterClass conClass;

    // 캐릭터 초상화
    [Header("초상화")]
    public Sprite portrait;
    [Tooltip("캐릭터 선택 팝업에 표시할 전신 로스터 일러스트")]
    public Sprite rosterIllust;

    // 캐릭터 고유 패시브
    [Header("패시브")]
    public PassiveSO passive;

    // 씬 진입 시 등장 연출 (null이면 GameRunBootstrapper의 기본 연출 사용)
    [Header("등장 연출")]
    public PlayerEntranceBehaviourSO playerEntrance;

    // 스타트 방 캐릭터 획득 시 재생할 대사
    // CSV 우선 (시퀀스 ID: {characterPrefabKey}_Pickup), 없으면 이 SO 사용
    [Header("획득 대사")]
    [SerializeField] private DialogueSequenceSO acquisitionDialogue;
    public DialogueSequenceSO AcquisitionDialogue => acquisitionDialogue;

    // 레거시 호환 — EffectData 등에서 사용
    public int GetTotalAttackPower() => UnityEngine.Mathf.Max(baseMeleeAttack, baseRangedAttack);

    // PlayerController.InitCharacterDataAsync에서 호출
    public void Initialize() { }
}
