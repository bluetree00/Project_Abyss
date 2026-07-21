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

        // [점프 공격 폐기] 공중(낙하·넉백 포함)에서는 공격 진입 자체를 막는다 → 공중/플런지 공격 제거.
        // 공중 상태 자체는 이동/물리용으로 유지하되, '공중에서 공격'이라는 플레이어 동작만 없앤다.
        // (이 가드로 아래 isAir 분기들은 도달 불가가 된다.)
        if (isAir)
        {
            _stateChanger.Change(ActState.None);
            return;
        }

        _controller.SetMoveScale(0f);

        // 매핑: (지상/공중) × (Light/Heavy) -> WeaponActionType
        WeaponActionType atype;

        // --- 입력 기반 PendingAttack 읽기 ---
        if (_controller.HasPendingAttack)
        {
            var pending = _controller.PendingAttackCommand;

            if (isAir)
                atype = WeaponActionType.AirLight;
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
