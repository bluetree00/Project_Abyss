//============================================================
// 네임스페이스 및 의존성
//============================================================
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Cinemachine;
using Game.CharacterStates;
using Game.CharacterStates.CharacterControllerStates;
using Cysharp.Threading.Tasks;

// 입력 버퍼
using Game.Inputs;

public abstract class PlayerController : CharacterBase
{
    //============================================================
    // 캐릭터 정보 및 핵심 시스템
    //============================================================

     public enum PlayerState
    {
        None,
        Idle,
        Move,
        Dodge,
        AttackReady,
        Attack,
        Die
    }


    [Header("Character & Weapon")]
    [SerializeField] protected CharacterData characterData;
    public CharacterData CharacterData => characterData;

    private IAttackInputPolicy _attackPolicy; //입력을 선택

    protected StateMachine<PlayerController> stateMachine = new();
    public StateMachine<PlayerController> StateMachine => stateMachine;

    public WeaponManagerSO weaponManagerSO;

    protected Vector3 moveDirection;
    public Vector3 MoveDirection => moveDirection;

    protected PlayerInputActions inputActions;
    public bool inputReady = false;
    private bool isInventoryOpen = false;
    protected bool isInputLocked = false;
    private float inputLockDuration = 2f;
    private Coroutine inputLockCoroutine;

     private AnimatorOverrideService _animSvc;

    [Header("Camera")]
    [SerializeField] protected CinemachineFreeLook cinemachineCamera;

    //============================================================
    // 입력 버퍼 & 시간축
    //============================================================
    protected IClock Clock { get; private set; }
    public InputBuffer InputBuffer { get; private set; }

    /// <summary>파생 클래스에서 버퍼로 커맨드를 밀어넣을 때 쓰는 헬퍼</summary>
    protected void EnqueueCommand(Command cmd) => InputBuffer?.Push(cmd);

    //============================================================
    // 캐릭터 능력 모듈
    //============================================================

    public IMoveAbility<PlayerController> MoveAbility { get; protected set; }
    public IDodgeAbility<PlayerController> DodgeAbility { get; protected set; }
    public ILightAttackAbility<PlayerController> LightAttackAbility { get; protected set; }
    public IHeavyAttackAbility<PlayerController> HeavyAttackAbility { get; protected set; }
    public IJumpAbility<PlayerController> JumpAbility { get; protected set; }

    public Transform handTransform;

    //============================================================
    // 캐릭터 상태 관리
    //============================================================

    public bool isAttacking = false;
    public bool isInChargingState = false;
    public float heavyAttackChargeThreshold => characterData.heavyAttackChargeThreshold;
    public bool canDodge = false; // 대시 가능 여부
    public bool IsInChargingState;
    public bool nextComboQueued = false; // 다음 콤보가 대기 중인지 여부

    // 레이어 FSM에 활용될 락 , 감속
    private int _moveLockCount = 0;
    public bool IsMoveLocked => _moveLockCount > 0;
    public float MoveScale { get; private set; } = 1f;

    public void AcquireMoveLock()  => _moveLockCount++;
    public void ReleaseMoveLock()  => _moveLockCount = Mathf.Max(0, _moveLockCount - 1);
    public void SetMoveScale(float s) => MoveScale = Mathf.Clamp01(s);


    //============================================================
    // 점프 및 공중 상태 관리
    //============================================================
    public bool isGrounded;
    public bool isJumping;
    private float airStartTime = 0f;


    public bool IsGrounded() => isGrounded;
    private void FreezeRotation() => Rigid.angularVelocity = Vector3.zero;

    //============================================================
    // 초기화
    //============================================================

    private async void Start() => await InitAsync();

    /// <summary>
    /// 캐릭터 초기화 비동기 메서드.
    /// 상위 InitAsync 호출 후 컴포넌트, 데이터, 입력, 능력, 무기 매니저, 카메라 초기화.
    /// </summary>
    protected override async UniTask InitAsync()
    {
        await base.InitAsync();

        // 시간축/입력 버퍼 초기화 (히트스톱/슬로모 무시를 위해 Unscaled 사용)
        Clock = new UnscaledClock();
        // capacity=16, window=0.18s, dedupe=40ms — 필요 시 튜닝
        InputBuffer = new InputBuffer(Clock, capacity: 16, bufferWindowSec: 0.18f, dedupeSec: 4f);

        InitCoreComponents();
        await InitCharacterDataAsync();
        InitInputActions();
        InitAbilities();
        InitWeaponManager();
        SetupCamera();

        if (inputReady) BindInputActions();
    }

  
    /// <summary>
    /// 캐릭터 필수 컴포넌트 초기화 (플레이어 등록, 무기 소켓 찾기 등).
    /// </summary>
    private void InitCoreComponents()
    {
        Managers.Player.SetPlayer(transform); // 매니저에 플레이어 등록
        handTransform = Util.FindDeepChild(transform, "WeaponSocket");
        if (handTransform == null)
            Debug.LogWarning("WeaponSocket 트랜스폼을 찾지 못했습니다.");
    }

    /// <summary>
    /// 캐릭터 데이터 비동기 로드 및 초기 설정.
    /// </summary>
    private async UniTask InitCharacterDataAsync()
    {
        string characterName = gameObject.name.Replace("(Clone)", "");
        await LoadCharacterDataAsync(characterName);
        Rigid.useGravity = false;
        Rigid.drag = characterData.groundDrag;
    }

    /// <summary>
    /// 캐릭터 데이터를 Addressables에서 로드하고 캐릭터 데이터 설정. //NOTE:추후 서버 동기화로 변경
    /// </summary>
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
            canDodge = true;
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
    
    // 무기 장착 이벤트에서 정책 교체
    public void OnWeaponEquipped()
    {
        var weapon = weaponManagerSO.CurrentWeapon;

        // 공격 어빌리티 주입
        if (weapon != null)
        {
            LightAttackAbility = weapon.LightAttack;
            HeavyAttackAbility = weapon.HeavyAttack;

            // 🔸 무기 타입에 맞는 입력 정책 장착
            switch (weapon.weaponType)
            {
                case Define.WeaponType.Sword:
                    _attackPolicy = new SwordAttackInputPolicy();
                    break;
                case Define.WeaponType.Bow:
                    _attackPolicy = new BowAttackInputPolicy();
                    break;
                default:
                    _attackPolicy = new SwordAttackInputPolicy(); // 기본값(원하면 라이트 탭 위주 정책)
                    break;
            }
        }
    }

    /// <summary>
    /// 입력 액션과 이벤트 바인딩
    /// - 이동은 지속 입력(즉시형)으로 유지
    /// - 공격/회피/스킬/궁극은 버퍼에 Push만 (실행/전이는 상태가 승인)
    /// </summary>
    protected virtual void BindInputActions()
    {
        if (!inputReady) return;

        // 공격 입력: 정책으로 위임(전이는 버퍼에서만)
        inputActions.Player.Attack.started  += _ => _attackPolicy?.OnStarted(this);
        inputActions.Player.Attack.canceled += _ => _attackPolicy?.OnCanceled(this);

        // 회피/스킬 등은 즉시 버퍼 푸시
        inputActions.Player.Dodge.performed    += _ => InputBuffer.Push(Game.Inputs.Command.Dodge);
        inputActions.Player.Skill.performed    += _ => InputBuffer.Push(Game.Inputs.Command.Skill);
        inputActions.Player.Ultimate.performed += _ => InputBuffer.Push(Game.Inputs.Command.Skill);

        // 나머지
        inputActions.Player.Jump.performed            += _ => ProcessJump();
        inputActions.Player.InventoryToggle.performed += _ => { ToggleInventory(); if (isInventoryOpen) InputBuffer.Clear(); };
        inputActions.Player.CloseInventory.performed  += _ => CloseInventory();
        inputActions.Player.ChangeWeapon1.performed   += _ => ChangeWeapon(0);
        inputActions.Player.ChangeWeapon2.performed   += _ => ChangeWeapon(1);
    }

    protected virtual void InitAbilities()
    {
        MoveAbility = new DefaultMoveAbility();
        DodgeAbility = new DefaultDodgeAbility();
        JumpAbility = new DefaultJumpAbility();
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

    // public void OnWeaponEquipped()
    // {
    //     var weapon = weaponManagerSO.CurrentWeapon;
    //     if (weapon != null)
    //     {
    //         LightAttackAbility = weapon.LightAttack;
    //         HeavyAttackAbility = weapon.HeavyAttack;
    //     }
    // }

    //============================================================
    // 입력 처리 템플릿 메서드(파생 훅)
    //============================================================

    private void LockInput(float sec)
    {
        if (inputLockCoroutine != null)
            StopCoroutine(inputLockCoroutine);
        inputLockCoroutine = StartCoroutine(LockInputCoroutine(sec));
    }

    private IEnumerator LockInputCoroutine(float sec)
    {
        isInputLocked = true;
        yield return new WaitForSeconds(sec);
        isInputLocked = false;
    }
    


    /// <summary>공격 입력 시작(차지 시작 등) — 파생에서 구현</summary>
    protected virtual void OnAttackStarted() { }

    /// <summary>
    /// 공격 입력 해제(탭/차지 판별) — 파생에서:
    /// - 탭이면: EnqueueCommand(Command.Light)
    /// - 차지면: 상태 전이 or HeavyAbility 호출/버퍼 사용
    /// </summary>
    protected virtual void OnAttackReleased() { }

    /// <summary>점프 입력 처리 — 파생에서 이동/상태 연계</summary>
    protected virtual void ProcessJump() { }

    /// <summary>스킬 입력 처리(즉시형으로 쓰고 싶다면 여기서 직접 실행해도 됨)</summary>
    protected virtual void OnSkillAttack() { }

    /// <summary>궁극기 입력 처리</summary>
    protected virtual void OnUltimateAttack() { }

    /// <summary>무기 변경</summary>
    protected virtual void ChangeWeapon(int index) { }

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
            LockInput(inputLockDuration);
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

    public virtual void OnAttackAnimationEnd() => isAttacking = false;

    //============================================================
    // 유니티 생명주기
    //============================================================

    protected override void Update()
    {
        if (!inputReady || characterData == null || cinemachineCamera == null) return;

        HandleFSM();

        // ① 입력 버퍼 만료 정리(프레임당 1회)
        InputBuffer?.TickPrune();

        // ② 이동 처리(지속 입력은 즉시형 — 보통 파생에서 moveDirection을 채움) 이제 idle, move에서 처리 예정
        //MoveAbility?.Move(this, moveDirection);

        // ③ 상태 머신 업데이트(현재 상태가 허용하면 버퍼를 소비해 전이/실행)
        stateMachine.Update();

        // ④ 차지 로직(무기 타입별)
        CheckHeavyAttackChargingState();

    }

    public abstract void HandleFSM();

    private void FixedUpdate()
    {
        if (characterData == null) return;
        UpdateGroundedCheck();
        ApplyMassBasedGravity();

        FreezeRotation();
    }

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
            if (Rigid.velocity.y < 0)
                gravity *= characterData.fallMultiplier;
            Rigid.AddForce(Vector3.up * gravity, ForceMode.Force);
        }
    }


    //============================================================
    // 무기 시스템  Ｓｗｏｒｄ Ａｎｄ Ｂｏｗ
    //============================================================

    public async UniTask<bool> PickupWeaponAsync(WeaponData newWeapon)
    {
        for (int i = 0; i < weaponManagerSO.SlotCount; i++)
        {
            if (weaponManagerSO.GetWeaponAtSlot(i) == null)
            {
                weaponManagerSO.EquipWeapon(newWeapon, i, anim);
                Debug.Log($"[무기 습득] {newWeapon.weaponName} 을 {i}번 슬롯에 장착함");

                // 무기 이름 기반 이펙트 풀 로드(예시)
                await Managers.Instance.InitializeObjectPoolAsync("BaseTest");

                weaponManagerSO.SwitchWeapon(i, anim);
                GoToIdleState();
                return true;
            }
        }

        Debug.Log("⚠ 모든 슬롯이 꽉 찼습니다!");
        return false;
    }

    private void CheckHeavyAttackChargingState()
    {
        if (weaponManagerSO?.CurrentWeapon == null) return;
        switch (weaponManagerSO.CurrentWeapon.weaponType)
        {
            case Define.WeaponType.Sword:
                CheckSwordHeavyAttackChargingState();
                break;
            case Define.WeaponType.Bow:
                CheckBowHeavyAttackChargingState();
                break;
        }
    }

    private void CheckSwordHeavyAttackChargingState()
    {
        if (isAttacking) return;

        if (inputActions.Player.Attack.IsPressed())
        {
            characterData.heavyAttackChargeTime += Time.deltaTime;

            if (!isInChargingState && characterData.heavyAttackChargeTime >= characterData.heavyAttackReleaseTime)
            {
                isInChargingState = true;
                HeavyAttackAbility?.HeavyAttackStartCharging(this);
            }

            if (isInChargingState)
            {
                HeavyAttackAbility?.HeavyAttackUpdateCharging(this, characterData.heavyAttackChargeTime);

                // 자동 발사 트리거: 임계 이상이면 바로 발동
                if (characterData.heavyAttackChargeTime >= heavyAttackChargeThreshold)
                {
                    GoToHeavyAttackChargedAttackState();
                    characterData.heavyAttackChargeTime = 0f;
                    isInChargingState = false;
                }
            }
        }
        else
        {
            characterData.heavyAttackChargeTime = 0f;
            isInChargingState = false;
        }
    }

    private void CheckBowHeavyAttackChargingState()
    {
        if (inputActions.Player.Attack.IsPressed())
        {
            characterData.heavyAttackChargeTime += Time.deltaTime;

            if (!isInChargingState && characterData.heavyAttackChargeTime >= characterData.heavyAttackReleaseTime)
            {
                isInChargingState = true;
                HeavyAttackAbility?.HeavyAttackStartCharging(this);
            }

            if (isInChargingState)
            {
                HeavyAttackAbility?.HeavyAttackUpdateCharging(this, characterData.heavyAttackChargeTime);
            }
        }
        else if (isInChargingState) // 뗄 때 발사
        {
            GoToHeavyAttackChargedAttackState();
            characterData.heavyAttackChargeTime = 0f;
            isInChargingState = false;
        }
        else
        {
            characterData.heavyAttackChargeTime = 0f;
            isInChargingState = false;
        }
    }

    //============================================================
    // 상태 전환 (파생에서 구현)
    //============================================================
    public virtual void GoToIdleState() { }
    public virtual void GotoDodgeState() { }
    public virtual void GoToComboAttackState() { }
    public virtual void GoToHeavyAttackChargeStartState() { }
    public virtual void GoToHeavyAttackChargeHoldingState() { }
    public virtual void GoToHeavyAttackChargedAttackState() { }
    public virtual void GoToHeavyAttackChargeCancelState() { }

    //============================================================
    // 카메라 설정
    //============================================================

    /// <summary>
    /// CinemachineFreeLook 카메라 초기 설정 (카메라 민감도, 거리 등).
    /// </summary>
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
