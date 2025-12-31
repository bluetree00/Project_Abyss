 using Cysharp.Threading.Tasks;
using Game.Inputs;
using UnityEngine;

public class Knight : PlayerController
{
    private async void Start()
    {
        await InitAsync();
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
        actSM.Register(ActState.None,         new ActNoneState());
        actSM.Register(ActState.AttackReady,  new ActAttackReadyState());
        actSM.Register(ActState.Attack,       new ActAttackState());
        actSM.Register(ActState.Charge,       new ActAttackChargeState());
        actSM.Register(ActState.HeavyAttack,  new ActHeavyAttackState());
        actSM.Register(ActState.QSkill,       new ActQSkillState());
        actSM.Register(ActState.ESkill,       new ActESkillState());

        locoSM.Change(IsGrounded() ? LocoState.Idle : LocoState.Air);
        actSM.Change(ActState.None);
    }

    // 입력 라우팅: 버퍼 소비 → 레이어 전이
    protected override void RouteInputsToLayers()
    {
        
        if (InputBuffer.TryConsume(Command.QSkill))
        {
            if (!CanAttack())
            {
                // 무기 없음 — 무시
                Debug.Log("[RouteInputsToLayers] Heavy ignored - no weapon");
                return;
            }
           
            actSM.Change(ActState.QSkill);
            return;
        }

        if (InputBuffer.TryConsume(Command.ESkill))
        {
            if (!CanAttack())
            {
                // 무기 없음 — 무
                Debug.Log("[RouteInputsToLayers] Heavy ignored - no weapon");
                return;
            }

            actSM.Change(ActState.ESkill);
            return;
        }

        if (InputBuffer.TryConsume(Command.Dodge))
        {
            locoSM.Change(LocoState.Dodge);
            return;
        }

        // RouteInputsToLayers 또는 매 프레임 입력 라우팅 위치
        if (InputBuffer != null && InputBuffer.TryConsume(Game.Inputs.Command.Charge))
        {
            if (!CanAttack())
            {
                Debug.Log("[RouteInputsToLayers] Charge ignored - no weapon");
                return;
            }
            actSM.Change(ActState.Charge);
            return; // Charge는 모으기 우선 처리
        }



            // 공격 입력: Heavy 우선 검사
        if (InputBuffer.TryConsume(Command.Heavy))
        {
            if (!CanAttack())
            {
                // 무기 없음 — 무시
                Debug.Log("[RouteInputsToLayers] Heavy ignored - no weapon");
                return;
            }
            SetPendingAttack(Command.Heavy);
            actSM.Change(ActState.AttackReady);
            return;
        }

        if (InputBuffer.TryConsume(Command.Light))
        {
            if (!CanAttack())
            {
                Debug.Log("[RouteInputsToLayers] Light ignored - no weapon");
                return;
            }
            SetPendingAttack(Command.Light);
            Debug.Log("[RouteInputsToLayers] Light attack input received.");
            actSM.Change(ActState.AttackReady);
            return;
        }
    }


    // 이동 입력 벡터 계산(기존 그대로)
   // 이동 입력 벡터 계산
    private void CheckMovementInput()
    {
        if (locoSM.CurrentId == LocoState.Air) return; // 공중이면 무시
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
