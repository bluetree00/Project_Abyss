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
        actSM.Register(ActState.None,        new ActNoneState());
        actSM.Register(ActState.AttackReady, new ActAttackReadyState());
        actSM.Register(ActState.Attack,      new ActAttackState());
        actSM.Register(ActState.Charge,      new ActAttackChargeState());
        actSM.Register(ActState.HeavyAttack, new ActHeavyAttackState());
        actSM.Register(ActState.QSkill,      new ActQSkillState());
        actSM.Register(ActState.ESkill,      new ActESkillState());
        actSM.Register(ActState.RSkill,      new ActRSkillState());
        actSM.Register(ActState.Plunge,      new ActPlungeState());
        actSM.Register(ActState.Pickup,      new ActPickupState());

        locoSM.Change(IsGrounded() ? LocoState.Idle : LocoState.Air);
        actSM.Change(ActState.None);
    }

    // Knight는 Charge/HeavyAttack도 공격 상태로 간주
    protected override bool IsInAttackOrSkillState() =>
        base.IsInAttackOrSkillState()           ||
        actSM.CurrentId == ActState.Charge      ||
        actSM.CurrentId == ActState.HeavyAttack;

    protected override void InitPassives()
    {
        RegisterPassive(new KnightBloodthirstPassive(healAmount: 15));
        RegisterPassive(new KnightComboFervorPassive(requiredComboStep: 2));
    }

    // 입력 라우팅: 버퍼 소비 → 레이어 전이
    protected override void RouteInputsToLayers()
    {
        if (actSM.CurrentId == ActState.Pickup) return;

        bool isInSkill = actSM.CurrentId == ActState.QSkill ||
                         actSM.CurrentId == ActState.ESkill ||
                         actSM.CurrentId == ActState.RSkill;
        bool isDodging = locoSM.CurrentId == LocoState.Dodge;
        bool isInAct   = actSM.CurrentId != ActState.None || isDodging;

        if (InputBuffer.TryConsume(Command.QSkill))
        {
            if (CanAttack() && !isInSkill) actSM.Change(ActState.QSkill);
            return;
        }
        if (InputBuffer.TryConsume(Command.ESkill))
        {
            if (CanAttack() && !isInSkill) actSM.Change(ActState.ESkill);
            return;
        }
        if (InputBuffer.TryConsume(Command.RSkill))
        {
            if (CanAttack() && !isInSkill) actSM.Change(ActState.RSkill);
            return;
        }

        if (InputBuffer.TryConsume(Command.Dodge))
        {
            if (!isDodging && Time.time >= DodgeCooldownEnd)
            {
                if (isInAct) actSM.Change(ActState.None);
                locoSM.Change(LocoState.Dodge);
            }
            return;
        }

        if (isInAct) return;

        if (InputBuffer.TryConsume(Command.Charge))
        {
            if (CanAttack()) actSM.Change(ActState.Charge);
            return;
        }
        if (InputBuffer.TryConsume(Command.Heavy))
        {
            if (CanAttack()) { SetPendingAttack(Command.Heavy); actSM.Change(ActState.AttackReady); }
            return;
        }
        if (InputBuffer.TryConsume(Command.Light))
        {
            if (CanAttack()) { SetPendingAttack(Command.Light); actSM.Change(ActState.AttackReady); }
            return;
        }
    }
}
