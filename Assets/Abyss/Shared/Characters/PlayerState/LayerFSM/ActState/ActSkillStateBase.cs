using System;

/// <summary>
/// Q/E/R 스킬 상태 공통 기반 (Template Method).
///
/// 고정 구조:
///   Enter → 쿨다운 체크 → OnSkillUse 패시브 → OnEnter()
///   Exit  → GetCooldown() 으로 쿨다운 시작   → OnExit()
///
/// 파생 클래스에서 구현할 것:
///   - Slot        : 어떤 슬롯(Q/E/R)인지
///   - GetCooldown : 현재 장비 기준 쿨다운(초)
///   - OnEnter     : 스킬 고유 동작 (애니메이션, AbilityExecution 등)
///   - OnExit      : 스킬 종료 후 정리 (선택)
/// </summary>
public abstract class ActSkillStateBase<TActState> : ILayerState<TActState>
    where TActState : struct, Enum
{
    protected PlayerController            _controller;
    protected ILayerStateChanger<TActState> _stateChanger;

    protected abstract SkillType Slot { get; }

    // ── ILayerState 구현 ─────────────────────────────────────────────────────

    public void Init(PlayerController controller, ILayerStateChanger<TActState> stateChanger)
    {
        _controller   = controller;
        _stateChanger = stateChanger;
    }

    public void Enter()
    {
        if (!_controller.CooldownTracker.IsReady(Slot))
        {
            _stateChanger.Change(default);   // None(0) 으로 복귀
            return;
        }

        _controller.FirePassive(PassiveTrigger.OnSkillUse,
            new PassiveContext { skillUsed = Slot });

        OnEnter();
    }

    public void Update() => OnUpdate();

    public void Exit()
    {
        float cd = GetCooldown();
        if (cd > 0f)
            _controller.CooldownTracker.StartCooldown(Slot, cd, _controller.RuntimeStats.SkillCooldownReduction);

        OnExit();
    }

    // ── 파생 클래스 오버라이드 포인트 ────────────────────────────────────────

    /// <summary>스킬 진입 동작 (애니메이션 재생, AbilityExecution 시작 등).</summary>
    protected abstract void OnEnter();

    /// <summary>스킬 상태 Update (필요 시 override).</summary>
    protected virtual void OnUpdate() { }

    /// <summary>스킬 종료 정리 (AbilityExecution 해제 등).</summary>
    protected virtual void OnExit() { }

    /// <summary>이 슬롯의 기본 쿨다운(초). 0이면 쿨다운 없음.</summary>
    protected abstract float GetCooldown();
}
