using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Cinemachine;
using Game.CharacterStates;
using Game.CharacterStates.VagabondStates;
using UnityEngine.InputSystem;
using Unity.VisualScripting;

public class Vagabond : CharacterBase
{
    [Header("Camera")]
    [SerializeField] private CinemachineFreeLook cinemachineCamera;

    private Coroutine inputLockCoroutine;
    private bool isInventoryOpen = false;
    private bool isInputLocked = false;
    private float inputLockDuration = 2f;

    private bool nextComboQueued = false;

    protected new StateMachine<Vagabond> stateMachine = new StateMachine<Vagabond>();
    public new StateMachine<Vagabond> StateMachine => stateMachine;

    private Dictionary<Type, State<Vagabond>> cachedStates = new();
  


    public float ChargeTime { get; private set; }

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
            cinemachineCamera.Follow = transform;
            cinemachineCamera.LookAt = transform;
            SetCameraToQuarterView();
        }

        if (inputReady)
            BindInputActions();
    }

    private void SetCameraToQuarterView()
    {
        float height = 5f;
        float radius = 2f;

        for (int i = 0; i < 3; i++)
        {
            cinemachineCamera.m_Orbits[i].m_Height = height;
            cinemachineCamera.m_Orbits[i].m_Radius = radius;
        }

        cinemachineCamera.m_XAxis.Value = 0.5f;
        cinemachineCamera.m_YAxis.Value = 0.6f;
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
        cachedStates[typeof(VagabondChargeStartState)] = new VagabondChargeStartState();
        cachedStates[typeof(VagabondChargeHoldingState)] = new VagabondChargeHoldingState();
        cachedStates[typeof(VagabondChargedAttackState)] = new VagabondChargedAttackState();
        cachedStates[typeof(VagabondChargeCancelState)] = new VagabondChargeCancelState();
    }

    public T GetState<T>() where T : State<Vagabond> => cachedStates[typeof(T)] as T;

    public override void GoToIdleState() => stateMachine.ChangeState(GetState<VagabondIdleState>());

    public override void GoToComboAttackState()
    {
        var comboState = GetState<VagabondComboAttackState>();
        comboState.SetComboIndex(characterData.attackComboStep);
        stateMachine.ChangeState(comboState);
    }

    public override void GotoDodgeState() => stateMachine.ChangeState(GetState<VagabondDodgeState>());
    public override void GoToHeavyAttackChargeStartState() => stateMachine.ChangeState(GetState<VagabondChargeStartState>());
    public override void GoToHeavyAttackChargeHoldingState() => stateMachine.ChangeState(GetState<VagabondChargeHoldingState>());
    public override void GoToHeavyAttackChargedAttackState() => stateMachine.ChangeState(GetState<VagabondChargedAttackState>());
    public override void GoToHeavyAttackChargeCancelState() => stateMachine.ChangeState(GetState<VagabondChargeCancelState>());

    private void BindInputActions()
    {
        inputActions.Player.Attack.started += _ => OnAttackStarted();
        inputActions.Player.Attack.canceled += _ => OnAttackReleased();
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

         //TODO: 기존 강공격이지만 마우스 유지만 사용하는 모으기 공격에 적합.
        CheckHeavyAttackChargingState();


        if (CurrentAirState == AirState.InAir && !(stateMachine.CurrentState is VagabondInAirState))
            stateMachine.ChangeState(GetState<VagabondInAirState>());

        if (characterData.comboTimer > 0)
        {
            characterData.comboTimer -= Time.deltaTime;
            if (characterData.comboTimer <= 0)
                ResetCombo();
        }
    }

    private void CheckHeavyAttackChargingState()
    {
        if (weaponManagerSO?.CurrentWeapon == null)
            return;

        var weaponType = weaponManagerSO.CurrentWeapon.weaponType;

        switch (weaponType)
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
        if (isAttacking) //기본 강공격 용으로 만들어야 함 기본 강공격만 걸러야 보우의 강공격이 놓은때를 인식할수 있음 아니면 걸림 is Attacking에
        {
            return;
        }

        if (inputActions.Player.Attack.IsPressed())
        {
            heavyAttackChargeTime += Time.deltaTime;

            if (!isInChargingState && heavyAttackChargeTime >= heavyAttackReleaseTime)
            {
                isInChargingState = true;
                HeavyAttackAbility.HeavyAttackStartCharging(this);
            }

            if (isInChargingState)
            {
                HeavyAttackAbility.HeavyAttackUpdateCharging(this, heavyAttackChargeTime);
            }
        }
        else
        {
            // ✅ 단순 입력 해제 시에는 내부 변수만 초기화
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
            {
                HeavyAttackAbility.HeavyAttackUpdateCharging(this, heavyAttackChargeTime);
            }
        }
        else
        {
            // ✅ 단순 입력 해제 시에는 내부 변수만 초기화
            heavyAttackChargeTime = 0f;
            isInChargingState = false;
        }
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
    
    private void OnAttackStarted()
    {
        // ✅ 공격이 입력되었지만 바로 처리되지 않는 상태라면, 시간만 저장
        if (!CanProcessInput())
        {
            attackInputTime = Time.time;
            return;
        }

        attackInputTime = Time.time;
        isInChargingState = false;
        heavyAttackChargeTime = 0f;
        heldDuration = 0f;
    }

    public void OnAttackReleased()
    {
        if (!CanProcessInput()) return;

        // ✅ fallback: attackInputTime이 설정되지 않았더라도 대응
        if (attackInputTime == 0f)
        {
            Debug.LogWarning("Attack released without a valid start time. Fallback initialized.");
             return;
        }

        heldDuration = Time.time - attackInputTime;

        if (heldDuration >= heavyAttackChargeThreshold)
        {
            HeavyAttackAbility?.HeavyAttackReleaseChargedAttack(this, heldDuration);
        }
        else
        {
            if (!isAttacking)
                LightAttackAbility?.LightAttack(this);
            else
                nextComboQueued = true;
        }

        attackInputTime = 0f;
        heavyAttackChargeTime = 0f;
        heldDuration = 0f;
        isInChargingState = false;

        OnAttackAnimationEnd();
    }



    public bool ShouldCancelAttack()
    {
        // 구르기(Dodge) 입력이 들어왔거나, 다른 취소 조건이 충족되면 true 반환
        return inputActions.Player.Dodge.triggered;  // 예시: 구르기 입력 감지
    }


    public void CancelHeavyAttack()
    {
        // 강공격을 취소하는 공용 메서드
        heavyAttackChargeTime = 0f;
        OnAttackAnimationEnd();  // 애니메이션 종료 처리
        StateMachine.ChangeState(GetState<VagabondIdleState>());  // Idle 상태로 전환
    }



    public void OnAttackAnimationStart()
    {
        isAttacking = true;
    }

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

    private void ChangeWeapon(int index)
    {
        if (isInputLocked) return;

        if (weaponManagerSO == null)
        {
            Debug.LogError("weaponManagerSO is not initialized.");
            return;
        }

        if (weaponManagerSO.GetCurrentSlotIndex() == index) return;

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