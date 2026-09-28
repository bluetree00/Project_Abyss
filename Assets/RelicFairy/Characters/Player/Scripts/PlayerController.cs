//============================================================
// PlayerController — 플레이어 캐릭터의 조율자
//
// 파일 구성 (partial):
//   PlayerController.cs            — 상태(필드·자동 속성) · 생명주기 · 초기화
//   PlayerController.Combat.cs     — 피해·사망·무적 · 포이즈·날아감 · 스탠스 · 상태이상 · 저스트 회피
//   PlayerController.Input.cs      — 입력 차단 채널 · 바인딩 · 명령 라우팅 · 이동 기준
//   PlayerController.Locomotion.cs — 이동 배율·공유 값 · 회전 · 조준 · 벽 접촉 · 이동 애니 파라미터
//   PlayerController.Actions.cs    — 스킬 게이트 · 유물 · 패시브 · 무기 교체 · 애니 이벤트
//
// 규칙: 필드와 자동 속성(상태)은 이 파일에만 둔다. 부분 파일에는 동작만 둔다.
//
// 독립적으로 떨어지는 기능은 전용 객체가 맡는다(이 클래스가 소유):
//   PlayerFacing · PlayerAim · PerfectDodgeController · PlayerStatusEffects ·
//   PlayerWallContact · CharacterPassiveRunner · RelicAppearance · PlayerDataResolver(정적)
//
// 캐릭터별 차이는 상속이 아니라 IRelicBehavior(유물 전략)로 합성한다 — 파생 클래스를 두지 않는다.
//============================================================
using System;
using System.Threading;
using UnityEngine;
using Cinemachine;
using Cysharp.Threading.Tasks;
using Game.Inputs;

public sealed partial class PlayerController : CharacterBase
{
    // ── Constants ─────────────────────────────────────────────────
    public const float IceStageWindowDuration = PlayerStatusEffects.IceStageWindowDuration;

    /// <summary>방향 전환 감쇠(이동 애니 2D 좌표). 너무 작으면 값이 튀고, 크면 몸이 굼뜨게 따라온다.</summary>
    private const float MoveDirDamp = 0.10f;

    // 입력이 "바뀌었다"고 볼 최소 변화량(제곱). 아날로그 스틱 미세 흔들림으로 기준이 재설정되지 않게 한다.
    private const float MoveBasisRelatchThresholdSqr = 0.04f;   // 약 0.2 변화

    // 이 각도(도)를 초과하는 한 프레임 heading 변화는 불연속 스냅으로 보고 이동 기준을 즉시 재정렬한다.
    // 방 회전(90° 단위 스냅)은 이 문턱을 넘고, 연출용 부드러운 회전(RotateHeadingTo)은 프레임당 변화가 훨씬 작아 걸리지 않는다.
    private const float MoveBasisSnapRelatchDeg = 60f;

    // ── Static ────────────────────────────────────────────────────
    // 2D 블렌드 트리(MoveBlend)가 몸 기준 로컬 속도를 좌표로 쓴다.
    private static readonly int MoveXHash = Animator.StringToHash("MoveX");
    private static readonly int MoveYHash = Animator.StringToHash("MoveY");

    // ── SerializeField ────────────────────────────────────────────
    [Header("Hit VFX")]
    [Tooltip("피격 시 스폰할 VolumetricBlood VFX 프리팹.")]
    [SerializeField] private GameObject _hitBloodVfxPrefab;
    [Tooltip("Blood VFX 스케일 배율.")]
    [SerializeField] private float _hitBloodVfxScale = 1f;
    [Tooltip("플레이어 발 기준 Blood VFX 높이 오프셋.")]
    [SerializeField] private float _hitBloodVfxHeightOffset = 1f;

    [Header("Combat Tuning")]
    [Tooltip("모든 공격 애니메이션 속도에 곱해지는 전역 배율. 레벨 디자인용 (기본값 1.0).")]
    [Range(0.1f, 3f)]
    [SerializeField] private float _globalAttackAnimSpeedScale = 1.0f;

    [Header("Character & Weapon")]
    [SerializeField] private CharacterData characterData;
    [Tooltip("선택된 유물 클래스 (패시브+고유스킬+외형). 비우면 유물 없음.")]
    [SerializeField] private RelicClassSO relicClass;
    // ⚠ 에디터 실측 도구(CovenantPlayProbeEditor)가 리플렉션으로 이 이름을 쓴다 — 이름을 바꾸지 말 것.
    [SerializeField] private bool debugInvincible = false;

    [Header("Camera")]
    [SerializeField] private CinemachineFreeLook cinemachineCamera;

    [Header("Status Effect")]
    [Tooltip("빙결 상태(얼음 쉴드+스크린 이펙트) 동안 반복 재생할 사운드")]
    [SerializeField] private AudioClip _freezeLoopSfx;

    [Header("Debug (ReadOnly)")]
    [SerializeField] private LocoState locoStateDebug;
    [SerializeField] private ActState actStateDebug;

    // ── Private: 소유 객체 ────────────────────────────────────────
    private readonly PlayerFacing           _facing          = new();
    private readonly PlayerAim              _aim             = new();
    private readonly PlayerWallContact      _wallContact     = new();
    private readonly CharacterPassiveRunner _passives        = new();
    private readonly RelicAppearance        _relicAppearance = new();
    private PerfectDodgeController _perfectDodge;   // this가 필요해 지연 생성(→ PerfectDodge)
    private PlayerStatusEffects    _status;         // 직렬화 값이 필요해 지연 생성(→ Status)
    private RuneEffectDispatcher   _runeEffects;    // 멀린 룬 속성 단계 효과(→ RuneEffects)

    private AnimatorOverrideService      _animSvc;
    private IAttackInputPolicy           _attackPolicy;   // 입력 정책(무기 타입별: 소드/활 등)
    private PlayerInputActions           _inputActions;
    private LayerStateMachine<LocoState> _locoSM;
    private LayerStateMachine<ActState>  _actSM;
    private CapsuleCollider              _capsule;        // 벽/계단 접촉 높이 기준. 물리 콜백에서 GetComponent 금지.

    // ── Private: 생존·유물·구독 ───────────────────────────────────
    private float _invincibleEnd;     // 아이템 효과: 시간 제한 무적 (DeathNegate 등)
    private bool  _dead;              // 사망 처리 1회 가드 (씬 전환 시 새 인스턴스라 리셋 불필요)
    private bool  _relicApplied;      // 유물 이중 적용 차단
    private bool  _aeSubscribed;
    // 틱 피해 연출 중복 억제 — 같은 공격자의 Light가 이 창 안에 다시 오면 피해만 넣고 연출은 생략한다.
    private const float LightHitRepeatWindow = 0.3f;    // 실시간 초
    private const float MediumHitHpRatio     = 0.08f;   // Auto 판정: 최종 피해/최대 HP가 이 이상이면 Medium
    private const float HeavyHitHpRatio      = 0.20f;   //                               이 이상이면 Heavy
    private GameObject _lastLightHitAttacker;
    private float      _lastLightHitAt = -1f;
    // 스킬 봉인 남은 시간(실시간 초) — SkillType 순서(Q, E, R). 보스 기믹이 건다(→ SealSkill).
    private readonly float[] _skillSealRemaining = new float[3];

    // ── Private: 입력 ─────────────────────────────────────────────
    // 입력 액션 생성이 끝났는가. 분신은 입력을 만들지 않으므로 끝까지 false다.
    private bool _inputInitialized;
    // 등장 연출 등이 컨트롤러 전체를 잠시 멈춘 상태. 초기화 여부와 별개 채널이다(→ SetControlSuspended).
    private bool _controlSuspended;
    // 외부(컷신 등)가 요청한 입력 차단이 유효한지. InitInputActions()가 이 의도를 존중한다.
    // ⚠ 테스트 허브 진단 메뉴(TestHubDebugMenu)가 리플렉션으로 이 이름을 읽는다 — 이름을 바꾸지 말 것.
    private bool _inputDisabledExternally;
    // 차단형 UI 팝업이 걸어둔 입력 차단. 컷신 차단과 독립.
    private bool _inputBlockedByUI;

    // 이동 기준으로 삼는 카메라 수평각. 입력을 누르고 있는 동안 고정된다(CheckMovementInput 참조).
    private float   _moveBasisYaw;
    private Vector2 _lastMoveInput;
    // 직전 프레임 카메라 heading. 한 프레임에 크게 튀는 '불연속 스냅'(방 전환의 SetHeadingImmediate 등) 감지용.
    private float   _prevCamYaw;
    private Vector3 _moveDirection;

    // ── Private: 이동 ─────────────────────────────────────────────
    // 공격·스킬·잡기가 거는 이동 잠금 채널. 슬로우는 PlayerStatusEffects의 별도 채널이다(→ MoveScale).
    private float   _actionMoveScale = 1f;
    private bool    _runAfterDash;
    // 다음 로코모션 진입(MoveBlend) 크로스페이드 길이 1회 오버라이드. -1이면 각 상태의 기본값 사용.
    // 회피 종료처럼 '자세 차이가 큰 상태에서 복귀'할 때만 길게 잡아 툭 튀는 스냅을 없앤다.
    private float   _pendingLocoBlend = -1f;
    // 날아감 진입 방향 — LaunchFrom이 세팅하고 LocoLaunchedState.Enter가 1회 소비한다.
    private Vector3 _pendingLaunchDir;
    // 스텝 오르기 — 상승 중에는 잠깐 공중 판정이 떠도(groundCheckDistance < 스텝높이) 낙하/공중 상태로
    // 전이하지 않도록 억제한다. StepClimb가 상승하는 프레임마다 MarkStepClimbing() 갱신.
    private float   _stepClimbUntil;

    // ── Private: 스킬 스탠스 ──────────────────────────────────────
    // 시전 중 '넘어지지 않고 버티는' 상태. 스킬마다 하드코딩하지 않도록 얇은 계층으로 둔다.
    //
    // 소유자 토큰을 두는 이유 — 스탠스가 겹치면 방어 가산이 누적되고, 먼저 끝난 스킬이
    // 나중 스킬의 스탠스를 꺼버린다. 한 번에 하나만 유효하게 만든다.
    private object _stanceOwner;
    private float  _stanceDefenseAdd;

    // ── Properties: 데이터 · 소유 객체 ─────────────────────────────
    public CharacterData CharacterData => characterData;
    public RelicClassSO  RelicClass    => relicClass;
    public float GlobalAttackAnimSpeedScale => _globalAttackAnimSpeedScale;

    /// <summary>추적 중인 FreeLook 카메라. 전투 동적 프레이밍 등이 읽는다.</summary>
    public CinemachineFreeLook CinemachineCamera => cinemachineCamera;

    /// <summary>현재 적용된 유물 행동 객체 (없으면 null). HolyShield 등 스킬/외부가 참조.</summary>
    public IRelicBehavior RelicBehavior { get; private set; }

    /// <summary>런타임 실시간 스탯 (HUD는 이걸 구독). ⚠ 에디터 실측 도구가 리플렉션으로 읽는다.</summary>
    public PlayerRuntimeStats RuntimeStats { get; private set; } = new PlayerRuntimeStats();

    /// <summary>멀린 룬 속성 단계 효과 디스패처 (단계 도달 시 MerlinRuneBridge가 Activate)</summary>
    public RuneEffectDispatcher RuneEffects => _runeEffects ??= new RuneEffectDispatcher(this);
    /// <summary>이미 생성된 룬 디스패처(없으면 null). 지연 생성을 강제하지 않는 읽기 전용 조회 — 버프창 수집용.</summary>
    public RuneEffectDispatcher RuneEffectsOrNull => _runeEffects;

    /// <summary>플레이어가 가지는 무기 매니저 (런타임에 부착)</summary>
    public PlayerWeaponManager WeaponManager { get; private set; }
    /// <summary>무기 장착 소켓(오른손 / 왼손).</summary>
    public Transform HandTransform     { get; private set; }
    public Transform HandTransformLeft { get; private set; }

    /// <summary>애니메이션 이벤트 리시버</summary>
    public PlayerAnimationEventReceiver EventReceiver { get; private set; }
    /// <summary>장비 이펙트 생성을 관리하는 핸들러</summary>
    public WeaponEffectHandler EffectHandler { get; private set; }

    public InputBuffer          InputBuffer     { get; private set; }
    public IMoveAbility         MoveAbility     { get; private set; }
    /// <summary>접지 판정·중력(낙하·호버) 담당. 점프는 폐기됐다(구 JumpAbility).</summary>
    private IGroundingAbility   Grounding       { get; set; }
    /// <summary>콤보 상태 (콤보 관련 상태는 ComboController에 위임)</summary>
    public ComboController      Combo           { get; private set; }
    /// <summary>포이즈(아머치) 게이지 — 누적 임팩트가 최대치를 넘으면 날아감(LocoState.Launched) 발동.</summary>
    private PoiseController     Poise           { get; set; }
    /// <summary>스태미너 게이지 — 대시(회피)의 자원 게이트. 쿨타임을 대체한다.</summary>
    public StaminaController    Stamina         { get; private set; }
    public SkillCooldownTracker CooldownTracker { get; private set; } = new SkillCooldownTracker();

    public LayerStateMachine<LocoState> LocoSM => _locoSM;

    private PerfectDodgeController PerfectDodge => _perfectDodge ??= new PerfectDodgeController(this);
    private PlayerStatusEffects    Status       => _status       ??= new PlayerStatusEffects(this, _freezeLoopSfx);

    // ── Properties: 공유 상태 (상태 클래스·어빌리티·스킬이 읽고 쓴다) ──────────
    public WeaponActionType CurrentAttackTypeForEffect { get; set; }
    /// <summary>현재 활성화된 실행 컨텍스트 (공격/스킬 상태가 Enter 시 할당, Exit 시 해제)</summary>
    public AbilityExecution ActiveExecution { get; set; }
    /// <summary>스킬 버프: 기본공격 시 추가 발사 횟수 (0이면 비활성)</summary>
    public int   ExtraShotCount   { get; set; }
    /// <summary>회피 쿨다운 종료 시각 (Time.time 기준). 0이면 즉시 사용 가능</summary>
    public float DodgeCooldownEnd { get; set; } = 0f;
    /// <summary>현재 달리기 중인지(LocoMoveState가 결정·설정, DefaultMoveAbility가 속도에 사용).</summary>
    public bool  IsRunning        { get; set; }
    /// <summary>걷기→달리기 속도 램프 진행도(0=걷기, 1=달리기). LocoMoveState가 설정, DefaultMoveAbility가 속도 보간에 사용.</summary>
    public float RunBlend01       { get; set; }
    /// <summary>이동 능력이 이번 프레임 겨냥한 목표 속도를 runMax 기준 0~1로 정규화(급반전 제동 적용 전).
    /// DefaultMoveAbility가 설정, LocoMoveState가 애니 블렌드 하한의 상한값으로 사용.</summary>
    public float IntendedSpeed01  { get; set; }
    /// <summary>착지 애니메이션 재생 중 여부. LocoAirState가 관리.</summary>
    public bool  IsLanding        { get; set; }

    public Vector3 MoveDirection => _moveDirection;

    /// <summary>보스 잡기 패턴에 붙들려 있는 중인가. 이 동안은 포이즈 브레이크(날아감)를 발동하지 않는다(→ SetGrabbed).</summary>
    private bool IsGrabbed    { get; set; }
    /// <summary>분신(ShadowClone)으로 동작 중인 경우. 카메라/입력/PlayerManager 등록 생략.</summary>
    public bool IsShadowClone { get; private set; }

    // ── Events ────────────────────────────────────────────────────
    public event Action OnDamageTaken;
    /// <summary>회피 무적 창 시작(true)/종료(false) 신호. 시각 "안전" 피드백(머티리얼 블링크/잔상 등) 구독용.</summary>
    public event Action<bool> OnDodgeIFrame;
    /// <summary>회피 시작(Enter)/종료(Exit) 신호. 대시 먼지·트레일 등 i-frame 창과 무관하게
    /// 회피 동작 전체에 걸리는 시각 연출 구독용(DodgePresentation).</summary>
    public event Action OnDodgeStart;
    public event Action OnDodgeEnd;
    /// <summary>저스트 회피 반격 창의 공격이 원인 적을 겨냥해 나갔다(출발·도착 지점, 창의 첫 공격 여부) — 연출 구독용.</summary>
    public event Action<Vector3, Vector3, bool> OnCounterStrike;
    /// <summary>
    /// 피격 연출 신호(등급, 가해자→피해자 방향, 최종 피해) — PlayerHitPresentation · HUD 비네트 구독용.
    /// 실제 피해가 들어갔을 때만 오고, 틱 중복 억제로 연출을 생략한 피격은 오지 않는다(피해 자체는 <see cref="OnDamageTaken"/>).
    /// </summary>
    public event Action<HitWeight, Vector3, int> OnHitTaken;
    /// <summary>스킬이 봉인됐다(슬롯, 남은 실시간 초) — HUD 잠금 표시용. 이미 봉인 중에 연장돼도 다시 온다.</summary>
    public event Action<SkillType, float> SkillSealed;
    /// <summary>스킬 봉인이 풀렸다.</summary>
    public event Action<SkillType> SkillUnsealed;

    // ── Lifecycle ─────────────────────────────────────────────────
    protected override async UniTask InitAsync(CancellationToken ct)
    {
        await base.InitAsync(ct);

        // 회전 목표 초기값 — 기본값은 (0,0,0,0) 영 쿼터니언이라 AimForward가 방향을 못 낸다.
        _facing.Reset(transform.rotation);

        InputBuffer = new InputBuffer(new UnscaledClock(), capacity: 16, bufferWindowSec: 0.4f, dedupeSec: 0.04f);
        Combo   = new ComboController();
        Poise   = new PoiseController();
        Stamina = new StaminaController();

        CachePlayerComponents();
        await InitCharacterDataAsync();

        // 비동기 로드 중 오브젝트가 파괴된 경우 중단한다.
        // 여기서 멈추지 않으면 InitInputActions()가 파괴된 객체 위에 PlayerInputActions를 생성·Enable하지만
        // OnDestroy는 이미 입력 액션이 null인 상태로 지나가, 해당 인스턴스가 Dispose되지 못해 누수 경고가 발생한다.
        if (ct.IsCancellationRequested) return;

        InitInputActions();
        InitAbilities();
        InitWeaponManager();
        SetupCamera();

        _locoSM = new LayerStateMachine<LocoState>(this);
        _actSM  = new LayerStateMachine<ActState>(this);
        RegisterStates();
        _locoSM.Change(LocoState.Idle);
        _actSM.Change(ActState.None);

        // 애니메이터 오버라이드 서비스 초기화
        _animSvc = new AnimatorOverrideService(anim);

        // 캐릭터 데이터에 지정된 애니메이션 클립으로 오버라이드 (Q스킬 등)
        ApplyCharacterAnimationOverrides();

        // 이펙트 핸들러 초기화
        EffectHandler = new WeaponEffectHandler(this);

        EventReceiver =
            GetComponent<PlayerAnimationEventReceiver>() ??
            GetComponentInChildren<PlayerAnimationEventReceiver>() ??
            gameObject.AddComponent<PlayerAnimationEventReceiver>();

        EventReceiver.SetTarget(this);
        SubscribeToAnimationReceiver(EventReceiver);

        if (WeaponManager != null)
        {
            WeaponManager.OnWeaponChanged += OnWeaponChangedApplyAnimation;
            WeaponManager.OnWeaponChanged += OnWeaponChangedApplyStats;
            WeaponManager.OnEquippedWeaponRefreshed += OnEquippedWeaponRefreshedApplyStats;
        }

        AutoSetIdleIfNoAction();
        ApplyRelic();

        if (!IsShadowClone)
            AddPresentationComponents();

        if (_inputInitialized) BindInputActions();
    }

    private void OnEnable()
    {
        // 비활성화(OnDisable에서 구독 해제) 뒤 다시 켜질 때 애니 이벤트를 되살린다.
        // 첫 활성화 때는 InitAsync가 이미 구독했거나(가드로 무시) 아직 리시버가 없다(InitAsync가 구독).
        SubscribeToAnimationReceiver(EventReceiver);

        // 타격 통지 — 저스트 회피 반격 창의 적중 환급에 쓴다.
        HitFeedbackService.OnHit += HandleHitFeedback;
    }

    private void Update()
    {
        if (!_inputInitialized || _controlSuspended || characterData == null || cinemachineCamera == null) return;

        _runeEffects?.Tick(Time.deltaTime);
        GameRunBootstrapper.Instance?.Run?.CovenantHandler?.Tick(Time.deltaTime);
        GameRunBootstrapper.Instance?.Run?.EffectManager?.OnTick(Time.deltaTime);   // 아이템 타임드/동적 효과 구동
        TickSkillSeals();   // 빙결 중에도 흘러야 하므로 빙결 조기 반환보다 앞

        // 상태이상 타이머(넉백·슬로우·얼음·빙결). 빙결 중이면 이번 프레임 입력·행동을 멈춘다.
        if (Status.Tick(Time.deltaTime, Time.unscaledDeltaTime))
        {
            _moveDirection = Vector3.zero;
            return;
        }
        _attackPolicy?.Tick(this, Time.unscaledDeltaTime);
        InputBuffer?.TickPrune();
        CheckMovementInput();
        // Combo.Tick을 FSM Update보다 먼저 실행해 actSM이 최신 창 상태를 즉시 반영하도록 한다
        Combo.Tick(Time.unscaledDeltaTime);

        // 포이즈 회복/넉백 면역 타이머
        if (CharacterData != null && RuntimeStats != null)
        {
            Poise.Tick(Time.deltaTime, RuntimeStats.MaxPoise,
                       CharacterData.poiseRegenDelay, CharacterData.poiseRegenPerSec);

            // 스태미너 회복 — 지연 경과 후 초당 회복(원신·명조 방식)
            Stamina.Tick(Time.deltaTime, RuntimeStats.MaxStamina, CharacterData.staminaRegenPerSec);
        }

        // 저스트 회피 슬로모 만료 (unscaled — 느려진 시간에 영향받지 않아야 한다)
        PerfectDodge.Tick();
        RouteInputs();

        _locoSM?.Update();
        _actSM?.Update();

        UpdateLocomotionDirection();

        if (_locoSM != null) locoStateDebug = _locoSM.CurrentId;
        if (_actSM != null) actStateDebug = _actSM.CurrentId;

        CooldownTracker.Tick(Time.unscaledDeltaTime);

    }

    private void FixedUpdate()
    {
        if (characterData == null) return;
        if (IsShadowClone) return;

        Grounding?.UpdateGroundCheck(this);
        Grounding?.ApplyGravity(this);
        FreezeRotation();
        _facing.Apply(Rigid);

        // 계단 오르기: FixedUpdate에서 실행. 상승 중엔 잠깐 공중 판정이 떠도(groundCheckDistance < 스텝높이)
        // 계속 호출해야 상승이 끊겨 떨어지는 진동을 막는다 → IsGrounded 또는 IsStepClimbing이면 호출.
        if ((IsGrounded() || IsStepClimbing) && _moveDirection.sqrMagnitude > 0.01f)
            MoveAbility?.StepClimb(this, _moveDirection.normalized);
    }

    private void OnCollisionStay(Collision collision)
    {
        // 캡슐 바닥 기준 높이로 계단과 벽을 가른다. 콜라이더를 아직 못 잡았으면 루트 높이로 폴백.
        float bottomY = _capsule != null ? _capsule.bounds.min.y : transform.position.y;
        _wallContact.Collect(collision, bottomY);
    }

    private void OnDisable()
    {
        UnsubscribeFromAnimationReceiver(EventReceiver);
        HitFeedbackService.OnHit -= HandleHitFeedback;
        Status.StopFreezeLoopSfx();

        // 저스트 회피 슬로모가 걸린 채 비활성화되면 시간이 느린 상태로 남는다 → 반드시 해제.
        PerfectDodge.Clear();
    }

    private void OnDestroy()
    {
        if (_inputActions != null)
        {
            _inputActions.Player.Disable();
            _inputActions.Disable();
            _inputActions.Dispose();
        }

        UnsubscribeFromAnimationReceiver(EventReceiver);

        if (WeaponManager != null)
        {
            WeaponManager.OnWeaponChanged -= OnWeaponChangedApplyAnimation;
            WeaponManager.OnWeaponChanged -= OnWeaponChangedApplyStats;
            WeaponManager.OnEquippedWeaponRefreshed -= OnEquippedWeaponRefreshedApplyStats;
        }

        RelicBehavior?.OnDetach(this);
        _relicAppearance.ReleaseAura();

        _runeEffects?.Detach();
    }

    // ── Private: 초기화 ───────────────────────────────────────────
    private void AutoSetIdleIfNoAction()
    {
        if (_actSM != null && _actSM.CurrentId == ActState.None && !Combo.IsAttacking)
        {
            if (_locoSM.CurrentId != LocoState.Move &&
                _locoSM.CurrentId != LocoState.Air &&
                _locoSM.CurrentId != LocoState.Dodge &&
                _locoSM.CurrentId != LocoState.Launched)   // 날아감은 자체적으로 착지까지 유지
            {
                _locoSM.Change(LocoState.Idle);
            }
        }
    }

    private void CachePlayerComponents()
    {
        if (!IsShadowClone)
            Managers.Player.SetPlayer(transform);

        TryGetComponent(out _capsule);   // 벽/계단 접촉 높이 판정 기준
        HandTransform = Util.FindDeepChild(transform, "WeaponMount")
                     ?? Util.FindDeepChild(transform, "WeaponSocket");
        HandTransformLeft = Util.FindDeepChild(transform, "WeaponMountLeft")
                         ?? Util.FindDeepChild(transform, "Cup_L")
                         ?? Util.FindDeepChild(transform, "Weapon_l")
                         ?? Util.FindDeepChild(transform, "hand_l");
        if (HandTransform == null) Debug.LogWarning("WeaponMount/WeaponSocket 트랜스폼을 찾지 못했습니다.");
    }

    private void InitWeaponManager()
    {
        WeaponManager = GetComponent<PlayerWeaponManager>() ?? gameObject.AddComponent<PlayerWeaponManager>();
        WeaponManager.Initialize(this);
    }

    private void InitAbilities()
    {
        MoveAbility = new DefaultMoveAbility();
        Grounding = new DefaultGroundingAbility(characterData);
    }

    /// <summary>이동·행동 상태 등록 — 모든 유물이 같은 상태 세트를 쓴다(유물 차이는 IRelicBehavior가 맡는다).</summary>
    private void RegisterStates()
    {
        _locoSM.Register(LocoState.Idle,     new LocoIdleState());
        _locoSM.Register(LocoState.Move,     new LocoMoveState());
        _locoSM.Register(LocoState.Air,      new LocoAirState());
        _locoSM.Register(LocoState.Dodge,    new LocoDodgeState());
        _locoSM.Register(LocoState.Launched, new LocoLaunchedState());

        _actSM.Register(ActState.None,        new ActNoneState());
        _actSM.Register(ActState.AttackReady, new ActAttackReadyState());
        _actSM.Register(ActState.Attack,      new ActAttackState());
        _actSM.Register(ActState.QSkill,      new ActSkillState(SkillType.Q, WeaponActionType.QSkill));
        _actSM.Register(ActState.ESkill,      new ActSkillState(SkillType.E, WeaponActionType.ESkill));
        _actSM.Register(ActState.RSkill,      new ActSkillState(SkillType.R, WeaponActionType.RSkill));

        _locoSM.Change(IsGrounded() ? LocoState.Idle : LocoState.Air);
        _actSM.Change(ActState.None);
    }

    private void SetupCamera()
    {
        if (IsShadowClone) return;
        if (cinemachineCamera == null)
            cinemachineCamera = FindFirstObjectByType<CinemachineFreeLook>();

        if (cinemachineCamera != null)
        {
            cinemachineCamera.Follow = transform;
            cinemachineCamera.LookAt = transform;
        }
    }

    /// <summary>플레이어 본체에 런타임으로 붙이는 연출·카메라 보조 컴포넌트(분신 제외).</summary>
    private void AddPresentationComponents()
    {
        if (!TryGetComponent<DodgePresentation>(out _))
            gameObject.AddComponent<DodgePresentation>();
        if (!TryGetComponent<PlayerHitPresentation>(out _))
            gameObject.AddComponent<PlayerHitPresentation>();   // DodgePresentation 뒤 — 몸 틴트 소유 여부를 본다
        if (!TryGetComponent<StaminaBarView>(out _))
            gameObject.AddComponent<StaminaBarView>();
        if (!TryGetComponent<CombatCameraFraming>(out _))
            gameObject.AddComponent<CombatCameraFraming>();
        if (!TryGetComponent<CameraRigAnchor>(out _))
            gameObject.AddComponent<CameraRigAnchor>();
        if (!TryGetComponent<PlayerWeaponTrailVfx>(out _))
            gameObject.AddComponent<PlayerWeaponTrailVfx>();
        if (!TryGetComponent<AimTargetIndicator>(out _))
            gameObject.AddComponent<AimTargetIndicator>();
    }

    /// <summary>
    /// CharacterData 에 지정된 키들로 AnimatorOverrideController 클립을 교체한다.
    /// 키가 비어 있으면 기본 클립(컨트롤러에 바인딩된 원본) 그대로 사용.
    /// 캐릭터별 Q스킬 등 고유 모션을 적용하는 통로.
    /// </summary>
    private void ApplyCharacterAnimationOverrides()
    {
        if (_animSvc == null || characterData == null) return;

        TryOverrideClip("QSkill_01", characterData.QSkillClipKey);
    }

    private void TryOverrideClip(string stateName, string addressableKey)
    {
        if (string.IsNullOrEmpty(addressableKey)) return;

        var clip = Managers.AnimationResources?.GetClip(addressableKey);
        if (clip == null)
        {
            Debug.LogWarning($"[PlayerController] AnimationClip '{addressableKey}' 로드 실패 — '{stateName}' 기본 클립 유지");
            return;
        }

        if (!_animSvc.Override(stateName, clip))
            Debug.LogWarning($"[PlayerController] '{stateName}' state 가 컨트롤러에 없어 override 스킵");
    }

    private async UniTask InitCharacterDataAsync()
    {
        // CharacterData SO 확보 (서버 데이터 사용 여부와 무관하게 필요)
        var preloaded = Managers.CharacterData?.M_CharacterData;
        if (preloaded != null)
        {
            characterData = preloaded;
            characterData.Initialize();
        }
        else if (characterData != null)
        {
            // 프리팹에 베이스 CharacterData가 직접 지정된 경우(범용 바디 등):
            // 이름 기반 Addressables 로드 대신 지정된 SO를 그대로 사용한다.
            // (GameObject 이름/스폰 키가 바뀌어도 안전 — 이름 의존성 제거)
            characterData.Initialize();
        }
        else
        {
            // SO가 없으면 Addressables에서 로드 (키 형식: "MageData", "KnightData" 등)
            string characterName = gameObject.name.Replace("(Clone)", "");
            var loaded = await PlayerDataResolver.LoadAsync(characterName + "Data", RuntimeStats);
            if (loaded != null) characterData = loaded;
        }

        // 무유물 기본 스탯은 SO(범용 바디)에서 초기화 — 유물 착용 시 ApplyRelic이 서버 char_id 행으로 교체.
        if (characterData != null)
        {
            RuntimeStats.InitializeFrom(characterData);
            Debug.Log($"[PlayerController] 무유물 기본 스탯(SO): {characterData.characterName}");
        }

        if (Rigid != null)
        {
            Rigid.useGravity = false;
            // 공격 lunge 등 MovePosition 호출 시 시각적 보간을 위해 Interpolate 강제
            if (Rigid.interpolation == RigidbodyInterpolation.None)
                Rigid.interpolation = RigidbodyInterpolation.Interpolate;
            if (characterData != null)
                Rigid.linearDamping = characterData.groundDrag;
        }
    }
}
