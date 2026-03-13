using Game.Inputs;
using UnityEngine;

public class Knight : PlayerController
{
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
        bool isInSkill  = actSM.CurrentId == ActState.QSkill || actSM.CurrentId == ActState.ESkill;
        bool isDodging  = locoSM.CurrentId == LocoState.Dodge;
        bool isInAct    = actSM.CurrentId != ActState.None || isDodging;

        // QSkill: 스킬 중에는 캔슬 불가, 공격 중에는 캔슬 가능
        if (InputBuffer.TryConsume(Command.QSkill))
        {
            if (CanAttack() && !isInSkill)
                actSM.Change(ActState.QSkill);
            return;
        }

        // ESkill: 스킬 중에는 캔슬 불가, 공격 중에는 캔슬 가능
        if (InputBuffer.TryConsume(Command.ESkill))
        {
            if (CanAttack() && !isInSkill)
                actSM.Change(ActState.ESkill);
            return;
        }

        // Dodge: 공격/스킬 캔슬 가능. 회피 중이거나 쿨다운 중이면 차단
        if (InputBuffer.TryConsume(Command.Dodge))
        {
            bool onCooldown = Time.time < DodgeCooldownEnd;

            if (!isDodging && !onCooldown)
            {
                if (isInAct)
                    actSM.Change(ActState.None);
                locoSM.Change(LocoState.Dodge);
            }
            return;
        }

        // 이하 입력(Charge, Heavy, Light)은 행동 중 모두 차단
        if (isInAct) return;

        if (InputBuffer.TryConsume(Command.Charge))
        {
            if (!CanAttack()) return;
            actSM.Change(ActState.Charge);
            return;
        }

        if (InputBuffer.TryConsume(Command.Heavy))
        {
            if (!CanAttack()) return;
            SetPendingAttack(Command.Heavy);
            actSM.Change(ActState.AttackReady);
            return;
        }

        if (InputBuffer.TryConsume(Command.Light))
        {
            if (!CanAttack()) return;
            SetPendingAttack(Command.Light);
            actSM.Change(ActState.AttackReady);
            return;
        }
    }

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

    }

}
