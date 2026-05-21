using System;
using System.Collections.Generic;
using UnityEngine;

namespace Abyss.Monster
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
    private readonly List<BossPatternSO>   _attackPool;
    private readonly Func<bool>            _isAlive;
    private readonly Func<bool>            _isInRange;
    private readonly Action<IMonsterState> _changeState;
    private readonly Action<BossPatternSO> _onExecuted;

    // ── 런타임 상태 ──────────────────────────────────────────
    private float                         _breakCooldown;
    private bool                          _wasInPattern;
    private readonly Queue<BossPatternSO> _comboQueue = new Queue<BossPatternSO>();
    private BossPatternSO                 _lastFiredAttack;
    private float                         _lastFiredTime;

    public bool  IsPatternActive      { get; private set; }
    public float PatternBreakCooldown => _breakCooldown;

    // ── 생성자 ───────────────────────────────────────────────
    public DKComboRunner(
        BossConfigSO          config,
        BossPatternContext     ctx,
        List<BossPatternSO>   attackPool,
        Func<bool>            isAlive,
        Func<bool>            isInRange,
        Action<IMonsterState> changeState,
        Action<BossPatternSO> onExecuted = null)
    {
        _config      = config;
        _ctx         = ctx;
        _attackPool  = attackPool ?? new List<BossPatternSO>();
        _isAlive     = isAlive;
        _isInRange   = isInRange;
        _changeState = changeState;
        _onExecuted  = onExecuted;
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

        // 새 콤보 시작 조건: 비전투 + 쿨다운 완료 + 생존 + 사정거리
        if (!inPattern && _comboQueue.Count == 0 &&
            _breakCooldown <= 0f && _isAlive() && _isInRange())
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

    /// <summary>patternEntries 의 DKComboConfigSO 중 가중치 기반 랜덤 선택.</summary>
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
                if (roll <= acc) return c;
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

    /// <summary>attackPool 에서 가중치+반복 패널티 기반으로 공격 1개 선택.</summary>
    private BossPatternSO SelectAttack()
    {
        if (_attackPool == null || _attackPool.Count == 0) return null;

        float total = 0f;
        foreach (var p in _attackPool)
        {
            if (p == null || !p.CanExecute(_ctx)) continue;
            total += ApplyRepeatPenalty(p);
        }

        if (total <= 0f)
        {
            // 모든 가중치가 0 이면 실행 가능한 첫 번째 공격으로 폴백
            foreach (var p in _attackPool)
                if (p != null && p.CanExecute(_ctx)) return p;
            return null;
        }

        float roll = UnityEngine.Random.Range(0f, total);
        float acc  = 0f;
        foreach (var p in _attackPool)
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

        _changeState(state);
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
