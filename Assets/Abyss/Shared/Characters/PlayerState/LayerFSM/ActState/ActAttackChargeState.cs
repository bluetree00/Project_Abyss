using UnityEngine;
using Game.Inputs;

public class ActAttackChargeState : ILayerState<ActState>
{
    private PlayerController _controller;
    private ILayerStateChanger<ActState> _changer;

    private float _elapsed;

    public void Init(PlayerController controller, ILayerStateChanger<ActState> changer)
    {
        _controller = controller;
        _changer = changer;
    }

    public void Enter()
    {
        if (!_controller.IsGrounded())
        {
            _changer.Change(ActState.None);
            return;
        }
        _controller.SetMoveScale(0f);
        _controller.Anim.CrossFade("HeavyCharge", 0.08f);
        _elapsed = 0f;
    }

    public void Update()
    {
        // 점프 등으로 공중 전환 시 차지 즉시 취소
        // SwordPolicy가 착지 후 타이머를 리셋하므로 재시도 가능
        if (!_controller.IsGrounded())
        {
            _controller.ClearPendingAttack();
            _changer.Change(ActState.None);
            return;
        }

        _elapsed += Time.deltaTime;

        var wd = _controller.WeaponManager?.CurrentWeaponData;
        float holdThreshold = wd != null ? wd.holdThreshold : 1.2f;

        // 차지를 시작한 시점(ChargeState 진입)부터 holdThreshold 카운트
        if (_elapsed >= holdThreshold)
        {
            _controller.CurrentAttackTypeForEffect = WeaponActionType.GroundHeavy;
            _controller.ClearPendingAttack();
            _controller.InputBuffer.TryConsume(Command.Heavy);
            _changer.Change(ActState.HeavyAttack);
            return;
        }

        // SwordPolicy 릴리즈 신호 처리
        var pending = _controller.PendingAttackCommand;
        if (pending == Command.None) return;

        // ChargeState 진입 = 강공격 확정. 어떤 릴리즈든 HeavyAttack 발동
        _controller.CurrentAttackTypeForEffect = WeaponActionType.GroundHeavy;
        _controller.ClearPendingAttack();
        _controller.InputBuffer.TryConsume(Command.Light);
        _controller.InputBuffer.TryConsume(Command.Heavy);
        _changer.Change(ActState.HeavyAttack);
    }

    public void Exit() { }
}
