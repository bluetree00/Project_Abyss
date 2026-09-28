/// <summary>
/// Q/E/R 스킬 상태 공통 기반 (Template Method).
///
/// 골격(Enter/Update/Exit)은 <c>sealed</c>다 — 파생 클래스는 아래 훅만 채우고 순서는 바꿀 수 없다.
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
public abstract class ActSkillStateBase : LayerStateBase<ActState>
{
    /// <summary>
    /// 스킬이 실제로 발동했는지. Enter()의 차단 분기가 Change(None)을 부르면 상태머신이
    /// "방금 진입한 이 상태"의 Exit()를 그 자리에서 실행한다 — 이 플래그가 없으면 그 Exit이
    /// 쿨다운을 새로 덮어써서, 쿨다운 중 연타 시 스킬이 영구히 안 나간다.
    /// </summary>
    private bool _entered;

    protected abstract SkillType Slot { get; }

    // ── 템플릿 골격 (파생 재정의 불가) ───────────────────────────────────────

    public sealed override void Enter()
    {
        _entered = false;

        if (!_controller.CooldownTracker.IsReady(Slot))
        {
            UnityEngine.Debug.Log($"[SkillBase] {Slot} blocked by cooldown");
            _stateChanger.Change(ActState.None);
            return;
        }

        // 유물 스킬 게이팅(정오 구간 한정 / 자동 발동형 수동 입력 차단 등).
        // 유물이 '소유'한 슬롯(런타임 제공)에만 적용 — 미소유 슬롯(무기 스킬 E/R, 스킬 없는 랜슬롯)은
        // 게이팅 대상이 아니므로 무기 스킬로 진행한다. (레거시 유물은 모든 슬롯 CanUseSkill=true였음)
        if (_controller.RelicBehavior != null
            && _controller.CreateCharacterSkillRuntime(Slot) != null
            && !_controller.RelicBehavior.CanUseSkill(Slot))
        {
            _stateChanger.Change(ActState.None);
            return;
        }

        _entered = true;

        UnityEngine.Debug.Log($"[SkillBase] {Slot} Enter");
        // 무기 스킬 시작음(09-25). Q는 유물 스킬이라 각자 소리를 낸다(가웨인 태양 낙하·랜슬롯 참격).
        if (Slot != SkillType.Q) Managers.Sound?.PlayEvent(SoundEvent.PlayerSkill);
        _controller.FirePassive(PassiveTrigger.OnSkillUse,
            new PassiveContext { skillUsed = Slot });

        // 아이템 효과: 스킬 사용 hook (FireExplosion, Lightning 등)
        var mgr = GameRunBootstrapper.Instance?.Run?.EffectManager;
        mgr?.OnSkillUse(Slot);

        // 서약: 스킬 사용 디스패치 (엘레인/이졸데 등)
        GameRunBootstrapper.Instance?.Run?.CovenantHandler?.OnSkillUse(Slot);

        // 룬 속성 효과: 스킬 사용 hook (전기 방전 등)
        _controller.RuneEffects.NotifySkillUsed();

        OnEnter();
    }

    public sealed override void Update() => OnUpdate();

    public sealed override void Exit()
    {
        // 차단 분기로 되돌아온 진입은 발동이 아니다 — 쿨다운도, OnExit 정리도 건드리지 않는다.
        if (!_entered) return;
        _entered = false;

        // 스킬 종료 시 쿨다운 시작 (SkillCooldownReduction 반영). 엘레인/베디비어 부활.
        float cd = GetCooldown();
        // 악몽 규칙(역류하는 술식) — 무기 스킬(E·R)만 늘린다. Q는 유물 전용(자원 게이트)이다.
        if (Slot != SkillType.Q) cd *= NightmareRules.WeaponSkillCooldownMultiplier;
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
