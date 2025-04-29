using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Cinemachine;
using Game.CharacterStates;
using Game.CharacterStates.VagabondStates;

public class Vagabond : CharacterController
{
    //============================================================
    // 🔶 상태 및 변수
    //============================================================

    [Header("Camera")]
    [SerializeField] private CinemachineFreeLook cinemachineCamera;

    private Coroutine inputLockCoroutine;
    private bool isInventoryOpen = false;
    private bool isInputLocked = false;
    private float inputLockDuration = 2f;

    private float attackInputTime = 0f; // 공격 입력 시작 시간
    private const float lightAttackDuration = 0.4f; // 일반 공격 최대 시간
    private const float heavyAttackDuration = 0.5f; // 강공격 최대 시간

    protected new StateMachine<Vagabond> stateMachine = new StateMachine<Vagabond>();
    public new StateMachine<Vagabond> StateMachine => stateMachine;

    private Dictionary<Type, State<Vagabond>> cachedStates = new();

    //============================================================
    // 🟢 초기화
    //============================================================

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
        }

        if (inputReady)
            BindInputActions();
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
    }

    public T GetState<T>() where T : State<Vagabond> => cachedStates[typeof(T)] as T;

    public override void GoToIdleState() => stateMachine.ChangeState(GetState<VagabondIdleState>());

    //============================================================
    // 🔹 입력 바인딩
    //============================================================

    private void BindInputActions()
    {
        inputActions.Player.Attack.started += _ => OnAttackStarted(); // 공격 시작 기록용
        inputActions.Player.Attack.performed += _ => ProcessAttack(); // 공격 수행
        inputActions.Player.Attack.canceled += _ => OnAttackCanceled(); // 공격 종료

        inputActions.Player.Dodge.performed += _ => DodgeAbility?.Dodge(this);
        inputActions.Player.Jump.performed += _ => ProcessJump();
        inputActions.Player.Skill.performed += _ => stateMachine.ChangeState(GetState<VagabondSkillState>());
        inputActions.Player.Ultimate.performed += _ => stateMachine.ChangeState(GetState<VagabondUltimateState>());
        inputActions.Player.InventoryToggle.performed += _ => ToggleInventory();
        inputActions.Player.CloseInventory.performed += _ => CloseInventory();
        inputActions.Player.ChangeWeapon1.performed += _ => ChangeWeapon(0);
        inputActions.Player.ChangeWeapon2.performed += _ => ChangeWeapon(1);
    }

    //============================================================
    // 🔄 Unity 생명주기
    //============================================================

    protected override void Update()
    {
        if (!inputReady || characterData == null || cinemachineCamera == null) return;

        base.Update();
        CheckMovementInput();

        UpdateMovement();
        FreezeRotation();
        stateMachine.Update();

        if (CurrentAirState == AirState.InAir && !(stateMachine.CurrentState is VagabondInAirState))
            stateMachine.ChangeState(GetState<VagabondInAirState>());

        if (characterData.comboTimer > 0)
        {
            characterData.comboTimer -= Time.deltaTime;
            if (characterData.comboTimer <= 0);
                //ResetCombo();
        }
    }

    private void CheckMovementInput()
    {
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
    private bool CanProcessInput() => !isInputLocked && !(stateMachine.CurrentState?.BlocksInput ?? false);

    //============================================================
    // 🗡️ 전투 및 스킬
    //============================================================

   // 공격 시작 기록
    private void OnAttackStarted()
    {
        attackInputTime = Time.time;  // 공격 입력 시작 시간
    }

    // 공격 수행
    private void ProcessAttack()
    {
        if (!CanProcessInput()) return;

        float inputTime = Time.time - attackInputTime;

        // 입력 시간이 lightAttackDuration 이하일 경우 일반 공격
        if (inputTime <= lightAttackDuration)
        {
            PerformLightAttack();
        }
        // 입력 시간이 heavyAttackDuration 이상일 경우 강공격
        else if (inputTime > heavyAttackDuration)
        {
            PerformHeavyAttack();
        }
        // 그 외에는 콤보 공격
        else
        {
            PerformComboAttack(inputTime);
        }
    }

    // 공격 취소
    private void OnAttackCanceled()
    {
        float inputTime = Time.time - attackInputTime;

        if (inputTime <= lightAttackDuration)
        {
            PerformLightAttack();
        }
        else if (inputTime > heavyAttackDuration)
        {
            PerformHeavyAttack();
        }
    }

    // 일반 공격 처리
    private void PerformLightAttack()
{
    if (weaponManagerSO.CurrentWeapon == null)
    {
        Debug.Log("No weapon equipped! Cannot perform attack.");
        return;
    }

    Debug.Log("Light Attack performed");

    // attackComboStep을 0-based로 다루기
    int comboIndex = characterData.attackComboStep;
    var attackAnimations = weaponManagerSO.CurrentWeapon.attackAnimations;

    if (comboIndex < 0 || comboIndex >= attackAnimations.Count)
    {
        Debug.LogWarning("Combo index out of bounds, using last available animation.");
        comboIndex = attackAnimations.Count - 1;
    }

    string comboAnimation = attackAnimations[comboIndex].name;
    stateMachine.ChangeState(new VagabondComboAttackState(comboAnimation, characterData.attackComboStep));
}




    // 강공격 처리
    private void PerformHeavyAttack()
    {
        if (weaponManagerSO.CurrentWeapon == null)
        {
            Debug.LogError("No weapon equipped! Cannot perform attack.");
            return; // 무기가 없으면 공격을 수행하지 않음
        }

        Debug.Log("Heavy Attack performed");
        characterData.attackComboStep = 1; // 강공격 시 콤보 초기화

        // 애니메이션 이름과 콤보 단계 전달
        string comboAnimation = weaponManagerSO.CurrentWeapon.normalAttackAnimations[characterData.attackComboStep - 1]; // 애니메이션 이름
        stateMachine.ChangeState(new VagabondComboAttackState(comboAnimation, characterData.attackComboStep)); // 콤보 단계와 애니메이션 이름 넘기기
    }


    // 콤보 공격 처리
   private void PerformComboAttack(float inputTime)
{
    if (weaponManagerSO.CurrentWeapon == null)
    {
        Debug.LogError("No weapon equipped! Cannot perform combo attack.");
        return; // 무기가 없으면 콤보 공격을 수행하지 않음
    }

    Debug.Log("Combo Attack performed");

    // 콤보 단계 증가 (0-based로 변경)
    int comboIndex = characterData.attackComboStep;

    // 공격 콤보 단계에 맞는 애니메이션 처리
    string comboAnimation = weaponManagerSO.CurrentWeapon.attackAnimations[comboIndex].name;

    // 콤보 진행 상태 전환
    stateMachine.ChangeState(new VagabondComboAttackState(comboAnimation, characterData.attackComboStep));

    // 콤보 단계 증가
    characterData.attackComboStep++;
    if (characterData.attackComboStep >= weaponManagerSO.CurrentWeapon.maxAttackCount)
    {
        // 최대 콤보 카운트에 도달하면 초기화
        characterData.attackComboStep = 0;
    }
}




    private void ProcessJump()
    {
        if (IsJumping() || !IsGrounded()) return;
        stateMachine.ChangeState(GetState<VagabondJumpStartState>());
    }

    //============================================================
    // 🧤 무기 관리
    //============================================================

    private void ChangeWeapon(int index)
    {
        if (isInputLocked) return;

        if (weaponManagerSO == null)
        {
            Debug.LogError("weaponManagerSO is not initialized.");
            return;
        }

        weaponManagerSO.SwitchWeapon(index, anim);
       
    }
   




    //============================================================
    // 🎒 인벤토리
    //============================================================

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

    //============================================================
    // 🔐 입력 잠금 처리
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

    //============================================================
    // ✨ 이펙트 스폰   // 추후 무기별 고유 공격으로 생성 개선 예정정
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
