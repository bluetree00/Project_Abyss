using System;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace RelicFairy.Monster
{
/// <summary>
/// DragonBoss 메인 클래스.
///
/// ━━ 설정 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  공통 스탯·패턴은 BossConfigSO (Addressables "DragonBossConfig") 에서 로드.
///  드래곤 고유 수치(걷기/달리기 임계값 등)는 프리팹 [SerializeField] 로 설정.
///  공중 이륙/체공/착지는 각 공중 공격 패턴 SO 내부 FullLockState 가 담당.
///
/// ━━ 상태 구성 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  공통: Idle(PatrolState) · WalkChase(ChaseState) ·
///        RunChase(별도 타입) · AttackReady · AttackState(안전망) ·
///        GetHit · Die
///
/// ━━ 패턴 시스템 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  BossPatternRunner 가 Update() 에서 틱되어 BossConfigSO 의
///  patternEntries 를 평가하고 패턴 SpecialState 를 발동한다.
/// </summary>
public class DragonBossMonster : MonsterBase, IBoss, IBossEntrance
{
    // ── 드래곤 전용 설정 (Inspector) ──────────────────────
    [Header("Dragon — 애니메이션 상태 이름")]
    [SerializeField] private string _walkChaseStateName  = "WalkChase";
    [SerializeField] private string _runChaseStateName   = "RunChase";
    [SerializeField] private string _walkLeftStateName  = "WalkLeft";
    [SerializeField] private string _walkRightStateName = "WalkRight";
    [SerializeField] private string _runLeftStateName   = "RunLeft";
    [SerializeField] private string _runRightStateName  = "RunRight";
    [SerializeField] private string _airChaseStateName = "AirChase";
    [SerializeField] private string _airChaseLeftStateName = "AirChaseLeft";
    [SerializeField] private string _airChaseRightStateName = "AirChaseRight";

    [Header("Dragon — 지상 히트박스")]
    [Tooltip("지상 상태 콜라이더 중심 Y (루트 기준). 프리팹 저장 상태와 무관하게 항상 이 값 사용.")]
    [SerializeField] private float _groundHitboxCenterY = 2.5f;
    [Tooltip("지상 상태 콜라이더 높이")]
    [SerializeField] private float _groundHitboxHeight = 5f;

    [Header("Dragon — 공중 히트박스")]
    [Tooltip("공중 상태에서 지상 높이 기준 배율 (수직 확장 — 투사체 피격 범위)")]
    [SerializeField] private float _airborneHitboxHeightMult = 5f;
    [Tooltip("공중 상태에서 콜라이더 중심을 아래로 이동하는 Y 오프셋 (지상 발사체가 닿도록)")]
    [SerializeField] private float _airborneHitboxCenterOffsetY = 8f;
    [Tooltip("공중 상태에서 콜라이더 반경 (넓을수록 맞추기 쉬움)")]
    [SerializeField] private float _airborneHitboxRadius = 3f;

    [Header("Dragon — 이동")]
    [Tooltip("이 거리 초과 시 RunChase, 이하 시 WalkChase")]
    [SerializeField] private float _walkToRunThreshold = 8f;
    [Tooltip("WalkChase 속도 배율 (moveSpeed 대비)")]
    [SerializeField] private float _walkChaseSpeedMult = 0.5f;
    [Tooltip("추적 중 NavMeshAgent 회전 속도 (낮을수록 천천히 방향 전환)")]
    [SerializeField] private float _chaseAngularSpeed = 12f;
    [Tooltip("방향 전환 애니 재생 중 이동 속도 배율")]
    [SerializeField] private float _turnSpeedMult = 0.4f;
    [SerializeField] private float _groundTurnOrbitAngle = 65f;
    [SerializeField] private float _groundTurnFaceAngle = 25f;
    [SerializeField] private float _airChaseHeight = 7f;
    [SerializeField] private float _airChaseSpeedMult = 1.4f;
    [SerializeField] private float _airTurnAngleThreshold = 40f;
    [SerializeField] private float _airTransitionWeightMultiplier = 8f;
    [SerializeField] private float _airOrbitAngularSpeed = 35f;
    [SerializeField] private float _airOrbitCatchUpSpeedMult = 2.1f;
    [SerializeField] private float _airOrbitRadiusTolerance = 1.2f;
    [Tooltip("선회 반경 = Floor 내접원 반지름 * 이 비율 (1=내접원에 정확히 접함, 작을수록 카메라 안쪽으로)")]
    [SerializeField] private float _airOrbitRadiusRatio = 0.8f;
    [Tooltip("공중 진입 직후 패턴 발동 잠금 최소 선회량 랜덤 범위 하한 (1=한 바퀴)")]
    [SerializeField] private float _airOrbitMinTurnsMin = 0.05f;
    [Tooltip("공중 진입 직후 패턴 발동 잠금 최소 선회량 랜덤 범위 상한 (1=한 바퀴)")]
    [SerializeField] private float _airOrbitMinTurnsMax = 0.5f;
    [SerializeField] private float _airOrbitRecenterThreshold = 9f;
    [SerializeField] private float _airOrbitCenterMoveSpeedMult = 1.1f;

    [Header("Dragon — 등장 연출")]
    [Tooltip("등장 대기 중 플레이어 감지 반경")]
    [SerializeField] private float _detectionRange = 15f;
    [Tooltip("등장 시 착지 지점 위쪽으로 띄우는 높이 — 브레스 발사 고도")]
    [SerializeField] private float _entranceDescendHeight = 45f;
    [Tooltip("등장 하강 초기 빠른 속도 (m/s) — 착지 트리거 구간 전까지 이 속도로 하강")]
    [SerializeField] private float _entranceDescendFastSpeed = 35f;
    [Tooltip("등장 착지 애니메이션 재생 구간 속도 (m/s) — Landing_Touchdown 클립 재생 중 이 속도 유지")]
    [SerializeField] private float _entranceDescendSpeed = 5f;
    [Tooltip("지붕 파괴 임팩트 시점 생성할 브레스 VFX 프리팹")]
    [SerializeField] private GameObject _entranceBreathVfxPrefab;
    [Tooltip("브레스 VFX 생성 위치. 비워두면 드래곤 위치 사용")]
    [SerializeField] private Transform _entranceVfxPoint;
    [Tooltip("브레스 VFX가 비행 방향 기준 아래로 꺾이는 각도 (도) — 진행방향 대각선 아래로 분사")]
    [SerializeField] private float _entranceBreathPitchDeg = 35f;
    [Tooltip("등장 브레스 VFX 시작 시 재생할 사운드")]
    [SerializeField] private AudioClip _entranceBreathSfx;
    [Tooltip("브레스 VFX가 보이기 전 사운드를 미리 재생하는 선행 시간(초)")]
    [SerializeField] private float _entranceBreathSfxLeadTime = 0.2f;
    private AudioSource _entranceBreathAudioSource;
    [Tooltip("착지 후 카메라 클로즈업 오프셋 (드래곤 기준 월드 좌표)")]
    [SerializeField] private Vector3 _entranceCameraOffset = new Vector3(7f, 0.5f, -2f);
    [Tooltip("착지 후 카메라가 바라보는 지점 = 드래곤 위치 + 이 오프셋 (월드 좌표). Y를 높이면 더 위쪽(얼굴)을 바라본다")]
    [SerializeField] private Vector3 _entranceCameraLookOffset = new Vector3(0f, 2.5f, 0f);
    [Tooltip("착지 직후 보스 클로즈업으로 전환하는 카메라 이동 시간 (초). 0이면 즉시 컷")]
    [SerializeField] private float _entranceCameraCloseUpDuration = 1.0f;
    [Tooltip("보스 이름 HUD 소멸 후 플레이어 카메라로 복귀하는 시간 (초)")]
    [SerializeField] private float _entranceCameraMoveDuration = 1.2f;
    [Tooltip("등장 비행 시작 위치 — 착지 지점(SpawnPosition) 기준 수평 오프셋 (X/Z). 이 위치에서 브레스를 뿜으며 착지 지점 위까지 날아온다")]
    [SerializeField] private Vector2 _entranceFlyInOffset = new Vector2(0f, 55f);
    [Tooltip("등장 비행 속도 (m/s)")]
    [SerializeField] private float _entranceFlyInSpeed = 25f;

    [Header("Dragon — 등장 카메라 1: 브레스 (드래곤 상공)")]
    [Tooltip("브레스 카메라 높이 — 드래곤 현재 Y + 이 값 (항상 드래곤보다 위에서 내려다봄)")]
    [SerializeField] private float   _entranceBreathCamHeight        = 12f;
    [Tooltip("브레스 카메라를 드래곤 비행방향 기준 우측으로 밀어내는 수평 거리 (m) — 클수록 측면 시점이 강해짐")]
    [SerializeField] private float   _entranceBreathCamRightShift    = 18f;
    [Tooltip("브레스 카메라 시선 목표: 드래곤 위치 기준 Y 오프셋 (0~3 권장 — 위에서 드래곤을 내려다보는 구도)")]
    [SerializeField] private float   _entranceBreathCamLookElevation = 2f;
    [Tooltip("현재 플레이어 카메라 → 브레스 카메라 전환 시간 (초)")]
    [SerializeField] private float   _entranceBreathCamDuration      = 1.2f;
    [Tooltip("드래곤이 착지 위치 위에 도달한 후 브레스 VFX를 추가 유지하는 시간 (초). 클수록 브레스가 더 길게 유지됨")]
    [SerializeField] private float   _entranceBreathExtraHoldTime    = 1.0f;

    [Header("Dragon — 등장 카메라 U-아크 (브레스 → 하강 연결)")]
    [Tooltip("Bezier 아크 컨트롤 포인트 — 드래곤 비행방향 기준 로컬 오프셋 (X+=우측, X-=좌측, Y+=위). 음수 X값이 U자 좌측 스윙을 만든다")]
    [SerializeField] private Vector3 _entranceArcCtrlOffset = new Vector3(-20f, 10f, 0f);
    [Tooltip("U-아크 이동 시간 (초)")]
    [SerializeField] private float   _entranceArcDuration    = 1.0f;

    [Header("Dragon — 등장 카메라 2: 하강 추적 (드래곤 로컬 공간)")]
    [Tooltip("하강 추적 카메라 오프셋 (드래곤 로컬 공간. Z+ = 진행방향 앞쪽, X+ = 오른쪽)")]
    [SerializeField] private Vector3 _entranceDescentCamOffset     = new Vector3(8f, 10f, -6f);
    [Tooltip("하강 추적 카메라 시선 오프셋 (드래곤 로컬 공간)")]
    [SerializeField] private Vector3 _entranceDescentCamLookOffset = new Vector3(0f, 2f, 0f);

    [Header("Dragon — 등장 바람 이펙트")]
    [Tooltip("보스 이름 HUD 등장과 함께 표시할 화면 전체 바람 이펙트 프리팹")]
    [SerializeField] private GameObject _entranceWindEffectPrefab;

    [Header("Dragon — 발톱 트레일")]
    [Tooltip("ClawAttackL/R 구간에 발톱 궤적을 표시하는 컨트롤러")]
    [SerializeField] private DragonClawTrailController _clawTrailCtrl;

    [Header("Dragon — 날개 펄럭임 사운드 (AirChase 계열 전용)")]
    [Tooltip("날개 다운스트로크마다 랜덤 재생할 사운드 클립 (Wing1~5)")]
    [SerializeField] private AudioClip[] _wingFlapClips;
    [Tooltip("UPFly 클립 기준 날개 다운스트로크 시점 (normalizedTime, 20/40 프레임 = 0.5)")]
    [SerializeField] private float _wingFlapPhase = 0.5f;

    [Header("Dragon — 생존감 사운드 (Normal)")]
    [Tooltip("등장 클로즈업 등에서 \"살아있는 보스\"처럼 들리도록 재생할 사운드 클립")]
    [SerializeField] private AudioClip _normalSfx;
    [Tooltip("평시(지상 + 패턴 비활성) 상태에서 Normal 사운드를 재생하는 최소 간격(초)")]
    [SerializeField] private float _normalSfxIntervalMin = 8f;
    [Tooltip("평시 상태에서 Normal 사운드를 재생하는 최대 간격(초)")]
    [SerializeField] private float _normalSfxIntervalMax = 15f;

    [Header("Dragon — 발걸음 사운드 (WalkChase/RunChase 계열 전용)")]
    [Tooltip("발걸음마다 랜덤 재생할 사운드 클립 (Walk1~6)")]
    [SerializeField] private AudioClip[] _footstepClips;
    [Tooltip("걷기/달리기 클립 1회 재생 중 발이 닿는 시점들 (normalizedTime, 0~1). 48프레임 클립 기준 24프레임마다 = {0, 0.5}")]
    [SerializeField] private float[] _footstepPhases = new float[] { 0f, 0.5f };

    [Header("Dragon — 공중 패시브 메테오")]
    [Tooltip("공중 상태일 때 독자 쿨타임으로 발동할 메테오 SO. null이면 비활성.")]
    [SerializeField] private DragonFireballRainPatternSO _passiveMeteorSO;
    [Tooltip("메테오 쿨타임 하한 (초)")]
    [SerializeField] private float _passiveMeteorCooldownMin = 12f;
    [Tooltip("메테오 쿨타임 상한 (초)")]
    [SerializeField] private float _passiveMeteorCooldownMax = 20f;
    [Tooltip("공중 진입 직후 첫 발동까지 대기 시간 (초)")]
    [SerializeField] private float _passiveMeteorInitialDelay = 4f;
    [Tooltip("메테오 버스트 지속 시간 하한 (초)")]
    [SerializeField] private float _passiveMeteorBurstMin = 5f;
    [Tooltip("메테오 버스트 지속 시간 상한 (초)")]
    [SerializeField] private float _passiveMeteorBurstMax = 10f;

    // ── 읽기 전용 프로퍼티 (상태 클래스에서 접근) ──────────
    public string WalkChaseStateName   => _walkChaseStateName;
    public string RunChaseStateName    => _runChaseStateName;
    public string WalkLeftStateName   => _walkLeftStateName;
    public string WalkRightStateName  => _walkRightStateName;
    public string RunLeftStateName    => _runLeftStateName;
    public string RunRightStateName   => _runRightStateName;
    public string AirChaseStateName   => _airChaseStateName;
    public string AirChaseLeftStateName => _airChaseLeftStateName;
    public string AirChaseRightStateName => _airChaseRightStateName;
    public float  WalkToRunThreshold  => _walkToRunThreshold;
    public float  ChaseAngularSpeed   => _chaseAngularSpeed;
    public float  WalkChaseSpeedMult  => _walkChaseSpeedMult;
    public float  TurnSpeedMult       => _turnSpeedMult;
    public float  GroundTurnOrbitAngle => _groundTurnOrbitAngle;
    public float  GroundTurnFaceAngle  => _groundTurnFaceAngle;
    public float  AirChaseHeight      => _airChaseHeight;
    public float  AirChaseSpeedMult   => _airChaseSpeedMult;
    public float  AirTurnAngleThreshold => _airTurnAngleThreshold;
    // Floor에 내접하는 원의 반지름(짧은 변 기준) * 비율 = 선회 반경 — 비율이 1보다 작으면 카메라 안쪽으로 들어옴
    public float  AirOrbitRadius      => Mathf.Min(DragonBossRoomContext.Width, DragonBossRoomContext.Height) * DragonBossRoomContext.CellSize * 0.5f * _airOrbitRadiusRatio;
    public float  AirOrbitAngularSpeed => _airOrbitAngularSpeed;
    public float  AirOrbitCatchUpSpeedMult => _airOrbitCatchUpSpeedMult;
    public float  AirOrbitRadiusTolerance => _airOrbitRadiusTolerance;
    public float  AirOrbitMinTurnsMin => _airOrbitMinTurnsMin;
    public float  AirOrbitMinTurnsMax => _airOrbitMinTurnsMax;
    public float  AirOrbitRecenterThreshold => _airOrbitRecenterThreshold;
    public float  AirOrbitCenterMoveSpeedMult => _airOrbitCenterMoveSpeedMult;

    public float   EntranceDescendHeight      => _entranceDescendHeight;
    public float   EntranceDescendFastSpeed   => _entranceDescendFastSpeed;
    public float   EntranceDescendSpeed       => _entranceDescendSpeed;
    public Vector3 EntranceCameraOffset      => _entranceCameraOffset;
    public Vector3 EntranceCameraLookOffset  => _entranceCameraLookOffset;
    public float   EntranceCameraCloseUpDuration => _entranceCameraCloseUpDuration;
    public float   EntranceCameraMoveDuration => _entranceCameraMoveDuration;
    public Vector2 EntranceFlyInOffset           => _entranceFlyInOffset;
    public float   EntranceFlyInSpeed            => _entranceFlyInSpeed;
    public float   EntranceBreathPitchDeg        => _entranceBreathPitchDeg;
    public float   EntranceBreathCamHeight        => _entranceBreathCamHeight;
    public float   EntranceBreathCamRightShift    => _entranceBreathCamRightShift;
    public float   EntranceBreathCamLookElevation => _entranceBreathCamLookElevation;
    public float   EntranceBreathCamDuration      => _entranceBreathCamDuration;
    public float   EntranceBreathExtraHoldTime    => _entranceBreathExtraHoldTime;
    public Vector3 EntranceArcCtrlOffset          => _entranceArcCtrlOffset;
    public float   EntranceArcDuration            => _entranceArcDuration;
    public Vector3 EntranceDescentCamOffset       => _entranceDescentCamOffset;
    public Vector3 EntranceDescentCamLookOffset   => _entranceDescentCamLookOffset;
    public GameObject EntranceWindEffectPrefab    => _entranceWindEffectPrefab;
    public float   EntranceBreathSfxLeadTime  => _entranceBreathSfxLeadTime;

    // ── IBoss ─────────────────────────────────────────────
    public float HpRatio =>
        (_config != null && _config.stat.maxHp > 0)
            ? (float)_runtime.CurrentHp / _config.stat.maxHp
            : 1f;

    public BossAttackBlackboard Blackboard => _dragonBB;

    // ── 보스 전용 필드 ────────────────────────────────────
    private DragonBossBlackboard         _dragonBB;
    private BossPatternContext           _patternCtx;
    private BossPatternRunner            _runner;
    private DragonDormantState           _dormantState;
    private bool                         _pendingTriggerEntrance;
    private DragonAirMeteorPassiveRunner _meteorPassiveRunner;
    private System.Threading.CancellationTokenSource _meteorPassiveCts;
    private Vector3                      _preEntranceSpawnPos;
    private bool                         _hasPreEntranceSpawnPos;

    // ── 공중 히트박스 ─────────────────────────────────────
    private CapsuleCollider _capsule;
    private Vector3         _capsuleCenterNormal; // X/Z 중심 보존용
    private float           _capsuleRadiusNormal; // 지상 반경 보존용
    private bool            _airborneHitboxActive;
    private int              _airChaseHash;
    private int              _airChaseLeftHash;
    private int              _airChaseRightHash;
    private int              _wingFlapStateHash;
    private float            _wingFlapPrevTime;
    private int              _walkChaseHash;
    private int              _walkLeftHash;
    private int              _walkRightHash;
    private int              _runChaseHash;
    private int              _runLeftHash;
    private int              _runRightHash;
    private int              _footstepStateHash;
    private float            _footstepPrevTime;
    private float            _normalSfxTimer;
    private float            _normalSfxNextInterval;
    private float            _airOrbitCurrentLockDegrees;
    private BodyState        _prevBodyState;

    // ── 외부 접근 ─────────────────────────────────────────
    public DragonBossBlackboard DragonBlackboard => _dragonBB;
    public DragonClawTrailController ClawTrailController => _clawTrailCtrl;

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // MonsterBase 추상 멤버
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected override string ConfigAddress => "DragonBossConfig";
    // ConfigAddress에 '/'가 없어 기본 ServerStatId 추출이 "DragonBossConfig"가 되어 CSV id("DragonBoss")와 불일치 → 명시 지정.
    protected override string ServerStatId  => "DragonBoss";
    protected override string DataAddress   => null;
    protected override bool   UseWorldHPBar => false;

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 상태 등록 및 보스 시스템 조립
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected override void RegisterStates()
    {
        base.RegisterStates();

        // ── 공통 슬롯 오버라이드 ───────────────────────────
        _fsm.RegisterAs<PatrolState>     (new DragonIdleState());
        _fsm.RegisterAs<ChaseState>      (new DragonWalkChaseState());
        _fsm.RegisterAs<AttackReadyState>(new DragonBossAttackReadyState());
        _fsm.RegisterAs<AttackState>     (new DragonBossAttackState());
        _fsm.RegisterAs<GetHitState>     (new DragonGetHitState());
        _fsm.RegisterAs<DieState>        (new DragonDieState());
        _fsm.Register                    (new DragonRunChaseState());

        // ── 보스 시스템 조립 ───────────────────────────────
        var bossConfig = _config as BossConfigSO;
        if (bossConfig == null) return;

        _dragonBB   = new DragonBossBlackboard();
        _patternCtx = new BossPatternContext
        {
            Boss       = this,
            Ctx        = _ctx,
            Blackboard = _dragonBB,
        };

        BuildConditions(bossConfig);
        InitializePatterns(bossConfig);

        _runner = new BossPatternRunner(
            bossConfig,
            _patternCtx,
            isAlive:     () => !_runtime.IsDead && !IsPlayerDead(),
            isInRange:   IsInEngagementRange,
            changeState: ChangeState,
            onExecuted:  OnPatternExecuted);

        UpdateTransitionPatternWeights();
    }

    protected override void OnInitialized()
    {
        // OnEnable에서 고공으로 이동했으므로 InitAsync가 잘못된 위치를 SpawnPosition으로 저장했을 수 있음 → 착지 지점으로 복원
        if (_hasPreEntranceSpawnPos && _runtime != null)
        {
            _runtime.SpawnPosition  = _preEntranceSpawnPos;
            _hasPreEntranceSpawnPos = false;
        }

        InitializeRoomContext(); // OnEnable이 _runtime 생성 전에 호출된 경우를 위한 재시도
        // BreathSweep/FireballRain 경고장판 풀 사전 워밍 — 전투 중 첫 스폰 시 CreatePrimitive 렉 방지
        QuadTilePool.Prewarm(DragonBossRoomContext.Width * DragonBossRoomContext.Height);
        BindBossHud();
        if (_clawTrailCtrl == null)
            _clawTrailCtrl = GetComponent<DragonClawTrailController>();
        _capsule = GetComponent<CapsuleCollider>();
        if (_capsule != null)
        {
            _capsuleCenterNormal = _capsule.center; // X/Z만 보존
            _capsuleRadiusNormal = _capsule.radius; // 지상 반경 보존
            // 프리팹 저장 상태와 무관하게 지상 히트박스로 초기화
            _airborneHitboxActive = true;           // 다음 SyncAirborneHitbox 호출 시 else(지상) 분기 강제 진입
        }
        // 초기 상태: Ice 페이즈 (HP 100%) 색상
        DragonBossVisualHelper.ApplyBodyTint(transform,
            DragonBossVisualHelper.GetElementColor(DragonBossBlackboard.DragonElement.Ice));

        _airChaseHash      = Animator.StringToHash(_airChaseStateName);
        _airChaseLeftHash  = Animator.StringToHash(_airChaseLeftStateName);
        _airChaseRightHash = Animator.StringToHash(_airChaseRightStateName);
        _walkChaseHash     = Animator.StringToHash(_walkChaseStateName);
        _walkLeftHash      = Animator.StringToHash(_walkLeftStateName);
        _walkRightHash     = Animator.StringToHash(_walkRightStateName);
        _runChaseHash      = Animator.StringToHash(_runChaseStateName);
        _runLeftHash       = Animator.StringToHash(_runLeftStateName);
        _runRightHash      = Animator.StringToHash(_runRightStateName);

        // 등장 대기 상태로 진입 — 하강/착지/지붕 파괴 연출은 TriggerEntrance() 호출 시 시작
        _dormantState = new DragonDormantState(_detectionRange);
        ChangeState(_dormantState);

        // InitAsync 완료 전에 BossSpawner가 TriggerEntrance()를 호출한 경우 즉시 적용
        if (_pendingTriggerEntrance)
        {
            _pendingTriggerEntrance = false;
            _dormantState.TriggerEntrance(_ctx);
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 매 프레임
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected override void Update()
    {
        base.Update();
        if (_runtime == null || _runtime.IsDead || _dragonBB == null) return;

        // 등장 연출 중에는 패턴 러너와 무브먼트 완전 정지
        if (_dormantState != null && _dormantState.IsActive) return;

        _dragonBB.TickCooldowns(Time.deltaTime);

        if (_runner != null)
        {
            if (!_runner.IsPatternActive)
                _dragonBB.NormalModeTimer += Time.deltaTime;
            else
                _dragonBB.NormalModeTimer = 0f;
        }

        UpdateTransitionPatternWeights();

        // 지상→공중 전환 시 선회 잠금 임계값 랜덤 재설정
        if (_dragonBB.BodyState == BodyState.Airborne && _prevBodyState == BodyState.Grounded)
            _airOrbitCurrentLockDegrees = UnityEngine.Random.Range(_airOrbitMinTurnsMin, _airOrbitMinTurnsMax) * 360f;
        _prevBodyState = _dragonBB.BodyState;

        // 공중 진입 직후 랜덤 선회량 채울 때까지 패턴 발동 잠금
        bool airPatternLocked = _dragonBB.BodyState == BodyState.Airborne
            && _dragonBB.AirOrbitAccumulatedDegrees < _airOrbitCurrentLockDegrees;
        if (!airPatternLocked)
            _runner?.Tick(Time.deltaTime);

        SyncAirborneHitbox();
        UpdateWingFlapSound();
        UpdateFootstepSound();
        UpdateNormalSfx();
    }

    /// <summary>평시(지상 + 패턴 비활성) 상태에서 랜덤 간격으로 Normal 사운드를 재생해 "살아있는 보스" 느낌을 준다.</summary>
    private void UpdateNormalSfx()
    {
        bool idleGrounded = _dragonBB.BodyState == BodyState.Grounded
            && (_runner == null || !_runner.IsPatternActive);
        if (!idleGrounded)
        {
            _normalSfxTimer = 0f;
            return;
        }

        _normalSfxTimer += Time.deltaTime;
        if (_normalSfxTimer < _normalSfxNextInterval) return;

        _normalSfxTimer = 0f;
        _normalSfxNextInterval = UnityEngine.Random.Range(_normalSfxIntervalMin, _normalSfxIntervalMax);
        PlayNormalSfx();
    }

    /// <summary>WalkChase/RunChase 계열(좌우 회전 포함) 재생 중, 클립의 발걸음 접지 시점(normalizedTime)을
    /// 지나갈 때마다 Walk1~6 중 하나를 랜덤 재생한다. 애니메이션 속도가 바뀌어도 항상 같은 프레임에 맞는다.</summary>
    private void UpdateFootstepSound()
    {
        if (_footstepClips == null || _footstepClips.Length == 0
            || _footstepPhases == null || _footstepPhases.Length == 0
            || _dragonBB == null || _animator == null) return;

        if (_dragonBB.BodyState != BodyState.Grounded)
        {
            _footstepStateHash = 0;
            return;
        }

        var info = _animator.GetCurrentAnimatorStateInfo(0);
        int hash = info.shortNameHash;
        bool isGroundChaseFamily = hash == _walkChaseHash || hash == _walkLeftHash || hash == _walkRightHash
            || hash == _runChaseHash || hash == _runLeftHash || hash == _runRightHash;
        if (!isGroundChaseFamily)
        {
            _footstepStateHash = 0;
            return;
        }

        if (_footstepStateHash != hash)
        {
            // 상태 진입 첫 프레임 — 기준 시간만 잡고 트리거는 다음 프레임부터
            _footstepStateHash = hash;
            _footstepPrevTime = info.normalizedTime;
            return;
        }

        float currentTime = info.normalizedTime;
        foreach (float phase in _footstepPhases)
        {
            int prevCycle = Mathf.FloorToInt(_footstepPrevTime - phase);
            int curCycle  = Mathf.FloorToInt(currentTime - phase);
            if (curCycle != prevCycle)
            {
                var clip = _footstepClips[UnityEngine.Random.Range(0, _footstepClips.Length)];
                Managers.Sound?.PlayEffectAt(clip, transform.position);
                break;
            }
        }
        _footstepPrevTime = currentTime;
    }

    /// <summary>등장 클로즈업 등 특정 연출 시점에 "살아있는 보스" 느낌의 사운드를 재생한다.</summary>
    public void PlayNormalSfx()
        => Managers.Sound?.PlayEffectAt(_normalSfx, transform.position);

    /// <summary>공중 선회 사이클과 무관하게 날개짓 사운드를 한 번 재생한다 (그라운드 브레스 Back 모션 등).</summary>
    public void PlayWingFlapSfxOnce()
    {
        if (_wingFlapClips == null || _wingFlapClips.Length == 0) return;
        var clip = _wingFlapClips[UnityEngine.Random.Range(0, _wingFlapClips.Length)];
        Managers.Sound?.PlayEffectAt(clip, transform.position);
    }

    /// <summary>AirChase/AirChaseLeft/AirChaseRight 재생 중, UPFly 클립의 다운스트로크 프레임(normalizedTime)을
    /// 지나갈 때마다 날개 펄럭임 사운드를 재생한다. 애니메이션 속도가 바뀌어도 항상 같은 프레임에 맞는다.</summary>
    private void UpdateWingFlapSound()
    {
        if (_wingFlapClips == null || _wingFlapClips.Length == 0 || _dragonBB == null || _animator == null) return;

        if (_dragonBB.BodyState != BodyState.Airborne)
        {
            _wingFlapStateHash = 0;
            return;
        }

        var info = _animator.GetCurrentAnimatorStateInfo(0);
        int hash = info.shortNameHash;
        bool isAirChaseFamily = hash == _airChaseHash || hash == _airChaseLeftHash || hash == _airChaseRightHash;
        if (!isAirChaseFamily)
        {
            _wingFlapStateHash = 0;
            return;
        }

        if (_wingFlapStateHash != hash)
        {
            // 상태 진입 첫 프레임 — 기준 시간만 잡고 트리거는 다음 프레임부터
            _wingFlapStateHash = hash;
            _wingFlapPrevTime = info.normalizedTime;
            return;
        }

        float currentTime = info.normalizedTime;
        int prevCycle = Mathf.FloorToInt(_wingFlapPrevTime - _wingFlapPhase);
        int curCycle  = Mathf.FloorToInt(currentTime - _wingFlapPhase);
        if (curCycle != prevCycle)
        {
            var clip = _wingFlapClips[UnityEngine.Random.Range(0, _wingFlapClips.Length)];
            Managers.Sound?.PlayEffectAt(clip, transform.position);
        }
        _wingFlapPrevTime = currentTime;
    }

    private void SyncAirborneHitbox()
    {
        if (_capsule == null || _dragonBB == null) return;
        bool shouldBeAirborne = _dragonBB.BodyState == BodyState.Airborne;
        if (shouldBeAirborne == _airborneHitboxActive) return;

        _airborneHitboxActive = shouldBeAirborne;
        float cx = _capsuleCenterNormal.x;
        float cz = _capsuleCenterNormal.z;
        if (shouldBeAirborne)
        {
            // 공중: 아래로 확장 + 반경 확대 — 지상에서 조준하기 쉽도록
            _capsule.height = _groundHitboxHeight * _airborneHitboxHeightMult;
            _capsule.center = new Vector3(cx, _groundHitboxCenterY - _airborneHitboxCenterOffsetY, cz);
            _capsule.radius = _airborneHitboxRadius;
        }
        else
        {
            // 지상: 명시적 수치로 바디 중앙에 콜라이더 배치, 반경 원래 값으로 복원
            _capsule.height = _groundHitboxHeight;
            _capsule.center = new Vector3(cx, _groundHitboxCenterY, cz);
            _capsule.radius = _capsuleRadiusNormal;
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 풀 재사용 리셋
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected override void OnEnable()
    {
        base.OnEnable();
        InitializeRoomContext();
        _dragonBB?.Reset();
        _runner?.Reset();
        CacheTransitionPatternBaseWeights();
        UpdateTransitionPatternWeights();
        BindBossHud();
        _airborneHitboxActive       = true; // 다음 프레임 SyncAirborneHitbox에서 지상 상태로 강제 복원
        _prevBodyState              = BodyState.Grounded;
        _airOrbitCurrentLockDegrees = 0f;
        _pendingTriggerEntrance     = false;
        _normalSfxTimer = 0f;
        _normalSfxNextInterval = UnityEngine.Random.Range(_normalSfxIntervalMin, _normalSfxIntervalMax);
        StopMeteorPassiveRunner();
        // pool 재활성: 등장 연출 재진입
        if (_dormantState != null)
            ChangeState(_dormantState);
        // pool 재활성 시 Ice 페이즈 색상으로 리셋
        DragonBossVisualHelper.ApplyBodyTint(transform,
            DragonBossVisualHelper.GetElementColor(DragonBossBlackboard.DragonElement.Ice));
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 드래곤 전용 상태 전환 (상태 클래스에서 호출)
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 피격 처리 (쉴드)
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    public override void TakeDamage(float amount, GameObject instigator,
        float knockbackMultiplier = 1f,
        bool isCrit = false)
    {
        if (_dragonBB != null && instigator != null)
        {
            Vector3 dir = instigator.transform.position - transform.position;
            _dragonBB.SetHitDirection(dir, transform.forward);
        }

        // HP가 소환 임계값에 이미 도달해 있고 해당 소환이 미발동이면 데미지 전 즉시 무적
        if (_dragonBB != null && _config != null && _runtime != null && _config.stat.maxHp > 0)
        {
            float ratio = HpRatio;
            if ((!_dragonBB.HasSummonedAt70 && ratio <= 0.7f) ||
                (!_dragonBB.HasSummonedAt40 && ratio <= 0.4f) ||
                (!_dragonBB.HasSummonedAt10 && ratio <= 0.1f))
                _dragonBB.SetSummonGated(true);
        }

        if (_dragonBB != null && _dragonBB.IsSummonGated) return;

        base.TakeDamage(amount, instigator, knockbackMultiplier, isCrit);

        // 지상 피격 시 히트스톱 (poise 파괴 = 강타격, 일반 = 약타격)
        // TimeScaleArbiter를 경유하는 HitFeelService 사용 — KillImpact 보호 윈도 자동 적용됨
        bool isAirborne = _dragonBB != null && _dragonBB.BodyState == BodyState.Airborne;
        if (!isAirborne)
        {
            float dur = (_dragonBB != null && _dragonBB.IsPoiseBroken) ? 0.14f : 0.05f;
            HitFeelService.HitStop(0.05f, dur);
        }
    }

    protected override void OnDamageTaken()
    {
        if (_dragonBB == null) return;

        // 소환 임계값 돌파 시 무적 게이트 설정 — 소환 패턴 완료까지 이후 데미지 차단
        float ratio = HpRatio;
        if ((!_dragonBB.HasSummonedAt70 && ratio <= 0.7f) ||
            (!_dragonBB.HasSummonedAt40 && ratio <= 0.4f) ||
            (!_dragonBB.HasSummonedAt10 && ratio <= 0.1f))
        {
            _dragonBB.SetSummonGated(true);
        }

        // 공중 상태 또는 쉴드가 이미 파괴된 상태에서는 GetHit 스킵
        if (_dragonBB.BodyState == BodyState.Airborne)
        {
            _suppressGetHitThisHit = true;
            return;
        }

        bool poiseBroke = _dragonBB.ApplyPoiseDamage();
        if (!poiseBroke)
            _suppressGetHitThisHit = true;
    }

    protected override void OnDisable()
    {
        StopMeteorPassiveRunner();
        base.OnDisable();
    }

    private void StartMeteorPassiveRunner()
    {
        if (_passiveMeteorSO == null) return;
        StopMeteorPassiveRunner();
        _meteorPassiveCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(
            destroyCancellationToken);
        _meteorPassiveRunner = new DragonAirMeteorPassiveRunner(
            _ctx,
            _passiveMeteorSO,
            _passiveMeteorCooldownMin,
            _passiveMeteorCooldownMax,
            _passiveMeteorInitialDelay,
            _passiveMeteorBurstMin,
            _passiveMeteorBurstMax);
        _meteorPassiveRunner.Start(_meteorPassiveCts.Token);
    }

    private void StopMeteorPassiveRunner()
    {
        _meteorPassiveCts?.Cancel();
        _meteorPassiveCts?.Dispose();
        _meteorPassiveCts    = null;
        _meteorPassiveRunner = null;
    }

    public void UnbindBossHudPublic() => UnbindBossHudIfBound();

    public void UnbindBossHudAfterDelay(float delay)
        => UnbindHudDelayedAsync(delay).Forget();

    private async UniTaskVoid UnbindHudDelayedAsync(float delay)
    {
        await UniTask.Delay(System.TimeSpan.FromSeconds(delay),
            cancellationToken: destroyCancellationToken);
        UnbindBossHudIfBound();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 내부 헬퍼
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    // 보스룸에서는 플레이어가 존재하면 항상 추적 시작
    public override bool ShouldStartChase(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return false;
        return !IsPlayerDead();
    }

    // 인식 이후에는 거리와 무관하게 죽을 때까지 추적을 포기하지 않는다
    public override bool ShouldGiveUpChase(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return true;
        return IsPlayerDead();
    }

    // 공중 상태에서는 근접 공격을 받지 않는다 (원거리 투사체는 ColliderInstance를 거치지 않아 영향 없음)
    public override bool IsMeleeImmuneNow
        => _dragonBB != null && _dragonBB.BodyState == BodyState.Airborne;

    /// <summary>Floor(BoxCollider) 영역을 감지해 DragonBossRoomContext를 초기화 — BreathSweep/FireballRain 등 Floor 기반 패턴이 이 바닥을 기준으로 동작한다.</summary>
    private void InitializeRoomContext()
    {
        // 배치된 보스의 첫 OnEnable은 InitAsync(_runtime 생성) 완료 전에 호출될 수 있음 — OnInitialized에서 재시도
        if (_runtime == null) return;

        Bounds floorBounds = DragonPatternFloorUtils.ResolveArenaBoundsXZ(_runtime.SpawnPosition, 15f);
        int width  = Mathf.Max(2, Mathf.RoundToInt(floorBounds.size.x));
        int height = Mathf.Max(2, Mathf.RoundToInt(floorBounds.size.z));
        Vector3 worldCenter = new Vector3(floorBounds.center.x, _runtime.SpawnPosition.y, floorBounds.center.z);
        DragonBossRoomContext.Initialize(width, height, 1f, worldCenter);
    }

    private bool IsInEngagementRange()
    {
        if (_runtime?.PlayerTarget == null)
            return false;

        return _runtime.DistToPlayer < _config.detection.chaseGiveUpRange;
    }

    private void OnPatternExecuted(BossPatternSO pattern)
    {
        if (_dragonBB == null || pattern == null)
            return;

        _dragonBB.LastPatternTag = pattern.patternTag;

        if (pattern is DragonTakeoffPatternSO
            || pattern is DragonLandingPatternSO
            || pattern is DragonSummonPatternSO
            || pattern is DragonIceSlamPatternSO)
        {
            _dragonBB.GroundedPatternStreak = 0;
            _dragonBB.AirbornePatternStreak = 0;
        }
        else if (_dragonBB.BodyState == BodyState.Airborne)
        {
            _dragonBB.AirbornePatternStreak++;
            _dragonBB.GroundedPatternStreak = 0;
        }
        else
        {
            _dragonBB.GroundedPatternStreak++;
            _dragonBB.AirbornePatternStreak = 0;
        }

        UpdateTransitionPatternWeights();
    }

    private void InitializePatterns(BossConfigSO config)
    {
        if (config.patternEntries == null) return;
        foreach (var entry in config.patternEntries)
        {
            if (entry?.patterns == null) continue;
            foreach (var p in entry.patterns)
                p?.Initialize(_patternCtx);
        }

        CacheTransitionPatternBaseWeights();
    }

    private void CacheTransitionPatternBaseWeights()
    {
        if (_dragonBB == null || _config is not BossConfigSO bossConfig || bossConfig.patternEntries == null)
            return;

        foreach (var entry in bossConfig.patternEntries)
        {
            if (entry?.patterns == null)
                continue;

            foreach (var pattern in entry.patterns)
            {
                // pattern.weight 는 런타임에 배수가 곱해지므로 오염될 수 있음.
                // BaseWeight (전용 SO 필드, 코드 비수정) 에서 읽어 SO weight 도 즉시 정규화.
                if (pattern is DragonTakeoffPatternSO tp)
                {
                    _dragonBB.TakeoffBaseWeight = Mathf.Max(0.01f, tp.BaseWeight);
                    pattern.weight = _dragonBB.TakeoffBaseWeight;
                }
                else if (pattern is DragonLandingPatternSO lp)
                {
                    _dragonBB.LandingBaseWeight = Mathf.Max(0.01f, lp.BaseWeight);
                    pattern.weight = _dragonBB.LandingBaseWeight;
                }
            }
        }
    }

    private void UpdateTransitionPatternWeights()
    {
        if (_dragonBB == null || _config is not BossConfigSO bossConfig || bossConfig.patternEntries == null)
            return;

        float takeoffMult = _dragonBB.GroundedPatternStreak >= 3
            ? _airTransitionWeightMultiplier
            : 1f;
        float landingMult = _dragonBB.AirbornePatternStreak >= 3
            ? _airTransitionWeightMultiplier
            : 1f;

        foreach (var entry in bossConfig.patternEntries)
        {
            if (entry?.patterns == null)
                continue;

            foreach (var pattern in entry.patterns)
            {
                if (pattern is DragonTakeoffPatternSO)
                    pattern.weight = _dragonBB.TakeoffBaseWeight * takeoffMult;
                else if (pattern is DragonLandingPatternSO)
                    pattern.weight = _dragonBB.LandingBaseWeight * landingMult;
            }
        }
    }

    private void BuildConditions(BossConfigSO config)
    {
        if (config.patternEntries == null) return;
        foreach (var entry in config.patternEntries)
        {
            if (entry == null) continue;
            if (entry.conditions == null || entry.conditions.Count == 0)
            {
                entry.BuiltConditions = null;
                continue;
            }
            var built = new ICondition[entry.conditions.Count];
            for (int i = 0; i < entry.conditions.Count; i++)
                built[i] = BuildCondition(entry.conditions[i], config);
            entry.BuiltConditions = built;
        }
    }

    private ICondition BuildCondition(BossConditionKey key, BossConfigSO config)
    {
        return key switch
        {
            BossConditionKey.Phase2               => new HpBelowCondition(config.condPhase2HpThreshold),
            BossConditionKey.Dist_Close           => new MaxRangeCondition(config.condDistClose),
            BossConditionKey.Dist_Far             => new MinRangeCondition(config.condDistFar),
            BossConditionKey.AfterBackstep        => new LastTagCondition("backstep"),
            BossConditionKey.AfterSidestep        => new LastTagCondition("sidestep"),
            BossConditionKey.TimePressure         => new NormalModeTimerCondition(config.condTimePressureSecs),
            BossConditionKey.Dragon_Summon70      => new DragonSummonedAtCondition(DragonSummonPhase.At70),
            BossConditionKey.Dragon_Summon40      => new DragonSummonedAtCondition(DragonSummonPhase.At40),
            BossConditionKey.Dragon_Summon10      => new DragonSummonedAtCondition(DragonSummonPhase.At10),
            // 속성 페이즈: HP 비율 범위로 판정 (Ice 100-70%, Thunder 70-40%, Fire 40-0%)
            BossConditionKey.Dragon_ElementIce     => new DragonElementPhaseCondition(DragonElementPhase.Ice),
            BossConditionKey.Dragon_ElementThunder => new DragonElementPhaseCondition(DragonElementPhase.Thunder),
            BossConditionKey.Dragon_ElementFire    => new DragonElementPhaseCondition(DragonElementPhase.Fire),
            // 바디 상태: 지상/공중 — 공중·지상 패턴 풀 필터링용
            BossConditionKey.Dragon_Body_Grounded  => new DragonBodyStateCondition(BodyState.Grounded),
            BossConditionKey.Dragon_Body_Airborne  => new DragonBodyStateCondition(BodyState.Airborne),
            _                                      => new AlwaysTrue(),
        };
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // IBossEntrance
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>
    /// BossSpawner가 SetActive 직전에 호출 — 비활성 상태에서 착지 지점을 저장하고 연출 시작 고공 위치로 이동한다.
    /// 활성화(SetActive=true) 시점에 이미 바닥 배치 위치가 아닌 고공에 위치하게 되어 배치 모습이 렌더링되지 않는다.
    /// </summary>
    public void PrePositionForEntrance()
    {
        _preEntranceSpawnPos    = transform.position;
        _hasPreEntranceSpawnPos = true;
        transform.position += new Vector3(_entranceFlyInOffset.x, _entranceDescendHeight, _entranceFlyInOffset.y);
        Vector3 flightDir = new Vector3(-_entranceFlyInOffset.x, 0f, -_entranceFlyInOffset.y);
        if (flightDir.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(flightDir.normalized, Vector3.up);
    }

    /// <summary>DragonDormantState가 플레이어를 감지했을 때 발행 — BossRoomController가 카메라 팬을 시작한다.</summary>
    public override bool HasEntranceAnimation => true;

    public event Action OnEntranceRequested;

    /// <summary>Appear 연출이 끝나고 전투가 시작되기 직전 발행 — 플레이어 입력 복구 등에 사용한다.</summary>
    public event Action OnCombatReady;

    /// <summary>DragonDormantState가 감지 직후 호출 — OnEntranceRequested 이벤트 발행.</summary>
    internal void FireEntranceRequest() => OnEntranceRequested?.Invoke();

    /// <summary>DragonDormantState가 ChaseState 전환 직전 호출 — OnCombatReady 이벤트 발행.</summary>
    internal void FireCombatReady()
    {
        _runner?.EnsureMinBreakCooldown(3f);
        StartMeteorPassiveRunner();
        OnCombatReady?.Invoke();
        RaiseBossCombatReady();
    }

    /// <summary>BossRoomController가 카메라 팬 완료 후 호출 — 하강/착지 연출 시작.</summary>
    public void TriggerEntrance()
    {
        if (_dormantState != null)
            _dormantState.TriggerEntrance(_ctx);
        else
            _pendingTriggerEntrance = true; // InitAsync 완료 전 호출된 경우 OnInitialized에서 적용
    }

    /// <summary>등장 비행 시작 직전 호출 — VFX보다 EntranceBreathSfxLeadTime만큼 먼저 브레스 사운드를 재생한다.</summary>
    public void PlayEntranceBreathSfx()
    {
        Vector3 pos = _entranceVfxPoint != null ? _entranceVfxPoint.position : transform.position;
        _entranceBreathAudioSource = Managers.Sound?.PlayEffectAt(_entranceBreathSfx, pos);
    }

    /// <summary>등장 비행 시작 시 호출 — 입(EntranceVfxPoint) 위치에서, 진행방향 대각선 아래로 분사되는 브레스 VFX를 생성한다.
    /// 회전이 애니메이션 중인 Jaw 본에 끌려가지 않도록 본체(드래곤 루트)에 고정한다.</summary>
    public GameObject SpawnEntranceBreathVfx(Vector3 flightDir)
    {
        if (_entranceBreathVfxPrefab == null) return null;

        Vector3    pos = _entranceVfxPoint != null ? _entranceVfxPoint.position : transform.position;
        Quaternion rot = transform.rotation;
        if (flightDir.sqrMagnitude > 0.0001f)
        {
            float   rad     = _entranceBreathPitchDeg * Mathf.Deg2Rad;
            Vector3 horizDir = flightDir.normalized;
            Vector3 aimDir  = (horizDir * Mathf.Cos(rad) + Vector3.down * Mathf.Sin(rad)).normalized;
            rot = Quaternion.LookRotation(aimDir, Vector3.up);
        }

        return BossEffectPool.Spawn(_entranceBreathVfxPrefab, pos, rot, transform, true);
    }

    /// <summary>등장 브레스 VFX가 파괴되는 시점에 함께 호출 — 사운드가 VFX보다 길게 남지 않도록 정지.</summary>
    public void StopEntranceBreathSfx()
        => Managers.Sound?.StopEffect(_entranceBreathAudioSource, _entranceBreathSfx);

    /// <summary>보스 이름 HUD 표시와 함께 호출 — 화면 전체 바람 이펙트를 생성한다. HUD 소멸 시 호출자가 Destroy한다.</summary>
    public GameObject SpawnEntranceWindVfx()
        => _entranceWindEffectPrefab != null ? BossEffectPool.Spawn(_entranceWindEffectPrefab, Vector3.zero, Quaternion.identity) : null;
}
}
