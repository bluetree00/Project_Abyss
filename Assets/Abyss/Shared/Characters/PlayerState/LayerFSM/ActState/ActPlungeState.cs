using UnityEngine;

/// <summary>
/// 낙하 공격 상태.
/// Enter 전에 PlayerController.PendingPlunge 에 클립명·속도를 세팅해야 합니다.
/// - fallClipName: 낙하 중 재생할 Animator 상태 이름 (비어있으면 "PlungeFall")
/// - fallSpeed   : 하강 속도 (0이면 DefaultPlungeSpeed 사용)
/// 착지 시 "PlungeLand" 클립 재생 + 히트 판정.
/// PlungeLand 끝에 OnAttackEnd 애니메이션 이벤트 필수.
/// </summary>
public class ActPlungeState : ILayerState<ActState>
{
    public const float DefaultPlungeSpeed = 18f;

    private PlayerController _controller;
    private ILayerStateChanger<ActState> _stateChanger;
    private PlayerAnimationEventReceiver _receiver;
    private AbilityExecution _execution;

    private float _plungeSpeed;
    private bool  _landed;
    private float _recoveryTimer;
    private const float RecoveryTimeout = 1.5f; // OnAttackEnd 누락 시 안전 탈출

    public void Init(PlayerController controller, ILayerStateChanger<ActState> stateChanger)
    {
        _controller   = controller;
        _stateChanger = stateChanger;
    }

    public void Enter()
    {
        _landed        = false;
        _recoveryTimer = 0f;

        // PendingPlunge에서 클립명·속도 읽기
        var info      = _controller.PendingPlunge;
        _plungeSpeed  = info.fallSpeed > 0f ? info.fallSpeed : DefaultPlungeSpeed;
        string clip   = string.IsNullOrEmpty(info.fallClipName) ? "PlungeFall" : info.fallClipName;
        _controller.PendingPlunge = default; // 소비

        _execution = new AbilityExecution();
        _controller.ActiveExecution = _execution;

        _controller.SetMoveScale(0f);
        _controller.StopHorizontalMovement();
        _controller.RotateTowardsMousePosition();

        _receiver = _controller.EventReceiver
                    ?? _controller.GetComponentInChildren<PlayerAnimationEventReceiver>();
        SubscribeReceiver();

        _controller.Anim.CrossFade(clip, 0.1f);
        _controller.BeginWeaponTrail();

        ApplyPlungeVelocity();
    }

    public void Update()
    {
        if (_landed)
        {
            _recoveryTimer += UnityEngine.Time.deltaTime;
            if (_recoveryTimer >= RecoveryTimeout)
                _stateChanger.Change(ActState.None);
            return;
        }

        if (!_controller.IsGrounded())
        {
            ApplyPlungeVelocity();
            return;
        }

        // 착지
        _landed = true;
        _controller.StopHorizontalMovement();
        _controller.Anim.CrossFade("PlungeLand", 0.05f);
        _controller.OnAttackHitStep(0);
        _controller.EndWeaponTrail();

        Debug.Log("[ActPlungeState] 착지 — PlungeLand 재생");
    }

    public void Exit()
    {
        UnsubscribeReceiver();
        _controller.EndWeaponTrail();
        _controller.SetMoveScale(1f);

        _controller.ActiveExecution = null;
        _execution?.Cleanup(forceEffects: false);
        _execution = null;

        _landed        = false;
        _recoveryTimer = 0f;
    }

    private void ApplyPlungeVelocity()
    {
        _controller.Rigid.linearVelocity = new UnityEngine.Vector3(0f, -_plungeSpeed, 0f);
    }

    private void SubscribeReceiver()
    {
        if (_receiver == null) return;
        _receiver.OnAttackEnd += OnAttackEnd;
        _receiver.OnHitStep   += OnHitStep;
    }

    private void UnsubscribeReceiver()
    {
        if (_receiver == null) return;
        _receiver.OnAttackEnd -= OnAttackEnd;
        _receiver.OnHitStep   -= OnHitStep;
    }

    private void OnAttackEnd() => _stateChanger.Change(ActState.None);

    private void OnHitStep(int stepIndex) => _controller.OnAttackHitStep(stepIndex);
}
