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

    public WeaponManagerSO weaponManagerSO;

    protected PlayerInputActions inputActions;
    public bool inputReady = false;

    protected Vector3 moveDirection;
    public Vector3 MoveDirection => moveDirection;

    private bool isInventoryOpen = false;
    private bool isRunChecked = false;
    public bool IsRunChecked => isRunChecked;
    protected bool isInputLocked = false;
    private float _inputLockDuration = 2f;
    private Coroutine _inputLockCoroutine;
    

    // 입력 정책(무기 타입별: 소드/활 등)
    private IAttackInputPolicy _attackPolicy;

    // 애니메이터 오버라이드 서비스(프로젝트의 구현체 사용)
    private AnimatorOverrideService _animSvc;

    //============================================================
    // 레이어 FSM (Locomotion / Action)
    //============================================================
    protected LayerStateMachine<LocoState> locoSM;
    protected LayerStateMachine<ActState>  actSM;

    [Header("Debug (ReadOnly)")]
    [SerializeField] private LocoState locoStateDebug;
    [SerializeField] private ActState actStateDebug;

    // 이동 제어 미들웨어(락 + 스케일)
    private int _moveLockCount = 0;
    public bool  IsMoveLocked => _moveLockCount > 0;
    public float MoveScale    { get; private set; } = 1f;
    public void AcquireMoveLock()        => _moveLockCount++;
    public void ReleaseMoveLock()        => _moveLockCount = Math.Max(0, _moveLockCount - 1);
    public void SetMoveScale(float s)    => MoveScale = Mathf.Clamp01(s);

    [Header("Camera")]
    [SerializeField] protected CinemachineFreeLook cinemachineCamera;

    //============================================================
    // 입력 버퍼 & 시간축
    //============================================================
    protected IClock Clock { get; private set; }
    public    InputBuffer InputBuffer { get; private set; }

    /// <summary>파생 클래스 헬퍼</summary>
    protected void EnqueueCommand(Command cmd) => InputBuffer?.Push(cmd);

    //============================================================
    // 어빌리티 모듈
    //============================================================
    public IMoveAbility<PlayerController>   MoveAbility   { get; protected set; }
    public IDodgeAbility<PlayerController>  DodgeAbility  { get; protected set; }
    public ILightAttackAbility<PlayerController> LightAttackAbility  { get; protected set; }
    public IHeavyAttackAbility<PlayerController> HeavyAttackAbility  { get; protected set; }
    public IJumpAbility<PlayerController>   JumpAbility   { get; protected set; }

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

    //============================================================
    // 초기화
    //============================================================
    private async void Start() => await InitAsync();

    protected override async UniTask InitAsync()
    {
        await base.InitAsync();

        // 시간축/입력 버퍼
        Clock      = new UnscaledClock();
        // capacity=16, window=0.18s, dedupeMs=40 — 추천값
        InputBuffer = new InputBuffer(Clock, capacity: 16, bufferWindowSec: 0.18f, dedupeSec: 40f);

        InitCoreComponents();
        await InitCharacterDataAsync();
        InitInputActions();
        InitAbilities();
        InitWeaponManager();
        SetupCamera();

         // FSM 틀은 부모에서 준비
        locoSM = new LayerStateMachine<LocoState>(this);
        actSM  = new LayerStateMachine<ActState>(this);

         // 자식이 자기 상태 등록하도록 훅 제공
        InitLayerFSMs();

        // 초기 상태
        locoSM.Change(LocoState.Idle);
        actSM.Change(ActState.None);

        _animSvc = new AnimatorOverrideService(anim); // 프로젝트 구현체에 맞게

        if (inputReady) BindInputActions();

    }

    private void InitCoreComponents()
    {
        Managers.Player.SetPlayer(transform);
        handTransform = Util.FindDeepChild(transform, "WeaponSocket");
        if (handTransform == null)
            Debug.LogWarning("WeaponSocket 트랜스폼을 찾지 못했습니다.");
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
        MoveAbility  = new DefaultMoveAbility();
        DodgeAbility = new DefaultDodgeAbility();
        JumpAbility  = new DefaultJumpAbility();
        // Light/Heavy는 무기 장착 시 주입
    }

    private void InitWeaponManager()
    {
        weaponManagerSO = ScriptableObject.CreateInstance<WeaponManagerSO>();
        weaponManagerSO.Initialize(2, anim);
        weaponManagerSO.weaponHandTransform = handTransform;
        weaponManagerSO.OnWeaponEquippedEvent += OnWeaponEquipped;
    }

    public void ClearWeaponAbilities()
    {
        LightAttackAbility = null;
        HeavyAttackAbility = null;
    }

    // 무기 장착 시: 어빌리티/입력정책 주입
    public void OnWeaponEquipped()
    {
        var weapon = weaponManagerSO.CurrentWeapon;
        ClearWeaponAbilities();

        if (weapon != null)
        {
            LightAttackAbility = weapon.LightAttack;
            HeavyAttackAbility = weapon.HeavyAttack;

            _attackPolicy = weapon.weaponType switch
            {
                Define.WeaponType.Sword => new SwordAttackInputPolicy(),
                Define.WeaponType.Bow   => new BowAttackInputPolicy(),
                _ => new SwordAttackInputPolicy(),
            };
        }
        else
        {
            _attackPolicy = null;
        }
    }

    //============================================================
    // 입력 바인딩(훅은 Push만/정책 위임)
    //============================================================
    protected virtual void BindInputActions()
    {
        if (!inputReady) return;

        // 공격 입력은 정책 위임 (전이는 레이어 FSM에서만)
        inputActions.Player.Attack.started  += _ => _attackPolicy?.OnStarted(this);
        inputActions.Player.Attack.canceled += _ => _attackPolicy?.OnCanceled(this);

        inputActions.Player.Run.started   += _ => isRunChecked = true;
        inputActions.Player.Run.canceled  += _ => isRunChecked = false;

        // 회피/스킬/궁극: 즉시 버퍼 푸시
        inputActions.Player.Dodge.performed    += _ => InputBuffer.Push(Command.Dodge);
        inputActions.Player.Skill.performed    += _ => InputBuffer.Push(Command.Skill);
        inputActions.Player.Ultimate.performed += _ => InputBuffer.Push(Command.Skill);

        // 달리기/점프/인벤토리/무기 교체
        
        inputActions.Player.Jump.performed            += _ => ProcessJump();
        inputActions.Player.InventoryToggle.performed += _ => { ToggleInventory(); if (isInventoryOpen) InputBuffer.Clear(); };
        inputActions.Player.CloseInventory.performed  += _ => CloseInventory();
        inputActions.Player.ChangeWeapon1.performed   += _ => ChangeWeapon(0);
        inputActions.Player.ChangeWeapon2.performed   += _ => ChangeWeapon(1);
    }

    //============================================================
    // 파생 훅
    //============================================================
    protected virtual void InitLayerFSMs() { /* Knight 등 파생에서 등록 */ }

    protected virtual void RouteInputsToLayers()
    {

    }

    //기능은 어빌리티에서 가져올 예정

    protected virtual void ProcessJump() { /* 파생에서 */ }
    protected virtual void OnSkillAttack() { /* 파생에서 */ }

    public void UseSkill() => OnSkillAttack();
    protected virtual void OnUltimateAttack() { /* 파생에서 */ }

    // 애니 세트 스왑 (무기 SO + 지상/공중)
    // PlayerController.cs (발췌)
    public int LightMaxComboCount { get; private set; } = 1;
    public float LightComboResetTime { get; private set; } = 1.5f;
    public IReadOnlyList<float> LightComboEndTimes { get; private set; }

    public void ApplyAttackAnimationSet(bool isAir)
    {
        var w = weaponManagerSO?.CurrentWeapon;
        if (w == null || _animSvc == null) return;

        // ── 라이트 세트: 이름 매핑(normalAttackAnimations ↔ attackAnimations) ──
        var light = w.lightSet; // LightAttackAnimationSetSO
        if (light != null)
        {
            int nameCount = light.normalAttackAnimations != null ? light.normalAttackAnimations.Length : 0;
            int clipCount = light.attackAnimations        != null ? light.attackAnimations.Count        : 0;
            int n = Mathf.Min(nameCount, clipCount);

            if (n > 0)
            {
                var map = new Dictionary<string, AnimationClip>(n);
                for (int i = 0; i < n; i++)
                {
                    string key  = light.normalAttackAnimations[i];
                    var    clip = light.attackAnimations[i];
                    if (!string.IsNullOrEmpty(key) && clip != null)
                        map[key] = clip;
                }
                _animSvc.OverrideMap(map);
            }
            else
            {
                Debug.LogWarning("[ApplyAttackAnimationSet] Light set has no valid name/clip pairs.");
            }

            // 콤보 메타 적용(상태에서 참조)
            LightMaxComboCount  = Mathf.Max(1, light.maxAttackCount);
            LightComboResetTime = Mathf.Max(0f, light.comboResetTime);
            LightComboEndTimes  = light.comboEndTimes; // null 가능
        }

        // ── 헤비 세트: 지상/공중 분기 ──
        var hs = w.heavyAttackSet;
        if (hs != null && hs.HeavyAttackAnimations != null && hs.HeavyAttackAnimations.Length >= 3)
        {
            var charge = isAir && hs.chargeClip_Air != null ? hs.chargeClip_Air : hs.chargeClip;
            var attack = isAir && hs.attackClip_Air != null ? hs.attackClip_Air : hs.attackClip;
            var end    = isAir && hs.endClip_Air    != null ? hs.endClip_Air    : hs.endClip;

            if (charge != null) _animSvc.Override(hs.HeavyAttackAnimations[0], charge);
            if (attack != null) _animSvc.Override(hs.HeavyAttackAnimations[1], attack);
            if (end    != null) _animSvc.Override(hs.HeavyAttackAnimations[2], end);
        }
    }


    // 콤보창 헬퍼(애니 이벤트에서 호출)
    public bool comboWindowOpen  = false;
    public int  currentComboStep = 0;
    public void OpenComboWindow()  => comboWindowOpen = true;
    public void CloseComboWindow() => comboWindowOpen = false;

    public virtual void OnAttackAnimationEnd() => isAttacking = false;

    //============================================================
    // 유니티 생명주기
    //============================================================
    protected override void Update()
    {
        if (!inputReady || characterData == null || cinemachineCamera == null) return;

        // 0) 무기별 입력 정책 Tick (홀드/릴리즈 판정 → 버퍼 Push)
        _attackPolicy?.Tick(this, Time.unscaledDeltaTime);

        // 2) 버퍼 만료 정리
        InputBuffer?.TickPrune();

        // 3) 버퍼 소비/전이 라우팅(파생에서 구현)
        RouteInputsToLayers();

        // 4) 레이어 FSM 업데이트
        locoSM?.Update();
        actSM?.Update();

        if (locoSM != null) locoStateDebug = locoSM.CurrentId;
        if (actSM  != null) actStateDebug  = actSM.CurrentId;


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
        if (isInputLocked) return;
        if (isInventoryOpen)
        {
            Managers.UI.CloseUI("UI_Inven");
            isInventoryOpen = false;
        }
        else
        {
            Managers.UI.ShowSceneUI<UI_Inven>("UI_Inven");
            isInventoryOpen = true;
            LockInput(_inputLockDuration);
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

    protected virtual void ChangeWeapon(int index) { /* 파생에서 */ }

    private void LockInput(float sec)
    {
        if (_inputLockCoroutine != null) StopCoroutine(_inputLockCoroutine);
        _inputLockCoroutine = StartCoroutine(LockInputCoroutine(sec));
    }

    private IEnumerator LockInputCoroutine(float sec)
    {
        isInputLocked = true;
        yield return new WaitForSeconds(sec);
        isInputLocked = false;
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
}
