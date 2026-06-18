using UnityEngine;

public class LocoDodgeState : ILayerState<LocoState>
{
    // 정지 회피 시 "직전 이동 속도방향" 폴백을 인정할 최소 수평 속도(제곱, m²/s²).
    // 이보다 느리면 사실상 정지로 보고 바라보는 방향으로 폴백 → 진짜 정지 상태는 현행과 동일.
    private const float MinVelSqrForDirFallback = 0.25f; // 0.5 m/s

    private PlayerController _controller;
    private ILayerStateChanger<LocoState> _stateChanger;

    private Vector3 _dodgeDir;
    private Vector3 _entryHorizVel; // 회피 진입 시점의 수평 속도(모멘텀 블렌드/방향 폴백용)
    private float _moveEndTime;
    private float _recoveryEndTime; // 대시 종료 후 무적 없는 회복창 종료 시각(=moveEndTime+recoveryWindow). window 0이면 moveEndTime과 동일.

    // i-frame(무적) 활성 창 — 회피 전체가 아니라 [startDelay, startDelay+duration] 구간만.
    // 타이머 기반. 후속: 애님 이벤트로 창 시작/종료를 정밀 제어하도록 전환 가능.
    private float _iframeApplyTime;  // 무적 켜는 절대 시각
    private float _iframeEndTime;    // 시각 피드백 창 닫는 절대 시각(무적 자체는 _invincibleEnd가 만료 관리)
    private float _iframeDuration;   // SetInvincible에 넘길 클램프된 지속시간
    private bool _iframeApplied;     // 이번 회피에서 SetInvincible 이미 호출했는지(중복 방지)
    private bool _iframeOpen;        // OnDodgeIFrame(true) 신호를 보냈고 아직 닫지 않았는지

    public void Init(PlayerController controller, ILayerStateChanger<LocoState> stateChanger)
    { _controller = controller; _stateChanger = stateChanger; }

    public void Enter()
    {
        _controller.RotateTowardsInput();

        // 진입 시점 수평 속도 캡처(아래 dash가 velocity를 덮어쓰기 전). 방향 폴백·모멘텀 블렌드 공용.
        _entryHorizVel = _controller.Rigid != null
            ? new Vector3(_controller.Rigid.linearVelocity.x, 0f, _controller.Rigid.linearVelocity.z)
            : Vector3.zero;

        // 회피 방향 규칙(설계 §5-1.1): 입력방향 → (입력 없으면) 직전 이동 속도방향 → (그것도 0이면) 바라보는 방향.
        // 이동 입력(MoveDirection)은 CheckMovementInput에서 월드축 고정으로 계산 → 카메라 상대 변환이 일관 적용된 상태라
        //   회피 방향도 동일 좌표계(MoveDirection)를 그대로 사용한다(별도 변환 불필요).
        // RequestFacing은 FixedUpdate에서 적용돼 transform.forward가 아직 갱신 전이므로, 방향은 여기서 직접 계산한다.
        // 속도 폴백 추가 효과: 직전에 공격/마우스 조준으로 facing이 이동방향과 어긋난 채 정지 회피해도
        //   엉뚱한(바라보는) 방향이 아니라 실제 미끄러지던 방향으로 회피한다. 진짜 정지(속도≈0)면 종전대로 바라보는 방향.
        // 8방향 클립 정렬은 애니메이터(Unity) 영역 — 여기서는 방향 벡터만 정확히 계산하고 클립 배선은 후속.
        if (_controller.MoveDirection.sqrMagnitude > 0.0001f)
            _dodgeDir = _controller.MoveDirection.normalized;
        else if (_entryHorizVel.sqrMagnitude > MinVelSqrForDirFallback)
            _dodgeDir = _entryHorizVel.normalized;
        else
            _dodgeDir = _controller.transform.forward;

        // 기본 거리 = dashSpeed × dashDuration, 보너스 거리만큼 duration 연장
        float baseSpeed = _controller.CharacterData.dashSpeed;
        float baseDuration = _controller.CharacterData.dashDuration;
        float distBonus = _controller.RuntimeStats?.RollDistanceBonus ?? 0f;
        float bonusDuration = baseSpeed > 0f ? distBonus / baseSpeed : 0f;
        _moveEndTime = Time.time + baseDuration + bonusDuration;

        // i-frame 창 계산 — 회피 총 길이를 넘지 않게 클램프(넘으면 회복 구간이 사라질 뿐, 안전).
        float totalDuration = _moveEndTime - Time.time;
        var data = _controller.CharacterData;

        // 회복(취약)창: 대시 끝난 뒤 무적 없이 잠깐 머무는 구간(설계 §5-1.4). 기본 0=현행(즉시 전환).
        // i-frame은 dash 길이 내로 클램프되므로(_iframeEndTime ≤ _moveEndTime) 회복창과 절대 겹치지 않는다.
        _recoveryEndTime = _moveEndTime + Mathf.Max(0f, data.dodgeRecoveryWindow);
        float startDelay = Mathf.Clamp(data.dodgeIFrameStartDelay, 0f, totalDuration);
        float available = Mathf.Max(0f, totalDuration - startDelay);
        _iframeDuration = Mathf.Min(Mathf.Max(0f, data.dodgeIFrameDuration), available);
        _iframeApplyTime = Time.time + startDelay;
        _iframeEndTime = _iframeApplyTime + _iframeDuration;
        _iframeApplied = false;
        _iframeOpen = false;

        _controller.Anim.CrossFade("Dodge", 0.05f);
        _controller.SetMoveScale(0f);
        _controller.FirePassive(PassiveTrigger.OnDodge, new PassiveContext());

        // 회피 시작 연출 신호(먼지·트레일). i-frame 창과 무관하게 회피 진입 즉시 1회.
        _controller.RaiseDodgeStart();

        // 대시 펀치 — 진입 순간 짧은 약한 카메라 셰이크로 가속감 부여.
        HitFeelService.CameraShake(0.05f, 0.1f);

        // startDelay==0이면 Enter 즉시 무적 적용(아래 헬퍼가 시각 도달 검사).
        TryApplyIFrame();
    }

    public void Update()
    {
        // 무적 창 켜기/시각 피드백 닫기 (이동 처리와 독립).
        TryApplyIFrame();
        if (_iframeOpen && Time.time >= _iframeEndTime)
        {
            _iframeOpen = false;
            _controller.RaiseDodgeIFrame(false);
        }

        if (Time.time < _moveEndTime)
        {
            // 수평 이동. y는 중력/점프 유지
            float spd = _controller.CharacterData.dashSpeed;
            float vy = _controller.Rigid.linearVelocity.y;

            // 대시 수평 속도. 기본은 _dodgeDir × dashSpeed(현행).
            Vector3 dashVel = _dodgeDir * spd;

            // [실험] 모멘텀 블렌드(설계 §5-1.2): 기본 0이면 Lerp가 dashVel 그대로 반환 → 현행 무변경.
            // 0보다 크면 진입 시점 수평 속도와 블렌딩해 방향 급전환 시 튐을 완화한다(에디터 튜닝).
            float blend = _controller.CharacterData.dodgeMomentumBlend;
            if (blend > 0f)
                dashVel = Vector3.Lerp(dashVel, _entryHorizVel, blend);

            _controller.Rigid.linearVelocity = new Vector3(dashVel.x, vy, dashVel.z);
        }
        else
        {
            // 대시 이동 종료 — 수평 속도 정지(중력 y 유지).
            float vy = _controller.Rigid.linearVelocity.y;
            _controller.Rigid.linearVelocity = new Vector3(0f, vy, 0f);

            // 회복(취약)창(설계 §5-1.4): 기본 0이면 _recoveryEndTime==_moveEndTime이라 이 분기 즉시 통과 → 현행과 동일 프레임 전환.
            // 0보다 크면 그동안 Dodge 상태에 머물러(무적 없음·재회피 불가) 남발을 억제한다. i-frame은 이미 종료된 뒤다.
            if (Time.time < _recoveryEndTime)
                return;

            _controller.SetMoveScale(1f);
            var next = !_controller.IsGrounded() ? LocoState.Air
                       : (_controller.MoveDirection.sqrMagnitude > 0.0001f ? LocoState.Move
                                                                           : LocoState.Idle);
            _stateChanger.Change(next);
        }
    }

    public void Exit()
    {
        // 회피 도중 중단(취소)되어도 무적(_invincibleEnd)은 억지 해제하지 않고 자연 만료시킨다(안전).
        // 시각 피드백 창만 닫아 신호 짝을 맞춘다.
        if (_iframeOpen)
        {
            _iframeOpen = false;
            _controller.RaiseDodgeIFrame(false);
        }

        // 회피 종료 연출 신호(트레일 OFF). 중단/취소 포함 모든 Exit 경로에서 짝을 맞춘다.
        _controller.RaiseDodgeEnd();

        // 아이템 효과: 구르기 쿨다운 보너스 적용
        float baseCooldown = _controller.CharacterData.dodgeCooldown;
        float bonus = _controller.RuntimeStats?.RollCooldownBonus ?? 0f;
        _controller.DodgeCooldownEnd = Time.time + baseCooldown * (1f + bonus);

        _controller.SetMoveScale(1f);

        // 우클릭 대시 후 — 유물 보유 시 다음 이동을 달리기로 시작(정지 전까지 유지)
        if (_controller.HasRelic && _controller.IsGrounded())
            _controller.RequestRunAfterDash();

        // 아이템 효과: 구르기 종료 hook
        var mgr = GameRunBootstrapper.Instance?.Run?.EffectManager;
        mgr?.OnRollEnd();

        // 착지 hook — 지상일 때만
        if (_controller.IsGrounded())
            mgr?.OnRollLand(_controller.transform.position);
    }

    // startDelay 도달 시 1회 무적 적용. SetInvincible은 더 긴 기존 무적(부활/리스폰)을 줄이지 않는다(Mathf.Max).
    private void TryApplyIFrame()
    {
        if (_iframeApplied || _iframeDuration <= 0f) return;
        if (Time.time < _iframeApplyTime) return;
        _iframeApplied = true;
        _iframeOpen = true;
        _controller.SetInvincible(_iframeDuration);
        _controller.RaiseDodgeIFrame(true);
    }
}
