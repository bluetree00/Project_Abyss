// ActSkillState.cs
using UnityEngine;

public class ActSkillState : ILayerState<ActState>
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
        _controller.UseSkill(); // 스킬 어빌리티 실행(프로젝트에 맞게)
    }

    public void Update()
    {
        _time += Time.deltaTime;
        if (_time >= _duration)
            _stateChanger.Change(ActState.None);
    }

    public void Exit()
    {

        _controller.SetMoveScale(1f);
    }
}
