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

    // 유물 파츠(개화) 효과 허브. 룬과 같은 전투 신호에 반응하되 데이터 소스·수명이 달라
    // 별도 핸들러가 팬아웃한다(상세는 RelicPartEffectHandler). 파츠 획득 시 지연 생성.
    private RelicPartEffectHandler _parts;
    /// <summary>유물 파츠 효과 허브(지연 생성). 드래프트 획득 후 SyncFromLoadout로 활성화한다.</summary>
    public RelicPartEffectHandler Parts => _parts ??= new RelicPartEffectHandler(_player);
    /// <summary>파츠 허브(미생성 시 null). 신호 팬아웃 지점에서 불필요한 생성 없이 참조.</summary>
    public RelicPartEffectHandler PartsOrNull => _parts;

    // 중앙(CENTER) 공명 — 활성 속성 효과의 value를 배수로 끌어올린다.
    // 효과들이 Entry.value를 직접 읽으므로, 24개 클래스를 건드리지 않고
    // "value를 곱한 사본 엔트리로 효과를 재생성"하는 방식으로 증폭한다.
    // → 원본(미증폭) 엔트리를 반드시 따로 보관해야 배수 변경 시 복리로 누적되지 않는다.
    private readonly Dictionary<string, RuneSynergyEntry> _sourceEntries = new();
    private float _amplifier = 1f;

    /// <summary>현재 중앙 공명 배수(1 = 증폭 없음).</summary>
    public float Amplifier => _amplifier;

    // [정제소 존핵] 존별 추가 배수 — 매칭 존에 놓인 존핵이 그 존 시너지만 강화한다.
    // 중앙 공명(_amplifier, 전역)과 곱연산으로 합쳐진다. 값 = 1.0 기준(1.3 = +30%).
    private readonly Dictionary<string, float> _zoneAmp = new();

    /// <summary>해당 존의 존핵 배수(없으면 1).</summary>
    public float ZoneAmplifier(string zoneId)
        => zoneId != null && _zoneAmp.TryGetValue(zoneId, out var m) ? m : 1f;

    // 룬 리소스 표시 테이블(key→라벨/아이콘). 표출처는 <b>버프칸 하나뿐</b>이다(Contribute).
    // 예전엔 같은 값을 플레이어 머리 위 월드 라벨("전기 3"/"어둠 5")로도 띄웠는데,
    // 버프칸이 들어오면서 같은 정보가 화면 한가운데 두 번 나오는 꼴이 돼 레거시 표출을 걷어냈다.
    private static readonly (string key, string label, string iconKey)[] s_resourceBadges =
    {
        ("ElecStatic",    "전기", "lightning"),
        ("LightRadiance", "광채", "light"),
        ("darkGauge",     "어둠", "dark"),
    };

    public RuneEffectDispatcher(PlayerController player)
    {
        _player = player;
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

    /// <summary>
    /// [정제소 존핵] 존별 배수 일괄 설정. 판에 놓인 존핵을 스캔한 결과를 룬판이 통보한다.
    /// 실제로 달라진 게 있을 때만 활성 효과를 재생성한다(전투 중 호출 없음 — 룬판은 시간정지).
    /// </summary>
    public void SetZoneAmplifiers(IReadOnlyDictionary<string, float> amps)
    {
        bool changed = false;

        // 새로 들어온 값 반영
        if (amps != null)
        {
            foreach (var kv in amps)
            {
                float v = Mathf.Max(0.01f, kv.Value);
                if (!_zoneAmp.TryGetValue(kv.Key, out var cur) || !Mathf.Approximately(cur, v))
                {
                    _zoneAmp[kv.Key] = v;
                    changed = true;
                }
            }
        }

        // 사라진 존(존핵 제거) 정리
        if (_zoneAmp.Count > 0)
        {
            var stale = new List<string>();
            foreach (var key in _zoneAmp.Keys)
                if (amps == null || !amps.ContainsKey(key)) stale.Add(key);
            for (int i = 0; i < stale.Count; i++) { _zoneAmp.Remove(stale[i]); changed = true; }
        }

        if (changed) RebuildActive();
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
        // 공명 재구성은 룬판(시간정지)에서 전체 재활성이라 발동 버스트가 무더기로 뜨는 걸 막는다.
        ElementVfxPlayer.SuppressBursts = true;
        try { for (int i = 0; i < _active.Count; i++) _active[i].OnActivate(_player); }
        finally { ElementVfxPlayer.SuppressBursts = false; }
    }

    /// <summary>
    /// value에 공명 배수를 적용한 <b>사본</b>을 만든다(원본 불변 — 복리 누적 방지).
    /// ⚠️ value만 곱한다. value2/value3는 지속시간·횟수·간격이 섞여 있어 일괄 배수가 위험하다.
    /// </summary>
    private RuneSynergyEntry Amplified(RuneSynergyEntry src)
    {
        if (src == null) return null;

        // 전역(중앙 공명) × 존별(정제소 존핵). 둘 다 1이면 원본 그대로.
        float mult = _amplifier * ZoneAmplifier(src.zone_id);
        if (Mathf.Approximately(mult, 1f)) return src;

        return new RuneSynergyEntry
        {
            zone_id      = src.zone_id,
            zone_name    = src.zone_name,
            threshold    = src.threshold,
            effect_type  = src.effect_type,
            trigger      = src.trigger,
            value        = src.value * mult,         // ← 증폭 지점(중앙 공명 × 존핵)
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
        _zoneAmp.Clear();
        _resources.Clear();
    }

    /// <summary>구독 해제 + 정리. PlayerController.OnDestroy에서 호출.</summary>
    public void Detach()
    {
        if (_subscribed)
        {
            QuestEvents.OnMonsterKilled -= HandleKill;
            _subscribed = false;
        }
        _parts?.Detach();   // 파츠는 런 고정 — 런 종료(플레이어 파괴)에서만 회수.
        Clear();
    }

    public void Tick(float dt)
    {
        _resources.Tick(dt);
        for (int i = 0; i < _active.Count; i++) _active[i].Tick(dt, _player);
        _parts?.Tick(dt);
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
        _parts?.NotifySkillUsed();
    }

    /// <summary>최근 스킬 사용 후 window초 이내인지. "스킬 적중" 근사 판정(빙결 등)에 사용 — 스킬 실행 경로(ActSkillStateBase)에 연동.</summary>
    public bool IsWithinSkillWindow(float window) => UnityEngine.Time.time - _lastSkillTime <= window;

    /// <summary>
    /// 공격 적중 통지(근접/원거리 공통). EffectManager.OnPostDealDamage에서 호출.
    /// DamageReport → HitInfo로 변환해 OnHit/OnCrit 라우팅.
    /// </summary>
    public void NotifyHit(in DamageReport report)
    {
        if (report.Target == null) return;
        bool hasParts = _parts != null && _parts.Active.Count > 0;
        if (_active.Count == 0 && !hasParts) return;

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
            actionType:      report.ActionType);

        for (int i = 0; i < _active.Count; i++)
        {
            _active[i].OnHit(info, _player);
            if (info.IsCritical) _active[i].OnCrit(info, _player);
        }
        if (hasParts) _parts.NotifyHit(info);

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
        if (_player == null) return;
        bool hasParts = _parts != null && _parts.Active.Count > 0;
        if (_active.Count == 0 && !hasParts) return;

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
        if (hasParts) _parts.NotifyDamaged(info);
    }

    // ── QuestEvents.OnMonsterKilled 라우팅 ──
    private void HandleKill(string codeName)
    {
        for (int i = 0; i < _active.Count; i++) _active[i].OnKill(_player);
        // 파츠 처치 신호는 죽은 적 GameObject가 필요해 ItemEffectManager.OnKill(target)에서 별도 발화한다.
    }

}
