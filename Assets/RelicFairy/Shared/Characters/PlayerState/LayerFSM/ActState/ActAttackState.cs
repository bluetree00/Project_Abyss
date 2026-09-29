using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Game.Inputs;

public class ActAttackState : LayerStateBase<ActState>
{
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

    // ── 회수(recovery) 상태 ─────────────────────────────────────────────────
    // attackEndAt 은 "다음 콤보가 발사되는 체인 지점"이라 그대로 올리면 콤보가 그만큼 느려진다.
    // 그래서 체인 지점은 두고, 후속 입력이 없을 때만 recoveryEndAt 까지 클립을 마저 재생해
    // 잘려나가던 회수 동작을 되살린다. 입력이 들어오면 즉시 취소되므로 반응성 손실은 없다.
    private bool  _inRecovery;
    private float _recoveryEnd;

    // ── Lunge Step 상태 ─────────────────────────────────────────────────────
    private WeaponAnimationSetSO.ClipMapping _currentMapping;
    private float _stepLastNT;
    private Vector3 _stepDir;
    private float _effectiveStepDistance;
    private float _aimCompleteBonus;   // 유도 완료 시 추가 전진 거리 (mapping 값)
    private bool  _bonusApplied;       // 이번 타에 보너스를 이미 반영했는지

    private bool  _hasLungeTarget;     // 에임어시스트가 잡은 적(런지 거리 신뢰 소스) 존재 여부
    private bool      _counterLunge;   // 저스트 회피 반격 창의 공격 — 원인 적에 고정(겨냥·전진 대상)
    private bool      _counterFirst;   // 그 창의 첫 공격 — 먼 거리 추격(사거리 확장)
    private Transform _counterTarget;  // 원인 적
    private float _slashNorm = 1f;     // 이번 클립의 첫 슬래시 이벤트 시각(정규화) — 전진은 이 전에 끝낸다
    private float _lungeTargetDist;    // 그 적까지의 수평 거리

    // ── 회전 Lerp 상태 ──────────────────────────────────────────────────────
    // RotateTowards 방식 — 매 프레임 현재 회전에서 목표로 일정 각속도로 접근.
    // 외부 회전(물리/충돌/넉백) 이 끼어들어도 그 시점의 회전에서 다시 목표로 수렴 (시작점 캐싱 없음).
    private Quaternion _aimTargetRot;
    private bool       _aimRotating;
    private float      _aimRotationDuration;
    private Quaternion _aimRotRequested;           // 이번 물리 스텝 안에서 마지막으로 요청한 회전
    private float      _aimRotFixedTime = -1f;     // 그 요청이 속한 물리 스텝(Time.fixedTime)

    // ── Lunge 타겟 부스트/정지 파라미터 ───────────────────────────────────
    private const float LungeStopGap      = 0.3f;   // (SphereCast) 적 표면 앞에서 정지할 마진(캐스트 반경 보정 후 실제 간격)
    private const float LungeTargetStopGap = 0.85f; // (에임타겟) 적 중심 기준 정지 마진(적 반경+접근 여유 근사)
    private const string SlashEventPrefix = "SpawnSlashEffect"; // 클립의 슬래시 이펙트 이벤트(SpawnSlashEffect0~3)
    private const string EffectStepEvent  = "AE_EffectStep";    // 같은 역할의 직접 이벤트
    private const float LungeSearchMargin = 0.6f;   // reach 너머까지 적 탐지 여유(이 안의 적이면 reach까지 돌진)
    private const float LungeBoostExtra   = 1.0f;   // lungeMaxRange 미설정 시 base + 이 값까지 부스트(레거시)
    private const float LungeTrackMinRadius = 7.0f; // 유도 타겟 탐지 최소 반경 — CSV aim_assist_radius(3~4)가 작아 추적이 안 걸리는 것 방지
    private const float LungeWallBuffer   = 0.4f;   // 벽 앞에서 정지할 거리
    private const float LungeCastRadius   = 0.4f;   // SphereCast 반경
    private const float LungeCastHeight   = 0.5f;   // 캐스트 원점 높이 오프셋(가슴 높이)
    private static readonly RaycastHit[] _lungeCastBuf = new RaycastHit[16];
    // 클립별 첫 슬래시 이벤트 시각(정규화) — AnimationClip.events가 매번 사본을 만들어 캐시한다
    private static readonly Dictionary<AnimationClip, float> s_slashNormCache = new();

    // ── Enter ────────────────────────────────────────────────────────────────
    // [공중 공격 폐기] 이 상태는 ActAttackReadyState를 통해서만 진입하고, 그쪽이 공중 진입을
    // 막으므로 Enter 시점은 항상 지상이다. 낙하공격 위임·체공·공중 콤보 수 분기는 전부 제거됐다.
    public override void Enter()
    {
        _execution = new AbilityExecution();
        _controller.ActiveExecution = _execution;
        // 회전은 PlayCurrentComboAnimation 에서 ClipMapping 의 AimAssist 옵션과 함께 일괄 처리한다.
        // 여기서 한 번 더 호출하면 클릭 위치 캐시가 먼저 소비되어 콤보 1단계의 aim assist 가 click 위치를 못 본다.

        if (!_controller.Combo.IsAttacking)
        {
            _controller.Combo.SetAttacking(true);
            _controller.Combo.SetNextComboQueued(false);
            _controller.Combo.CloseWindow();

            // 초기 MoveScale은 PlayCurrentComboAnimation 에서 mapping.moveInputScale 로 덮어씀
            _controller.SetMoveScale(0f);
        }

        var wd = _controller.WeaponManager?.CurrentWeaponData;
        _maxCombo = wd != null ? Mathf.Max(1, wd.groundEndCount) : 1;

        _waitingForComboInput = false;
        _stateElapsed = 0f;

        PlayCurrentComboAnimation();
    }

    // ── Update ───────────────────────────────────────────────────────────────
    public override void Update()
    {
        PollAnimationTiming();
        if (_inRecovery) { UpdateRecovery(); return; }
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

        // 회수 구간 종료 — 클립 끝(1.0)도 안전망으로 함께 본다.
        if (_inRecovery && (t >= _recoveryEnd || t >= 1f))
        {
            EndRecovery();
            return;
        }

        // 회전 Lerp — 목표 회전까지 부드럽게 (순간이동 느낌 방지)
        ApplyAimRotation();

        // Lunge Step — normalizedTime 구간 안에 forward 평행이동 적용.
        // 다음 애니 갱신에서 나아갈 정규화 시간도 함께 넘긴다(애니 속도 × 이번 프레임 시간 ÷ 상태 길이).
        float predictedDelta = stateInfo.length > 0f ? anim.speed * PlayerDeltaTime() / stateInfo.length : 0f;
        ApplyLungeStep(t, predictedDelta);

        // 반격 창(저스트 회피 슬로모)에선 물리 스텝이 3배 드물어 보간된 몸(transform)이 추격을 한참 늦게 따라온다.
        // 슬래시 이펙트·판정은 몸 기준으로 나가므로 출발점에서 헛스윙했다(실측: 첫 슬래시 순간 과녁까지
        // transform 7.2m / 물리 1.3m). 추격한 프레임에만 맞추면 다음 프레임 보간이 몸을 추격 전 자리 쪽으로
        // 되돌려 베는 순간 0.7~1.2m 뒤처졌다(09-19 실측) — 반격 공격 동안은 매 프레임 몸을 물리 위치에 둔다.
        // 추격이 끝난 뒤엔 물리 위치가 멈춰 있으므로 맞춰도 움직이지 않는다. 경로는 잔상이 채운다.
        if (_counterLunge) SnapBodyToPhysics();
    }

    private void SnapBodyToPhysics()
    {
        var rb = _controller.Rigid;
        if (rb != null && !rb.isKinematic) _controller.transform.position = rb.position;
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
        // 단, Rigidbody.rotation은 물리 스텝에서만 갱신된다 — 같은 스텝 안의 프레임마다 그 값을 읽으면
        // 같은 '한 프레임 분량' 요청만 되풀이돼 회전이 스텝당 한 프레임치로 묶였다(고주사율·슬로모에서 크게 느려짐).
        // 스텝이 바뀌면 실제 회전에서, 같은 스텝 안에선 직전 요청값에서 이어 돈다.
        Quaternion cur;
        if (_controller.Rigid == null || Time.fixedTime != _aimRotFixedTime)
        {
            cur = _controller.Rigid != null ? _controller.Rigid.rotation : _controller.transform.rotation;
            _aimRotFixedTime = Time.fixedTime;
        }
        else
        {
            cur = _aimRotRequested;
        }
        // 180° 를 duration 안에 완주하는 각속도 (deg/s) — 시간은 애니와 같은 기준(저스트 회피 중엔 실시간)
        float maxAngleStep = (180f / _aimRotationDuration) * PlayerDeltaTime();
        Quaternion next = Quaternion.RotateTowards(cur, _aimTargetRot, maxAngleStep);
        _aimRotRequested = next;
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
    private void ApplyLungeStep(float normalizedTime, float predictedDelta)
    {
        if (_currentMapping == null || _effectiveStepDistance <= 0f) return;

        float start = _currentMapping.attackStepStartNorm;
        float end   = _currentMapping.attackStepEndNorm;
        if (end <= start) return;

        // 베기 전에 들어가고, 벤 뒤엔 멈춘다 — 슬래시 이펙트·판정은 월드에 남으므로 그 뒤에도 전진하면
        // 캐릭터가 제 이펙트를 두고 미끄러지고 판정도 뒤에 남아 다가간 적을 놓친다.
        if (_slashNorm < end) end = _slashNorm;
        if (end <= start) return;

        // 슬래시 애니 이벤트는 이 Update 뒤 애니메이터 갱신에서 나간다 — 여기서 쓰는 시각은 한 프레임 전 것이라
        // 다음 갱신에 끝을 넘을 것 같으면 지금 끝낸다(안 그러면 마지막 조각이 슬래시 뒤에 들어간다, 실측 0.05~0.14m).
        // 다음 갱신 진행량은 계산값을 쓴다 — 직전 프레임 차이만 쓰면 구간 직전(진행 기록이 start에 고정)에서 0이 돼
        // 한 프레임 폭 구간(반격 가속 + 이른 베기)을 놓쳤다(09-19 실측: 반격 첫 타가 과녁 7.1m 앞에서 헛스윙).
        float nextDelta = Mathf.Max(predictedDelta, normalizedTime - _stepLastNT);
        bool  finishNow = normalizedTime + nextDelta >= end;

        // 윈도우 진입 전: lastNT 를 start 로 고정해, 첫 진입 시 캐치업 점프 방지.
        // 단 구간 전체가 다음 갱신 한 번에 지나갈 것 같으면 기다리지 않고 지금 다 민다.
        // 이미 다 민 뒤(lastNT = end)엔 되돌리지 않는다 — 되돌리면 구간 전 프레임마다 전진을 한 번 더 밀었다(추격 2배).
        if (normalizedTime < start)
        {
            if (_stepLastNT < start) _stepLastNT = start;
            if (!finishNow) return;
        }
        // 윈도우 끝을 넘는 프레임도 아래 클램프로 '남은 조각'까지 적용한다(그 뒤 프레임은 이동 0).
        // 예전엔 끝을 넘는 순간 바로 반환해 마지막 조각을 버렸다 — 구간이 짧으면 한 프레임이 구간의
        // 절반을 차지해 계획 거리의 25%가 사라졌다(저스트 회피 반격 추격 실측 6.1m 중 4.6m).

        float prevClamped = Mathf.Clamp(_stepLastNT, start, end);
        float currClamped = finishNow ? end : Mathf.Clamp(normalizedTime, start, end);
        float window      = end - start;

        // ease-out (1 - (1-t)^2) — 적분이 t 의 단조 증가이며 끝에 감속.
        // 누적 거리 함수: f(t) = (1 - (1-t)^2) → t=0 에서 0, t=1 에서 1
        float prevT = (prevClamped - start) / window;
        float currT = (currClamped - start) / window;
        float prevDist = EaseOut(prevT) * _effectiveStepDistance;
        float currDist = EaseOut(currT) * _effectiveStepDistance;
        float distThisFrame = currDist - prevDist;

        // 앞당겨 끝냈으면 끝 시각으로 기록한다 — 지금 시각으로 두면 다음 프레임이 같은 마지막 조각을
        // 한 번 더 밀었다(09-19 실측: 슬래시 0.10 카타나 1타에서 슬래시 뒤 +0.19m).
        // 한 공격 안에서 진행 기록은 줄지 않는다(Enter에서만 0으로) — 줄면 이미 민 조각을 다시 민다.
        _stepLastNT = Mathf.Max(_stepLastNT, Mathf.Max(normalizedTime, currClamped));

        if (distThisFrame <= 0f) return;

        Vector3 delta = _stepDir * distThisFrame;

        var rb = _controller.Rigid;
        if (rb != null && !rb.isKinematic)
        {
            // Rigidbody.MovePosition: Rigidbody.interpolation=Interpolate 와 결합 시 부드럽게 렌더링
            // (이 리지드바디는 호출 즉시 rb.position이 갱신된다 — 프레임마다 이어 더해도 손실·중복이 없다. 09-19 실측)
            rb.MovePosition(rb.position + delta);
        }
        else
        {
            _controller.transform.position += delta;
        }
    }

    /// <summary>
    /// 상태에 걸린 실제 클립(오버라이드 반영)의 첫 슬래시 이벤트 시각(정규화). 없으면 1 = 제한 없음.
    /// 이벤트 배열은 사본이 할당되므로 클립당 한 번만 읽어 캐시한다.
    /// </summary>
    private static float ResolveSlashNorm(Animator anim, string stateName)
    {
        var aoc  = anim.runtimeAnimatorController as AnimatorOverrideController;
        var clip = aoc != null ? aoc[stateName] : null;
        if (clip == null || clip.length <= 0f) return 1f;
        if (s_slashNormCache.TryGetValue(clip, out float cached)) return cached;

        float first  = 1f;
        var   events = clip.events;
        for (int i = 0; i < events.Length; i++)
        {
            string fn = events[i].functionName;
            if (fn == EffectStepEvent || fn.StartsWith(SlashEventPrefix, StringComparison.Ordinal))
                first = Mathf.Min(first, events[i].time / clip.length);
        }
        s_slashNormCache[clip] = first;
        return first;
    }

    /// <summary>
    /// 플레이어 동작 시간 — 애니가 실시간으로 도는 동안(저스트 회피 슬로모) 실시간 델타, 평소엔 게임 델타.
    /// 공격 애니는 실시간인데 회전만 느려지면 몸이 돌기 전에 베어 버린다.
    /// </summary>
    private float PlayerDeltaTime()
    {
        var anim = _controller.Anim;
        return anim != null && anim.updateMode == AnimatorUpdateMode.UnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
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
        // 전진 없는 클립(원거리)은 반격 창에서도 끌려가지 않는다 — 겨냥만 원인 적에 고정한다.
        if (baseDist <= 0f || _controller == null) return 0f;

        // 유도 돌진 사거리: 클립에 lungeMaxRange 설정 시 그 값(전진 지능), 아니면 레거시(base + 1.0).
        float reach = (_currentMapping != null && _currentMapping.lungeMaxRange > 0f)
            ? _currentMapping.lungeMaxRange
            : baseDist + LungeBoostExtra;
        if (_counterFirst && _controller.CharacterData != null)
            reach = Mathf.Max(reach, _controller.CharacterData.perfectDodgeCounterLungeReach);
        float searchRange = reach + LungeCastRadius + LungeSearchMargin;
        Vector3 origin    = _controller.transform.position + Vector3.up * LungeCastHeight;

        // 바닥 레이어 제외 — 경사/단차/평지 바닥을 '벽'으로 오인해 런지가 잘리는 것 방지.
        int groundBits = _controller.CharacterData != null ? _controller.CharacterData.groundLayer.value : 0;
        int castMask   = ~groundBits;

        int count = Physics.SphereCastNonAlloc(
            origin, LungeCastRadius, _stepDir, _lungeCastBuf, searchRange,
            castMask, QueryTriggerInteraction.Ignore);

        float monsterDist = float.PositiveInfinity;
        float counterDist = float.PositiveInfinity;   // 반격 대상(원인 적)의 표면까지
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
                if (_counterTarget != null && h.distance < counterDist &&
                    (ct == _counterTarget || ct.IsChildOf(_counterTarget)))
                    counterDist = h.distance;
            }
            else
            {
                if (h.distance < wallDist) wallDist = h.distance;
            }
        }

        float effective = baseDist;

        // 적까지 전진 거리 — 두 소스 중 더 멀리(둘 다 같은 표적을 가리킴):
        //  ① 에임어시스트 타겟(OverlapSphere 기반 — 높이/각도 무관, 신뢰 소스)
        //  ② 전방 SphereCast 가 직접 잡은 적(정면 직격 보조)
        float advance = -1f;
        if (_counterLunge)
        {
            // 반격은 대상이 확정돼 있다 — 캐스트가 그 뒤의 다른 대상을 잡으면 두 소스 중 먼 쪽을 따라 원인 적을
            // 지나쳐 버린다. 원인 적의 '표면'(다른 적과 같은 정지 규칙) 앞에서 멈춘다: 슬로모 속 추격은 한 물리
            // 스텝에 몇 m를 옮겨, 중심 기준으로 멈추면 콜라이더 안에 박혔다가 반대편으로 밀려 나갔다.
            // 캐스트가 못 잡으면(시작점 겹침 등) 중심 거리 − 여유. 이미 붙어 있으면 0 — 음수는 '대상 없음'으로 읽힌다.
            advance = !float.IsInfinity(counterDist)
                ? counterDist + LungeCastRadius - LungeStopGap
                : Mathf.Max(0f, _lungeTargetDist - LungeTargetStopGap);
        }
        else
        {
            if (_hasLungeTarget)
                advance = Mathf.Max(advance, _lungeTargetDist - LungeTargetStopGap);
            if (!float.IsInfinity(monsterDist))
                advance = Mathf.Max(advance, monsterDist + LungeCastRadius - LungeStopGap);
        }

        if (advance >= 0f)
            effective = Mathf.Clamp(advance, 0f, reach);

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
                if (!TryEnterRecovery())
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
    public override void Exit()
    {
        _waitingForComboInput = false;
        _inRecovery           = false;
        _recoveryEnd          = 0f;
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
            _controller.Anim.CrossFadeInFixedTime("JumpBlend", 0.10f);

        _controller.ActiveExecution = null;
        _execution?.Cleanup(forceEffects: false);
        _execution = null;
    }

    // ── 내부 이벤트 핸들러 ───────────────────────────────────────────────────
    private void OnAttackEnd()
    {
        _controller.Combo.IncrementStep();

        if (_controller.Combo.CurrentComboStep >= _maxCombo)
        {
            int finalStep = _controller.Combo.CurrentComboStep;
            _controller.Combo.ResetStep();
            _controller.Combo.CloseWindow();
            _controller.NotifyComboFinished(finalStep);
            // 콤보 마지막 타 — 회수를 재생한다(TryEnterRecovery가 공중이면 스스로 거른다).
            if (TryEnterRecovery()) return;
            _stateChanger.Change(ActState.None);
            return;
        }

        if (_controller.Combo.NextComboQueued)
        {
            _controller.Combo.SetNextComboQueued(false);
            _waitingForComboInput = false;
            PlayCurrentComboAnimation();
        }
        else if (!_controller.Combo.ComboWindowOpen)
        {
            // 콤보 창이 이미 닫힌 채 체인 지점에 도달 — 후속 입력이 없다는 뜻이니 회수를 재생한다.
            if (TryEnterRecovery()) return;
            _controller.Combo.ResetStep();
            _stateChanger.Change(ActState.None);
        }
        else
        {
            _waitingForComboInput = true;
        }
    }

    // ── 회수(recovery) ───────────────────────────────────────────────────────
    /// <summary>
    /// 회수 구간 진입 시도. mapping.recoveryEndAt 이 체인 지점(attackEndAt)보다 뒤일 때만 성립한다.
    /// 성립하면 상태를 종료하지 않고 그 시점까지 현재 클립을 계속 재생한다(잘려나가던 회수 동작 복원).
    /// </summary>
    private bool TryEnterRecovery()
    {
        if (_inRecovery) return true;
        if (_currentMapping == null || _currentStateHash == 0) return false;
        if (!_controller.IsGrounded()) return false;

        float rec = _currentMapping.recoveryEndAt;
        if (rec <= _attackEnd) return false;

        _inRecovery  = true;
        _recoveryEnd = rec;
        return true;
    }

    /// <summary>
    /// 회수 중 입력 감시. 어떤 입력이든(공격·회피·스킬·이동) 들어오면 그 프레임에 회수를 끊는다.
    /// 버퍼는 소비하지 않는다 — 상태를 빠져나간 뒤 평소 입력 경로가 그대로 처리하므로
    /// 회수를 넣기 전과 동일하게 반응한다(추가 지연 0).
    /// </summary>
    private void UpdateRecovery()
    {
        if (_controller.InputBuffer.Count > 0) { EndRecovery(); return; }
        if (_controller.MoveDirection.sqrMagnitude > 0.0001f) EndRecovery();
    }

    private void EndRecovery()
    {
        _inRecovery = false;
        _controller.Combo.ResetStep();
        _stateChanger.Change(ActState.None);
    }

    // ── 애니메이션 재생 ──────────────────────────────────────────────────────
    private void PlayCurrentComboAnimation()
    {
        if (_controller == null || _controller.Anim == null) return;

        int step   = _controller.Combo.CurrentComboStep;
        var action = _controller.CurrentAttackTypeForEffect;

        // 이 단계의 mapping 확보 (회전/이동/MoveScale 결정).
        // [공중 공격 폐기] 지상 클립만 조회한다 — 콤보 도중 턱에서 떨어져 잠깐 공중이 되어도
        // 진행 중인 공격은 지상 공격이므로 클립 그룹을 바꾸면 안 된다.
        _currentMapping = TryGetClipMapping(step, action);

        // 목표 회전 계산 — 적용은 RotateTowards 로 매 프레임 (외부 회전 영향에도 자연 수렴)
        // 동시에 에임어시스트가 고른 적을 받아 런지 거리의 신뢰 소스로 사용(좁은 SphereCast 수직/각도 빗나감 보완).
        if (_currentMapping != null && _currentMapping.useAimAssist)
        {
            // CSV가 aim_assist_radius를 작게(3~4) 덮어쓰면 추적이 거의 안 걸리므로 최소 반경 보장.
            float trackRadius = Mathf.Max(_currentMapping.aimAssistRadius, LungeTrackMinRadius);
            _aimTargetRot = _controller.ComputeMouseAimAssistRotation(
                trackRadius,
                _currentMapping.aimAssistConeHalfAngle,
                _currentMapping.aimAssistStrength,
                out var aimEnemy, out var aimDist);
            _hasLungeTarget  = aimEnemy != null;
            _lungeTargetDist = aimDist;
            _aimRotationDuration = _currentMapping.aimRotationDuration;
        }
        else
        {
            _aimTargetRot = _controller.ComputeMouseAimAssistRotation(0f, 0f, 0f);
            _hasLungeTarget  = false;
            _lungeTargetDist = 0f;
            _aimRotationDuration = _currentMapping != null ? _currentMapping.aimRotationDuration : 0.10f;
        }

        // 저스트 회피 반격 창의 공격 — 마우스 조준 대신 원인 적에 고정한다(첫 공격은 먼 거리 추격).
        _counterLunge  = false;
        _counterFirst  = false;
        _counterTarget = null;
        if (_controller.TryGetCounterTarget(out var counterTarget, out bool firstCounter))
        {
            Vector3 toTarget = counterTarget.position - _controller.transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 0.0001f)
            {
                _aimTargetRot        = Quaternion.LookRotation(toTarget);
                _hasLungeTarget      = true;
                _lungeTargetDist     = toTarget.magnitude;
                _counterLunge        = true;
                _counterFirst        = firstCounter;
                _counterTarget       = counterTarget;
                _aimRotationDuration = 0f;   // 즉시 돌아선다 — 슬로모 속 순간 추격이라 도는 동안 등 뒤로 베면 안 된다
            }
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
            _aimRotating     = true;
            _aimRotFixedTime = -1f;   // 이전 타의 요청값을 이어 쓰지 않게 — 첫 프레임은 실제 회전에서 시작
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

        // 반격 창의 공격 — 연출(추격 잔상·발광 펄스)에 출발·도착 지점을 알린다.
        if (_counterLunge)
        {
            Vector3 from = _controller.transform.position;
            _controller.RaiseCounterStrike(from, from + _stepDir * _effectiveStepDistance, _counterFirst);
        }

        // 이 단계의 이동 입력 스케일 적용 (0 = 평소대로 정지, >0 = 약간 반영)
        if (_currentMapping != null)
            _controller.SetMoveScale(_currentMapping.moveInputScale);

        string mappedBaseName    = TryGetMappedBaseClipName(step, action);
        string fallbackStateName = $"{action}Attack_{(step + 1):00}";
        string stateToPlay       = !string.IsNullOrEmpty(mappedBaseName)
                                   ? mappedBaseName
                                   : fallbackStateName;

        Animator anim       = _controller.Anim;
        int      layerIndex = 0;
        int      stateHash  = Animator.StringToHash(stateToPlay);
        int      playedHash = 0;
        string   playedName = null;

        if (anim.HasState(layerIndex, stateHash))
        {
            anim.CrossFadeInFixedTime(stateHash, 0.06f);
            playedHash = stateHash;
            playedName = stateToPlay;
        }
        else if (stateToPlay != fallbackStateName)
        {
            int fbHash = Animator.StringToHash(fallbackStateName);
            if (anim.HasState(layerIndex, fbHash))
            {
                anim.CrossFadeInFixedTime(fbHash, 0.06f);
                playedHash = fbHash;
                playedName = fallbackStateName;
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

        _slashNorm = playedName != null ? ResolveSlashNorm(anim, playedName) : 1f;

        // 폴링 상태 초기화 및 타이밍 로드
        _currentStateHash  = playedHash;
        _comboWindowOpened = false;
        _comboWindowClosed = false;
        _attackEndFired    = false;
        _stateElapsed      = 0f;

        var mapping = TryGetClipMapping(step, action);
        var animSet = _controller.WeaponManager?.CurrentWeaponData?.animationSet
                      as WeaponAnimationSetSO;
        (_comboOpen, _comboClose, _attackEnd) = ResolveTiming(mapping, animSet);

        float baseSpeed = animSet?.lightAttackAnimSpeed ?? 1.0f;
        anim.speed = _controller.GlobalAttackAnimSpeedScale
                   * (_controller.RuntimeStats?.AttackSpeedMultiplier ?? 1f)
                   * baseSpeed
                   * WeaponTempo(_controller)
                   * _controller.CounterAttackSpeedMultiplier;   // 저스트 회피 반격 창 가속(평소 1)
    }

    /// <summary>
    /// 무기별 공격 템포 배율(EQUIPMENT_DATA <c>attack_speed</c>). 1.0 = 기준 템포.
    ///
    /// 이 값은 예전엔 <b>어디에서도 읽히지 않아</b> 카타나 1.5 / 대검 1.0 / 석궁 1.0 / 보우 1.8이
    /// 캐릭터 정보창에 "공속 x1.5"로 표시만 되고 실제 템포는 애니메이션 클립 길이가 100% 결정했다.
    /// 표시와 실제를 일치시키기 위해 여기서 소비한다.
    ///
    /// <c>animSet.lightAttackAnimSpeed</c>와 역할이 다르다 —
    /// 그쪽은 애니 팩마다 다른 <b>원본 클립 템포를 맞추는 에셋 보정</b>이고,
    /// 이 값은 차트에서 굴리는 <b>무기 밸런스 노브</b>다. 그래서 곱해서 함께 쓴다.
    ///
    /// 0 이하(차트 컬럼 누락·구 SO)는 "값 없음"으로 보고 1배로 막는다 — 0이면 애니가 정지한다.
    /// </summary>
    private static float WeaponTempo(PlayerController controller)
    {
        var wd = controller?.WeaponManager?.CurrentWeaponData;
        return (wd != null && wd.attackSpeed > 0f) ? wd.attackSpeed : 1f;
    }

    // ── 타이밍 해석 ──────────────────────────────────────────────────────────
    /// <summary>
    /// ClipMapping override → AnimSet 기본값 → 하드코딩 fallback 순으로 타이밍을 결정한다.
    ///
    /// 세 값은 <b>open &lt; close ≤ end</b> 순서를 지켜야 한다. 콤보 창이 닫히기도 전에 공격이
    /// 끝나버리면(end &lt; close) 창은 열려 있는데 상태는 이미 다음 스텝을 판단해버려,
    /// 입력이 먹다 말다 하는 조작감이 된다. 무형검이 실제로 이 상태였다
    /// (지상·공중 전 타 open 0.30 / close 0.72 / <b>end 0.55</b>).
    ///
    /// 데이터가 어긋나도 동작이 무너지지 않도록 여기서 순서를 강제한다 — 창을 줄이는 대신
    /// 공격 종료를 창 끝까지 미룬다(카타나 0.45/0.80/0.85, 대검 0.25/0.35/0.35과 같은 형태).
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

        if (close < open) close = open;   // 창 자체가 뒤집힌 경우
        if (end   < close) end   = close; // 공격 종료가 창보다 앞선 경우

        return (open, close, end);
    }

    // ── ClipMapping 조회 유틸 ────────────────────────────────────────────────
    // [공중 공격 폐기] 공중 그룹(WeaponAnimGroup.Air) 조회 경로는 제거됐다.
    // enum 값 자체는 무기 SO 에셋에 직렬화돼 있어 남겨둔다.
    private WeaponAnimationSetSO.ClipMapping TryGetClipMapping(
        int comboStep, WeaponActionType action)
    {
        var wd      = _controller.WeaponManager?.CurrentWeaponData;
        var animSet = wd?.animationSet as WeaponAnimationSetSO;
        if (animSet == null) return null;

        return animSet.GetMappings(WeaponAnimGroup.Ground, action)
                      .FirstOrDefault(m => m.comboIndex == comboStep);
    }

    private string TryGetMappedBaseClipName(int comboStep, WeaponActionType action)
    {
        var mapping = TryGetClipMapping(comboStep, action);
        return (mapping != null && !string.IsNullOrEmpty(mapping.baseClipName))
            ? mapping.baseClipName
            : null;
    }
}
