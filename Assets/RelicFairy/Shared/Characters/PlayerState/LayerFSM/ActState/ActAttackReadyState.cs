// ActAttackReadyState.cs
using UnityEngine;

/// <summary>
/// 공격 진입 관문 — 지상 약공격 하나로만 이어진다.
///
/// [점프 공격 폐기] 공중(낙하·넉백 포함)에서는 공격 진입 자체를 막는다.
/// 공중 상태 자체는 이동/물리용으로 유지하되, '공중에서 공격'이라는 플레이어 동작만 없앤다.
/// [강공격 봉인] 강공격 분기도 제거됐다 — 라우팅이 Heavy를 Light로 치환하므로 여기까지 오지 않는다.
/// </summary>
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

        if (!_controller.IsGrounded())
        {
            _stateChanger.Change(ActState.None);
            return;
        }

        _controller.SetMoveScale(0f);

        _controller.CurrentAttackTypeForEffect = WeaponActionType.GroundLight;
        _controller.ClearPendingAttack();

        _stateChanger.Change(ActState.Attack);
    }

    public void Update() { }
    public void Exit() { }
}
