using System.Collections.Generic;

/// <summary>
/// 캐릭터(PlayerController)에 부착되는 룬 속성 효과 디스패처.
///
/// 연결 경로:
///  - 단계 도달: MerlinRuneBridge.ApplyMechanicEffect → Activate(entry)
///  - 공격/피격: HitFeedbackService.OnHit 구독 → OnHit/OnCrit/OnDamaged 라우팅
///  - 스킬/처치: 캐릭터에서 NotifySkillUsed / NotifyKill 호출
///  - 매 프레임: PlayerController.Update → Tick(dt)
///
/// 효과 본문은 비어 있어도(RuneEffect 스켈레톤) 연결 구조는 완성된다.
/// </summary>
public sealed class RuneEffectDispatcher
{
    private readonly PlayerController       _player;
    private readonly List<IRuneEffect>      _active      = new();
    private readonly HashSet<string>        _activeTypes = new();
    private bool _subscribed;

    public RuneEffectDispatcher(PlayerController player)
    {
        _player = player;
        HitFeedbackService.OnHit    += HandleHit;
        QuestEvents.OnMonsterKilled += HandleKill;
        _subscribed = true;
    }

    public IReadOnlyList<IRuneEffect> Active => _active;

    /// <summary>단계 도달 시 호출. 동일 effect_type 중복 활성 방지.</summary>
    public void Activate(RuneSynergyEntry entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.effect_type)) return;
        if (!_activeTypes.Add(entry.effect_type)) return;

        var fx = RuneEffectFactory.Create(entry);
        if (fx == null) { _activeTypes.Remove(entry.effect_type); return; }

        _active.Add(fx);
        fx.OnActivate(_player);
    }

    /// <summary>모든 활성 효과 해제(런 종료 등).</summary>
    public void Clear()
    {
        for (int i = 0; i < _active.Count; i++) _active[i].OnDeactivate();
        _active.Clear();
        _activeTypes.Clear();
    }

    /// <summary>구독 해제 + 정리. PlayerController.OnDestroy에서 호출.</summary>
    public void Detach()
    {
        if (_subscribed)
        {
            HitFeedbackService.OnHit    -= HandleHit;
            QuestEvents.OnMonsterKilled -= HandleKill;
            _subscribed = false;
        }
        Clear();
    }

    public void Tick(float dt)
    {
        for (int i = 0; i < _active.Count; i++) _active[i].Tick(dt, _player);
    }

    // ── 캐릭터 직접 호출 진입점 (ActSkillStateBase에서 연결) ──
    public void NotifySkillUsed()
    {
        for (int i = 0; i < _active.Count; i++) _active[i].OnSkillUsed(_player);
    }

    // ── QuestEvents.OnMonsterKilled 라우팅 ──
    private void HandleKill(string codeName)
    {
        for (int i = 0; i < _active.Count; i++) _active[i].OnKill(_player);
    }

    // ── HitFeedbackService.OnHit 라우팅 ──
    private void HandleHit(HitInfo info)
    {
        if (_active.Count == 0) return;

        // 피격자가 플레이어면 OnDamaged, 아니면 플레이어의 공격으로 간주 → OnHit/OnCrit
        // TODO: Attacker가 플레이어 측인지 정밀 필터(현재는 플레이어→적 단일 가정)
        bool targetIsPlayer = info.Target != null && _player != null &&
            (info.Target == _player.gameObject || info.Target.transform.IsChildOf(_player.transform));

        if (targetIsPlayer)
        {
            for (int i = 0; i < _active.Count; i++) _active[i].OnDamaged(info, _player);
            return;
        }

        for (int i = 0; i < _active.Count; i++)
        {
            _active[i].OnHit(info, _player);
            if (info.IsCritical) _active[i].OnCrit(info, _player);
        }
    }
}
