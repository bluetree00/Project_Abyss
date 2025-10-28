using Cysharp.Threading.Tasks;
using Game.Inputs;
using UnityEngine;

public class Knight : PlayerController
{
    private async void Start()
    {
        await InitAsync();
        InitLayerFSMs();
    }

    protected override async UniTask InitAsync()
    {
        await base.InitAsync();
    }

    protected override void InitLayerFSMs()
    {
  
        // Locomotion 상태 등록
        locoSM.Register(LocoState.Idle,  new LocoIdleState());
        locoSM.Register(LocoState.Move,  new LocoMoveState());
        locoSM.Register(LocoState.Air,   new LocoAirState());
        locoSM.Register(LocoState.Dodge, new LocoDodgeState());

        // Action 상태 등록
        actSM.Register(ActState.None,        new ActNoneState());
        actSM.Register(ActState.AttackReady, new ActAttackReadyState());
        actSM.Register(ActState.Attack,      new ActAttackState());
        actSM.Register(ActState.Skill,       new ActSkillState());

        locoSM.Change(IsGrounded() ? LocoState.Idle : LocoState.Air);
        actSM.Change(ActState.None);
    }

    // 입력 라우팅: 버퍼 소비 → 레이어 전이
    protected override void RouteInputsToLayers()
    {
        if (InputBuffer.TryConsume(Command.Dodge))
        {
            locoSM.Change(LocoState.Dodge);
            return;
        }

        if (InputBuffer.TryConsume(Command.Skill))
        {
            actSM.Change(ActState.Skill);
            return;
        }

        // 공격 입력: Heavy 우선 검사, 발견 시 Pending에 기록 후 상태 전이
        if (InputBuffer.TryConsume(Command.Heavy))
        {
            SetPendingAttack(Command.Heavy);
            actSM.Change(ActState.AttackReady);
            return;
        }

        if (InputBuffer.TryConsume(Command.Light))
        {
            SetPendingAttack(Command.Light);
            actSM.Change(ActState.AttackReady);
            return;
        }
    }


    // 이동 입력 벡터 계산(기존 그대로)
   // 이동 입력 벡터 계산
    private void CheckMovementInput()
    {
        // isInputLocked 제거 → 항상 입력 벡터 계산
        var input   = inputActions.Player.Move.ReadValue<Vector2>();
        var forward = cinemachineCamera.transform.forward; forward.y = 0;
        var right   = cinemachineCamera.transform.right;   right.y   = 0;

        // 정규화하여 moveDirection 저장
        moveDirection = (forward.normalized * input.y + right.normalized * input.x).normalized;
    }

    protected override void Update()
    {
        if (!inputReady || characterData == null || cinemachineCamera == null) return;

        base.Update();

        // 입력 → 버퍼
        CheckMovementInput();

        // 버퍼 만료 정리
        InputBuffer?.TickPrune();

        // 콤보 타이머 같은 부가 로직이 있다면 여기서
        if (characterData.comboTimer > 0f)
        {
            characterData.comboTimer -= Time.deltaTime;
            if (characterData.comboTimer <= 0f) ResetCombo();
        }

    }

    private void ResetCombo()
    {
        characterData.attackComboStep = 0;
        characterData.comboTimer = 0;
        nextComboQueued = false;
        // Debug.Log("Combo reset due to timer expiration.");
    }

    // 무기 변경도 액션 중립 상태에서만 허용 권장
    protected override void ChangeWeapon(int index)
    {

    }
}
