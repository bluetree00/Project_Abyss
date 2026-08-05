using UnityEngine;

public class LocoDodgeState : ILayerState<LocoState>
{
    // 정지 회피 시 "직전 이동 속도방향" 폴백을 인정할 최소 수평 속도(제곱, m²/s²).
    // 이보다 느리면 사실상 정지로 보고 바라보는 방향으로 폴백 → 진짜 정지 상태는 현행과 동일.
    private const float MinVelSqrForDirFallback = 0.25f; // 0.5 m/s

    // 구르기 → 로코모션 복귀 크로스페이드 길이(초, 고정시간). 로코모션 기본(0.14s)은 자세 차가 커서 짧다.
    private const float DodgeExitBlend = 0.18f;

    // 대시 중 허용할 최대 상승 속도(m/s).
    // 경사/계단을 고속으로 타면 호버 스프링(지면이 급히 솟아 error 급증)과 콜라이더 충돌 반발이
    // 겹쳐 캐릭터가 위로 쏘아올려진다. 대시는 매 프레임 속도를 덮어쓰므로, 여기서 상승분만
    // 잘라내면 원인이 무엇이든 확실히 막힌다(오르막 주행은 그대로 가능).
    private const float MaxDashRiseSpeed = 3f;

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

        // 저스트 회피 장전 — 대시 중(무적 구간)에 공격을 맞으면 슬로모 + 이동 보너스로 보상. 회피당 1회.
        _controller.ArmPerfectDodge();

        _controller.Anim.CrossFadeInFixedTime("Dodge", 0.06f);
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

            // 램프 발사 차단 — 경사/단차를 고속으로 탈 때 위로 쏘아올려지는 상승분을 잘라낸다.
            // 하강(중력)은 건드리지 않는다.
            if (vy > MaxDashRiseSpeed) vy = MaxDashRiseSpeed;

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
            // 대시 이동 종료 — 속도를 0으로 죽이지 않고 '이어받는다'.
            // 예전엔 22m/s → 0 으로 한 프레임에 급정지시켜, 멈췄다 다시 가속하는 불연속(=경직감)이 생겼다.
            //  · 이동 입력이 있으면 → 그 방향 최고속(걷기~달리기)으로 이어받아 그대로 달려나간다.
            //  · 입력이 없으면 → 대시 방향 속도를 최고속으로만 낮춰 넘기고, 정지는 Idle의 감속(moveDecel)에 맡긴다.
            float vy = _controller.Rigid.linearVelocity.y;

            float walkSpd = _controller.CharacterData.baseMoveSpeed;
            float runSpd  = _controller.CharacterData.baseRunSpeed > 0.01f
                ? _controller.CharacterData.baseRunSpeed
                : walkSpd;
            bool hasMoveInput = _controller.MoveDirection.sqrMagnitude > 0.0001f;

            // 이동 입력이 있으면 회피 직전 상태와 무관하게 '달리기 최고속'으로 이어받는다.
            // 예전엔 회피 이전의 RunBlend01(정지/걷기에서 회피했으면 대개 0)로 걷기속도를 물려줘,
            // 회피가 끝난 뒤 걷기부터 램프를 다시 타는 재가속 구간이 남았다.
            // 입력이 없으면 종전대로 현재 최고속으로만 낮춰 넘기고 정지는 Idle의 감속(moveDecel)에 맡긴다.
            float maxSpd = hasMoveInput
                ? runSpd
                : Mathf.Max(walkSpd, Mathf.Lerp(walkSpd, runSpd, Mathf.Clamp01(_controller.RunBlend01)));
            maxSpd *= _controller.RuntimeStats?.MoveSpeedMultiplier ?? 1f;

            Vector3 carryDir = hasMoveInput ? _controller.MoveDirection.normalized : _dodgeDir;
            Vector3 carry = carryDir * maxSpd;

            _controller.Rigid.linearVelocity = new Vector3(carry.x, vy, carry.z);

            // 회복(취약)창(설계 §5-1.4): 기본 0이면 _recoveryEndTime==_moveEndTime이라 이 분기 즉시 통과 → 현행과 동일 프레임 전환.
            // 0보다 크면 그동안 Dodge 상태에 머물러(무적 없음·재회피 불가) 남발을 억제한다. i-frame은 이미 종료된 뒤다.
            if (Time.time < _recoveryEndTime)
                return;

            _controller.SetMoveScale(1f);
            var next = !_controller.IsGrounded() ? LocoState.Air
                       : (hasMoveInput ? LocoState.Move : LocoState.Idle);

            // 회피 → 이동 복귀 프라임. 취소/피격 경로(Exit)가 아니라 정상 종료 지점에서만 건다.
            //  · RotateTowardsInput: 회피 방향과 입력 방향이 다를 때 facing을 입력 쪽으로 즉시 스냅.
            //    속도(carryDir)는 이미 입력 방향이라, 슬루(720°/s)에 맡기면 반대 방향 회피 후
            //    최대 0.25s 동안 옆/뒤로 미끄러지는 주행이 보인다.
            //  · RequestRunAfterDash: LocoMoveState.Enter가 이걸 소비해 _runCharge01/_animFloor를
            //    풀 달리기로 시작 → 걷기 램프를 다시 타지 않는다.
            if (next == LocoState.Move)
            {
                _controller.RotateTowardsInput();
                _controller.RequestRunAfterDash();
            }

            _stateChanger.Change(next);
        }
    }

    public void Exit()
    {
        // 구르기 자세 → 로코모션 복귀는 자세 차이가 커서 로코모션 기본 블렌드(0.14s)면 아직 짧다.
        // 다음 로코모션 진입의 크로스페이드를 길게 예약해 부드럽게 이어붙인다(다른 전이엔 영향 없음).
        _controller.RequestLocoBlend(DodgeExitBlend);

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

        // 다음 이동을 달리기로 시작하는 프라임은 Update의 정상 종료 분기로 옮겼다.
        // 여기(Exit)는 피격/중단 취소 경로에서도 불리므로 프라임 지점으로 부적절했고,
        // HasRelic 게이트도 실효가 없었다(LocoMoveState의 걷기→달리기 램프는 이미 무장비 포함).

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
