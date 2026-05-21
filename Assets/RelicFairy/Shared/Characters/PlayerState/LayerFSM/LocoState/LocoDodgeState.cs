using UnityEngine;

public class LocoDodgeState : ILayerState<LocoState>
{
    private PlayerController _controller;
    private ILayerStateChanger<LocoState> _stateChanger;

    private Vector3 _dodgeDir;
    private float _moveEndTime;

    public void Init(PlayerController controller, ILayerStateChanger<LocoState> stateChanger)
    { _controller = controller; _stateChanger = stateChanger; }

    public void Enter()
    {
        _controller.RotateTowardsInput();
        _dodgeDir = _controller.transform.forward;

        // 기본 거리 = dashSpeed × dashDuration, 보너스 거리만큼 duration 연장
        float baseSpeed = _controller.CharacterData.dashSpeed;
        float baseDuration = _controller.CharacterData.dashDuration;
        float distBonus = _controller.RuntimeStats?.RollDistanceBonus ?? 0f;
        float bonusDuration = baseSpeed > 0f ? distBonus / baseSpeed : 0f;
        _moveEndTime = Time.time + baseDuration + bonusDuration;

        _controller.Anim.CrossFade("Dodge", 0.05f);
        _controller.SetMoveScale(0f);
        _controller.FirePassive(PassiveTrigger.OnDodge, new PassiveContext());
    }

    public void Update()
    {
        if (Time.time < _moveEndTime)
        {
            // 수평 이동. y는 중력/점프 유지
            float spd = _controller.CharacterData.dashSpeed;
            float vy = _controller.Rigid.linearVelocity.y;
            _controller.Rigid.linearVelocity = new Vector3(
                _dodgeDir.x * spd,
                vy,
                _dodgeDir.z * spd
            );
        }
        else
        {
            // 수평 속도 정지 후 다음 상태로
            float vy = _controller.Rigid.linearVelocity.y;
            _controller.Rigid.linearVelocity = new Vector3(0f, vy, 0f);

            _controller.SetMoveScale(1f);
            var next = !_controller.IsGrounded() ? LocoState.Air
                       : (_controller.MoveDirection.sqrMagnitude > 0.0001f ? LocoState.Move
                                                                           : LocoState.Idle);
            _stateChanger.Change(next);
        }
    }

    public void Exit()
    {
        // 아이템 효과: 구르기 쿨다운 보너스 적용
        float baseCooldown = _controller.CharacterData.dodgeCooldown;
        float bonus = _controller.RuntimeStats?.RollCooldownBonus ?? 0f;
        _controller.DodgeCooldownEnd = Time.time + baseCooldown * (1f + bonus);

        _controller.SetMoveScale(1f);

        // 아이템 효과: 구르기 종료 hook
        var mgr = GameRunBootstrapper.Instance?.Run?.EffectManager;
        mgr?.OnRollEnd();

        // 착지 hook — 지상일 때만
        if (_controller.IsGrounded())
            mgr?.OnRollLand(_controller.transform.position);
    }
}
