//============================================================
// 네임스페이스 및 의존성
//============================================================
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using Cinemachine;
using Cysharp.Threading.Tasks;

// 입력 버퍼
using Game.Inputs;
using System.Collections.Generic;

public class PlayerController : CharacterBase
{
    //============================================================
    // 캐릭터 / 무기 / 입력
    //============================================================
    [Header("Character & Weapon")]
    [SerializeField] protected CharacterData characterData;
    public CharacterData CharacterData => characterData;

    // 플레이어가 가지는 무기 매니저 (인스펙터에서 붙이거나 런타임에 AddComponent)
    public PlayerWeaponManager WeaponManager;

    protected PlayerInputActions inputActions;
    public bool inputReady = false;

    protected Vector3 moveDirection;
    public Vector3 MoveDirection => moveDirection;

    private bool isInventoryOpen = false;
    private bool isRunChecked = false;
    public bool IsRunChecked => isRunChecked;

    // 입력 정책(무기 타입별: 소드/활 등)
    private IAttackInputPolicy _attackPolicy;

    // 애니메이터 오버라이드 서비스
    public AnimatorOverrideService _animSvc;

    // 애니메이션 이벤트 리시버
    public PlayerAnimationEventReceiver EventReceiver;

    //장비 이펙트 생성을 관리하는 핸들러
    public WeaponEffectHandler EffectHandler;

    private bool _aeSubscribed = false;

    //============================================================
    // 레이어 FSM (Locomotion / Action)
    //============================================================
    protected LayerStateMachine<LocoState> locoSM;
    protected LayerStateMachine<ActState> actSM;
    public LayerStateMachine<LocoState> LocoSM => locoSM;

    [Header("Debug (ReadOnly)")]
    [SerializeField] private LocoState locoStateDebug;
    [SerializeField] private ActState actStateDebug;

    //============================================================
    // 이동 관련 스케일 (MoveLock 제거)
    //============================================================
    public float MoveScale { get; private set; } = 1f;
    public void SetMoveScale(float s) => MoveScale = Mathf.Clamp01(s);

    [Header("Camera")]
    [SerializeField] protected CinemachineFreeLook cinemachineCamera;

    //============================================================
    // 입력 버퍼 & 시간축
    //============================================================
    protected IClock Clock { get; private set; }
    public InputBuffer InputBuffer { get; private set; }

    protected void EnqueueCommand(Command cmd) => InputBuffer?.Push(cmd);

    //============================================================
    // 어빌리티 모듈
    //============================================================
    public IMoveAbility<PlayerController> MoveAbility { get; protected set; }
    public IDodgeAbility<PlayerController> DodgeAbility { get; protected set; }
    public IJumpAbility<PlayerController> JumpAbility { get; protected set; }

    public Transform handTransform;

    //============================================================
    // 런타임 플래그
    //============================================================
    public bool isAttacking = false;
    public bool nextComboQueued = false;

    public bool isGrounded;
    public bool IsGrounded() => isGrounded;

    public bool isJumping;

    private void FreezeRotation() => Rigid.angularVelocity = Vector3.zero;

    public WeaponActionType CurrentAttackTypeForEffect;

    

    //============================================================
    // 초기화
    //============================================================
    private async void Start() => await InitAsync();

    protected override async UniTask InitAsync()
    {
        await base.InitAsync();

        Clock = new UnscaledClock();
        InputBuffer = new InputBuffer(Clock, capacity: 16, bufferWindowSec: 0.18f, dedupeSec: 40f);

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

        EventReceiver = GetComponent<PlayerAnimationEventReceiver>() ?? GetComponentInChildren<PlayerAnimationEventReceiver>() ?? gameObject.AddComponent<PlayerAnimationEventReceiver>();
        EventReceiver.SetTarget(this);
        SubscribeToAnimationReceiver(EventReceiver);

        if (WeaponManager != null)
        {
            WeaponManager.OnWeaponChanged += OnWeaponChangedApplyAnimation;
        }

        AutoSetIdleIfNoAction();

        if (inputReady) BindInputActions();
    }

    private void AutoSetIdleIfNoAction()
    {
        if (actSM != null && actSM.CurrentId == ActState.None && !isAttacking)
        {
            if (locoSM.CurrentId != LocoState.Move && locoSM.CurrentId != LocoState.Air && locoSM.CurrentId != LocoState.Dodge)
                locoSM.Change(LocoState.Idle);
        }
    }

    //============================================================
    // 구독 / 해제 헬퍼
    //============================================================
    private void SubscribeToAnimationReceiver(PlayerAnimationEventReceiver receiver)
    {
        if (receiver == null || _aeSubscribed) return;

        receiver.OnAttackEnd += Safe_OnAttackAnimationEnd;
        receiver.OnHitStep += Safe_OnHitStep;
        receiver.OnOpenCombo += Safe_OpenCombo;
        receiver.OnCloseCombo += Safe_CloseCombo;
        receiver.OnGenericTag += Safe_GenericTag;
        receiver.OnEffectStep += safe_EffectStep;

        _aeSubscribed = true;
    }

    private void UnsubscribeFromAnimationReceiver(PlayerAnimationEventReceiver receiver)
    {
        if (receiver == null || !_aeSubscribed) return;

        receiver.OnAttackEnd -= Safe_OnAttackAnimationEnd;
        receiver.OnHitStep -= Safe_OnHitStep;
        receiver.OnOpenCombo -= Safe_OpenCombo;
        receiver.OnCloseCombo -= Safe_CloseCombo;
        receiver.OnGenericTag -= Safe_GenericTag;
        receiver.OnEffectStep -= safe_EffectStep;

        _aeSubscribed = false;
    }

    private void OnDisable() => UnsubscribeFromAnimationReceiver(EventReceiver);
    private void OnDestroy()
    {
        UnsubscribeFromAnimationReceiver(EventReceiver);
        if (WeaponManager != null)
            WeaponManager.OnWeaponChanged -= OnWeaponChangedApplyAnimation;
    }

    //============================================================
    // WeaponChanged 이벤트 콜백
    //============================================================
    private void OnWeaponChangedApplyAnimation(WeaponData newWeapon, GameObject weaponInstance)
    {
        if (newWeapon == null || _animSvc == null || newWeapon.animationSet == null || !Managers.AnimationResources.IsInitialized)
            return;

        var animSet = newWeapon.animationSet;
        foreach (var mapping in animSet.GetAllMappings())
        {
            var clip = Managers.AnimationResources.GetClip(mapping.addressableKey);
            if (clip != null) _animSvc.Override(mapping.baseClipName, clip);
        }

        AssignAttackPolicyForWeapon(newWeapon);
    }

    private void AssignAttackPolicyForWeapon(WeaponData wd)
    {
        if (wd == null) { _attackPolicy = null; return; }

        var key = wd.weaponPrefabKey?.ToLowerInvariant() ?? "";
        if (key.Contains("bow") || key.Contains("arch") || (wd.abilitySet != null && wd.abilitySet.name.ToLower().Contains("bow")))
            _attackPolicy = new BowAttackPolicy();
        else
            _attackPolicy = new SwordAttackPolicy();
    }

    private void InitCoreComponents()
    {
        Managers.Player.SetPlayer(transform);
        handTransform = Util.FindDeepChild(transform, "WeaponSocket");
        if (handTransform == null) Debug.LogWarning("WeaponSocket 트랜스폼을 찾지 못했습니다.");
    }

    private void InitWeaponManager()
    {
        WeaponManager = GetComponent<PlayerWeaponManager>() ?? gameObject.AddComponent<PlayerWeaponManager>();
        WeaponManager.Initialize(this);
    }

    private async UniTask InitCharacterDataAsync()
    {
        string characterName = gameObject.name.Replace("(Clone)", "");
        await LoadCharacterDataAsync(characterName);
        Rigid.useGravity = false;
        Rigid.drag = characterData.groundDrag;
    }

    private async UniTask LoadCharacterDataAsync(string characterName)
    {
        var utcs = new UniTaskCompletionSource<bool>();
        Managers.AddressableManager.LoadAsset<CharacterData>(characterName, data =>
        {
            if (data == null)
            {
                Debug.LogError("캐릭터 데이터가 null입니다.");
                utcs.TrySetResult(false);
                return;
            }

            characterData = data;
            Managers.CharacterData.SetCharacterData(data);
            utcs.TrySetResult(true);
        });

        await utcs.Task;
    }

    private void InitInputActions()
    {
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
    // 입력 바인딩
    //============================================================
    protected virtual void BindInputActions()
    {
        if (!inputReady) return;

        inputActions.Player.Attack.started += _ => _attackPolicy?.OnStarted(this);
        inputActions.Player.Attack.canceled += _ => _attackPolicy?.OnCanceled(this);

        inputActions.Player.Run.started += _ => isRunChecked = true;
        inputActions.Player.Run.canceled += _ => isRunChecked = false;

        inputActions.Player.Dodge.performed += _ => InputBuffer.Push(Command.Dodge);
        inputActions.Player.Skill.performed += _ => InputBuffer.Push(Command.Skill);
        inputActions.Player.Ultimate.performed += _ => InputBuffer.Push(Command.Skill);

        inputActions.Player.Jump.performed += _ => ProcessJump();
        inputActions.Player.InventoryToggle.performed += _ => { ToggleInventory(); if (isInventoryOpen) InputBuffer.Clear(); };
        inputActions.Player.CloseInventory.performed += _ => CloseInventory();
        inputActions.Player.ChangeWeapon1.performed += _ => ChangeWeapon(0);
        inputActions.Player.ChangeWeapon2.performed += _ => ChangeWeapon(1);
    }

    //============================================================
    // 파생 훅
    //============================================================
    protected virtual void InitLayerFSMs() { }
    protected virtual void RouteInputsToLayers() { }
    protected virtual void ProcessJump() { }
    protected virtual void OnSkillAttack() { }
    protected virtual void OnUltimateAttack() { }
    public void UseSkill() => OnSkillAttack();

    //============================================================
    // 콤보 관련
    //============================================================
    public int LightMaxComboCount { get; private set; } = 1;
    public float LightComboResetTime { get; private set; } = 2f;
    public float comboTimeRemaining = 0f;
    public IReadOnlyList<float> LightComboEndTimes { get; private set; }

    public bool comboWindowOpen = false;
    public int currentComboStep = 0;
    public void OpenComboWindow()
    {
        comboWindowOpen = true;
        comboTimeRemaining = LightComboResetTime;
    }
    public void CloseComboWindow() => comboWindowOpen = false;
    public virtual void OnAttackAnimationEnd() => isAttacking = false;

    public void OnAttackHitStep(int stepIndex) { /* 구현 */ }
    public void OnAnimationEventTag(string tag) { /* 구현 */ }

    //============================================================
    // 유니티 생명주기
    //============================================================
    protected override void Update()
    {
        if (!inputReady || characterData == null || cinemachineCamera == null) return;

        _attackPolicy?.Tick(this, Time.unscaledDeltaTime);
        InputBuffer?.TickPrune();
        RouteInputsToLayers();

        locoSM?.Update();
        actSM?.Update();

        if (locoSM != null) locoStateDebug = locoSM.CurrentId;
        if (actSM != null) actStateDebug = actSM.CurrentId;

       
        comboTimeRemaining -= Time.unscaledDeltaTime;
        if (comboTimeRemaining <= 0f)
        {
                // 시간 만료 시 콤보 초기화
            currentComboStep = 0;
            comboWindowOpen = false;
            nextComboQueued = false;
            isAttacking = false;
            comboTimeRemaining = 0f;
        }
        
    }

    private void FixedUpdate()
    {
        if (characterData == null) return;

        UpdateGroundedCheck();
        ApplyMassBasedGravity();
        FreezeRotation();
    }

    //============================================================
    // 입력/물리 유틸
    //============================================================
    private void UpdateGroundedCheck()
    {
        Vector3 rayOrigin = transform.position + Vector3.up * 0.1f;
        float rayLength = characterData.groundCheckDistance + 0.1f;
        isGrounded = Physics.Raycast(rayOrigin, Vector3.down, out var hit, rayLength, characterData.groundLayer)
                     && hit.distance <= characterData.groundCheckDistance + 0.05f;
        Debug.DrawRay(rayOrigin, Vector3.down * rayLength, isGrounded ? Color.green : Color.red);
    }

    private void ApplyMassBasedGravity()
    {
        if (!isGrounded || isJumping)
        {
            float gravity = characterData.gravity;
            if (Rigid.velocity.y < 0) gravity *= characterData.fallMultiplier;
            Rigid.AddForce(Vector3.up * gravity, ForceMode.Force);
        }
    }

    //============================================================
    // 인벤토리/무기/카메라
    //============================================================
    protected virtual void ToggleInventory()
    {
        if (isInventoryOpen)
        {
            Managers.UI.CloseUI("UI_Inven");
            isInventoryOpen = false;
        }
        else
        {
            Managers.UI.ShowSceneUI<UI_Inven>("UI_Inven");
            isInventoryOpen = true;
        }
    }

    protected virtual void CloseInventory()
    {
        if (isInventoryOpen)
        {
            Managers.UI.CloseUI("UI_Inven");
            isInventoryOpen = false;
        }
    }

    protected virtual void ChangeWeapon(int index) { }

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
        Rigid.velocity = new Vector3(0f, Rigid.velocity.y, 0f);
    }

    public void RotateTowardsMousePosition()
    {
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
    // 안전 핸들러
    //============================================================
    private void Safe_OnAttackAnimationEnd()
    {
        isAttacking = false;
        if (actSM.CurrentId == ActState.Attack || actSM.CurrentId == ActState.AttackReady)
            actSM.Change(ActState.None);
    }

    private void Safe_OnHitStep(int stepIndex)
    {
        if (stepIndex < 0) return;
        OnAttackHitStep(stepIndex);
    }

    private void Safe_OpenCombo() => OpenComboWindow();
    private void Safe_CloseCombo() => CloseComboWindow();
    private void Safe_GenericTag(string tag) => OnAnimationEventTag(tag);
    private void safe_EffectStep(int step)
    {
        Debug.Log($"[PlayerController] EffectStep received: {step}");
        if (EffectHandler != null && WeaponManager.HasWeapon)
        {
            var group = isGrounded ? WeaponAnimGroup.Ground : WeaponAnimGroup.Air;
            var actionType = CurrentAttackTypeForEffect; // Light, Heavy, QSkill 등
            int effectIndex = currentComboStep;
            Debug.Log($"[PlayerController] Playing effect: Group={group}, ActionType={actionType}, EffectIndex={effectIndex}, Step={step}");
            EffectHandler.PlayEffect(group, actionType, effectIndex, step);
        }
    }

}
