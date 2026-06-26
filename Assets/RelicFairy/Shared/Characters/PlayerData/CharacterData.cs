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
    [Tooltip("대시 중 표시할 트레일 VFX 프리팹(INab Weapon Trail). 비우면 대시 트레일 스킵. PlayerWeaponTrailVfx가 읽음.")]
    public GameObject dashTrailVfxPrefab;
    [Tooltip("대시 트레일 상단 앵커의 플레이어 루트 기준 로컬 오프셋(칼날이 아닌 몸을 따라감). 보통 머리/가슴 높이.")]
    public Vector3 dashTrailTopOffset = new Vector3(0f, 1.3f, 0f);
    [Tooltip("대시 트레일 하단 앵커의 플레이어 루트 기준 로컬 오프셋. 보통 발밑 높이.")]
    public Vector3 dashTrailBottomOffset = new Vector3(0f, 0.05f, 0f);

    [Header("점프 및 중력 설정")]
    public float jumpForce = 5f;
    public float gravity = -30f;
    public float fallMultiplier = 2f;

    [Header("지면 체크 및 착지 관련")]
    public float groundCheckDistance = 1f;
    public float hardLandingTimeThreshold = 0.8f;
    public LayerMask groundLayer;
    [Tooltip("낙하(점프 아님) 진입 시 발밑 낙하 높이가 이 값(m) 미만이면 추락/착지 애니를 생략하고 로코모션 유지(작은 단차·턱). 이상이면 추락 애니 재생. 0 이하면 기본 0.6.")]
    public float minFallAnimHeight = 0.6f;

    [Header("플로팅 캡슐 컨트롤러 (실험 · 기본 off)")]
    [Tooltip("켜면 접지/계단을 스프링-호버로 처리(StepClimb 대체, 위치 강제 없음). 끄면 기존 레이 접지 사용. 콜라이더 바닥을 floatRideHeight만큼 띄워야 발이 지면 안착.")]
    public bool useFloatingController = false;
    [Tooltip("캡슐 바닥과 지면 사이 유지 높이(m). 이 이하 단차를 흡수.")]
    public float floatRideHeight = 0.4f;
    [Tooltip("호버 스프링 강도(mass 50 기준). 클수록 단단.")]
    public float floatSpring = 4000f;
    [Tooltip("호버 댐퍼(진동 억제). 임계감쇠 ≈ 2·√(spring·mass) ≈ mass 50·spring 4000 기준 900.")]
    public float floatDamper = 900f;
    [Tooltip("rideHeight 아래로 추가 탐지 거리(m). 단차 하강/리프트 여유.")]
    public float floatProbeExtra = 0.3f;
    [Tooltip("계단 하강 스냅 최대 단차(m). 직전 접지 상태에서 이 이하로 지면이 낮아지면 낙하 대신 접지 유지(스프링이 따라 내려감). 이보다 크게 떨어지면 낙하. 즉 보장되는 최소 단차 처리.")]
    public float floatStepDownDistance = 0.5f;

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
    [Tooltip("급반전(sharpTurnAngle 이상) 시 '마찰 제동' — 이동속도를 이 비율(0~1)만큼 깎아 무게감을 준다. 0이면 기본 0.6 적용. 안쪽 각도는 감속 없이 속도 유지.")]
    [Range(0f, 1f)]
    public float sharpTurnMoveSlowdown = 0f;
    [Tooltip("이 각도(도) 이상으로 방향을 꺾으면 '급반전' 구간 — 마찰 제동(sharpTurnMoveSlowdown)으로 감속하며 정면으로 빠르게 피벗. 미만은 속도 유지하며 즉시 정면 전환. 0 이하면 기본 135.")]
    public float sharpTurnAngle = 135f;

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
