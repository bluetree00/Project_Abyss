//============================================================
// 네임스페이스 및 의존성
//============================================================
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using Cinemachine;
using Game.CharacterStates;
using Game.CharacterStates.CharacterControllerStates;

public class PlayerCharacter : CharacterBase
{
    //============================================================
    // 캐릭터 정보 및 핵심 시스템
    //============================================================

    [Header("Character & Weapon")]
    [SerializeField] protected CharacterData characterData;
    public CharacterData CharacterData => characterData;

    protected StateMachine<PlayerCharacter> stateMachine = new();
    public StateMachine<PlayerCharacter> StateMachine => stateMachine;

    public WeaponManagerSO weaponManagerSO;

    protected Vector3 moveDirection;
    public Vector3 MoveDirection => moveDirection;

    protected PlayerInputActions inputActions;
    public bool inputReady = false;

    public float heavyAttackChargeThreshold = 1.5f;
    public float heldDuration = 0f, attackInputTime = 0f;
    public float heavyAttackChargeTime = 0f, heavyAttackReleaseTime = 0.4f;

    public bool isAttacking = false;
    public bool isInChargingState = false;
    private bool isInventoryOpen = false;
    protected bool isInputLocked = false;
    private float inputLockDuration = 2f;
    private Coroutine inputLockCoroutine;
    protected bool nextComboQueued = false;

    [Header("Camera")]
    [SerializeField] protected CinemachineFreeLook cinemachineCamera;

    //============================================================
    // 캐릭터 능력 모듈
    //============================================================

    public IMoveAbility<PlayerCharacter> MoveAbility { get; protected set; }
    public IDodgeAbility<PlayerCharacter> DodgeAbility { get; protected set; }
    public ILightAttackAbility<PlayerCharacter> LightAttackAbility { get; protected set; }
    public IHeavyAttackAbility<PlayerCharacter> HeavyAttackAbility { get; protected set; }
    public IJumpAbility<PlayerCharacter> JumpAbility { get; protected set; }

    public Transform handTransform;

    //============================================================
    // 점프 및 공중 상태 관리
    //============================================================
    public bool isGrounded;
    public bool isJumping;
    private float airStartTime = 0f;

    public enum AirState { None, JumpStart, InAir, Landing }
    public AirState CurrentAirState { get; private set; } = AirState.None;

    public bool IsInAir => CurrentAirState == AirState.InAir;
    public bool IsHardLanding => (Time.time - airStartTime) >= characterData.hardLandingTimeThreshold;
    public bool IsGrounded() => isGrounded;
    public bool IsJumping() => isJumping;
    public void FinishJump() => isJumping = false;
    private void FreezeRotation() => Rigid.angularVelocity = Vector3.zero;

    //============================================================
    // 초기화
    //============================================================

    private async void Start() => await InitAsync();


    /// <summary>
    /// 캐릭터 초기화 비동기 메서드.
    /// 상위 InitAsync 호출 후 컴포넌트, 데이터, 입력, 능력, 무기 매니저, 카메라 초기화.
    /// </summary>
    protected override async Task InitAsync()
    {
        await base.InitAsync();
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
        Managers.Player.RegisterPlayer(transform);
        handTransform = Util.FindDeepChild(transform, "WeaponSocket");
        if (handTransform == null)
            Debug.LogWarning("WeaponSocket 트랜스폼을 찾지 못했습니다.");
    }


    /// <summary>
    /// 캐릭터 데이터 비동기 로드 및 초기 설정.
    /// </summary>
    private async Task InitCharacterDataAsync()
    {
        string characterName = gameObject.name.Replace("(Clone)", "");
        await LoadCharacterDataAsync(characterName);
        Rigid.useGravity = false;
        Rigid.drag = characterData.groundDrag;
    }

    /// <summary>
    /// 캐릭터 데이터를 Addressables에서 로드하고 캐릭터 데이터 설정.
    /// </summary>
    /// <param name="characterName">로드할 캐릭터 데이터 이름</param>
    private async Task LoadCharacterDataAsync(string characterName)
    {
        var tcs = new TaskCompletionSource<bool>();
        Managers.AddressableManager.LoadAsset<CharacterData>(characterName, data =>
        {
            if (data == null)
            {
                Debug.LogError("캐릭터 데이터가 null입니다.");
                tcs.SetResult(false);
                return;
            }
            characterData = data;
            Managers.CharacterData.SetCharacterData(data);
            characterData.canDodge = true;
            tcs.SetResult(true);
        });
        await tcs.Task;
    }

    private void InitInputActions()
    {
        inputActions = new PlayerInputActions();
        inputActions.Enable();
        inputReady = true;
    }

    /// <summary>
    /// 입력 액션과 이벤트 바인딩 (공격, 회피, 점프 등).
    /// </summary>
    protected virtual void BindInputActions()
    {
        inputActions.Player.Attack.started += _ => OnAttackStarted();
        inputActions.Player.Attack.canceled += _ => OnAttackReleased();
        inputActions.Player.Dodge.performed += _ => DodgeAbility?.Dodge(this);
        inputActions.Player.Jump.performed += _ => ProcessJump();
        inputActions.Player.Skill.performed += _ => OnSkillAttack();
        inputActions.Player.Ultimate.performed += _ => OnUltimateAttack();
        inputActions.Player.InventoryToggle.performed += _ => ToggleInventory();
        inputActions.Player.CloseInventory.performed += _ => CloseInventory();
        inputActions.Player.ChangeWeapon1.performed += _ => ChangeWeapon(0);
        inputActions.Player.ChangeWeapon2.performed += _ => ChangeWeapon(1);
    }

    protected virtual void InitAbilities()
    {
        MoveAbility = new DefaultMoveAbility();
        DodgeAbility = new DefaultDodgeAbility();
        JumpAbility = new DefaultJumpAbility();
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

    public void OnWeaponEquipped()
    {
        var weapon = weaponManagerSO.CurrentWeapon;
        if (weapon != null)
        {
            LightAttackAbility = weapon.LightAttack;
            HeavyAttackAbility = weapon.HeavyAttack;
        }
    }

    //============================================================
    // 입력 처리 템플릿 매서드 패턴 방식
    //============================================================

    public bool CanProcessInput() => !isInputLocked && !(stateMachine.CurrentState?.BlocksInput ?? false);

    /// <summary>
    /// 입력 잠금 코루틴 처리.
    /// </summary>
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

    /// <summary>
    /// 공격 입력 시작 시 처리 (상속 구현).
    /// </summary>
    protected virtual void OnAttackStarted() {}

    /// <summary>
    /// 공격 입력 해제 시 처리 (상속 구현).
    /// </summary>
    protected virtual void OnAttackReleased() { }
    
    /// <summary>
    /// 점프 입력 처리 (상속 구현).
    /// </summary>
    protected virtual void ProcessJump() { }

    /// <summary>
    /// 스킬 공격 입력 처리 (상속 구현).
    /// </summary>
    protected virtual void OnSkillAttack() { }

    /// <summary>
    /// 궁극기 공격 입력 처리 (상속 구현).
    /// </summary>
    protected virtual void OnUltimateAttack() { }

    /// <summary>
    /// 무기 변경 처리 (상속 구현).
    /// </summary>
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


        MoveAbility?.Move(this, moveDirection);
        CheckHeavyAttackChargingState();

        if (characterData.comboTimer > 0)
        {
            characterData.comboTimer -= Time.deltaTime;
            if (characterData.comboTimer <= 0)
                ResetCombo();
        }
    }

    private void FixedUpdate()
    {
        if (characterData == null) return;
        UpdateGroundedCheck();
        UpdateAirStateAuto();
        ApplyMassBasedGravity();

        FreezeRotation();
        Rigid.drag = IsInAir ? characterData.airDrag : characterData.groundDrag;
    }

    private void UpdateGroundedCheck()
    {
        Vector3 rayOrigin = transform.position + Vector3.up * 0.1f;
        float rayLength = characterData.groundCheckDistance + 0.1f;
        isGrounded = Physics.Raycast(rayOrigin, Vector3.down, out var hit, rayLength, characterData.groundLayer)
            && hit.distance <= characterData.groundCheckDistance + 0.05f;
        Debug.DrawRay(rayOrigin, Vector3.down * rayLength, isGrounded ? Color.green : Color.red);
    }

    private void UpdateAirStateAuto()
    {
        if (!isGrounded && CurrentAirState == AirState.None)
            SetAirState(AirState.InAir);
        else if (isGrounded && CurrentAirState != AirState.None)
        {
            SetAirState(AirState.None);
            FinishJump();
        }
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

    public void SetAirState(AirState state)
    {
        CurrentAirState = state;
        if (state == AirState.InAir)
            airStartTime = Time.time;
    }

    

    //============================================================
    // 무기 시스템  Ｓｗｏｒｄ Ａｎｄ Ｂｏｗ
    //============================================================

    public bool PickupWeapon(WeaponData newWeapon)
    {
        for (int i = 0; i < weaponManagerSO.SlotCount; i++)
        {
            if (weaponManagerSO.GetWeaponAtSlot(i) == null)
            {
                weaponManagerSO.EquipWeapon(newWeapon, i, anim);
                Debug.Log($"[무기 습득] {newWeapon.weaponName} 을 {i}번 슬롯에 장착함");
                Managers.Instance.StartCoroutine(Managers.Instance.InitializeObjectPool("BaseTest"));
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
            heavyAttackChargeTime += Time.deltaTime;
            if (!isInChargingState && heavyAttackChargeTime >= heavyAttackReleaseTime)
            {
                isInChargingState = true;
                HeavyAttackAbility.HeavyAttackStartCharging(this);
            }
            if (isInChargingState)
                HeavyAttackAbility.HeavyAttackUpdateCharging(this, heavyAttackChargeTime);
        }
        else
        {
            heavyAttackChargeTime = 0f;
            isInChargingState = false;
        }
    }

    private void CheckBowHeavyAttackChargingState()
    {
        if (inputActions.Player.Attack.IsPressed())
        {
            heavyAttackChargeTime += Time.deltaTime;
            if (!isInChargingState && heavyAttackChargeTime >= heavyAttackReleaseTime)
            {
                isInChargingState = true;
                HeavyAttackAbility.HeavyAttackStartCharging(this);
            }
            if (isInChargingState)
                HeavyAttackAbility.HeavyAttackUpdateCharging(this, heavyAttackChargeTime);
        }
        else
        {
            heavyAttackChargeTime = 0f;
            isInChargingState = false;
        }
    }

    private void ResetCombo()
    {
        characterData.attackComboStep = 0;
        characterData.comboTimer = 0;
        nextComboQueued = false;
    }

    //============================================================
    // 상태 전환
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
        if (IsInAir) return;
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