using System;
using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// BossConfigSO.patternEntries 기반 패턴 평가·선택·실행 공통 로직.
///
/// 보스마다 이 로직을 중복 구현하지 않도록 분리.
/// 보스 클래스는 의존성을 델리게이트로 주입하고 Tick()을 호출하기만 하면 된다.
///
/// 사용 예시:
///   _runner = new BossPatternRunner(
///       _bossConfig, _patternCtx,
///       isAlive:       () => !_runtime.IsDead && !IsPlayerDead(),
///       isInRange:     () => IsInEngagementRange(),
///       changeState:   state => ChangeState(state),
///       onExecuted:    pattern => _bb.LastPatternTag = pattern.patternTag);
///
///   // Update()
///   _runner.Tick(Time.deltaTime);
///
///   // OnEnable()
///   _runner.Reset();
/// </summary>
public class BossPatternRunner
{
    // ── 외부 의존성 (생성 시 주입) ────────────────────────
    readonly BossConfigSO          _config;
    readonly BossPatternContext    _ctx;
    readonly Func<bool>            _isAlive;       // !isDead && !playerDead
    readonly Func<bool>            _isInRange;     // IsInEngagementRange
    readonly Action<IMonsterState> _changeState;
    readonly Action<BossPatternSO> _onExecuted;    // 패턴 실행 직후 콜백 (태그 기록 등)

    // ── 런타임 상태 ────────────────────────────────────────
    float _patternBreakCooldown;
    bool  _wasInPattern;
    (BossPatternEntry entry, BossPatternSO pattern) _pendingForce;
    BossPatternSO _lastPatternSO;
    float         _lastPatternTime;
    float         _lastPatternEndTime = float.NegativeInfinity;
    // 후보가 직전 패턴 하나뿐일 때 되풀이를 허용하기까지 기다리는 시간(패턴이 끝난 뒤, 초) — 그동안 보스는 추격한다(09-28 반복 검증).
    const float   RepeatGraceSeconds = 2.5f;
    // 연계(2페이지 구성 §9) — 끝난 패턴의 followUp을 이만큼 쉬고 곧바로 낸다
    const float   FollowUpDelay = 0.5f;
    BossPatternSO _queuedFollowUp;
    readonly Dictionary<BossPatternSO, float> _followUpAt = new();   // 연계를 마지막으로 이어 낸 때(앞 패턴별)
    readonly Dictionary<BossPatternEntry, int> _seqIndex = new();
    // SelectRandom 평가마다 새 List를 할당하지 않도록 재사용 (보스 1마리·동기 Tick이라 공유 안전)
    readonly List<BossPatternSO> _randomCandidates = new();
    // 가중치 런타임 배율 — 패턴 SO는 여러 보스 인스턴스가 공유하는 에셋이라 weight에 직접 쓰면 값이 남는다
    // (숲의 수호자가 그렇게 써서 Smash 가중치가 1.4e-8로 굳은 채 커밋돼 있었다). 배율은 이 러너에만 산다.
    readonly Dictionary<BossPatternSO, float> _weightScales = new();

    /// <summary>현재 패턴이 실행 중인지. NormalModeTimer 계산에 사용.</summary>
    public bool  IsPatternActive      { get; private set; }
    public float PatternBreakCooldown => _patternBreakCooldown;

    // ── 생성자 ─────────────────────────────────────────────
    public BossPatternRunner(
        BossConfigSO          config,
        BossPatternContext     ctx,
        Func<bool>            isAlive,
        Func<bool>            isInRange,
        Action<IMonsterState> changeState,
        Action<BossPatternSO> onExecuted = null)
    {
        _config      = config;
        _ctx         = ctx;
        _isAlive     = isAlive;
        _isInRange   = isInRange;
        _changeState = changeState;
        _onExecuted  = onExecuted;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 풀 재사용 초기화
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>패턴 가중치에 런타임 배율을 건다(0 = 당분간 안 뽑힘). 에셋은 건드리지 않는다.</summary>
    public void SetWeightScale(BossPatternSO pattern, float scale)
    {
        if (pattern == null) return;
        if (scale >= 1f) _weightScales.Remove(pattern);
        else             _weightScales[pattern] = Mathf.Max(0f, scale);
    }

    /// <summary>선택에 쓰는 실제 가중치 — 에셋 값 × 런타임 배율.</summary>
    public float WeightOf(BossPatternSO pattern)
    {
        if (pattern == null) return 0f;
        float w = pattern.weight;
        if (_weightScales.TryGetValue(pattern, out float scale)) w *= scale;
        return Mathf.Max(0f, w);
    }

    /// <summary>현재 break cooldown이 minDuration보다 짧으면 minDuration으로 늘린다.</summary>
    public void EnsureMinBreakCooldown(float minDuration)
    {
        if (_patternBreakCooldown < minDuration)
            _patternBreakCooldown = minDuration;
    }

    /// <summary>보스 풀 재사용(OnEnable) 시 호출.</summary>
    public void Reset()
    {
        _patternBreakCooldown = 0f;
        _wasInPattern         = false;
        _pendingForce         = default;
        _lastPatternSO        = null;
        _lastPatternEndTime   = float.NegativeInfinity;
        _queuedFollowUp       = null;
        _followUpAt.Clear();
        _seqIndex.Clear();
        _weightScales.Clear();
        IsPatternActive       = false;
    }


    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 메인 틱
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    public void Tick(float dt)
    {
        // 기절·빙결 등 무력화 중에는 패턴을 새로 시작하지 않는다(쉬는 시간도 멈춘다).
        // MonsterBase는 무력화 중 FSM 업데이트만 건너뛰고, 이 러너는 보스 Update가 따로 돌린다 —
        // 여기서 막지 않으면 기절한 보스가 그 자리에서 공격 패턴 상태(ChangeState)로 넘어간다.
        var monster = _ctx?.Ctx?.Monster;
        if (monster != null && monster.Status.IsCcActive) return;

        if (_patternBreakCooldown > 0f) _patternBreakCooldown -= dt;

        bool inPattern  = IsPatternActive = _ctx.Ctx.Monster.IsInSpecialState;

        // ── 패턴 종료 감지 ─────────────────────────────
        if (_wasInPattern && !inPattern && _config != null)
        {
            _lastPatternEndTime = Time.time;
            if (_pendingForce.pattern != null)
            {
                var pending = _pendingForce;
                _pendingForce = default;

                // entry 조건이 여전히 유효할 때만 실행 (패턴 실행 중 조건이 무효화된 경우 폐기)
                // 사망 중 보류된 강제 패턴이 좀비 실행되지 않도록 생존 체크 추가
                if (_isAlive() && (pending.entry == null || pending.entry.EvaluateConditions(_ctx)))
                {
                    _patternBreakCooldown = 0f;
                    ExecutePattern(pending.pattern);
                }
            }
            else
            {
                _patternBreakCooldown = (_lastPatternSO != null && _lastPatternSO.breakOverride >= 0f)
                    ? _lastPatternSO.breakOverride
                    : UnityEngine.Random.Range(GetBreakDurationMin(), GetBreakDurationMax());
                // 2페이지 후반(간판 뒤) — 쉬는 시간 −25%, 컨셉 연계기는 짧게 쉬고 곧바로(§9)
                var pages = (_ctx.Ctx.Monster as IPagedBoss)?.Pages;
                if (pages != null) _patternBreakCooldown *= pages.BreakScale;
                if (_lastPatternSO != null && _lastPatternSO.FollowUpActive(_ctx.Ctx.Monster)
                    && (!_followUpAt.TryGetValue(_lastPatternSO, out float at) || Time.time - at >= _lastPatternSO.followUpCooldown))
                {
                    _followUpAt[_lastPatternSO] = Time.time;
                    _queuedFollowUp       = _lastPatternSO.followUp;
                    _patternBreakCooldown = Mathf.Min(_patternBreakCooldown, FollowUpDelay);
                }
            }
        }
        _wasInPattern = inPattern;

        // 패턴 실행 직후 inPattern 갱신
        inPattern = IsPatternActive = _ctx.Ctx.Monster.IsInSpecialState;

        // ── 강제 인터럽트 체크 ──────────────────────────
        HandleForceInterrupts(inPattern);
        inPattern = IsPatternActive = _ctx.Ctx.Monster.IsInSpecialState;

        // ── 일반 패턴 평가 ──────────────────────────────
        if (!inPattern && _patternBreakCooldown <= 0f && _isAlive() && _isInRange())
            EvaluateNormalPatterns();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 강제 인터럽트
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    void HandleForceInterrupts(bool inPattern)
    {
        if (_pendingForce.pattern != null) return;
        if (_config?.patternEntries == null) return;
        if (!_isAlive()) return;

        foreach (var entry in _config.patternEntries)
        {
            if (!entry.forceExecute) continue;
            if (!entry.EvaluateConditions(_ctx)) continue;

            var pattern = SelectPatternByForceInterrupt(entry);
            if (pattern == null) continue;

            if (inPattern)
                _pendingForce = (entry, pattern);
            else
            {
                _patternBreakCooldown = 0f;
                _queuedFollowUp       = null;   // 강제 패턴(간판 등)이 끼면 대기 중인 연계는 버린다
                ExecutePattern(pattern);
            }
            return;
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 일반 패턴 평가
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    void EvaluateNormalPatterns()
    {
        if (_config?.patternEntries == null) return;

        // 연계(§9) — 직전 패턴에 이어 낼 패턴이 줄 서 있으면 먼저(안 되면 평소대로 고른다)
        if (_queuedFollowUp != null)
        {
            var next = _queuedFollowUp;
            _queuedFollowUp = null;
            if (next.CanFollowUp(_ctx)) { ExecutePattern(next); return; }
        }

        foreach (var entry in _config.patternEntries)
        {
            if (entry.forceExecute) continue;
            if (!entry.EvaluateConditions(_ctx)) continue;

            var pattern = SelectPattern(entry);
            if (pattern == null) continue;

            ExecutePattern(pattern);
            return;
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 패턴 선택
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    BossPatternSO SelectPattern(BossPatternEntry entry)
    {
        if (entry.patterns == null || entry.patterns.Count == 0) return null;
        return entry.selectionMode switch
        {
            PatternSelectionMode.Sequential => SelectSequential(entry, checkCanExecute: true),
            PatternSelectionMode.Random     => SelectRandom(entry,     checkCanExecute: true),
            _                               => SelectWeightedRandom(entry),
        };
    }

    BossPatternSO SelectPatternSkipCanExecute(BossPatternEntry entry)
    {
        if (entry.patterns == null || entry.patterns.Count == 0) return null;
        return entry.selectionMode switch
        {
            PatternSelectionMode.Sequential => SelectSequential(entry, checkCanExecute: false),
            PatternSelectionMode.Random     => SelectRandom(entry,     checkCanExecute: false),
            _                               => SelectWeightedRandomSkip(entry),
        };
    }

    BossPatternSO SelectPatternByForceInterrupt(BossPatternEntry entry)
    {
        if (entry.patterns == null || entry.patterns.Count == 0) return null;
        return entry.selectionMode switch
        {
            PatternSelectionMode.Sequential => SelectSequential(entry, checkCanExecute: false),
            PatternSelectionMode.Random     => SelectRandom(entry,     checkCanExecute: false),
            _                               => SelectWeightedRandomForceInterrupt(entry),
        };
    }

    BossPatternSO SelectWeightedRandom(BossPatternEntry entry)
    {
        // 1차: 직전 패턴 제외
        float total = 0f;
        foreach (var p in entry.patterns)
        {
            if (p == null || !p.CanExecute(_ctx) || p == _lastPatternSO) continue;
            total += WeightOf(p);
        }
        if (total > 0f)
        {
            float roll = UnityEngine.Random.Range(0f, total);
            float acc  = 0f;
            foreach (var p in entry.patterns)
            {
                if (p == null || !p.CanExecute(_ctx) || p == _lastPatternSO) continue;
                acc += WeightOf(p);
                if (roll <= acc) return p;
            }
        }

        // 폴백: 선택 가능한 패턴이 직전 패턴 하나뿐인 경우 — 끝난 지 얼마 안 됐으면 되풀이하지 않고 기다린다(보스는 추격)
        if (Time.time - _lastPatternEndTime < RepeatGraceSeconds) return null;
        total = 0f;
        foreach (var p in entry.patterns)
        {
            if (p == null || !p.CanExecute(_ctx)) continue;
            total += ApplyRepeatPenalty(p);
        }
        if (total <= 0f) return null;

        float r = UnityEngine.Random.Range(0f, total);
        float a  = 0f;
        foreach (var p in entry.patterns)
        {
            if (p == null || !p.CanExecute(_ctx)) continue;
            a += ApplyRepeatPenalty(p);
            if (r <= a) return p;
        }
        return null;
    }

    BossPatternSO SelectWeightedRandomSkip(BossPatternEntry entry)
    {
        float total = 0f;
        foreach (var p in entry.patterns)
        {
            if (p == null) continue;
            total += WeightOf(p);
        }
        if (total <= 0f) return entry.patterns[0];

        float roll = UnityEngine.Random.Range(0f, total);
        float acc  = 0f;
        foreach (var p in entry.patterns)
        {
            if (p == null) continue;
            acc += WeightOf(p);
            if (roll <= acc) return p;
        }
        return entry.patterns[entry.patterns.Count - 1];
    }

    BossPatternSO SelectWeightedRandomForceInterrupt(BossPatternEntry entry)
    {
        float total = 0f;
        foreach (var p in entry.patterns)
        {
            if (p == null || !p.CanForceInterrupt(_ctx)) continue;
            total += WeightOf(p);
        }
        if (total <= 0f) return null;

        float roll = UnityEngine.Random.Range(0f, total);
        float acc  = 0f;
        foreach (var p in entry.patterns)
        {
            if (p == null || !p.CanForceInterrupt(_ctx)) continue;
            acc += WeightOf(p);
            if (roll <= acc) return p;
        }
        return null;
    }

    BossPatternSO SelectSequential(BossPatternEntry entry, bool checkCanExecute)
    {
        if (!_seqIndex.TryGetValue(entry, out int idx)) idx = 0;
        int count = entry.patterns.Count;
        for (int i = 0; i < count; i++)
        {
            int realIdx = (idx + i) % count;
            var p = entry.patterns[realIdx];
            if (p == null) continue;
            if (checkCanExecute && !p.CanExecute(_ctx)) continue;
            _seqIndex[entry] = (realIdx + 1) % count;
            return p;
        }
        return null;
    }

    BossPatternSO SelectRandom(BossPatternEntry entry, bool checkCanExecute)
    {
        // 1차: 직전 패턴 제외
        _randomCandidates.Clear();
        foreach (var p in entry.patterns)
        {
            if (p == null || p == _lastPatternSO) continue;
            if (checkCanExecute && !p.CanExecute(_ctx)) continue;
            _randomCandidates.Add(p);
        }
        if (_randomCandidates.Count > 0)
            return _randomCandidates[UnityEngine.Random.Range(0, _randomCandidates.Count)];

        // 폴백: 직전 패턴 하나뿐인 경우 — 끝난 지 얼마 안 됐으면 되풀이하지 않고 기다린다(보스는 추격)
        if (Time.time - _lastPatternEndTime < RepeatGraceSeconds) return null;
        _randomCandidates.Clear();
        foreach (var p in entry.patterns)
        {
            if (p == null) continue;
            if (checkCanExecute && !p.CanExecute(_ctx)) continue;
            _randomCandidates.Add(p);
        }
        if (_randomCandidates.Count == 0) return null;
        return _randomCandidates[UnityEngine.Random.Range(0, _randomCandidates.Count)];
    }

    float GetBreakDurationMin()
    {
        var bb = _ctx?.Blackboard;
        return (bb != null && bb.BreakDurationMinOverride >= 0f)
            ? bb.BreakDurationMinOverride
            : _config?.patternBreakDurationMin ?? 0.5f;
    }

    float GetBreakDurationMax()
    {
        var bb = _ctx?.Blackboard;
        return (bb != null && bb.BreakDurationMaxOverride >= 0f)
            ? bb.BreakDurationMaxOverride
            : _config?.patternBreakDurationMax ?? 1.5f;
    }

    float ApplyRepeatPenalty(BossPatternSO pattern)
    {
        float w = WeightOf(pattern);
        if (_config != null && _lastPatternSO == pattern)
        {
            float elapsed = Time.time - _lastPatternTime;
            if (elapsed < _config.patternRepeatPenaltyDuration)
                w *= _config.patternRepeatPenaltyMult;
        }
        return Mathf.Max(0f, w);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 패턴 실행
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    void ExecutePattern(BossPatternSO pattern)
    {
        var state = pattern.GetRuntimeState();
        if (state == null) return;
        _changeState(state);
        (_ctx.Ctx.Monster as IPagedBoss)?.Pages?.NotePatternStarted();   // 2페이지 개막 판정(§9)
        _lastPatternSO   = pattern;
        _lastPatternTime = Time.time;
        _onExecuted?.Invoke(pattern);
    }
}
}
