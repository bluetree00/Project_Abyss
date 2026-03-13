// ActAttackReadyState.cs
using System;
using UnityEngine;
using Game.Inputs;

public class ActAttackReadyState : ILayerState<ActState>
{
    private PlayerController _controller;
    private ILayerStateChanger<ActState> _stateChanger;

    public void Init(PlayerController controller, ILayerStateChanger<ActState> stateChanger)
    {
        _controller = controller;
        _stateChanger = stateChanger;
    }

    public void Enter()
    {
        if (_controller.Combo.IsAttacking)
        {
            Debug.Log("[ActAttackReadyState] Already attacking, aborting Enter.");
            _stateChanger.Change(ActState.None); // 공격 중이면 바로 None으로
            return;
        }

        var isAir = !_controller.IsGrounded();
        _controller.SetMoveScale(0f);

        // 매핑: (지상/공중) × (Light/Heavy) -> WeaponActionType
        WeaponActionType atype;

        // --- 입력 기반 PendingAttack 읽기 ---
        if (_controller.HasPendingAttack)
        {
            var pending = _controller.PendingAttackCommand;

            if (isAir)
                atype = (pending == Command.Heavy) ? WeaponActionType.AirHeavy : WeaponActionType.AirLight;
            else
                atype = (pending == Command.Heavy) ? WeaponActionType.GroundHeavy : WeaponActionType.GroundLight;

            _controller.CurrentAttackTypeForEffect = atype;

            // Pending 초기화
            _controller.ClearPendingAttack();
        }
        else
        {
            // 안전장치: 기본 라이트/지상
            atype = isAir ? WeaponActionType.AirLight : WeaponActionType.GroundLight;
            _controller.CurrentAttackTypeForEffect = atype;
        }

        Debug.Log($"[ActAttackReadyState] Entered. isAir: {isAir}, AttackType: {_controller.CurrentAttackTypeForEffect}");

        // --- 여기서 분기: Heavy이면 Heavy 상태로, 아니면 일반 Attack 상태로 ---
        if (atype == WeaponActionType.GroundHeavy || atype == WeaponActionType.AirHeavy)
            _stateChanger.Change(ActState.HeavyAttack);
        else
            _stateChanger.Change(ActState.Attack);
    }

    public void Update() { }
    public void Exit() { }
}
