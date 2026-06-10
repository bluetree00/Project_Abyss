using System;
using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// DeathKnight 전용 콤보 패턴 러너.
///
/// BossConfigSO.patternEntries 의 DKComboConfigSO 에서 콤보 횟수를 결정하고,
/// 별도 attackPool 에서 N개의 공격을 무작위로 선택해 브레이크 없이 연속 실행한다.
/// 전체 콤보 완료 후에만 정상 브레이크 쿨다운을 적용한다.
/// </summary>
public class DKComboRunner
{
    // ── 외부 의존성 ──────────────────────────────────────────
    private readonly BossConfigSO          _config;
    private readonly BossPatternContext    _ctx;
    private readonly List<BossPatternSO>   _phase1AreaPool;   // 1페이즈 광역 공격 풀
    private readonly List<BossPatternSO>   _basicPool;        // 기본 공격 풀 (1·2페이즈 공용)
    private readonly List<BossPatternSO>   _phase2AreaPool;   // 2페이즈 광역 공격 풀
    private readonly Func<bool>                         _isAlive;
    private readonly Func<bool>                         _isInRange;
    private readonly Func<bool>                         _isPhase2;
    private readonly Func<bool>                         _isStaggered;  // GetHit 등 경직 중 콤보 차단
    private readonly Action<IMonsterState>              _changeState;
    private readonly Action<BossPatternSO>              _onExecuted;
    private readonly Func<IMonsterState, IMonsterState> _stateDecorator;

    // ── 런타임 상태 ──────────────────────────────────────────
    private float                         _breakCooldown;
    private bool                          _wasInPattern;
    private readonly Queue<BossPatternSO> _comboQueue = new Queue<BossPatternSO>();
    private BossPatternSO                 _lastFiredAttack;
    private float                         _lastFiredTime;
    private bool                          _currentComboUsePhase1Position;

    public bool  IsPatternActive               { get; private set; }
    public float PatternBreakCooldown          => _breakCooldown;
    public bool  CurrentComboUsePhase1Position => _currentComboUsePhase1Position;

    // ── 생성자 ───────────────────────────────────────────────
    public DKComboRunner(
        BossConfigSO                        config,
        BossPatternContext                   ctx,
        List<BossPatternSO>                 phase1AreaPool,
        List<BossPatternSO>                 basicPool,
        List<BossPatternSO>                 phase2AreaPool,
        Func<bool>                          isAlive,
        Func<bool>                          isInRange,
        Func<bool>                          isPhase2,
        Func<bool>                          isStaggered,
        Action<IMonsterState>               changeState,
        Action<BossPatternSO>               onExecuted     = null,
        Func<IMonsterState, IMonsterState>  stateDecorator = null)
    {
        _config          = config;
        _ctx             = ctx;
        _phase1AreaPool  = phase1AreaPool  ?? new List<BossPatternSO>();
        _basicPool       = basicPool       ?? new List<BossPatternSO>();
        _phase2AreaPool  = phase2AreaPool  ?? new List<BossPatternSO>();
        _isAlive         = isAlive;
        _isInRange       = isInRange;
        _isPhase2        = isPhase2        ?? (() => false);
        _isStaggered     = isStaggered     ?? (() => false);
        _changeState     = changeState;
        _onExecuted      = onExecuted;
        _stateDecorator  = stateDecorator;
    }

    // ── 풀 재사용 ────────────────────────────────────────────

    /// <summary>현재 break cooldown이 minDuration보다 짧으면 늘린다.</summary>
    public void EnsureMinBreakCooldown(float minDuration)
    {
        if (_breakCooldown < minDuration)
            _breakCooldown = minDuration;
    }

    /// <summary>보스 풀 재사용(OnEnable) 시 호출.</summary>
    public void Reset()
    {
        _breakCooldown   = 0f;
        _wasInPattern    = false;
        _comboQueue.Clear();
        _lastFiredAttack = null;
        IsPatternActive  = false;
    }

    // ── 메인 틱 ─────────────────────────────────────────────

    public void Tick(float dt)
    {
        if (_breakCooldown > 0f) _breakCooldown -= dt;

        bool inPattern = IsPatternActive = _ctx.Ctx.Monster.IsInSpecialState;

        // 패턴 종료 감지
        if (_wasInPattern && !inPattern)
        {
            // GetHit 등 경직으로 패턴이 중단된 경우 — 콤보 큐 초기화 후 쿨다운 적용
            if (_isStaggered())
            {
                _comboQueue.Clear();
                _wasInPattern    = false;
                _breakCooldown   = UnityEngine.Random.Range(
                    _config.patternBreakDurationMin,
                    _config.patternBreakDurationMax);
                return;
            }

            if (_comboQueue.Count > 0)
            {
                // 콤보 연속 — 브레이크 없이 즉시 다음 공격
                FireNextFromQueue();
                _wasInPattern = _ctx.Ctx.Monster.IsInSpecialState;
                return;
            }
            // 콤보 전체 완료 — 정상 브레이크 설정
            _breakCooldown = UnityEngine.Random.Range(
                _config.patternBreakDurationMin,
                _config.patternBreakDurationMax);
        }
        _wasInPattern = inPattern;

        inPattern = IsPatternActive = _ctx.Ctx.Monster.IsInSpecialState;

        // 새 콤보 시작 조건: 비전투 + 쿨다운 완료 + 생존 + 사정거리 + 경직 없음
        if (!inPattern && _comboQueue.Count == 0 &&
            _breakCooldown <= 0f && _isAlive() && _isInRange() && !_isStaggered())
        {
            var combo = SelectComboConfig();
            if (combo != null)
            {
                BuildComboQueue(combo.comboCount);
                FireNextFromQueue();
            }
        }
    }

    // ── 콤보 설정 선택 ───────────────────────────────────────

    /// <summary>
    /// patternEntries 의 DKComboConfigSO 중 가중치 기반 랜덤 선택.
    /// 선택 결과의 usePhase1Position을 _currentComboUsePhase1Position에 캡처한다.
    /// </summary>
    private DKComboConfigSO SelectComboConfig()
    {
        if (_config?.patternEntries == null || _config.patternEntries.Count == 0) return null;

        float total = 0f;
        foreach (var entry in _config.patternEntries)
        {
            if (entry?.patterns == null || !entry.EvaluateConditions(_ctx)) continue;
            foreach (var p in entry.patterns)
            {
                var c = p as DKComboConfigSO;
                if (c != null && c.CanExecute(_ctx))
                    total += Mathf.Max(0f, c.weight);
            }
        }
        if (total <= 0f) return null;

        float roll = UnityEngine.Random.Range(0f, total);
        float acc  = 0f;
        foreach (var entry in _config.patternEntries)
        {
            if (entry?.patterns == null || !entry.EvaluateConditions(_ctx)) continue;
            foreach (var p in entry.patterns)
            {
                var c = p as DKComboConfigSO;
                if (c == null || !c.CanExecute(_ctx)) continue;
                acc += Mathf.Max(0f, c.weight);
                if (roll <= acc)
                {
                    _currentComboUsePhase1Position = c.usePhase1Position;
                    return c;
                }
            }
        }
        return null;
    }

    // ── 콤보 큐 구성 ────────────────────────────────────────

    private void BuildComboQueue(int count)
    {
        _comboQueue.Clear();
        for (int i = 0; i < count; i++)
        {
            var attack = SelectAttack();
            if (attack != null)
                _comboQueue.Enqueue(attack);
        }
    }

    /// <summary>
    /// 현재 콤보 설정에 따라 적절한 공격 풀에서 가중치+반복 패널티 기반으로 1개 선택.
    ///
    /// usePhase1Position=false → _basicPool (기본 공격, 1·2페이즈 공용)
    /// usePhase1Position=true  → Phase2이면 _phase2AreaPool, Phase1이면 _phase1AreaPool
    /// </summary>
    private BossPatternSO SelectAttack()
    {
        List<BossPatternSO> pool;
        if (!_currentComboUsePhase1Position)
        {
            pool = _basicPool.Count > 0 ? _basicPool : _phase1AreaPool;
        }
        else
        {
            pool = (_isPhase2() && _phase2AreaPool.Count > 0)
                ? _phase2AreaPool
                : _phase1AreaPool;
        }

        if (pool == null || pool.Count == 0) return null;

        float total = 0f;
        foreach (var p in pool)
        {
            if (p == null || !p.CanExecute(_ctx)) continue;
            total += ApplyRepeatPenalty(p);
        }

        if (total <= 0f)
        {
            foreach (var p in pool)
                if (p != null && p.CanExecute(_ctx)) return p;
            return null;
        }

        float roll = UnityEngine.Random.Range(0f, total);
        float acc  = 0f;
        foreach (var p in pool)
        {
            if (p == null || !p.CanExecute(_ctx)) continue;
            acc += ApplyRepeatPenalty(p);
            if (roll <= acc) return p;
        }
        return null;
    }

    // ── 공격 발동 ────────────────────────────────────────────

    private void FireNextFromQueue()
    {
        if (_comboQueue.Count == 0) return;
        var pattern = _comboQueue.Dequeue();
        var state   = pattern.GetRuntimeState();
        if (state == null) return;

        // Phase2 텔레포트 등 사전 처리 상태가 있으면 감싸서 실행
        IMonsterState finalState = _stateDecorator != null ? _stateDecorator(state) : state;

        _changeState(finalState);
        _lastFiredAttack = pattern;
        _lastFiredTime   = Time.time;
        IsPatternActive  = true;
        _wasInPattern    = true;
        _onExecuted?.Invoke(pattern);
    }

    // ── 반복 패널티 ──────────────────────────────────────────

    private float ApplyRepeatPenalty(BossPatternSO pattern)
    {
        float w = pattern.weight;
        if (_config != null && _lastFiredAttack == pattern)
        {
            float elapsed = Time.time - _lastFiredTime;
            if (elapsed < _config.patternRepeatPenaltyDuration)
                w *= _config.patternRepeatPenaltyMult;
        }
        return Mathf.Max(0f, w);
    }
}
}
