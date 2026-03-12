using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Game.Inputs;

public class ActAttackChargeState : ILayerState<ActState>
{
    private PlayerController _controller;
    private ILayerStateChanger<ActState> _changer;
    private IAttackInputPolicy _policy;

    private bool _wasChargeReadyBefore = false;

    public void Init(PlayerController controller, ILayerStateChanger<ActState> changer)
    {
        _controller = controller;
        _changer = changer;
    }

    public void Enter()
    {

        // 이동/애니 제한
        _controller.SetMoveScale(0f);
        _controller.Combo.SetAttacking(true);

        // 애니 재생(예: 모으기 시작)
        _controller.Anim.CrossFade("HeavyCharge", 0.08f);

    }

    public void Update()
    {

    }

    public void Exit()
    {
        _controller.SetMoveScale(1f);
        Debug.Log("[ActAttackChargeState] Exit - cleaned up");
    }
}
