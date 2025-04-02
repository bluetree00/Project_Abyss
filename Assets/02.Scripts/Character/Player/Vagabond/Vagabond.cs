using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using Game.CharacterStates;
using Game.CharacterStates.VagabondStates;
using Cinemachine;

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

        Managers.Input_M.KeyAction -= OnInput;
        Managers.Input_M.KeyAction += OnInput;

        await Task.CompletedTask;
    }

    protected override void Update()
    {
        if (characterData == null || cinemachineCamera == null) return;

        base.Update();
        CheckMovementInput();
        UpdateMovement();
        FreezeRotation();
        stateMachine.Update();

         // 공중 상태 연동
        if (CurrentAirState == AirState.InAir && !(stateMachine.CurrentState is VagabondInAirState))
        {
            stateMachine.ChangeState(new VagabondInAirState());
        }


        if (characterData.comboTimer > 0)
        {
            characterData.comboTimer -= Time.deltaTime;
            if (characterData.comboTimer <= 0) ResetCombo();
        }
    }

    private void OnInput()
    {
        if (Input.GetMouseButtonDown(1)) ProcessDodge();
        if (!CanProcessInput()) return;

        if (Input.GetMouseButtonDown(0)) ProcessAttack();
        if (Input.GetKeyDown(KeyCode.E)) ProcessSkile();
        if (Input.GetKeyDown(KeyCode.Q)) ProcessUltimateSkile();
        if (Input.GetKeyDown(KeyCode.I)) ToggleInventory();
        if (Input.GetKeyDown(KeyCode.Escape)) CloseInventory();
        if (Input.GetKeyDown(KeyCode.Alpha1)) ChangeWeapon(1);
        if (Input.GetKeyDown(KeyCode.Alpha2)) ChangeWeapon(2);
        if (Input.GetKeyDown(KeyCode.G)) SetWeapon("basic_Knight_02");
        if (Input.GetKeyDown(KeyCode.F1)) RemoveWeapon(1);
        if (Input.GetKeyDown(KeyCode.F2)) RemoveWeapon(2);
        if (Input.GetKeyDown(KeyCode.Space)) ProcessJump(); // ✅ 점프 입력 처리
    }

    private bool CanProcessInput() => !(stateMachine.CurrentState?.BlocksInput ?? false);

    private IEnumerator LockInput(float sec)
    {
        isInputLocked = true;
        yield return new WaitForSeconds(sec);
        isInputLocked = false;
    }

    private void CheckMovementInput()
    {
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");

        Vector3 forward = cinemachineCamera.transform.forward;
        Vector3 right = cinemachineCamera.transform.right;

        forward.y = right.y = 0;
        moveDirection = (forward.normalized * v + right.normalized * h).normalized;
    }

    protected void UpdateMovement()
    {
        if (!CanProcessInput()) return;

        if (moveDirection.magnitude > 0)
        {
            if (Input.GetKey(KeyCode.LeftShift))
                stateMachine.ChangeState(new VagabondRunState());
            else
                stateMachine.ChangeState(new VagabondMoveState());
        }
        else
        {
            stateMachine.ChangeState(new VagabondIdleState());
        }
    }

    private void FreezeRotation() => rb.angularVelocity = Vector3.zero;

    private void ProcessAttack()
    {
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

    private void ProcessSkile() => stateMachine.ChangeState(new VagabondSkillState());
    private void ProcessUltimateSkile() => stateMachine.ChangeState(new VagabondUltimateState());

    private void ProcessDodge()
    {
        if (characterData.canDodge)
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

        stateMachine.ChangeState(new VagabondJumpStartState()); // ✅ 점프 시작 상태로 전환
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
