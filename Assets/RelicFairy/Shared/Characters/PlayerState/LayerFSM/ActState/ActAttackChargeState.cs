using UnityEngine;
using Game.Inputs;

public class ActAttackChargeState : ILayerState<ActState>
{
    private enum GuardPhase { Looping, Accepting }

    private PlayerController _controller;
    private ILayerStateChanger<ActState> _changer;
    private PlayerAnimationEventReceiver _receiver;

    private float _elapsed;
    private bool _releaseRequested;
    private bool _exitFired;

    private bool _isGuardCharge;
    private GuardPhase _guardPhase;
    private float _acceptElapsed;
    private const float AcceptTimeoutSec = 2f;
    private const float GuardSafetyTimeoutSec = 10f;

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

        _elapsed = 0f;
        _releaseRequested = false;
        _exitFired = false;

        var animSet = _controller.WeaponManager?.CurrentWeaponData?.animationSet as WeaponAnimationSetSO;
        _isGuardCharge = animSet != null && animSet.useGuardCharge;

        _controller.SetMoveScale(0f);

        _receiver = _controller.EventReceiver ?? _controller.GetComponentInChildren<PlayerAnimationEventReceiver>();

        if (_isGuardCharge)
        {
            _guardPhase = GuardPhase.Looping;
            _acceptElapsed = 0f;
            _controller.Anim.CrossFadeInFixedTime("HeavyCharge", 0.06f);
            SubscribeGuardEvents();
        }
        else
        {
            _controller.Anim.CrossFadeInFixedTime("HeavyCharge", 0.06f);
        }
    }

    public void Update()
    {
        if (!_controller.IsGrounded())
        {
            _controller.ClearPendingAttack();
            FireExit(ActState.None);
            return;
        }

        if (_isGuardCharge)
            UpdateGuardCharge();
        else
            UpdateSimpleCharge();
    }

    public void Exit()
    {
        UnsubscribeGuardEvents();
        _controller.SetMoveScale(1f);
    }

    // ─── 일반 차지 ────────────────────────────────────────────────

    private void UpdateSimpleCharge()
    {
        _elapsed += Time.deltaTime;

        var wd = _controller.WeaponManager?.CurrentWeaponData;
        float holdThreshold = wd != null ? wd.holdThreshold : 1.2f;

        // 최대 차지 도달 → 자동 발동(풀 차지)
        if (_elapsed >= holdThreshold)
        {
            LaunchCharged(1f);
            return;
        }

        // 버튼 릴리즈(PendingAttack) → 그 시점 차지량으로 발동
        if (_controller.PendingAttackCommand == Command.None) return;

        LaunchCharged(Mathf.Clamp01(_elapsed / Mathf.Max(0.01f, holdThreshold)));
    }

    // 차지레벨을 기록하고 강공격으로 전이. 원형 AoE 반경은 WeaponEffectHandler가 이 값으로 스케일한다.
    private void LaunchCharged(float chargeLevel01)
    {
        _controller.HeavyChargeLevel01 = chargeLevel01;
        _controller.CurrentAttackTypeForEffect = WeaponActionType.GroundHeavy;
        _controller.ClearPendingAttack();
        _controller.InputBuffer.TryConsume(Command.Light);
        _controller.InputBuffer.TryConsume(Command.Heavy);
        FireExit(ActState.HeavyAttack);
    }

    // ─── 가드 차지 ────────────────────────────────────────────────

    private void UpdateGuardCharge()
    {
        if (_guardPhase == GuardPhase.Looping)
            UpdateLooping();
        else
            UpdateAccepting();
    }

    private void UpdateLooping()
    {
        _elapsed += Time.deltaTime;

        // 안전 타임아웃 (입력 오작동 대비)
        if (_elapsed >= GuardSafetyTimeoutSec)
        {
            LaunchHeavyAttack();
            return;
        }

        // 버튼 릴리즈 신호 → 강공격 발동
        if (_releaseRequested || _controller.PendingAttackCommand != Command.None)
        {
            _releaseRequested = false;
            _controller.ClearPendingAttack();
            _controller.InputBuffer.TryConsume(Command.Light);
            _controller.InputBuffer.TryConsume(Command.Heavy);
            LaunchHeavyAttack();
        }
    }

    private void UpdateAccepting()
    {
        _acceptElapsed += Time.deltaTime;

        // Accept 애니메이션 완료(OnAttackEnd) 전 타임아웃 보호
        if (_acceptElapsed >= AcceptTimeoutSec)
        {
            OnAcceptEnd();
        }
    }

    private void LaunchHeavyAttack()
    {
        _controller.CurrentAttackTypeForEffect = WeaponActionType.GroundHeavy;
        FireExit(ActState.HeavyAttack);
    }

    private void EnterAcceptPhase()
    {
        UnsubscribeAcceptEnd(); // 연속 피격 시 중복 구독 방지
        _guardPhase = GuardPhase.Accepting;
        _acceptElapsed = 0f;
        _controller.Anim.CrossFadeInFixedTime("HeavyChargeAccept", 0.06f);
        SubscribeAcceptEnd();
    }

    private void OnAcceptEnd()
    {
        UnsubscribeAcceptEnd();

        if (_exitFired) return;

        // Accept 완료 → Loop로 복귀
        _guardPhase = GuardPhase.Looping;
        _acceptElapsed = 0f;
        _controller.Anim.CrossFadeInFixedTime("HeavyCharge", 0.10f);
    }

    private void FireExit(ActState next)
    {
        if (_exitFired) return;
        _exitFired = true;
        _changer.Change(next);
    }

    // ─── 이벤트 구독 ─────────────────────────────────────────────

    private void SubscribeGuardEvents()
    {
        _controller.OnDamageTaken += OnPlayerDamageTaken;
    }

    private void UnsubscribeGuardEvents()
    {
        _controller.OnDamageTaken -= OnPlayerDamageTaken;
        UnsubscribeAcceptEnd();
    }

    private void SubscribeAcceptEnd()
    {
        if (_receiver == null) return;
        _receiver.OnAttackEnd += OnAcceptEnd;
    }

    private void UnsubscribeAcceptEnd()
    {
        if (_receiver == null) return;
        _receiver.OnAttackEnd -= OnAcceptEnd;
    }

    private void OnPlayerDamageTaken()
    {
        EnterAcceptPhase();
    }
}
