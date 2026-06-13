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
    [Tooltip("이동 지속 시 걷기→달리기 속도까지 점진적으로 차오르는 시간(초). 0 이하면 기본 1초 사용.")]
    public float runRampDuration = 1.0f;
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
    [Tooltip("회피 시작 후 무적이 켜지기까지의 그레이스 시간(초). 이 동안은 피격 가능. 회피 총 길이를 넘으면 자동 클램프.")]
    public float dodgeIFrameStartDelay = 0.05f;
    [Tooltip("무적 지속 시간(초). startDelay 경과 후 이만큼 무적. 회복 구간은 다시 피격 가능. 창 끝이 회피 길이를 넘으면 자동 클램프.")]
    public float dodgeIFrameDuration = 0.3f;
    [Tooltip("[실험] 회피 시작 속도에 직전 이동 속도벡터를 블렌딩하는 비율(0~1). 0=현행(블렌딩 없음). 방향 급전환 시 튐 완화용 — 에디터에서 튜닝.")]
    [Range(0f, 1f)]
    public float dodgeMomentumBlend = 0f;
    [Tooltip("대시 종료 후~다음 상태 복귀 사이의 무적 없는 짧은 회복(취약) 구간(초). 0=현행(즉시 전환). i-frame과 겹치지 않음(무적 종료 뒤). 남발 억제용 — 플레이테스트로 튜닝(예 0.05).")]
    public float dodgeRecoveryWindow = 0f;

    // 회피 연출(시각) — 전부 선택. 미할당 시 해당 효과만 무동작(DodgePresentation이 읽음).
    [Header("회피 연출 (시각 — 선택, 미할당 시 무동작)")]
    [Tooltip("잔상(afterimage) 머티리얼. 비우면 잔상 스킵. 반투명 블렌딩 머티리얼 권장.")]
    public Material dodgeGhostMaterial;
    [Tooltip("잔상 스냅샷 간격(초). i-frame 동안 이 간격으로 BakeMesh 잔상 생성.")]
    public float dodgeGhostInterval = 0.05f;
    [Tooltip("잔상 1개의 페이드아웃 수명(초). 이 동안 알파가 0으로 감소.")]
    public float dodgeGhostLifetime = 0.25f;
    [Tooltip("잔상 시작 색·알파. 수명 동안 알파가 0으로 페이드. 잔상 머티리얼의 _BaseColor를 덮어쓴다.")]
    public Color dodgeGhostColor = new Color(0.4f, 0.7f, 1f, 0.6f);
    [Tooltip("i-frame 동안 플레이어 렌더러에 입힐 틴트/발광 색. a≤0이면 틴트 스킵. (발광은 머티리얼 Emission 활성 필요)")]
    public Color dodgeIFrameTint = new Color(0f, 0f, 0f, 0f);
    [Tooltip("i-frame 틴트 발광(_EmissionColor) 강도 배수. 0이면 발광 없이 베이스 틴트만.")]
    public float dodgeIFrameTintEmission = 1.5f;
    [Tooltip("회피 시작 시 발밑에 1회 스폰할 먼지 VFX 프리팹. 비우면 스킵.")]
    public GameObject dodgeDustVfxPrefab;
    [Tooltip("먼지 VFX 발밑 높이 오프셋(m).")]
    public float dodgeDustHeightOffset = 0.05f;

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

    [Header("이동 가속 모델")]
    [Tooltip("가속도(m/s²). 목표속도를 추격하는 빠른 레이어. 0 이하면 기본값(≈0→8m/s 90ms).")]
    public float moveAccel = 90f;
    [Tooltip("정지 감속도(m/s²). 정밀 멈춤 위해 accel 이상 권장. 0 이하면 기본값(≈8→0 73ms).")]
    public float moveDecel = 110f;
    [Tooltip("역방향 입력 전환 시 가속 배율(1.5~2 권장). 0 이하면 기본 1.75.")]
    public float reverseAccelMultiplier = 1.75f;
    [Tooltip("정지→출발 첫 프레임 최소 출발속도(walkMax 비율, 0~0.15). 0이면 비활성.")]
    public float initialBoost = 0f;

    [Header("회전 (선회)")]
    [Tooltip("이동 방향으로 도는 각속도(도/초). '일정 각속도' 회전이라 프레임률 독립·점근(빙 도는 느낌) 없음. 클수록 빠릿. 0 이하면 기본 720.")]
    public float turnSpeedDegPerSec = 720f;
    [Tooltip("[P1·옵션] 입력 방향과 현재 바라보는 방향의 각도 차가 클수록 이동속도를 이 비율(0~1)만큼 감속 → 급선회 반경 축소. 0=현행(감속 없음).")]
    [Range(0f, 1f)]
    public float sharpTurnMoveSlowdown = 0f;
    [Tooltip("[P1·옵션] 입력-facing 각도 차가 이 값(도) 이상이면 즉시 스냅 회전. 180=실질 비활성(급반전만). 너무 낮추면 휙휙거림.")]
    public float snapTurnAngle = 180f;

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

    // Q스킬 필살기 카메라 연출 (null 이면 연출 없이 즉시 발동)
    [Header("Q스킬 필살기 연출")]
    [SerializeField, Tooltip("Q 입력 시 재생할 카메라 연출. 비우면 연출 생략.")]
    private UltimateCinematicConfig qSkillCinematic;
    public UltimateCinematicConfig QSkillCinematic => qSkillCinematic;

    // 캐릭터별 애니메이션 오버라이드 (AnimatorOverrideService 가 PlayerBaseController 의 state 클립을 교체)
    // 키는 Addressables 에 등록된 AnimationClip 이름과 일치해야 한다. 비워두면 기본 클립 사용.
    [Header("애니메이션 오버라이드 (Addressables 키)")]
    [SerializeField, Tooltip("Q스킬 'QSkill_01' state 클립으로 사용할 Addressables 키. 비우면 기본값 유지.")]
    private string qSkillClipKey = "";
    public string QSkillClipKey => qSkillClipKey;

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
