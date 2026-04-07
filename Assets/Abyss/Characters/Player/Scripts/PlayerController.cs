//============================================================
// 네임스페이스 및 의존성
//============================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using Cinemachine;
using Cysharp.Threading.Tasks;

// 입력 버퍼
using Game.Inputs;

public class PlayerController : CharacterBase
{
    //============================================================
    // Public API (Combat / HUD)
    //============================================================

    // 무기 장착 여부 검사 유틸
    public bool CanAttack()
    {
        return WeaponManager != null && WeaponManager.HasWeapon;
    }

    // PendingAttack 설정 (무기가 없으면 무시)
    public void SetPendingAttack(Command cmd)
    {
        if (!CanAttack())
        {
            // (선택) 디버그 메시지 또는 UI 안내 호출
            Debug.Log("[PlayerController] 공격 시도했지만 무기가 없습니다.");
            return;
        }

        PendingAttackCommand = cmd;
    }

    public Command PendingAttackCommand { get; private set; } = Command.None;
    public bool HasPendingAttack => PendingAttackCommand != Command.None;

    public WeaponActionType CurrentAttackTypeForEffect { get; set; }

    // PendingAttack 초기화
    public void ClearPendingAttack()
    {
        PendingAttackCommand = Command.None;
    }

    //============================================================
    // Character / Runtime Stats (HUD)
    //============================================================
    [Header("Character & Weapon")]
    [SerializeField] protected CharacterData characterData;
    [Header("Debug")]
    [SerializeField] private bool debugInvincible = true;
    public CharacterData CharacterData => characterData;

    // 런타임 실시간 스탯 (HUD는 이걸 구독)
    public PlayerRuntimeStats RuntimeStats { get; private set; } = new PlayerRuntimeStats();

    // 테스트용: 피격/회복
    public void TakeDamage(int dmg)
    {
        if (debugInvincible)
            return;

        RuntimeStats.Damage(dmg);
        FirePassive(PassiveTrigger.OnTakeDamage, new PassiveContext { damage = dmg });
    }
    public void Heal(int amount) => RuntimeStats.Heal(amount);

    public event Action OnHudStatChanged
    {
        add => RuntimeStats.OnChanged += value;
        remove => RuntimeStats.OnChanged -= value;
    }

    //============================================================
    // References (Weapon / Input / Camera / Animation)
    //============================================================

    // 플레이어가 가지는 무기 매니저 (인스펙터에서 붙이거나 런타임에 AddComponent)
    public PlayerWeaponManager WeaponManager { get; private set; }

    protected PlayerInputActions inputActions;
    [System.NonSerialized] public bool inputReady = false;

    [Header("Camera")]
    [SerializeField] protected CinemachineFreeLook cinemachineCamera;

    // 애니메이터 오버라이드 서비스
    private AnimatorOverrideService _animSvc;

    // 애니메이션 이벤트 리시버
    public PlayerAnimationEventReceiver EventReceiver;

    // 장비 이펙트 생성을 관리하는 핸들러
    public WeaponEffectHandler EffectHandler;

    // 현재 활성화된 실행 컨텍스트 (공격/스킬 상태가 Enter 시 할당, Exit 시 해제)
    public AbilityExecution ActiveExecution { get; set; }

    // 회피 쿨다운 종료 시각 (Time.time 기준). 0이면 즉시 사용 가능
    public float DodgeCooldownEnd { get; set; } = 0f;

    private bool _aeSubscribed = false;


    //============================================================
    // Input / Movement State
    //============================================================

    // PlayerController.cs (입력 시 클릭 위치 저장)
    private Vector3? _lastClickedPosition;

    protected Vector3 moveDirection;
    public Vector3 MoveDirection => moveDirection;

    private bool isRunChecked = false;
    public bool IsRunChecked => isRunChecked;

    // 입력 정책(무기 타입별: 소드/활 등)
    private IAttackInputPolicy _attackPolicy;

    //============================================================
    // Layer FSM (Locomotion / Action)
    //============================================================
    protected LayerStateMachine<LocoState> locoSM;
    protected LayerStateMachine<ActState> actSM;
    public LayerStateMachine<LocoState> LocoSM => locoSM;

    [Header("Debug (ReadOnly)")]
    [SerializeField] private LocoState locoStateDebug;
    [SerializeField] private ActState actStateDebug;

    //============================================================
    // Move Scale (MoveLock 제거)
    //============================================================
    public float MoveScale { get; private set; } = 1f;
    public void SetMoveScale(float s) => MoveScale = Mathf.Clamp01(s);

    private float _slowTimer;

    /// <summary>이동 속도를 scale 배율로 duration초 동안 감소시킨다. 종료 시 자동으로 1f로 복구.</summary>
    public void ApplySlow(float scale, float duration)
    {
        SetMoveScale(scale);
        _slowTimer = Mathf.Max(_slowTimer, duration);
    }

    //============================================================
    // Knockback
    //============================================================
    private float _knockbackTimer;
    public bool IsKnockback => _knockbackTimer > 0f;

    /// <summary>외부 힘(넉백)을 가하고 일정 시간 동안 수평 이동 잠금을 스킵한다.</summary>
    public void ApplyKnockback(Vector3 force, float duration = 0.3f)
    {
        Rigid?.AddForce(force, ForceMode.Impulse);
        _knockbackTimer = duration;
    }

    //============================================================
    // Input Buffer & Time
    //============================================================
    protected IClock Clock { get; private set; }
    public InputBuffer InputBuffer { get; private set; }
    protected void EnqueueCommand(Command cmd) => InputBuffer?.Push(cmd);

    //============================================================
    // Ability Modules
    //============================================================
    public IMoveAbility<PlayerController> MoveAbility { get; protected set; }
    public IDodgeAbility<PlayerController> DodgeAbility { get; protected set; }
    public IJumpAbility<PlayerController> JumpAbility { get; protected set; }

    public Transform handTransform;
    public Transform handTransformLeft;

    //============================================================
    // Combo State (콤보 관련 상태는 ComboController에 위임)
    //============================================================
    public ComboController Combo { get; private set; }

    //============================================================
    // Skill Cooldown
    //============================================================
    public SkillCooldownTracker CooldownTracker { get; private set; } = new SkillCooldownTracker();

    //============================================================
    // Passive System
    //============================================================
    private readonly List<ICharacterPassive> _passives = new();

    protected void RegisterPassive(ICharacterPassive passive) => _passives.Add(passive);

    /// <summary>
    /// 트리거 조건이 맞는 패시브를 모두 실행한다.
    /// 상태 클래스 및 외부에서 호출 가능.
    /// </summary>
    public void FirePassive(PassiveTrigger trigger, in PassiveContext ctx)
    {
        foreach (var p in _passives)
            if (p.Trigger == trigger && p.CanApply(this, ctx))
                p.Apply(this, ctx);
    }

    /// <summary>캐릭터별 패시브 등록 — 파생 클래스에서 override.</summary>
    protected virtual void InitPassives() { }

    //============================================================
    // Runtime Flags
    //============================================================
    public bool isGrounded { get; private set; }
    public bool IsGrounded() => isGrounded;

    public bool isJumping { get; private set; }
    public void SetJumping(bool value) { isJumping = value; }

    //============================================================
    // Unity Lifecycle / Initialization
    //============================================================
    protected override async UniTask InitAsync()
    {
        await base.InitAsync();

        Clock = new UnscaledClock();
        InputBuffer = new InputBuffer(Clock, capacity: 16, bufferWindowSec: 0.4f, dedupeSec: 0.04f);
        Combo = new ComboController();

        InitCoreComponents();
        await InitCharacterDataAsync();
        InitInputActions();
        InitAbilities();
        InitWeaponManager();
        SetupCamera();

        locoSM = new LayerStateMachine<LocoState>(this);
        actSM = new LayerStateMachine<ActState>(this);
        InitLayerFSMs();
        locoSM.Change(LocoState.Idle);
        actSM.Change(ActState.None);

        // 애니메이터 오버라이드 서비스 초기화
        _animSvc = new AnimatorOverrideService(anim);

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
        }

        AutoSetIdleIfNoAction();
        InitPassives();

        if (inputReady) BindInputActions();
    }

    protected override void Update()
    {
        if (!inputReady || characterData == null || cinemachineCamera == null) return;

        _knockbackTimer = Mathf.Max(0f, _knockbackTimer - Time.deltaTime);
        if (_slowTimer > 0f)
        {
            _slowTimer = Mathf.Max(0f, _slowTimer - Time.deltaTime);
            if (_slowTimer <= 0f)
                SetMoveScale(1f);
        }
        _attackPolicy?.Tick(this, Time.unscaledDeltaTime);
        InputBuffer?.TickPrune();
        CheckMovementInput();
        // Combo.Tick을 FSM Update보다 먼저 실행해 actSM이 최신 창 상태를 즉시 반영하도록 한다
        Combo.Tick(Time.unscaledDeltaTime);
        RouteInputsToLayers();

        locoSM?.Update();
        actSM?.Update();

        if (locoSM != null) locoStateDebug = locoSM.CurrentId;
        if (actSM != null) actStateDebug = actSM.CurrentId;

        CooldownTracker.Tick(Time.unscaledDeltaTime);

        // ITickablePassive 틱 (시간 기반 스택 만료 등)
        foreach (var p in _passives)
            if (p is ITickablePassive tickable)
                tickable.Tick(Time.deltaTime);
    }

    private void FixedUpdate()
    {
        if (characterData == null) return;

        UpdateGroundedCheck();
        ApplyAirborneGravity();
        FreezeRotation();
    }

    private void OnDisable() => UnsubscribeFromAnimationReceiver(EventReceiver);

    private void OnDestroy()
    {
        if (inputActions != null)
        {
            inputActions.Player.Disable();
            inputActions.Disable();
            inputActions.Dispose();
        }

        UnsubscribeFromAnimationReceiver(EventReceiver);

        if (WeaponManager != null)
        {
            WeaponManager.OnWeaponChanged -= OnWeaponChangedApplyAnimation;
            WeaponManager.OnWeaponChanged -= OnWeaponChangedApplyStats;
        }
    }

    //============================================================
    // Init Helpers
    //============================================================
    private void AutoSetIdleIfNoAction()
    {
        if (actSM != null && actSM.CurrentId == ActState.None && !Combo.IsAttacking)
        {
            if (locoSM.CurrentId != LocoState.Move &&
                locoSM.CurrentId != LocoState.Air &&
                locoSM.CurrentId != LocoState.Dodge)
            {
                locoSM.Change(LocoState.Idle);
            }
        }
    }

    private void InitCoreComponents()
    {
        Managers.Player.SetPlayer(transform);
        handTransform = Util.FindDeepChild(transform, "WeaponMount")
                     ?? Util.FindDeepChild(transform, "WeaponSocket");
        handTransformLeft = Util.FindDeepChild(transform, "WeaponMountLeft")
                         ?? Util.FindDeepChild(transform, "Cup_L")
                         ?? Util.FindDeepChild(transform, "Weapon_l")
                         ?? Util.FindDeepChild(transform, "hand_l");
        if (handTransform == null) Debug.LogWarning("WeaponMount/WeaponSocket 트랜스폼을 찾지 못했습니다.");
    }

    private void InitWeaponManager()
    {
        WeaponManager = GetComponent<PlayerWeaponManager>() ?? gameObject.AddComponent<PlayerWeaponManager>();
        WeaponManager.Initialize(this);
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
        else
        {
            // SO가 없으면 Addressables에서 로드
            string characterName = gameObject.name.Replace("(Clone)", "");
            await LoadCharacterDataAsync(characterName);
        }

        // 스탯 초기화: 서버 우선 → SO 폴백
        bool serverApplied = TryInitFromServer();
        if (!serverApplied && characterData != null)
        {
            RuntimeStats.InitializeFrom(characterData);
            Debug.Log($"[PlayerController] SO 데이터 사용: {characterData.characterName}");
        }

        if (Rigid != null)
        {
            Rigid.useGravity = false;
            if (characterData != null)
                Rigid.linearDamping = characterData.groundDrag;
        }
    }

    /// <summary>서버 PlayerStatEntry로 RuntimeStats 초기화 시도. 성공하면 true.</summary>
    private bool TryInitFromServer()
    {
        var mgr = Managers.PlayerData;
        if (mgr == null || !mgr.IsInitialized || mgr.GetAllPlayers().Count == 0)
            return false;

        // 캐릭터 ID 결정: 로비 선택 or 프리팹 이름
        string charId = null;
        var preloaded = Managers.CharacterData?.M_CharacterData;
        if (preloaded != null)
            charId = preloaded.conClass.ToString().ToLower(); // Knight → knight
        if (string.IsNullOrEmpty(charId))
            charId = gameObject.name.Replace("(Clone)", "").ToLower();

        var entry = mgr.GetPlayer(charId);
        if (entry == null)
            return false;

        var passives = mgr.GetPassives(entry.passive_id);
        RuntimeStats.InitializeFromServer(entry, passives);

        // SO도 여전히 로드해둠 (이동속도, 점프 등 SO 전용 값 필요)
        if (preloaded != null)
        {
            characterData = preloaded;
            characterData.Initialize();
        }

        Debug.Log($"[PlayerController] 서버 데이터 사용: {entry.char_id} (HP:{entry.max_health}, Melee:{entry.base_melee_attack})");
        return true;
    }

    private async UniTask LoadCharacterDataAsync(string characterName)
    {
        if (string.IsNullOrEmpty(characterName))
        {
            Debug.LogError("캐릭터 이름이 비어있습니다.");
            return;
        }

        try
        {
            // UniTask 기반 Addressables 로드
            CharacterData data = await Managers.AddressableManager.LoadAssetAsync<CharacterData>(characterName);

            if (data == null)
            {
                Debug.LogError($"캐릭터 데이터 '{characterName}' 로드 실패");
                return;
            }

            characterData = data;
            Managers.CharacterData.SetCharacterData(data);

            // HUD/전투용 실시간 스탯 초기화 (SO는 템플릿)
            characterData.Initialize();
            RuntimeStats.InitializeFrom(characterData);

            Debug.Log($"캐릭터 데이터 '{characterName}' 로드 완료");
        }
        catch (Exception e)
        {
            Debug.LogError($"캐릭터 데이터 로드 중 예외 발생: {e.Message}");
        }
    }

    private void InitInputActions()
    {
        if (inputActions != null)
        {
            inputActions.Player.Disable();
            inputActions.Disable();
            inputActions.Dispose();
        }
        inputActions = new PlayerInputActions();
        inputActions.Enable();
        inputReady = true;
    }

    protected virtual void InitAbilities()
    {
        MoveAbility = new DefaultMoveAbility();
        DodgeAbility = new DefaultDodgeAbility();
        JumpAbility = new DefaultJumpAbility();
    }

    //============================================================
    // Input Binding
    //============================================================
    protected virtual void BindInputActions()
    {
        if (!inputReady) return;

        inputActions.Player.Attack.started += ctx =>
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            // 스킬 중에는 공격 입력 무시
            bool inSkill = actSM.CurrentId == ActState.QSkill
                        || actSM.CurrentId == ActState.ESkill
                        || actSM.CurrentId == ActState.RSkill;
            if (inSkill) return;

            if (CanAttack())
                _attackPolicy?.OnStarted(this);
            else
                Debug.Log("[Input] Attack started ignored - no weapon");

            if (Physics.Raycast(Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue()), out var hit, 100f, LayerMask.GetMask("Ground")))
                _lastClickedPosition = hit.point;
        };

        inputActions.Player.Attack.canceled += _ =>
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            bool inSkill = actSM.CurrentId == ActState.QSkill
                        || actSM.CurrentId == ActState.ESkill
                        || actSM.CurrentId == ActState.RSkill;
            if (inSkill) return;
            _attackPolicy?.OnCanceled(this);
        };

        inputActions.Player.Run.started += _ => isRunChecked = true;
        inputActions.Player.Run.canceled += _ => isRunChecked = false;

        inputActions.Player.Dodge.performed += _ => InputBuffer.Push(Command.Dodge);
        inputActions.Player.QSkill.performed += _ => InputBuffer.Push(Command.QSkill);
        inputActions.Player.ESkill.performed += _ => InputBuffer.Push(Command.ESkill);
        inputActions.Player.RSkill.performed += _ => InputBuffer.Push(Command.RSkill);

        inputActions.Player.Jump.performed += _ => ProcessJump();
        inputActions.Player.ChangeWeapon1.performed += _ => ChangeWeapon(0);
        inputActions.Player.ChangeWeapon2.performed += _ => ChangeWeapon(1);
    }

    //============================================================
    // Hooks (Derived)
    //============================================================
    protected virtual void InitLayerFSMs() { }
    protected virtual void RouteInputsToLayers() { }

    /// <summary>
    /// 기본 FSM 등록 — 모든 캐릭터가 공유하는 Loco/Act 상태 세트.
    /// 캐릭터별 InitLayerFSMs()에서 호출.
    /// </summary>
    protected void RegisterDefaultFSMs()
    {
        locoSM.Register(LocoState.Idle,  new LocoIdleState());
        locoSM.Register(LocoState.Move,  new LocoMoveState());
        locoSM.Register(LocoState.Air,   new LocoAirState());
        locoSM.Register(LocoState.Dodge, new LocoDodgeState());

        actSM.Register(ActState.None,        new ActNoneState());
        actSM.Register(ActState.AttackReady, new ActAttackReadyState());
        actSM.Register(ActState.Attack,      new ActAttackState());
        actSM.Register(ActState.Charge,      new ActAttackChargeState());
        actSM.Register(ActState.HeavyAttack, new ActHeavyAttackState());
        actSM.Register(ActState.QSkill,      new ActSkillState(SkillType.Q, WeaponActionType.QSkill));
        actSM.Register(ActState.ESkill,      new ActSkillState(SkillType.E, WeaponActionType.ESkill));
        actSM.Register(ActState.RSkill,      new ActSkillState(SkillType.R, WeaponActionType.RSkill));
        actSM.Register(ActState.Plunge,      new ActPlungeState());
        actSM.Register(ActState.Pickup,      new ActPickupState());

        locoSM.Change(IsGrounded() ? LocoState.Idle : LocoState.Air);
        actSM.Change(ActState.None);
    }

    /// <summary>
    /// 기본 입력 라우팅 — 무기 기반 스킬 전환 (캐릭터 공통).
    /// 캐릭터별 RouteInputsToLayers()에서 호출.
    /// </summary>
    protected void DefaultRouteInputsToLayers()
    {
        if (actSM.CurrentId == ActState.Pickup) return;

        bool isInSkill = actSM.CurrentId == ActState.QSkill ||
                         actSM.CurrentId == ActState.ESkill ||
                         actSM.CurrentId == ActState.RSkill;
        bool isDodging = locoSM.CurrentId == LocoState.Dodge;
        bool isInAct   = actSM.CurrentId != ActState.None || isDodging;

        if (InputBuffer.TryConsume(Game.Inputs.Command.QSkill))
        {
            Debug.Log($"[Input] Q pressed: CanAttack={CanAttack()}, isInSkill={isInSkill}, actState={actSM.CurrentId}");
            if (CanAttack() && !isInSkill) actSM.Change(ActState.QSkill);
            return;
        }
        if (InputBuffer.TryConsume(Game.Inputs.Command.ESkill))
        {
            if (CanAttack() && !isInSkill) actSM.Change(ActState.ESkill);
            return;
        }
        if (InputBuffer.TryConsume(Game.Inputs.Command.RSkill))
        {
            if (CanAttack() && !isInSkill) actSM.Change(ActState.RSkill);
            return;
        }

        // 스킬 중에는 공격 관련 입력 소비하고 무시
        if (isInSkill)
        {
            InputBuffer.TryConsume(Game.Inputs.Command.Light);
            InputBuffer.TryConsume(Game.Inputs.Command.Heavy);
            InputBuffer.TryConsume(Game.Inputs.Command.Charge);
        }

        if (InputBuffer.TryConsume(Game.Inputs.Command.Dodge))
        {
            if (!isDodging && UnityEngine.Time.time >= DodgeCooldownEnd)
            {
                if (isInAct) actSM.Change(ActState.None);
                locoSM.Change(LocoState.Dodge);
            }
            return;
        }

        if (isInAct) return;

        if (InputBuffer.TryConsume(Game.Inputs.Command.Charge))
        {
            if (CanAttack()) actSM.Change(ActState.Charge);
            return;
        }
        if (InputBuffer.TryConsume(Game.Inputs.Command.Heavy))
        {
            if (CanAttack()) { SetPendingAttack(Game.Inputs.Command.Heavy); actSM.Change(ActState.AttackReady); }
            return;
        }
        if (InputBuffer.TryConsume(Game.Inputs.Command.Light))
        {
            if (CanAttack()) { SetPendingAttack(Game.Inputs.Command.Light); actSM.Change(ActState.AttackReady); }
            return;
        }
    }

    /// <summary>
    /// 카메라 기준 이동 방향 계산 → moveDirection 갱신.
    /// 공중 상태에서는 지상 방향 갱신을 생략한다.
    /// 다른 이동 방식이 필요한 캐릭터는 override.
    /// </summary>
    protected virtual void CheckMovementInput()
    {
        if (locoSM?.CurrentId == LocoState.Air) return;
        if (cinemachineCamera == null) return;
        if (inputActions == null) return;

        var input   = inputActions.Player.Move.ReadValue<Vector2>();
        var forward = cinemachineCamera.transform.forward; forward.y = 0f;
        var right   = cinemachineCamera.transform.right;   right.y   = 0f;

        moveDirection = (forward.normalized * input.y + right.normalized * input.x).normalized;
    }

    /// <summary>
    /// 공격·스킬 상태 여부 판별 (Safe_OnAttackAnimationEnd 내부 사용).
    /// Knight처럼 추가 상태가 있는 캐릭터는 override해서 포함시킨다.
    /// </summary>
    protected virtual bool IsInAttackOrSkillState() =>
        actSM.CurrentId == ActState.Attack      ||
        actSM.CurrentId == ActState.AttackReady ||
        actSM.CurrentId == ActState.QSkill      ||
        actSM.CurrentId == ActState.ESkill      ||
        actSM.CurrentId == ActState.RSkill;

    //============================================================
    // Weapon Changed → Animation / Policy
    //============================================================
    private void OnWeaponChangedApplyAnimation(WeaponData newWeapon, GameObject weaponInstance)
    {
        // null 무기면 정책 제거
        if (newWeapon == null)
        {
            _attackPolicy = null;
            Debug.Log("[PlayerController] 무기 해제 - 공격 불가 상태로 전환");
            return;
        }

        if (_animSvc == null || newWeapon.animationSet == null || !Managers.AnimationResources.IsInitialized)
        {
            AssignAttackPolicyForWeapon(newWeapon);
            return;
        }

        var animSet = newWeapon.animationSet;
        foreach (var mapping in animSet.GetAllMappings())
        {
            var clip = Managers.AnimationResources.GetClip(mapping.addressableKey);
            if (clip != null) _animSvc.Override(mapping.baseClipName, clip);
        }

        AssignAttackPolicyForWeapon(newWeapon);
    }

    private void OnWeaponChangedApplyStats(WeaponData newWeapon, GameObject _)
    {
        if (newWeapon == null)
        {
            RuntimeStats.SetWeaponStats(0, 0, 0);
            return;
        }

        var kind = newWeapon.weaponType.GetAttackStatKind();
        int melee  = kind == AttackStatKind.Melee  ? (int)newWeapon.baseAttack : 0;
        int ranged = kind == AttackStatKind.Ranged ? (int)newWeapon.baseAttack : 0;
        RuntimeStats.SetWeaponStats(melee, ranged, (int)newWeapon.baseDefense);
    }

    private void AssignAttackPolicyForWeapon(WeaponData wd)
    {
        if (wd == null)
        {
            _attackPolicy = null;
            return;
        }

        switch (wd.weaponType)
        {
            case WeaponType.Katana:
                _attackPolicy = new SwordAttackPolicy(
                    enterThreshold: 1.5f,
                    fullThreshold: wd.holdThreshold,
                    maxChargeStage: wd.chargeStages
                );
                break;

            case WeaponType.Greatsword:
                _attackPolicy = new SwordAttackPolicy(
                    enterThreshold: 3f,
                    fullThreshold: wd.holdThreshold,
                    maxChargeStage: wd.chargeStages
                );
                break;

            case WeaponType.Bow:
            case WeaponType.Crossbow:
                _attackPolicy = new BowAttackPolicy();
                break;

            default:
                _attackPolicy = new SwordAttackPolicy();
                break;
        }
    }

    public void OnAttackHitStep(int stepIndex)
    {
        FirePassive(PassiveTrigger.OnAttackHit,
            new PassiveContext { comboStep = stepIndex });
    }
    public void OnAnimationEventTag(string tag) { /* 구현 */ }

    //============================================================
    // Jump / Air Entry Flag
    //============================================================
    public bool EnterAirAsJump { get; private set; } = false;

    /// <summary>공중 공격 1사이클 사용 여부. 착지 시 리셋.</summary>
    public bool AirAttackUsed { get; set; } = false;

    [Header("Jump Settings")]
    public float jumpForce = 6f;

    public void ProcessJump()
    {
        if (Rigid == null) return;
        // 이미 공중이면 점프 불가
        if (!isGrounded || locoSM.CurrentId == LocoState.Air) return;

        // 점프 의도 표시
        EnterAirAsJump = true;
        isJumping = true;

        // Rigidbody로 점프 힘 적용
        Rigid.linearVelocity = new Vector3(Rigid.linearVelocity.x, 0f, Rigid.linearVelocity.z);
        Rigid.AddForce(Vector3.up * jumpForce, ForceMode.VelocityChange);

        // 상태 전환 요청
        locoSM.Change(LocoState.Air);
    }

    public void ConsumeEnterAirAsJump()
    {
        EnterAirAsJump = false;
    }

    //============================================================
    // Ground Check / Gravity
    //============================================================
    private void UpdateGroundedCheck()
    {
        Vector3 rayOrigin = transform.position + Vector3.up * 0.1f;
        float rayLength = characterData.groundCheckDistance + 0.1f;

        isGrounded =
            Physics.Raycast(rayOrigin, Vector3.down, out var hit, rayLength, characterData.groundLayer) &&
            hit.distance <= characterData.groundCheckDistance + 0.05f;

        Debug.DrawRay(rayOrigin, Vector3.down * rayLength, isGrounded ? Color.green : Color.red);

        // 착지 시점에 점프/공중공격 상태 초기화
        if (isGrounded && isJumping)
            isJumping = false;
        if (isGrounded)
            AirAttackUsed = false;
    }

    /// <summary>공중 공격 중 체공을 위한 중력 감소 비율 (0 = 무중력, 1 = 정상)</summary>
    private const float AirAttackGravityScale = 0.05f;

    /// <summary>공중 공격 진입 시 호출 — 낙하 속도를 즉시 멈추고 체공 시작</summary>
    public void StartAirHover()
    {
        if (Rigid != null && !isGrounded)
        {
            Rigid.linearVelocity = new Vector3(Rigid.linearVelocity.x, 0f, Rigid.linearVelocity.z);
        }
    }

    private void ApplyAirborneGravity()
    {
        if (isGrounded || !Rigid) return;

        float gravityMultiplier = characterData.gravity;
        if (Rigid.linearVelocity.y < 0)
            gravityMultiplier *= characterData.fallMultiplier;

        // 공중 공격 중이면 체공 — AirAttackUsed(공중에서 공격 시작)이고 아직 공격 중일 때만
        bool isAirAttacking = AirAttackUsed && Combo != null && Combo.IsAttacking;
        if (isAirAttacking)
        {
            gravityMultiplier *= AirAttackGravityScale;
            if (Rigid.linearVelocity.y < -1f)
                Rigid.linearVelocity = new Vector3(Rigid.linearVelocity.x, -1f, Rigid.linearVelocity.z);
        }

        Rigid.AddForce(Vector3.up * gravityMultiplier, ForceMode.Acceleration);
    }

    //============================================================
    // Inventory / Weapon / Camera
    //============================================================
    protected virtual void ChangeWeapon(int index)
    {
        if (WeaponManager != null)
            _ = WeaponManager.SwitchToSlotAsync(index);
    }

    protected virtual void SetupCamera()
    {
        if (cinemachineCamera == null)
            cinemachineCamera = FindObjectOfType<CinemachineFreeLook>();

        if (cinemachineCamera != null)
        {
            cinemachineCamera.Follow = transform;
            cinemachineCamera.LookAt = transform;
            ConfigureCameraView();
        }
    }

    protected virtual void ConfigureCameraView() { }

    public void StopHorizontalMovement()
    {
        Rigid.linearVelocity = new Vector3(0f, Rigid.linearVelocity.y, 0f);
    }

    /// <summary>낙하 공격 상태인지 여부 (LocoAirState 착지 처리 분기용)</summary>
    public bool IsPlunging => actSM?.CurrentId == ActState.Plunge;

    /// <summary>픽업 대기 중인 무기 데이터 (WorldWeaponDisplay → ActPickupState 전달용)</summary>
    public WeaponData PendingPickupWeapon { get; set; }

    /// <summary>픽업 소스 오브젝트 (팝업 결과 후 확정/취소 처리용)</summary>
    public WorldWeaponDisplay PendingPickupSource { get; set; }

    /// <summary>무기 픽업 요청 — ActPickupState로 전환</summary>
    public void RequestPickup(WeaponData data, WorldWeaponDisplay source = null)
    {
        if (data == null) return;
        if (actSM == null) return;

        PendingPickupWeapon = data;
        PendingPickupSource = source;
        actSM.Change(ActState.Pickup);
    }

    /// <summary>낙하 공격 진입 시 전달할 데이터 (공격 상태 → ActPlungeState)</summary>
    public struct PlungeInfo
    {
        public string fallClipName;
        public float  fallSpeed;
        public float  descendAt;   // 하강 시작 normalizedTime (0 = 즉시)
    }
    public PlungeInfo PendingPlunge { get; set; }

    /// <summary>
    /// actSM이 None이 아니면 강제로 None으로 전환 (착지·회피 캔슬 시 사용)
    /// </summary>
    public void CancelActState()
    {
        if (actSM != null && actSM.CurrentId != ActState.None)
            actSM.Change(ActState.None);
        Combo?.ResetStep();
    }

    /// <summary>
    /// 현재 이동 입력 방향으로 즉시 회전. 입력이 없으면 유지.
    /// </summary>
    public void RotateTowardsInput()
    {
        if (moveDirection.sqrMagnitude < 0.0001f) return;
        transform.rotation = Quaternion.LookRotation(moveDirection);
    }

    public void RotateTowardsMousePosition()
    {
        // 1) 입력으로 저장된 클릭 위치 우선 사용
        if (_lastClickedPosition.HasValue)
        {
            Vector3 target = _lastClickedPosition.Value;
            Vector3 lookDir = target - transform.position;
            lookDir.y = 0f;

            if (lookDir.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(lookDir);

            _lastClickedPosition = null;
            return;
        }

        // 2) fallback: 마우스 기반 레이캐스트
        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (Physics.Raycast(ray, out var hit, 100f, LayerMask.GetMask("Ground")))
        {
            Vector3 lookDir = hit.point - transform.position;
            lookDir.y = 0f;
            if (lookDir.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(lookDir);
        }
    }

    //============================================================
    // Animation Receiver Subscribe / Unsubscribe
    //============================================================
    private void SubscribeToAnimationReceiver(PlayerAnimationEventReceiver receiver)
    {
        if (receiver == null || _aeSubscribed) return;

        // OnAttackEnd: ActAttackState는 normalizedTime 폴링으로 자체 처리 → 구독 안 함
        // OnOpenCombo / OnCloseCombo: ActAttackState가 폴링으로 처리 → 구독 안 함
        // 아래는 PlayerController가 직접 처리해야 하는 시각적/전역 이벤트만 유지
        receiver.OnAttackEnd  += Safe_OnAttackAnimationEnd; // 스킬 상태 종료용
        receiver.OnHitStep    += Safe_OnHitStep;
        receiver.OnGenericTag += Safe_GenericTag;
        receiver.OnEffectStep += safe_EffectStep;
        receiver.OnBeginTrail += Safe_BeginTrail;
        receiver.OnEndTrail   += Safe_EndTrail;

        _aeSubscribed = true;
    }

    private void UnsubscribeFromAnimationReceiver(PlayerAnimationEventReceiver receiver)
    {
        if (receiver == null || !_aeSubscribed) return;

        receiver.OnAttackEnd  -= Safe_OnAttackAnimationEnd;
        receiver.OnHitStep    -= Safe_OnHitStep;
        receiver.OnGenericTag -= Safe_GenericTag;
        receiver.OnEffectStep -= safe_EffectStep;
        receiver.OnBeginTrail -= Safe_BeginTrail;
        receiver.OnEndTrail   -= Safe_EndTrail;

        _aeSubscribed = false;
    }

    //============================================================
    // Safe Handlers
    //============================================================

    /// <summary>
    /// AE_AttackEnd 애니메이션 이벤트 수신 — 스킬 상태 종료 전용.
    /// ActState.Attack 및 ActState.HeavyAttack은 각 상태가 자체 처리하므로 스킵한다.
    /// </summary>
    private void Safe_OnAttackAnimationEnd()
    {
        // Attack / HeavyAttack은 각 State가 자체적으로 종료를 처리한다
        if (actSM.CurrentId == ActState.Attack ||
            actSM.CurrentId == ActState.HeavyAttack)
            return;

        Combo.SetAttacking(false);

        if (IsInAttackOrSkillState())
            actSM.Change(ActState.None);
    }

    /// <summary>
    /// 콤보가 최대 스텝까지 완료됐을 때 ActAttackState에서 호출된다.
    /// 파생 캐릭터는 override해 캐릭터 전용 로직을 추가할 수 있다.
    /// </summary>
    
    public void NotifyComboFinished(int finalStep)
    {
        OnComboFinished(finalStep);
    }

    protected virtual void OnComboFinished(int finalStep)
    {
        FirePassive(PassiveTrigger.OnComboFinish,
            new PassiveContext { comboStep = finalStep });
    }

    private void Safe_OnHitStep(int stepIndex)
    {
        if (stepIndex < 0) return;
        OnAttackHitStep(stepIndex);
    }

    private void Safe_GenericTag(string tag) => OnAnimationEventTag(tag);

    private void safe_EffectStep(int step)
    {
        if (EffectHandler != null && WeaponManager.HasWeapon)
            _ = EffectHandler.PlayEffect(CurrentAttackTypeForEffect, Combo.CurrentComboStep, step, ActiveExecution);
    }

    /// <summary>공격 state에서 직접 호출 (AnimationEvent 불필요)</summary>
    public void BeginWeaponTrail() => Safe_BeginTrail();
    public void EndWeaponTrail()   => Safe_EndTrail();

    private void Safe_BeginTrail()
    {
        if (WeaponManager == null) return;
        var wi = WeaponManager.GetCurrentWeaponComponent<WeaponInstance>();
        if (wi == null || wi.TrailDetector == null) return;

        float damage = 0f;
        float knockback = 1f;
        float radiusOverride = 0f;

        var weaponData = WeaponManager.CurrentWeaponData;
        if (weaponData != null && weaponData.abilitySet != null)
        {
            var ability = weaponData.abilitySet.GetAbility(CurrentAttackTypeForEffect, Combo.CurrentComboStep);
            if (ability != null)
            {
                foreach (var s in ability.steps)
                {
                    if (s.collider != null && s.collider.mode == WeaponAbilitySO.ColliderMode.Trail)
                    {
                        damage = s.baseDamage > 0f ? s.baseDamage : s.collider.damage;
                        knockback = s.knockbackMultiplier;
                        radiusOverride = s.collider.trailRadiusOverride;
                        break;
                    }
                }
            }

            // 캐릭터 공격 스탯 반영
            var kind = weaponData.weaponType.GetAttackStatKind();
            damage = DamageFormula.Calculate(damage, RuntimeStats.GetEffectiveAttack(kind));
        }

        wi.TrailDetector.OnTrailHit -= OnWeaponTrailHit;
        wi.TrailDetector.OnTrailHit += OnWeaponTrailHit;
        wi.TrailDetector.BeginTrail(damage, knockback, radiusOverride);
    }

    private void Safe_EndTrail()
    {
        if (WeaponManager == null) return;
        var wi = WeaponManager.GetCurrentWeaponComponent<WeaponInstance>();
        if (wi == null || wi.TrailDetector == null) return;

        wi.TrailDetector.EndTrail();
        wi.TrailDetector.OnTrailHit -= OnWeaponTrailHit;
    }

    private void OnWeaponTrailHit(RaycastHit hit, float damage, float knockback)
    {
        if (hit.collider == null) return;
        if (!hit.collider.TryGetComponent<IDamageable>(out var damageable)) return;

        damageable.TakeDamage(damage, gameObject, knockback);

        // 타격 이펙트 스폰
        SpawnTrailHitEffect(hit.point);

        var wt = WeaponManager?.CurrentWeaponData?.weaponType;
        var ctx = new PassiveContext { target = hit.collider.gameObject, damage = damage, weaponType = wt };
        FirePassive(PassiveTrigger.OnAttackHit, ctx);

        if (hit.collider.TryGetComponent<IKillable>(out var killable) && killable.IsDead)
            FirePassive(PassiveTrigger.OnKill, ctx);
    }

    private async void SpawnTrailHitEffect(Vector3 hitPoint)
    {
        var weaponData = WeaponManager?.CurrentWeaponData;
        if (weaponData?.abilitySet == null) return;

        var ability = weaponData.abilitySet.GetAbility(CurrentAttackTypeForEffect, Combo.CurrentComboStep);
        if (ability == null) return;

        // 현재 스텝의 hitEffectKey 찾기
        string hitKey = null;
        float hitScale = 1f;
        foreach (var step in ability.steps)
        {
            if (!string.IsNullOrEmpty(step.hitEffectKey))
            {
                hitKey = step.hitEffectKey;
                hitScale = step.hitEffectScale;
                break;
            }
        }

        if (string.IsNullOrEmpty(hitKey)) return;

        var effectObj = await Managers.ObjectPooler.SpawnAsync(
            hitKey, ObjectPoolerManager.PoolType.Effect, hitPoint, Quaternion.identity);
        if (effectObj == null) return;

        effectObj.transform.localScale = Vector3.one * hitScale;
        if (effectObj.TryGetComponent<EffectBehaviour>(out var eb))
            eb.Initialize(eb.behaviorSO, null, 1f);
    }
}
