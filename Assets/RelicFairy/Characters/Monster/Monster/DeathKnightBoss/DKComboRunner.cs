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
/// 항목에 콤보 SO가 아닌 패턴을 직접 넣으면 풀 없이 한 번씩 낸다 — 2페이지(해방) 풀은 이 방식이다(Page_2 항목).
/// 2페이지 전환 · 간판은 이 러너가 아니라 보스(DeathKnightBossMonster)가 직접 건다.
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
    private BossPatternSO                 _lastPickedInCombo;
    private float                         _breakMin = -1f, _breakMax = -1f;   // 2페이즈 쉬는 시간 — 공용 설정 SO에 쓰지 않는다

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

    /// <summary>
    /// 쉬는 시간 범위를 이 러너에만 정한다(음수 = 설정 SO 값). 예전엔 보스가 공용 설정 SO에 직접 써서
    /// 에디터에서 에셋까지 바뀌었다(06-17 커밋에 2/4 → 1/2.5로 섞여 들어감, 10-01 감사).
    /// </summary>
    public void SetBreakRange(float min, float max)
    {
        _breakMin = min;
        _breakMax = max;
    }

    private float BreakMin => _breakMin >= 0f ? _breakMin : _config.patternBreakDurationMin;
    private float BreakMax => _breakMax >= 0f ? _breakMax : _config.patternBreakDurationMax;

    /// <summary>현재 break cooldown이 minDuration보다 짧으면 늘린다.</summary>
    public void EnsureMinBreakCooldown(float minDuration)
    {
        if (_breakCooldown < minDuration)
            _breakCooldown = minDuration;
    }

    /// <summary>보스 풀 재사용(OnEnable) 시 호출.</summary>
    public void Reset()
    {
        _breakCooldown     = 0f;
        _wasInPattern      = false;
        _comboQueue.Clear();
        _lastFiredAttack   = null;
        _lastPickedInCombo = null;
        IsPatternActive    = false;
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
                _breakCooldown   = UnityEngine.Random.Range(BreakMin, BreakMax);
                return;
            }

            if (_comboQueue.Count > 0)
            {
                // 콤보 연속 — 브레이크 없이 즉시 다음 공격
                FireNextFromQueue();
                _wasInPattern = _ctx.Ctx.Monster.IsInSpecialState;
                return;
            }
            // 콤보 전체 완료 — 정상 브레이크 설정(2페이지 후반은 −25%, §9 · 악몽 「지휘」 기수가 서 있으면 ×0.85)
            _breakCooldown = UnityEngine.Random.Range(BreakMin, BreakMax) * (Pages?.BreakScale ?? 1f)
                           * DKStandardBearer.RestScale(_ctx.Ctx.Monster);
        }
        _wasInPattern = inPattern;

        inPattern = IsPatternActive = _ctx.Ctx.Monster.IsInSpecialState;

        // 새 콤보 시작 조건: 비전투 + 쿨다운 완료 + 생존 + 사정거리 + 경직 없음
        if (!inPattern && _comboQueue.Count == 0 &&
            _breakCooldown <= 0f && _isAlive() && _isInRange() && !_isStaggered())
        {
            TryFireNext();
        }
    }

    // ── 패턴/콤보 선택 후 발동 ───────────────────────────────

    /// <summary>
    /// patternEntries에서 DKComboConfigSO(풀 기반)와 직접 BossPatternSO를 통합 가중치로 선택해 발동.
    /// </summary>
    private void TryFireNext()
    {
        if (_config?.patternEntries == null || _config.patternEntries.Count == 0) return;

        // ⓪ 강제 항목(forceExecute) 우선 — 영혼 소환처럼 HP 게이트를 여는 패턴은 조건이 되면 바로 낸다.
        //    가중치 추첨에 맡기면 50%에서 체력바가 멈춘 채 0.4~30초씩 밀렸다(09-25·26 보스 시뮬 실측).
        foreach (var entry in _config.patternEntries)
        {
            if (entry == null || !entry.forceExecute || entry.patterns == null || !entry.EvaluateConditions(_ctx)) continue;
            foreach (var p in entry.patterns)
            {
                if (p == null || p is DKComboConfigSO || p.GetRuntimeState() == null || !p.Available(_ctx)) continue;
                _comboQueue.Clear();
                _comboQueue.Enqueue(p);
                FireNextFromQueue();
                return;
            }
        }

        // ① 전체 가중치 집계
        float total = 0f;
        foreach (var entry in _config.patternEntries)
        {
            if (entry?.patterns == null || !entry.EvaluateConditions(_ctx)) continue;
            foreach (var p in entry.patterns)
            {
                if (p == null || !p.Available(_ctx)) continue;
                if (p is DKComboConfigSO c)
                    total += Mathf.Max(0f, c.weight);
                else if (p.GetRuntimeState() != null)
                    total += ApplyRepeatPenalty(p);
            }
        }
        if (total <= 0f) return;

        // ② 가중치 롤
        float roll = UnityEngine.Random.Range(0f, total);
        float acc  = 0f;
        foreach (var entry in _config.patternEntries)
        {
            if (entry?.patterns == null || !entry.EvaluateConditions(_ctx)) continue;
            foreach (var p in entry.patterns)
            {
                if (p == null || !p.Available(_ctx)) continue;

                float w;
                if (p is DKComboConfigSO c)
                    w = Mathf.Max(0f, c.weight);
                else if (p.GetRuntimeState() != null)
                    w = ApplyRepeatPenalty(p);
                else
                    continue;

                acc += w;
                if (roll <= acc)
                {
                    if (p is DKComboConfigSO selectedCombo)
                    {
                        _currentComboUsePhase1Position = selectedCombo.usePhase1Position;
                        BuildComboQueue(selectedCombo.comboCount);
                    }
                    else
                    {
                        // 직접 패턴 — 풀 없이 1회 발동. 다음 추첨에서 같은 패턴이 바로 또 나오지 않게 기록한다
                        // (2페이지 풀은 직접 패턴만 쓴다 — 09-28 규칙 R5 「바로 연속 금지」)
                        _comboQueue.Clear();
                        _comboQueue.Enqueue(p);
                        _lastPickedInCombo = p;
                        // 연계(§9) — 2페이지 후반이면 이어 낼 패턴을 콤보 큐에 붙인다(끝나면 쉬지 않고 바로)
                        if (p.FollowUpActive(_ctx.Ctx.Monster) && p.followUp.GetRuntimeState() != null && p.followUp.CanFollowUp(_ctx)
                            && Time.time - _lastFollowUpTime >= p.followUpCooldown)
                        {
                            _comboQueue.Enqueue(p.followUp);
                            _lastFollowUpTime = Time.time;
                        }
                    }
                    FireNextFromQueue();
                    return;
                }
            }
        }
    }

    // ── 콤보 큐 구성 ────────────────────────────────────────

    private void BuildComboQueue(int count)
    {
        _comboQueue.Clear();
        _lastPickedInCombo = null;
        for (int i = 0; i < count; i++)
        {
            var attack = SelectAttack();
            if (attack != null)
            {
                _comboQueue.Enqueue(attack);
                _lastPickedInCombo = attack;
            }
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
            if (p == null || !p.Available(_ctx)) continue;
            total += ApplyRepeatPenalty(p);
        }

        if (total <= 0f)
        {
            foreach (var p in pool)
                if (p != null && p.Available(_ctx) && p != _lastPickedInCombo) return p;
            foreach (var p in pool)
                if (p != null && p.Available(_ctx)) return p;
            return null;
        }

        float roll = UnityEngine.Random.Range(0f, total);
        float acc  = 0f;
        foreach (var p in pool)
        {
            if (p == null || !p.Available(_ctx)) continue;
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
        Pages?.NotePatternStarted();   // 2페이지 개막 판정(§9)
        _lastFiredAttack = pattern;
        _lastFiredTime   = Time.time;
        IsPatternActive  = true;
        _wasInPattern    = true;
        _onExecuted?.Invoke(pattern);
    }

    private BossPages Pages => (_ctx.Ctx.Monster as IPagedBoss)?.Pages;
    private float _lastFollowUpTime = float.NegativeInfinity;   // 연계 쿨다운(§9, 09-28 반복 방지)

    // ── 반복 패널티 ──────────────────────────────────────────

    private float ApplyRepeatPenalty(BossPatternSO pattern)
    {
        if (pattern == _lastPickedInCombo)
            return 0f;
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
