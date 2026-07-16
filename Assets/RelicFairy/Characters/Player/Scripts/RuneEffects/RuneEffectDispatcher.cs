using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 캐릭터(PlayerController)에 부착되는 룬 속성 효과 디스패처.
///
/// 연결 경로:
///  - 단계 도달: MerlinRuneBridge.ApplyMechanicEffect → Activate(entry)
///  - 공격 적중: EffectManager.OnPostDealDamage → NotifyHit (근접/원거리 공통 단일 경로)
///  - 피격:     PlayerController.TakeDamage → NotifyDamaged (플레이어 피해의 유일한 싱크)
///  - 스킬/처치: 캐릭터에서 NotifySkillUsed / NotifyKill 호출
///  - 매 프레임: PlayerController.Update → Tick(dt)
///
/// 효과 본문은 비어 있어도(RuneEffect 스켈레톤) 연결 구조는 완성된다.
/// </summary>
public sealed class RuneEffectDispatcher : IBuffViewSource
{
    private readonly PlayerController       _player;
    private readonly List<IRuneEffect>      _active      = new();
    private readonly HashSet<string>        _activeTypes = new();
    private readonly RuneResourceState      _resources   = new();
    private float _lastSkillTime = -999f;
    private bool _subscribed;

    // 중앙(CENTER) 공명 — 활성 속성 효과의 value를 배수로 끌어올린다.
    // 효과들이 Entry.value를 직접 읽으므로, 24개 클래스를 건드리지 않고
    // "value를 곱한 사본 엔트리로 효과를 재생성"하는 방식으로 증폭한다.
    // → 원본(미증폭) 엔트리를 반드시 따로 보관해야 배수 변경 시 복리로 누적되지 않는다.
    private readonly Dictionary<string, RuneSynergyEntry> _sourceEntries = new();
    private float _amplifier = 1f;

    /// <summary>현재 중앙 공명 배수(1 = 증폭 없음).</summary>
    public float Amplifier => _amplifier;

    // [가이드라인 비주얼] 룬 리소스 배지 폴링(0.25s throttle). key→라벨/아이콘/색 고정 테이블.
    private float _badgePollAccum;
    private static readonly (string key, string label, string iconKey, GuidelineVisual.BadgeTint tint)[] s_resourceBadges =
    {
        ("ElecStatic",    "전기", "lightning", GuidelineVisual.BadgeTint.Electric),
        ("LightRadiance", "광채", "light",     GuidelineVisual.BadgeTint.Light),
        ("darkGauge",     "어둠", "dark",      GuidelineVisual.BadgeTint.Dark),
    };

    public RuneEffectDispatcher(PlayerController player)
    {
        _player = player;
        _resources.SetAnchor(player != null ? player.transform : null);   // [가이드라인 비주얼] 리소스 토스트 위치
        QuestEvents.OnMonsterKilled += HandleKill;
        _subscribed = true;
    }

    public IReadOnlyList<IRuneEffect> Active => _active;

    /// <summary>플레이어 룬 리소스(스택/게이지/레지스터). 속성 효과들이 공유하는 일반 컨테이너.</summary>
    public RuneResourceState Resources => _resources;

    /// <summary>해당 effect_type 단계가 현재 활성인지. 누적형 상위→하위 의존 판정에 사용.</summary>
    public bool IsActive(string effectType) => _activeTypes.Contains(effectType);

    /// <summary>활성 effect_type의 핸들러 조회(상위 단계가 하위 파라미터를 읽을 때). 없으면 null.</summary>
    public IRuneEffect GetActive(string effectType)
    {
        for (int i = 0; i < _active.Count; i++)
            if (_active[i].EffectType == effectType) return _active[i];
        return null;
    }

    /// <summary>단계 도달 시 호출. 동일 effect_type 중복 활성 방지.</summary>
    public void Activate(RuneSynergyEntry entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.effect_type)) return;
        if (!_activeTypes.Add(entry.effect_type)) return;

        _sourceEntries[entry.effect_type] = entry;   // 원본 보관(증폭 재계산의 기준)

        var fx = RuneEffectFactory.Create(Amplified(entry));
        if (fx == null)
        {
            _activeTypes.Remove(entry.effect_type);
            _sourceEntries.Remove(entry.effect_type);
            return;
        }

        _active.Add(fx);
        // 누적형 단계 의존(전기 방전→감전, 어둠 잠식→해방→잔상)은 OnSkillUsed/OnHit/Tick의
        // _active 순회 순서에 기댄다. 활성화 호출 순서(데이터 순서)에 의존하지 않도록 threshold
        // 오름차순을 강제 — 하위 단계가 항상 먼저 실행되어 RS 레지스터를 채운 뒤 상위가 읽는다.
        _active.Sort(CompareByThreshold);
        fx.OnActivate(_player);
    }

    private static int CompareByThreshold(IRuneEffect a, IRuneEffect b)
        => (a.Entry != null ? a.Entry.threshold : 0).CompareTo(b.Entry != null ? b.Entry.threshold : 0);

    // ── 중앙(CENTER) 공명 ──────────────────────────────────────

    /// <summary>
    /// 중앙 공명 배수 설정. 활성 속성 효과를 <b>원본 엔트리 기준</b>으로 전부 재생성한다.
    /// 룬판(시간정지) 안에서만 값이 바뀌므로 전투 중 재생성은 발생하지 않는다.
    /// 배수가 그대로면 아무 것도 하지 않는다(불필요한 재생성 방지).
    /// </summary>
    public void SetAmplifier(float mult)
    {
        mult = Mathf.Max(0.01f, mult);
        if (Mathf.Approximately(mult, _amplifier)) return;

        _amplifier = mult;
        RebuildActive();
    }

    /// <summary>원본 엔트리로 활성 효과 전체를 재생성(증폭 반영).</summary>
    private void RebuildActive()
    {
        if (_sourceEntries.Count == 0) return;

        var sources = new List<RuneSynergyEntry>(_sourceEntries.Values);

        for (int i = 0; i < _active.Count; i++) _active[i].OnDeactivate();
        _active.Clear();
        _activeTypes.Clear();

        foreach (var src in sources)
        {
            var fx = RuneEffectFactory.Create(Amplified(src));
            if (fx == null) continue;
            _activeTypes.Add(src.effect_type);
            _active.Add(fx);
        }

        _active.Sort(CompareByThreshold);
        for (int i = 0; i < _active.Count; i++) _active[i].OnActivate(_player);
    }

    /// <summary>
    /// value에 공명 배수를 적용한 <b>사본</b>을 만든다(원본 불변 — 복리 누적 방지).
    /// ⚠️ value만 곱한다. value2/value3는 지속시간·횟수·간격이 섞여 있어 일괄 배수가 위험하다.
    /// </summary>
    private RuneSynergyEntry Amplified(RuneSynergyEntry src)
    {
        if (src == null || Mathf.Approximately(_amplifier, 1f)) return src;

        return new RuneSynergyEntry
        {
            zone_id      = src.zone_id,
            zone_name    = src.zone_name,
            threshold    = src.threshold,
            effect_type  = src.effect_type,
            trigger      = src.trigger,
            value        = src.value * _amplifier,   // ← 증폭 지점
            value2       = src.value2,
            value3       = src.value3,
            max_stack    = src.max_stack,
            duration     = src.duration,
            description  = src.description,
            stat_version = src.stat_version,
        };
    }

    /// <summary>
    /// 단일 단계 효과 해제(런 중 룬 재배치로 점유가 임계 미만이 됐을 때). OnDeactivate로 동적 스탯/예약을 되돌린 뒤 목록에서 제거.
    /// 누적형은 상위 단계만 골라 해제 가능(예: 감전만 빠지고 정전기/방전은 유지) — 각 효과의 OnDeactivate가 자기 기여만 정리한다.
    /// </summary>
    public void Deactivate(string effectType)
    {
        if (string.IsNullOrEmpty(effectType)) return;
        if (!_activeTypes.Remove(effectType)) return;
        _sourceEntries.Remove(effectType);
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            if (_active[i].EffectType != effectType) continue;
            _active[i].OnDeactivate();
            _active.RemoveAt(i);
            break;
        }
    }

    /// <summary>모든 활성 효과 해제(런 종료 등).</summary>
    public void Clear()
    {
        for (int i = 0; i < _active.Count; i++) _active[i].OnDeactivate();
        _active.Clear();
        _activeTypes.Clear();
        _sourceEntries.Clear();
        _amplifier = 1f;
        _resources.Clear();

        // [가이드라인 비주얼] 리소스 배지 정리
        for (int i = 0; i < s_resourceBadges.Length; i++)
            GuidelineVisual.ClearBadge("rune_" + s_resourceBadges[i].key);
    }

    /// <summary>구독 해제 + 정리. PlayerController.OnDestroy에서 호출.</summary>
    public void Detach()
    {
        if (_subscribed)
        {
            QuestEvents.OnMonsterKilled -= HandleKill;
            _subscribed = false;
        }
        Clear();
    }

    public void Tick(float dt)
    {
        _resources.Tick(dt);
        for (int i = 0; i < _active.Count; i++) _active[i].Tick(dt, _player);

        // [가이드라인 비주얼] 룬 리소스 배지 갱신(throttle)
        _badgePollAccum += dt;
        if (_badgePollAccum >= 0.25f) { _badgePollAccum = 0f; UpdateResourceBadges(); }
    }

    private void UpdateResourceBadges()
    {
        if (_player == null) return;
        var t = _player.transform;
        for (int i = 0; i < s_resourceBadges.Length; i++)
        {
            var b = s_resourceBadges[i];
            int v = _resources.Get(b.key);
            string badgeKey = "rune_" + b.key;
            if (v > 0) GuidelineVisual.SetBadge(t, badgeKey, b.label + " " + v, b.tint);
            else       GuidelineVisual.ClearBadge(badgeKey);
        }
    }

    // ── 버프창 수집(IBuffViewSource) ────────────────────────
    /// <summary>현재 활성 룬 리소스(스택/게이지>0)를 버프창 항목으로 기여. 읽기 전용 — 리소스 로직 미변경.</summary>
    public void Contribute(List<BuffViewItem> into)
    {
        for (int i = 0; i < s_resourceBadges.Length; i++)
        {
            var b = s_resourceBadges[i];
            int v = _resources.Get(b.key);
            if (v <= 0) continue;
            // 카운트는 스택 배지(×N)로, 상한 대비 충전%는 게이지로. 라벨은 속성명만.
            int max = _resources.GetMax(b.key);
            float fill = max > 0 ? (float)v / max : -1f;
            into.Add(new BuffViewItem(b.iconKey, b.label, v, fill, "", BuffSource.Rune, isDebuff: false));
        }
    }

    // ── 캐릭터 직접 호출 진입점 (ActSkillStateBase에서 연결) ──
    public void NotifySkillUsed()
    {
        _lastSkillTime = UnityEngine.Time.time;
        for (int i = 0; i < _active.Count; i++) _active[i].OnSkillUsed(_player);
    }

    /// <summary>최근 스킬 사용 후 window초 이내인지. "스킬 적중" 근사 판정(빙결 등)에 사용 — 스킬 실행 경로(ActSkillStateBase)에 연동.</summary>
    public bool IsWithinSkillWindow(float window) => UnityEngine.Time.time - _lastSkillTime <= window;

    /// <summary>
    /// 공격 적중 통지(근접/원거리 공통). EffectManager.OnPostDealDamage에서 호출.
    /// DamageReport → HitInfo로 변환해 OnHit/OnCrit 라우팅.
    /// </summary>
    public void NotifyHit(in DamageReport report)
    {
        if (_active.Count == 0 || report.Target == null) return;

        Vector3 dir = report.Attacker != null
            ? (report.Target.transform.position - report.Attacker.transform.position)
            : Vector3.forward;

        var info = new HitInfo(
            attacker:        report.Attacker,
            target:          report.Target,
            hitPoint:        report.HitPosition,
            attackDirection: dir,
            damage:          report.DamageDealt,
            isCritical:      report.IsCrit,
            actionType:      default);

        for (int i = 0; i < _active.Count; i++)
        {
            _active[i].OnHit(info, _player);
            if (info.IsCritical) _active[i].OnCrit(info, _player);
        }

        // [가이드라인 비주얼] 룬 활성 중 치명타 표시(통지만)
        if (info.IsCritical && report.Target != null)
            GuidelineVisual.Crit(report.Target.transform.position);
    }

    /// <summary>
    /// 피격 통지(갭2). PlayerController.TakeDamage에서 실제 피해 적용 시 호출.
    /// 몬스터 공격은 RaiseHit를 안 타므로 HitFeedbackService 경로로는 OnDamaged가 발화하지 않아 이 진입점이 필요.
    /// </summary>
    public void NotifyDamaged(float damage, GameObject attacker)
    {
        if (_active.Count == 0 || _player == null) return;

        Vector3 dir = attacker != null
            ? (_player.transform.position - attacker.transform.position)
            : Vector3.forward;

        var info = new HitInfo(
            attacker:        attacker,
            target:          _player.gameObject,
            hitPoint:        _player.transform.position,
            attackDirection: dir,
            damage:          damage,
            isCritical:      false,
            actionType:      default);

        for (int i = 0; i < _active.Count; i++) _active[i].OnDamaged(info, _player);
    }

    // ── QuestEvents.OnMonsterKilled 라우팅 ──
    private void HandleKill(string codeName)
    {
        for (int i = 0; i < _active.Count; i++) _active[i].OnKill(_player);
    }

}
