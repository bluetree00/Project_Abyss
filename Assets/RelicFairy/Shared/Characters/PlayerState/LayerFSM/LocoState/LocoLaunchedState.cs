using UnityEngine;

/// <summary>
/// 피격 날아감 상태 — 포이즈(아머치) 브레이크 시 진입.
/// 공격자 반대방향 수평 임펄스 + 상향 리프트로 띄우고, 착지할 때까지 조작 불가(공중 경직).
/// 착지 후 짧은 회복 경직과 무적(i-frame)을 주고 Idle/Move로 복귀 — 즉시 재피격/무한 저글링 방지.
/// 진입 방향은 PlayerController.LaunchFrom이 세팅하고 이 상태가 소비한다.
/// </summary>
public class LocoLaunchedState : ILayerState<LocoState>
{
    // 진입 직후엔 아직 발이 지면에 닿아 있어 IsGrounded가 true → 즉시 착지로 오판하는 것을 막는 최소 체공 시간.
    private const float MinAirTime = 0.12f;
    // 물리 이상(끼임 등) 대비 안전 탈출.
    private const float SafetyTimeout = 4f;

    private PlayerController _controller;
    private ILayerStateChanger<LocoState> _stateChanger;

    private float _airElapsed;
    private bool  _landed;
    private float _landRecoveryEnd;

    public void Init(PlayerController controller, ILayerStateChanger<LocoState> stateChanger)
    { _controller = controller; _stateChanger = stateChanger; }

    public void Enter()
    {
        var data = _controller.CharacterData;
        _airElapsed = 0f;
        _landed     = false;

        // 날아가는 중에 공격/스킬이 남아 있으면 안 된다 — 진행 중 액션 취소.
        _controller.CancelActions();
        _controller.SetMoveScale(0f);

        // 공격자 반대방향(수평) + 상향 리프트.
        // 기존 속도를 지우고 임펄스를 얹어야 이동 중/정지 중에 관계없이 세기가 일정하다.
        Vector3 dir = _controller.ConsumeLaunchDirection();
        float h  = data != null ? data.launchHorizontalForce : 8f;
        float up = data != null ? data.launchUpForce         : 6f;

        if (_controller.Rigid != null)
        {
            _controller.Rigid.linearVelocity = Vector3.zero;
            _controller.Rigid.AddForce(dir * h + Vector3.up * up, ForceMode.VelocityChange);
        }

        _controller.Anim.CrossFade("HitLaunch", 0.05f);
    }

    public void Update()
    {
        // 착지 후 회복 경직 — 끝나면 복귀
        if (_landed)
        {
            if (Time.time >= _landRecoveryEnd)
            {
                var next = _controller.MoveDirection.sqrMagnitude > 0.0001f ? LocoState.Move : LocoState.Idle;
                _stateChanger.Change(next);
            }
            return;
        }

        _airElapsed += Time.deltaTime;

        if (_airElapsed >= SafetyTimeout)
        {
            Land();
            return;
        }

        // 최소 체공을 지난 뒤, 하강 중에 접지하면 착지로 판정
        bool descending = _controller.Rigid == null || _controller.Rigid.linearVelocity.y <= 0.01f;
        if (_airElapsed >= MinAirTime && _controller.IsGrounded() && descending)
            Land();
    }

    public void Exit()
    {
        _controller.SetMoveScale(1f);
    }

    private void Land()
    {
        _landed = true;

        var data = _controller.CharacterData;
        float recovery = data != null ? Mathf.Max(0f, data.launchLandRecovery) : 0.25f;
        float iframe   = data != null ? Mathf.Max(0f, data.launchLandIFrame)   : 0.3f;

        _landRecoveryEnd = Time.time + recovery;

        // 착지 미끄러짐 방지 — 수평 속도만 정지(중력 y는 유지)
        if (_controller.Rigid != null)
        {
            float vy = _controller.Rigid.linearVelocity.y;
            _controller.Rigid.linearVelocity = new Vector3(0f, vy, 0f);
        }

        // 착지 직후 무적 — 체인 방지의 두 번째 축(넉백 면역과 별개로 '즉시 재피격'을 막는다)
        if (iframe > 0f) _controller.SetInvincible(iframe);

        _controller.Anim.CrossFade("HitLand", 0.05f);
    }
}
