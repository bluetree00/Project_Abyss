using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using Cinemachine;
using Game.CharacterStates;
using Game.CharacterStates.VagabondStates;

public class Vagabond : CharacterController
{
    [SerializeField] private CinemachineFreeLook cinemachineCamera;

    private Coroutine dodgeCoroutine;
    private bool isInventoryOpen = false;
    private bool isInputLocked = false;
    private float inputLockDuration = 2f;

    protected new StateMachine<Vagabond> stateMachine = new StateMachine<Vagabond>();
    public new StateMachine<Vagabond> StateMachine => stateMachine;

    private async void Start()
    {
        await InitAsync();
        stateMachine.Setup(this, new VagabondIdleState());
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
            BindInputActions(); // ✅ 입력 바인딩
    }

    private void BindInputActions()
    {
        inputActions.Player.Attack.performed += _ => ProcessAttack();
        inputActions.Player.Dodge.performed += _ => ProcessDodge();
        inputActions.Player.Jump.performed += _ => ProcessJump();
        inputActions.Player.Skill.performed += _ => ProcessSkill();
        inputActions.Player.Ultimate.performed += _ => ProcessUltimate();
        inputActions.Player.InventoryToggle.performed += _ => ToggleInventory();
        inputActions.Player.CloseInventory.performed += _ => CloseInventory();
        inputActions.Player.ChangeWeapon1.performed += _ => ChangeWeapon(1);
        inputActions.Player.ChangeWeapon2.performed += _ => ChangeWeapon(2);
        inputActions.Player.SetWeapon.performed += _ => SetWeapon("basic_Knight_02");
        inputActions.Player.RemoveWeapon1.performed += _ => RemoveWeapon(1);
        inputActions.Player.RemoveWeapon2.performed += _ => RemoveWeapon(2);
    }

    protected override void Update()
    {
        if (!inputReady || characterData == null || cinemachineCamera == null) return;

        base.Update();
        CheckMovementInput();
        UpdateMovement();
        FreezeRotation();
        stateMachine.Update();

        if (CurrentAirState == AirState.InAir && !(stateMachine.CurrentState is VagabondInAirState))
            stateMachine.ChangeState(new VagabondInAirState());

        if (characterData.comboTimer > 0)
        {
            characterData.comboTimer -= Time.deltaTime;
            if (characterData.comboTimer <= 0) ResetCombo();
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

    protected void UpdateMovement()
    {
        if (!CanProcessInput()) return;

        if (moveDirection.magnitude > 0)
        {
            if (inputActions.Player.Run.IsPressed())
                stateMachine.ChangeState(new VagabondRunState());
            else
                stateMachine.ChangeState(new VagabondMoveState());
        }
        else
        {
            stateMachine.ChangeState(new VagabondIdleState());
        }
    }

    private bool CanProcessInput() => !(stateMachine.CurrentState?.BlocksInput ?? false);
    private void FreezeRotation() => rb.angularVelocity = Vector3.zero;

    private void ProcessAttack()
    {
        if (!CanProcessInput()) return;

        characterData.comboTimer = characterData.comboDuration;
        characterData.attackComboStep++;

        if (characterData.attackComboStep > currentWeapon.maxComboCount)
            characterData.attackComboStep = 1;

        string animName = currentWeapon.normalAttackAnimations[characterData.attackComboStep - 1];
        stateMachine.ChangeState(new VagabondComboAttackState(animName, characterData.attackComboStep));
    }

    private void ResetCombo()
    {
        characterData.attackComboStep = 0;
        characterData.comboTimer = 0;
        stateMachine.ChangeState(new VagabondIdleState());
    }

    private void ProcessSkill() => stateMachine.ChangeState(new VagabondSkillState());
    private void ProcessUltimate() => stateMachine.ChangeState(new VagabondUltimateState());

    private void ProcessDodge()
    {
        if (!CanProcessInput() || !characterData.canDodge) return;
        dodgeCoroutine = StartCoroutine(DashCoroutine());
    }

    private IEnumerator DashCoroutine()
    {
        characterData.canDodge = false;
        stateMachine.ChangeState(new VagabondDodgeState());

        float startTime = Time.time;
        while (Time.time < startTime + characterData.dashDuration)
        {
            CheckMovementInput();
            Vector3 dashDir = moveDirection != Vector3.zero ? moveDirection : transform.forward;
            rb.velocity = dashDir * characterData.dashSpeed;

            Quaternion targetRot = Quaternion.LookRotation(dashDir);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 10f);
            yield return null;
        }

        rb.velocity = Vector3.zero;
        yield return new WaitForSeconds(characterData.dodgeCooldown);
        characterData.canDodge = true;
    }

    private void ProcessJump()
    {
        if (IsJumping() || !IsGrounded()) return;
        stateMachine.ChangeState(new VagabondJumpStartState());
    }

    private void ChangeWeapon(int index)
    {
        if (isInputLocked) return;

        weaponContainer.isWeaponEquipped = false;
        Managers.Weapon.ChangeWeapon(index);

        if (weaponContainer.ownWeapons[index - 1] == null)
        {
            stateMachine.ChangeState(new VagabondIdleState());
        }
        else
        {
            stateMachine.ChangeState(new VagabondChangeWeaponState());
            currentWeapon = Managers.Weapon.GetCurrentWeaponData();
        }

        StartCoroutine(LockInput(inputLockDuration));
    }

    private void SetWeapon(string weaponName)
    {
        if (isInputLocked) return;
        Managers.Weapon.SetWeapon(weaponName);
        StartCoroutine(LockInput(inputLockDuration));
    }

    private void RemoveWeapon(int index)
    {
        Managers.Weapon.RemoveWeapon(index);
        if (currentWeapon == null)
            stateMachine.ChangeState(new VagabondIdleState());
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
            StartCoroutine(LockInput(inputLockDuration));
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

    private IEnumerator LockInput(float sec)
    {
        isInputLocked = true;
        yield return new WaitForSeconds(sec);
        isInputLocked = false;
    }

    #region 이펙트 관련
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
        effectObject.transform.position = spawnPosition;
        effectObject.transform.rotation = spawnRotation;
    }
    #endregion
}
