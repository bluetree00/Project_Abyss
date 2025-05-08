using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Cinemachine;
using Game.CharacterStates;
using Game.CharacterStates.VagabondStates;
using UnityEngine.InputSystem;

public class Vagabond : CharacterController
{
    [Header("Camera")]
    [SerializeField] private CinemachineFreeLook cinemachineCamera;

    private Coroutine inputLockCoroutine;
    private bool isInventoryOpen = false;
    private bool isInputLocked = false;
    private float inputLockDuration = 2f;

    private float attackInputTime = 0f;
    private const float lightAttackDuration = 0.4f;
    private const float heavyAttackDuration = 1f;

    private bool isAttacking = false;
    private bool nextComboQueued = false;

    protected new StateMachine<Vagabond> stateMachine = new StateMachine<Vagabond>();
    public new StateMachine<Vagabond> StateMachine => stateMachine;

    private Dictionary<Type, State<Vagabond>> cachedStates = new();

    private bool isChargingHeavyAttack = false;
    private float heavyAttackChargeTime = 0f;

    private async void Start()
    {
        await InitAsync();
        CacheStates();
        stateMachine.Setup(this, GetState<VagabondIdleState>());
    }

    protected override async Task InitAsync()
    {
        await base.InitAsync();

        if (cinemachineCamera == null)
            cinemachineCamera = FindObjectOfType<CinemachineFreeLook>();

        if (cinemachineCamera != null)
        {
            // 카메라의 LookAt과 Follow를 캐릭터로 설정
            cinemachineCamera.Follow = transform;
            cinemachineCamera.LookAt = transform;

            // 쿼터뷰 고정 각도 설정
            SetCameraToQuarterView();
        }

        if (inputReady)
            BindInputActions();
    }


    
    private void SetCameraToQuarterView()
    {
        // 카메라의 Orbit 값을 수동으로 설정하여 쿼터뷰 각도를 고정
        float height = 5f; // 카메라의 높이
        float radius = 2f; // 캐릭터와의 거리

        // 카메라의 Orbit 3개의 Rig (Top, Middle, Bottom) 모두 같은 값으로 설정하여 일관되게 유지
        cinemachineCamera.m_Orbits[0].m_Height = height; 
        cinemachineCamera.m_Orbits[0].m_Radius = radius;
        cinemachineCamera.m_Orbits[1].m_Height = height;
        cinemachineCamera.m_Orbits[1].m_Radius = radius;
        cinemachineCamera.m_Orbits[2].m_Height = height;
        cinemachineCamera.m_Orbits[2].m_Radius = radius;

        // 카메라의 X/Y축 회전 각도를 쿼터뷰에 맞게 고정
        cinemachineCamera.m_XAxis.Value = 0.5f; // 45도 각도 (0.25는 360도 기준으로 90도 회전)
        cinemachineCamera.m_YAxis.Value = 0.6f;  // 약간 내려다보는 시점 (0 ~ 1 사이)

        // 카메라 회전 속도를 0으로 설정하여, 플레이어가 회전해도 카메라가 고정되게 함
        cinemachineCamera.m_XAxis.m_MaxSpeed = 0f;
        cinemachineCamera.m_YAxis.m_MaxSpeed = 0f;
    }

    protected override void InitAbilities()
    {
        base.InitAbilities();
    }

    private void CacheStates()
    {
        cachedStates[typeof(VagabondIdleState)] = new VagabondIdleState();
        cachedStates[typeof(VagabondMoveBlendState)] = new VagabondMoveBlendState();
        cachedStates[typeof(VagabondInAirState)] = new VagabondInAirState();
        cachedStates[typeof(VagabondJumpStartState)] = new VagabondJumpStartState();
        cachedStates[typeof(VagabondSkillState)] = new VagabondSkillState();
        cachedStates[typeof(VagabondUltimateState)] = new VagabondUltimateState();
        cachedStates[typeof(VagabondDodgeState)] = new VagabondDodgeState();
        cachedStates[typeof(VagabondChangeWeaponState)] = new VagabondChangeWeaponState();
        cachedStates[typeof(VagabondComboAttackState)] = new VagabondComboAttackState();
    }

    public T GetState<T>() where T : State<Vagabond> => cachedStates[typeof(T)] as T;

    public override void GoToIdleState() => stateMachine.ChangeState(GetState<VagabondIdleState>());

    public override void GoToComboAttackState() //자신에 필요한 연속 공격 내용
    {
        var comboState = GetState<VagabondComboAttackState>();
        comboState.SetComboIndex(characterData.attackComboStep);
        stateMachine.ChangeState(comboState);
    }

    private void BindInputActions()
    {
        inputActions.Player.Attack.started += _ => OnAttackStarted();
        inputActions.Player.Attack.canceled += _ => OnAttackReleased(); // 변경 포인트
        

        inputActions.Player.Dodge.performed += _ => DodgeAbility?.Dodge(this);
        inputActions.Player.Jump.performed += _ => ProcessJump();
        inputActions.Player.Skill.performed += _ => stateMachine.ChangeState(GetState<VagabondSkillState>());
        inputActions.Player.Ultimate.performed += _ => stateMachine.ChangeState(GetState<VagabondUltimateState>());
        inputActions.Player.InventoryToggle.performed += _ => ToggleInventory();
        inputActions.Player.CloseInventory.performed += _ => CloseInventory();
        inputActions.Player.ChangeWeapon1.performed += _ => ChangeWeapon(0);
        inputActions.Player.ChangeWeapon2.performed += _ => ChangeWeapon(1);
    }

    protected override void Update()
    {
        if (!inputReady || characterData == null || cinemachineCamera == null) return;

        base.Update();
        CheckMovementInput();
        UpdateMovement();
        FreezeRotation();
        stateMachine.Update();

        HandleHeavyAttackCharging();

        if (CurrentAirState == AirState.InAir && !(stateMachine.CurrentState is VagabondInAirState))
            stateMachine.ChangeState(GetState<VagabondInAirState>());

        if (characterData.comboTimer > 0)
        {
            characterData.comboTimer -= Time.deltaTime;
            if (characterData.comboTimer <= 0)
                ResetCombo();
        }
    }

    private void HandleHeavyAttackCharging()
    {
        if (!isChargingHeavyAttack) return;

        heavyAttackChargeTime += Time.deltaTime;
        HeavyAttackAbility?.HeavyAttackUpdateCharging(this, heavyAttackChargeTime);
    }


    private void CheckMovementInput()
    {
        if (!CanProcessInput())
        {
            moveDirection = Vector3.zero;
            return;
        }

        Vector2 input = inputActions.Player.Move.ReadValue<Vector2>();
        Vector3 forward = cinemachineCamera.transform.forward;
        Vector3 right = cinemachineCamera.transform.right;
        forward.y = right.y = 0;
        moveDirection = (forward.normalized * input.y + right.normalized * input.x).normalized;
    }

    private void UpdateMovement()
    {
        if (!CanProcessInput()) return;

        if (moveDirection.magnitude > 0.01f && !(stateMachine.CurrentState is VagabondMoveBlendState))
            stateMachine.ChangeState(GetState<VagabondMoveBlendState>());
    }

    private void FreezeRotation() => Rigid.angularVelocity = Vector3.zero;
    public bool CanProcessInput() => !isInputLocked && !(stateMachine.CurrentState?.BlocksInput ?? false);

    //============================================================
    // 🗡️ 공격 시스템 개선
    //============================================================

    public bool IsAttacking { get; private set; }
    private void OnAttackStarted()
    {
         if (!CanProcessInput()) return;

        attackInputTime = Time.time;

            // 차징 시작
        if (weaponManagerSO?.CurrentWeapon != null)
        {
            RotateTowardsMousePosition();
        }

        if (HeavyAttackAbility != null)
        {
            isChargingHeavyAttack = true;
            heavyAttackChargeTime = 0f;
            HeavyAttackAbility.HeavyAttackStartCharging(this);
        }
    }

    private void OnAttackReleased()
    {
        if (!CanProcessInput()) return;

        float inputHeldDuration = Time.time - attackInputTime;

        if (isAttacking)
        {
            nextComboQueued = true;
            return;
        }

        if (weaponManagerSO?.CurrentWeapon != null)
            RotateTowardsMousePosition();

        if (isChargingHeavyAttack)
        {
            isChargingHeavyAttack = false;
            HeavyAttackAbility?.HeavyAttackReleaseChargedAttack(this, heavyAttackChargeTime);
            heavyAttackChargeTime = 0f;
            return;
        }

        if (inputHeldDuration <= lightAttackDuration)
        {
            LightAttackAbility?.LightAttack(this);
        }
    }

    

    private void RotateTowardsMousePosition()
    {
        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        RaycastHit hit;
        int groundMask = LayerMask.GetMask("Ground");

        if (Physics.Raycast(ray, out hit, 100f, groundMask))
        {
            Vector3 lookDir = hit.point - transform.position;
            lookDir.y = 0f;

            if (lookDir.sqrMagnitude > 0.01f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(lookDir);
                transform.rotation = targetRotation;
            }
        }
    }

    public void OnAttackAnimationStart() => isAttacking = true;
    public void OnAttackAnimationEnd()
    {
        isAttacking = false;
    }

    private void ResetCombo()
    {
        characterData.attackComboStep = 0;
        characterData.comboTimer = 0;
        nextComboQueued = false;
    }

    private void ProcessJump()
    {
        if (IsJumping() || !IsGrounded()) return;
        stateMachine.ChangeState(GetState<VagabondJumpStartState>());
    }

    //============================================================
    // 🎒 인벤토리 & 무기
    //============================================================

    private void ChangeWeapon(int index)
    {
        if (isInputLocked) return;

        if (weaponManagerSO == null)
        {
            Debug.LogError("weaponManagerSO is not initialized.");
            return;
        }

        // ✅ 현재 슬롯과 같은 슬롯이면 무시
        if (weaponManagerSO.GetCurrentSlotIndex() == index)
            return;

        stateMachine.ChangeState(GetState<VagabondIdleState>());
        weaponManagerSO.SwitchWeapon(index, anim);
    }

    private void ToggleInventory()
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

    private void CloseInventory()
    {
        if (isInventoryOpen)
        {
            Managers.UI.CloseUI("UI_Inven");
            isInventoryOpen = false;
        }
    }

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

    //============================================================
    // ✨ 이펙트 스폰 추후 장비 데이터로 이전
    //============================================================

    public void FrontAttack() => SpawnEffect("FrontAttack", Vector3.forward);
    public void SpawnShinySlashEffect1() => SpawnEffect("ShinySlash", Vector3.forward, new Vector3(0, 0, 68));
    public void SpawnShinySlashEffect2() => SpawnEffect("ShinySlash", Vector3.forward, new Vector3(0, 0, 180));
    public void SpawnShinySlashEffect3() => SpawnEffect("ShinySlash", Vector3.forward, new Vector3(0, 360, -60));
    public void SpawnShinySlashEffect4() => SpawnEffect("ShinySlash", Vector3.forward, new Vector3(0, 360, -140));

    private void SpawnEffect(string effectName, Vector3 forwardOffset, Vector3? additionalRotation = null)
    {
        if (Managers.ObjectPooler == null)
        {
            Debug.LogError("ObjectPoolerManager is not initialized.");
            return;
        }

        Vector3 spawnPosition = transform.position + transform.TransformDirection(forwardOffset);
        Quaternion spawnRotation = transform.rotation;

        if (additionalRotation.HasValue)
            spawnRotation *= Quaternion.Euler(additionalRotation.Value);

        GameObject effectObject = Managers.ObjectPooler.SpawnFromPool(effectName, spawnPosition, spawnRotation);
        if (effectObject == null)
        {
            Debug.LogWarning($"{effectName} 이펙트 생성 실패");
            return;
        }

        effectObject.transform.SetPositionAndRotation(spawnPosition, spawnRotation);
    }
}
