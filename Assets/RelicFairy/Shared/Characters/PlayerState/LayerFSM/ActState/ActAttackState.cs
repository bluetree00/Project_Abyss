using System.Linq;
using UnityEngine;
using Game.Inputs;

public class ActAttackState : ILayerState<ActState>
{
    private PlayerController              _controller;
    private ILayerStateChanger<ActState>  _stateChanger;
    private PlayerAnimationEventReceiver  _receiver;
    private AbilityExecution              _execution;
    private int                           _maxCombo = 1;

    // ── normalizedTime 폴링 상태 ─────────────────────────────────────────────
    private int   _currentStateHash   = 0;
    private bool  _comboWindowOpened  = false;
    private bool  _comboWindowClosed  = false;
    private bool  _attackEndFired     = false;
    private bool  _waitingForComboInput = false;
    private float _comboOpen;
    private float _comboClose;
    private float _attackEnd;
    private float _stateElapsed;
    private const float StateTimeout = 1f;

    // ── Lunge Step 상태 ─────────────────────────────────────────────────────
    private WeaponAnimationSetSO.ClipMapping _currentMapping;
    private float _stepLastNT;
    private Vector3 _stepDir;
    private float _effectiveStepDistance;
    private float _aimCompleteBonus;   // 유도 완료 시 추가 전진 거리 (mapping 값)
    private bool  _bonusApplied;       // 이번 타에 보너스를 이미 반영했는지

    // ── 회전 Lerp 상태 ──────────────────────────────────────────────────────
    // RotateTowards 방식 — 매 프레임 현재 회전에서 목표로 일정 각속도로 접근.
    // 외부 회전(물리/충돌/넉백) 이 끼어들어도 그 시점의 회전에서 다시 목표로 수렴 (시작점 캐싱 없음).
    private Quaternion _aimTargetRot;
    private bool       _aimRotating;
    private float      _aimRotationDuration;

    // ── Lunge 타겟 부스트/정지 파라미터 ───────────────────────────────────
    private const float LungeTouchBuffer  = 0.6f;   // 몬스터 중심에서 정지할 거리(닿기 전 마진)
    private const float LungeBoostExtra   = 1.0f;   // 기본 거리에 최대 +N m 까지 부스트 허용
    private const float LungeWallBuffer   = 0.4f;   // 벽 앞에서 정지할 거리
    private const float LungeCastRadius   = 0.4f;   // SphereCast 반경
    private const float LungeCastHeight   = 0.5f;   // 캐스트 원점 높이 오프셋(가슴 높이)
    private static readonly RaycastHit[] _lungeCastBuf = new RaycastHit[16];

    private static readonly int AirLightAttackValueHash =
        Animator.StringToHash("AirLightAttackValue");

    // ── 초기화 ───────────────────────────────────────────────────────────────
    public void Init(PlayerController controller, ILayerStateChanger<ActState> stateChanger)
    {
        _controller   = controller;
        _stateChanger = stateChanger;
    }

    // ── Enter ────────────────────────────────────────────────────────────────
    public void Enter()
    {
        // 공중 콤보 스텝이 낙하 공격으로 지정된 경우 ActPlungeState로 위임
        if (!_controller.IsGrounded())
        {
            int  plungeStep    = _controller.Combo.CurrentComboStep;
            var  plungeAction  = _controller.CurrentAttackTypeForEffect;
            var  plungeMapping = TryGetClipMapping(plungeStep, plungeAction, isAir: true);
            if (plungeMapping != null && plungeMapping.isPlunge)
            {
                _controller.CurrentAttackTypeForEffect = WeaponActionType.AirPlunge;
                _controller.PendingPlunge = new PlayerController.PlungeInfo
                {
                    fallClipName = plungeMapping.baseClipName,
                    fallSpeed    = plungeMapping.plungeFallSpeed,
                    descendAt    = plungeMapping.plungeDescendAt
                };
                _stateChanger.Change(ActState.Plunge);
                return;
            }
        }

        _execution = new AbilityExecution();
        _controller.ActiveExecution = _execution;
        // 회전은 PlayCurrentComboAnimation 에서 ClipMapping 의 AimAssist 옵션과 함께 일괄 처리한다.
        // 여기서 한 번 더 호출하면 _lastClickedPosition 이 먼저 소비되어 콤보 1단계의 aim assist 가 click 위치를 못 본다.

        _receiver = _controller.EventReceiver
                 ?? _controller.GetComponentInChildren<PlayerAnimationEventReceiver>();

        if (!_controller.Combo.IsAttacking)
        {
            _controller.Combo.SetAttacking(true);
            _controller.Combo.SetNextComboQueued(false);
            _controller.Combo.CloseWindow();

            // 초기 MoveScale은 PlayCurrentComboAnimation 에서 mapping.moveInputScale 로 덮어씀
            _controller.SetMoveScale(0f);
        }

        var  action = _controller.CurrentAttackTypeForEffect;
        var  wd     = _controller.WeaponManager?.CurrentWeaponData;
        bool isAir  = !_controller.IsGrounded();
        _maxCombo = wd != null
            ? (isAir ? Mathf.Max(1, wd.airEndCount) : Mathf.Max(1, wd.groundEndCount))
            : 1;

        // 공중 공격 진입 시 체공 + 사용 플래그
        if (isAir)
        {
            // 활: 화살 발사 시점에만 체공 (WeaponEffectHandler에서 처리)
            bool isBow = wd != null && (wd.weaponType == WeaponType.Bow || wd.weaponType == WeaponType.Crossbow);
            if (!isBow)
                _controller.StartAirHover();
            _controller.AirAttackUsed = true;
        }

        _waitingForComboInput = false;
        _stateElapsed = 0f;

        SubscribeReceiver();
        PlayCurrentComboAnimation();
    }

    // ── Update ───────────────────────────────────────────────────────────────
    public void Update()
    {
        PollAnimationTiming();
        HandleComboInput();
    }

    /// <summary>
    /// 현재 재생 중인 애니메이션의 normalizedTime을 폴링해
    /// 콤보 창 열기/닫기와 공격 종료 타이밍을 처리한다.
    /// FBX meta 이벤트에 의존하지 않으므로 애니메이션 교체 시에도 안정적이다.
    /// </summary>
    private void PollAnimationTiming()
    {
        _stateElapsed += Time.deltaTime;

        if (_currentStateHash == 0) return;

        var anim      = _controller.Anim;
        var stateInfo = anim.IsInTransition(0)
            ? anim.GetNextAnimatorStateInfo(0)
            : anim.GetCurrentAnimatorStateInfo(0);

        if (stateInfo.shortNameHash != _currentStateHash)
        {
            // 상태 해시 불일치 시 타임아웃으로 강제 종료
            if (_stateElapsed >= StateTimeout)
            {
                Debug.LogWarning("[ActAttackState] State hash mismatch timeout — forcing exit.");
                _stateChanger.Change(ActState.None);
            }
            return;
        }

        float t = stateInfo.normalizedTime;

        // 콤보 창 열기
        if (!_comboWindowOpened && t >= _comboOpen)
        {
            _comboWindowOpened = true;
            _controller.Combo.OpenWindow();
        }

        // 콤보 창 닫기
        if (_comboWindowOpened && !_comboWindowClosed && t >= _comboClose)
        {
            _comboWindowClosed = true;
            _controller.Combo.CloseWindow();
        }

        // 공격 종료 (다음 스텝 판단)
        if (!_attackEndFired && t >= _attackEnd)
        {
            _attackEndFired = true;
            OnAttackEnd();
        }

        // 회전 Lerp — 목표 회전까지 부드럽게 (순간이동 느낌 방지)
        ApplyAimRotation();

        // Lunge Step — normalizedTime 구간 안에 forward 평행이동 적용
        ApplyLungeStep(t);
    }

    /// <summary>
    /// 매 프레임 현재 회전 → _aimTargetRot 로 일정 각속도로 접근한다.
    /// 외부 회전(물리/충돌/넉백) 이 끼어들어도 그 시점의 회전에서 자연 수렴 (시작점 캐싱 X).
    /// duration 은 "180° 회전에 걸리는 시간" 으로 해석 — 짧은 회전은 비례해서 더 빨리 완료.
    /// </summary>
    private void ApplyAimRotation()
    {
        if (!_aimRotating || _aimRotationDuration <= 0f) return;

        // 현재 회전은 Rigidbody(실제 적용 주체)에서 읽고, 목표는 RequestFacing으로 넘겨
        // FixedUpdate(ApplyFacing)에서 적용한다. (Update 직접 대입 시 보간과 충돌해 진동)
        Quaternion cur = _controller.Rigid != null ? _controller.Rigid.rotation : _controller.transform.rotation;
        // 180° 를 duration 안에 완주하는 각속도 (deg/s)
        float maxAngleStep = (180f / _aimRotationDuration) * Time.deltaTime;
        Quaternion next = Quaternion.RotateTowards(cur, _aimTargetRot, maxAngleStep);
        _controller.RequestFacing(next);

        // 목표 근처(0.5° 미만)면 완료
        if (Quaternion.Angle(next, _aimTargetRot) < 0.5f)
        {
            _controller.RequestFacing(_aimTargetRot);
            _aimRotating = false;
            TryApplyAimCompleteBonus();
        }
    }

    /// <summary>
    /// 유도 회전이 목표에 정렬 완료된 순간(1회) 추가 전진 거리를 반영한다.
    /// "현재 전진거리 + 보너스" 를 다시 SphereCast 로 캡 계산해, 정면 적/벽 앞에서
    /// 멈추도록 한다(관통 방지). 보너스가 없거나 이미 반영했으면 무시.
    /// </summary>
    private void TryApplyAimCompleteBonus()
    {
        if (_bonusApplied || _aimCompleteBonus <= 0f) return;
        _bonusApplied = true;
        _effectiveStepDistance = ComputeEffectiveStepDistance(_effectiveStepDistance + _aimCompleteBonus);
    }

    /// <summary>
    /// 현재 ClipMapping 에 설정된 attackStepDistance 만큼 [stepStart, stepEnd] 구간에 걸쳐 전진.
    /// 정규화 시간 progress 에 ease-out 곡선을 적용해, 초반은 빠르고 후반은 감속하는
    /// 자연스러운 "밀어주는" 느낌을 낸다. Rigidbody.MovePosition 으로 적용해
    /// Rigidbody.interpolation = Interpolate 와 함께 시각적으로 부드러운 렌더링.
    /// </summary>
    private void ApplyLungeStep(float normalizedTime)
    {
        if (_currentMapping == null || _effectiveStepDistance <= 0f) return;

        float start = _currentMapping.attackStepStartNorm;
        float end   = _currentMapping.attackStepEndNorm;
        if (end <= start) return;

        // 윈도우 진입 전: lastNT 를 start 로 고정해, 첫 진입 시 캐치업 점프 방지
        if (normalizedTime < start)
        {
            _stepLastNT = start;
            return;
        }
        // 윈도우 종료 후: lastNT 를 end 로 고정
        if (normalizedTime >= end)
        {
            _stepLastNT = end;
            return;
        }

        float prevClamped = Mathf.Clamp(_stepLastNT, start, end);
        float currClamped = Mathf.Clamp(normalizedTime, start, end);
        float window      = end - start;

        // ease-out (1 - (1-t)^2) — 적분이 t 의 단조 증가이며 끝에 감속.
        // 누적 거리 함수: f(t) = (1 - (1-t)^2) → t=0 에서 0, t=1 에서 1
        float prevT = (prevClamped - start) / window;
        float currT = (currClamped - start) / window;
        float prevDist = EaseOut(prevT) * _effectiveStepDistance;
        float currDist = EaseOut(currT) * _effectiveStepDistance;
        float distThisFrame = currDist - prevDist;

        _stepLastNT = normalizedTime;

        if (distThisFrame <= 0f) return;

        Vector3 delta = _stepDir * distThisFrame;

        var rb = _controller.Rigid;
        if (rb != null && !rb.isKinematic)
        {
            // Rigidbody.MovePosition: Rigidbody.interpolation=Interpolate 와 결합 시 부드럽게 렌더링
            rb.MovePosition(rb.position + delta);
        }
        else
        {
            _controller.transform.position += delta;
        }
    }

    /// <summary>1 - (1-t)^2 ease-out. t∈[0,1] → [0,1].</summary>
    private static float EaseOut(float t)
    {
        float u = 1f - Mathf.Clamp01(t);
        return 1f - u * u;
    }

    /// <summary>
    /// 공격 시작 시점에 전방을 SphereCast 로 미리 살펴 lunge 거리를 결정한다.
    /// - 정면 IDamageable 적: 닿기 전 (LungeTouchBuffer) 까지 거리 확장 (최대 baseDist + LungeBoostExtra)
    /// - 벽이 더 가까우면 그 앞에 멈추도록 추가 캡
    /// - 적/벽 없으면 baseDist 그대로
    /// </summary>
    private float ComputeEffectiveStepDistance(float baseDist)
    {
        if (baseDist <= 0f || _controller == null) return 0f;

        float maxBoost    = baseDist + LungeBoostExtra;
        float searchRange = maxBoost + LungeTouchBuffer;
        Vector3 origin    = _controller.transform.position + Vector3.up * LungeCastHeight;

        int count = Physics.SphereCastNonAlloc(
            origin, LungeCastRadius, _stepDir, _lungeCastBuf, searchRange,
            ~0, QueryTriggerInteraction.Ignore);

        float monsterDist = float.PositiveInfinity;
        float wallDist    = float.PositiveInfinity;
        Transform selfT   = _controller.transform;

        for (int i = 0; i < count; i++)
        {
            var h = _lungeCastBuf[i];
            if (h.collider == null) continue;
            var ct = h.collider.transform;
            if (ct == selfT || ct.IsChildOf(selfT)) continue;

            var dmg = h.collider.GetComponent<IDamageable>() ?? h.collider.GetComponentInParent<IDamageable>();
            if (dmg != null)
            {
                if (h.distance < monsterDist) monsterDist = h.distance;
            }
            else
            {
                if (h.distance < wallDist) wallDist = h.distance;
            }
        }

        float effective = baseDist;

        // 정면 몬스터 발견 — 닿기 전(buffer) 까지, 단 부스트 상한 적용
        if (!float.IsInfinity(monsterDist))
            effective = Mathf.Clamp(monsterDist - LungeTouchBuffer, 0f, maxBoost);

        // 벽이 더 가까우면 벽 앞에서 추가로 캡
        if (!float.IsInfinity(wallDist))
            effective = Mathf.Min(effective, Mathf.Max(0f, wallDist - LungeWallBuffer));

        return effective;
    }

    /// <summary>
    /// 콤보 창이 열려있는 동안 Light 입력을 처리한다.
    /// 창이 닫혔고 대기 중이면 상태를 종료한다.
    /// </summary>
    private void HandleComboInput()
    {
        if (!_controller.Combo.ComboWindowOpen)
        {
            if (_waitingForComboInput)
            {
                _waitingForComboInput = false;
                _stateChanger.Change(ActState.None);
            }
            return;
        }

        if (_controller.InputBuffer.TryConsume(Command.Light))
        {
            if (_waitingForComboInput)
            {
                _waitingForComboInput = false;
                PlayCurrentComboAnimation();
            }
            else
            {
                _controller.Combo.SetNextComboQueued(true);
            }
        }
    }

    // ── Exit ─────────────────────────────────────────────────────────────────
    public void Exit()
    {
        UnsubscribeReceiver();

        _waitingForComboInput = false;
        _currentStateHash     = 0;
        _currentMapping        = null;
        _stepLastNT            = 0f;
        _effectiveStepDistance = 0f;
        _aimCompleteBonus      = 0f;
        _bonusApplied          = false;
        _aimRotating           = false;

        _controller.Combo.SetAttacking(false);
        _controller.Combo.SetNextComboQueued(false);
        _controller.Combo.ResetStep();
        _controller.Combo.CloseWindow();
        _controller.SetMoveScale(1f);

        // Animator speed 복원
        if (_controller.Anim != null)
            _controller.Anim.speed = 1f;

        // 공중 공격 종료 후 체공 애니메이션 복귀
        if (!_controller.IsGrounded())
            _controller.Anim.CrossFade("JumpBlend", 0.1f);

        _controller.ActiveExecution = null;
        _execution?.Cleanup(forceEffects: false);
        _execution = null;
    }

    // ── 이벤트 구독 ────────────────────────────────────────────────────────
    // OnHitStep은 PlayerController.Safe_OnHitStep이 전역 처리 → 중복 구독 제거
    private void SubscribeReceiver()
    {
        if (_receiver == null) return;
        _receiver.OnGenericTag += OnGenericTag;
    }

    private void UnsubscribeReceiver()
    {
        if (_receiver == null) return;
        _receiver.OnGenericTag -= OnGenericTag;
    }

    // ── 내부 이벤트 핸들러 ───────────────────────────────────────────────────
    private void OnAttackEnd()
    {
        _controller.Combo.IncrementStep();
        bool isAir = !_controller.IsGrounded();

        if (_controller.Combo.CurrentComboStep >= _maxCombo)
        {
            int finalStep = _controller.Combo.CurrentComboStep;
            _controller.Combo.ResetStep();
            _controller.Combo.CloseWindow();
            _controller.NotifyComboFinished(finalStep);
            _stateChanger.Change(ActState.None);
            return;
        }

        // 공중: 콤보 대기 없이 입력이 있으면 즉시 다음 타, 없으면 종료
        if (isAir)
        {
            if (_controller.Combo.NextComboQueued)
            {
                _controller.Combo.SetNextComboQueued(false);
                var wd2 = _controller.WeaponManager?.CurrentWeaponData;
                bool isBow2 = wd2 != null && (wd2.weaponType == WeaponType.Bow || wd2.weaponType == WeaponType.Crossbow);
                if (!isBow2)
                    _controller.StartAirHover();
                PlayCurrentComboAnimation();
            }
            else
            {
                _controller.Combo.ResetStep();
                _stateChanger.Change(ActState.None);
            }
            return;
        }

        // 지상: 기존 콤보 로직
        if (_controller.Combo.NextComboQueued)
        {
            _controller.Combo.SetNextComboQueued(false);
            _waitingForComboInput = false;
            PlayCurrentComboAnimation();
        }
        else if (!_controller.Combo.ComboWindowOpen)
        {
            _controller.Combo.ResetStep();
            _stateChanger.Change(ActState.None);
        }
        else
        {
            _waitingForComboInput = true;
        }
    }



    private void OnGenericTag(string tag)
    {
        if (!_controller.Combo.IsAttacking) return;
        _controller.OnAnimationEventTag(tag);
    }

    // ── 애니메이션 재생 ──────────────────────────────────────────────────────
    private void PlayCurrentComboAnimation()
    {
        if (_controller == null || _controller.Anim == null) return;

        int  step   = _controller.Combo.CurrentComboStep;
        var  action = _controller.CurrentAttackTypeForEffect;
        bool isAir  = !_controller.IsGrounded();

        // 이 단계의 mapping 확보 (회전/이동/MoveScale 결정)
        _currentMapping = TryGetClipMapping(step, action, isAir);

        // 목표 회전 계산 — 적용은 RotateTowards 로 매 프레임 (외부 회전 영향에도 자연 수렴)
        if (_currentMapping != null && _currentMapping.useAimAssist)
        {
            _aimTargetRot = _controller.ComputeMouseAimAssistRotation(
                _currentMapping.aimAssistRadius,
                _currentMapping.aimAssistConeHalfAngle,
                _currentMapping.aimAssistStrength);
            _aimRotationDuration = _currentMapping.aimRotationDuration;
        }
        else
        {
            _aimTargetRot = _controller.ComputeMouseAimAssistRotation(0f, 0f, 0f);
            _aimRotationDuration = _currentMapping != null ? _currentMapping.aimRotationDuration : 0.10f;
        }

        // 공격 시작 시점에 잔류 angularVelocity 클리어 — 외부 충돌로 쌓인 회전력이 lerp 중 회전을 어긋나게 하는 것 방지
        if (_controller.Rigid != null)
            _controller.Rigid.angularVelocity = Vector3.zero;

        // duration 이 0 이면 즉시 적용, 아니면 매 프레임 RotateTowards
        if (_aimRotationDuration <= 0f)
        {
            _controller.RequestFacing(_aimTargetRot);
            _aimRotating = false;
        }
        else
        {
            _aimRotating = true;
        }

        // Lunge Step 의 forward 방향은 "최종 목표 회전" 의 forward 를 캐시
        // (lerp 도중 transform.forward 를 쓰면 step 이 휘어진 궤적이 됨)
        _stepDir = _aimTargetRot * Vector3.forward;
        _stepDir.y = 0f;
        if (_stepDir.sqrMagnitude > 0.0001f) _stepDir.Normalize();
        else                                  _stepDir = _controller.transform.forward;
        _stepLastNT = 0f;

        // 정면 SphereCast 로 적/벽 사전 탐지 → effective lunge 거리 결정
        _effectiveStepDistance = ComputeEffectiveStepDistance(
            _currentMapping != null ? _currentMapping.attackStepDistance : 0f);

        // 유도 완료 시 추가 전진 보너스 — 이번 타 기준으로 초기화
        _aimCompleteBonus = _currentMapping != null ? _currentMapping.aimCompleteStepBonus : 0f;
        _bonusApplied = false;
        // 유도 회전이 즉시 스냅(_aimRotating=false)이면 이미 정렬 완료 → 바로 보너스 반영
        if (!_aimRotating) TryApplyAimCompleteBonus();

        // 이 단계의 이동 입력 스케일 적용 (0 = 평소대로 정지, >0 = 약간 반영)
        if (_currentMapping != null)
            _controller.SetMoveScale(_currentMapping.moveInputScale);

        string mappedBaseName    = TryGetMappedBaseClipName(step, action, isAir);
        string fallbackStateName = $"{action}Attack_{(step + 1):00}";
        string stateToPlay       = !string.IsNullOrEmpty(mappedBaseName)
                                   ? mappedBaseName
                                   : fallbackStateName;

        // Debug.Log($"[ActAttackState] Play: step={step}, action={action}, isAir={isAir}, mapped={mappedBaseName ?? "null"}, fallback={fallbackStateName}, final={stateToPlay}");

        Animator anim       = _controller.Anim;
        int      layerIndex = 0;
        int      stateHash  = Animator.StringToHash(stateToPlay);
        int      playedHash = 0;

        if (!isAir)
        {
            if (anim.HasState(layerIndex, stateHash))
            {
                anim.CrossFade(stateHash, 0.08f);
                playedHash = stateHash;
            }
            else if (stateToPlay != fallbackStateName)
            {
                int fbHash = Animator.StringToHash(fallbackStateName);
                if (anim.HasState(layerIndex, fbHash))
                {
                    anim.CrossFade(fbHash, 0.08f);
                    playedHash = fbHash;
                    Debug.Log($"[ActAttackState] Fallback to ground state: {fallbackStateName}");
                }
                else
                {
                    Debug.LogWarning($"[ActAttackState] State not found: {stateToPlay}");
                }
            }
            else
            {
                Debug.LogWarning($"[ActAttackState] State not found: {stateToPlay}");
            }
        }
        else
        {
            if (anim.HasState(layerIndex, stateHash))
            {
                anim.CrossFade(stateHash, 0.08f);
                playedHash = stateHash;
            }
            else
            {
                int fbHash = Animator.StringToHash(fallbackStateName);
                if (anim.HasState(layerIndex, fbHash))
                {
                    anim.CrossFade(fbHash, 0.08f);
                    playedHash = fbHash;
                    Debug.Log($"[ActAttackState] Air state not found ({stateToPlay}), fallback: {fallbackStateName}");
                }
                else
                {
                    Debug.LogWarning($"[ActAttackState] Air state not found: {stateToPlay}, fallback: {fallbackStateName}");
                }
            }
        }

        // 폴링 상태 초기화 및 타이밍 로드
        _currentStateHash  = playedHash;
        _comboWindowOpened = false;
        _comboWindowClosed = false;
        _attackEndFired    = false;
        _stateElapsed      = 0f;

        var mapping = TryGetClipMapping(step, action, isAir);
        var animSet = _controller.WeaponManager?.CurrentWeaponData?.animationSet
                      as WeaponAnimationSetSO;
        (_comboOpen, _comboClose, _attackEnd) = ResolveTiming(mapping, animSet);

        float baseSpeed = animSet?.lightAttackAnimSpeed ?? 1.0f;
        anim.speed = _controller.GlobalAttackAnimSpeedScale
                   * (_controller.RuntimeStats?.AttackSpeedMultiplier ?? 1f)
                   * baseSpeed;
    }

    // ── 타이밍 해석 ──────────────────────────────────────────────────────────
    /// <summary>
    /// ClipMapping override → AnimSet 기본값 → 하드코딩 fallback 순으로 타이밍을 결정한다.
    /// </summary>
    private static (float open, float close, float end) ResolveTiming(
        WeaponAnimationSetSO.ClipMapping mapping,
        WeaponAnimationSetSO             animSet)
    {
        float defOpen  = animSet?.defaultComboWindowOpen  ?? 0.25f;
        float defClose = animSet?.defaultComboWindowClose ?? 0.75f;
        float defEnd   = animSet?.defaultAttackEndAt      ?? 0.85f;

        float open  = (mapping != null && mapping.comboWindowOpen  >= 0f) ? mapping.comboWindowOpen  : defOpen;
        float close = (mapping != null && mapping.comboWindowClose >= 0f) ? mapping.comboWindowClose : defClose;
        float end   = (mapping != null && mapping.attackEndAt      >= 0f) ? mapping.attackEndAt      : defEnd;

        return (open, close, end);
    }

    // ── ClipMapping 조회 유틸 ────────────────────────────────────────────────
    private WeaponAnimationSetSO.ClipMapping TryGetClipMapping(
        int comboStep, WeaponActionType action, bool isAir)
    {
        var wd      = _controller.WeaponManager?.CurrentWeaponData;
        var animSet = wd?.animationSet as WeaponAnimationSetSO;
        if (animSet == null) return null;

        WeaponAnimGroup group = isAir ? WeaponAnimGroup.Air : WeaponAnimGroup.Ground;
        return animSet.GetMappings(group, action).FirstOrDefault(m => m.comboIndex == comboStep);
    }

    private string TryGetMappedBaseClipName(int comboStep, WeaponActionType action, bool isAir)
    {
        var mapping = TryGetClipMapping(comboStep, action, isAir);
        return (mapping != null && !string.IsNullOrEmpty(mapping.baseClipName))
            ? mapping.baseClipName
            : null;
    }
}
