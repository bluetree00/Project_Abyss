// ActSkillState.cs
using UnityEngine;

public class ActQSkillState : ILayerState<ActState>
{
    private PlayerController _controller;
    private ILayerStateChanger<ActState> _stateChanger;

    private float _time;
    private float _duration = 0.6f; // 스킬 연출 시간

    public void Init(PlayerController controller, ILayerStateChanger<ActState> stateChanger)
    { _controller = controller; _stateChanger = stateChanger; }

    public void Enter()
    {
        _time = 0f;
        _controller.SetMoveScale(0f);
        _controller.Anim.CrossFade("QSkill_01", 0.08f);
    }

    public void Update()
    {

    }

    public void Exit()
    {

        _controller.SetMoveScale(1f);
    }
}
