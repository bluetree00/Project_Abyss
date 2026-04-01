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
        _controller.RotateTowardsMousePosition();

        _receiver = _controller.EventReceiver
                 ?? _controller.GetComponentInChildren<PlayerAnimationEventReceiver>();

        if (!_controller.Combo.IsAttacking)
        {
            _controller.Combo.SetAttacking(true);
            _controller.Combo.SetNextComboQueued(false);
            _controller.Combo.CloseWindow();
            _controller.SetMoveScale(0f);
        }

        var  action = _controller.CurrentAttackTypeForEffect;
        var  wd     = _controller.WeaponManager?.CurrentWeaponData;
        bool isAir  = !_controller.IsGrounded();
        _maxCombo = wd != null
            ? (isAir ? Mathf.Max(1, wd.airEndCount) : Mathf.Max(1, wd.groundEndCount))
            : 1;

        // 공중 공격 진입 시 즉시 체공 + 사용 플래그
        if (isAir)
        {
            _controller.StartAirHover();
            _controller.AirAttackUsed = true;
        }

        _waitingForComboInput = false;

        SubscribeReceiver();
        PlayCurrentComboAnimation();
        _controller.BeginWeaponTrail();
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
        if (_currentStateHash == 0) return;

        var anim      = _controller.Anim;
        var stateInfo = anim.IsInTransition(0)
            ? anim.GetNextAnimatorStateInfo(0)
            : anim.GetCurrentAnimatorStateInfo(0);

        if (stateInfo.shortNameHash != _currentStateHash) return;

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
        _controller.EndWeaponTrail();

        _waitingForComboInput = false;
        _currentStateHash     = 0;

        _controller.Combo.SetAttacking(false);
        _controller.Combo.SetNextComboQueued(false);
        _controller.Combo.ResetStep();
        _controller.Combo.CloseWindow();
        _controller.SetMoveScale(1f);

        _controller.ActiveExecution = null;
        _execution?.Cleanup(forceEffects: false);
        _execution = null;
    }

    // ── 이벤트 구독 (HitStep / GenericTag만 유지) ────────────────────────────
    private void SubscribeReceiver()
    {
        if (_receiver == null) return;
        _receiver.OnHitStep    += OnHitStep;
        _receiver.OnGenericTag += OnGenericTag;
    }

    private void UnsubscribeReceiver()
    {
        if (_receiver == null) return;
        _receiver.OnHitStep    -= OnHitStep;
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

    private void OnHitStep(int stepIndex)
    {
        if (!_controller.Combo.IsAttacking || stepIndex < 0) return;
        _controller.OnAttackHitStep(stepIndex);
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

        _controller.RotateTowardsMousePosition();

        int  step   = _controller.Combo.CurrentComboStep;
        var  action = _controller.CurrentAttackTypeForEffect;
        bool isAir  = !_controller.IsGrounded();

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

        var mapping = TryGetClipMapping(step, action, isAir);
        var animSet = _controller.WeaponManager?.CurrentWeaponData?.animationSet
                      as WeaponAnimationSetSO;
        (_comboOpen, _comboClose, _attackEnd) = ResolveTiming(mapping, animSet);
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
