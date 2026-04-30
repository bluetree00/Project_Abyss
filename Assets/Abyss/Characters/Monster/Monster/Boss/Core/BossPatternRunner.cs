using System;
using System.Collections.Generic;
using UnityEngine;

namespace Abyss.Monster
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
    readonly Dictionary<BossPatternEntry, int> _seqIndex = new();

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

    /// <summary>보스 풀 재사용(OnEnable) 시 호출.</summary>
    public void Reset()
    {
        _patternBreakCooldown = 0f;
        _wasInPattern         = false;
        _pendingForce         = default;
        _lastPatternSO        = null;
        _seqIndex.Clear();
        IsPatternActive       = false;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 메인 틱
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    public void Tick(float dt)
    {
        if (_patternBreakCooldown > 0f) _patternBreakCooldown -= dt;

        bool inPattern  = IsPatternActive = _ctx.Ctx.Monster.IsInSpecialState;

        // ── 패턴 종료 감지 ─────────────────────────────
        if (_wasInPattern && !inPattern && _config != null)
        {
            if (_pendingForce.pattern != null)
            {
                // 예약된 강제 패턴 즉시 실행
                var pending = _pendingForce;
                _pendingForce         = default;
                _patternBreakCooldown = 0f;
                ExecutePattern(pending.pattern);
            }
            else
            {
                _patternBreakCooldown = (_lastPatternSO != null && _lastPatternSO.breakOverride >= 0f)
                    ? _lastPatternSO.breakOverride
                    : UnityEngine.Random.Range(_config.patternBreakDurationMin, _config.patternBreakDurationMax);
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
        float total = 0f;
        foreach (var p in entry.patterns)
        {
            if (p == null || !p.CanExecute(_ctx)) continue;
            total += ApplyRepeatPenalty(p);
        }
        if (total <= 0f) return null;

        float roll = UnityEngine.Random.Range(0f, total);
        float acc  = 0f;
        foreach (var p in entry.patterns)
        {
            if (p == null || !p.CanExecute(_ctx)) continue;
            acc += ApplyRepeatPenalty(p);
            if (roll <= acc) return p;
        }
        return null;
    }

    BossPatternSO SelectWeightedRandomSkip(BossPatternEntry entry)
    {
        float total = 0f;
        foreach (var p in entry.patterns)
        {
            if (p == null) continue;
            total += Mathf.Max(0f, p.weight);
        }
        if (total <= 0f) return entry.patterns[0];

        float roll = UnityEngine.Random.Range(0f, total);
        float acc  = 0f;
        foreach (var p in entry.patterns)
        {
            if (p == null) continue;
            acc += Mathf.Max(0f, p.weight);
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
            total += Mathf.Max(0f, p.weight);
        }
        if (total <= 0f) return null;

        float roll = UnityEngine.Random.Range(0f, total);
        float acc  = 0f;
        foreach (var p in entry.patterns)
        {
            if (p == null || !p.CanForceInterrupt(_ctx)) continue;
            acc += Mathf.Max(0f, p.weight);
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
        var candidates = new List<BossPatternSO>();
        foreach (var p in entry.patterns)
        {
            if (p == null) continue;
            if (checkCanExecute && !p.CanExecute(_ctx)) continue;
            candidates.Add(p);
        }
        if (candidates.Count == 0) return null;
        return candidates[UnityEngine.Random.Range(0, candidates.Count)];
    }

    float ApplyRepeatPenalty(BossPatternSO pattern)
    {
        float w = pattern.weight;
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
        _lastPatternSO   = pattern;
        _lastPatternTime = Time.time;
        _onExecuted?.Invoke(pattern);
    }
}
}
