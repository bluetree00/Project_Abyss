using System;
using UnityEngine;

/// <summary>
/// 서약 원인(조건절) 상태기계 — 연격 · 학살 · 전환 · 개선 · 심장박동 · 연주 · 사냥 개시 · 선제 · 포위 · 행군.
/// 조건이 맞으면 <c>fire(대상)</c>을 부른다. 조립 서약과 서약서(문장)가 같은 추적기를 쓴다(10-02 「한 장의 서약서」 E1 —
/// 예전엔 <see cref="AssembledCovenant"/> 안에 효과 실행과 한데 있었다).
/// <para>재진입 가드(효과 적용 중 들어온 사건 무시)는 주인이 쥔다 — 주인이 가드 밖에서만 이 추적기에 사건을 넘긴다.</para>
/// </summary>
public sealed class CovenantCauseTracker
{
    // 근접 판정 상수(포위)
    private const float ProximityRadius        = 5f;
    private const float ProximityCheckInterval = 0.5f;

    // ── 전투 게이트 ───────────────────────────────────────
    // 상시 원인(심장박동·행군)이 적을 요구하는 반경. 전투 중인 방은 덮되 클리어된 방·복도·상점은 벗어난다.
    private const float CombatGateRadius   = 18f;
    // 마지막으로 적을 본 뒤 게이트가 열린 채 남는 시간(초). 카이팅·처치 직후 공백으로 발동이 끊기지 않게.
    private const float CombatGateGrace    = 4f;
    // 게이트 판정 주기(초) — 매 프레임 OverlapSphere를 새로 돌 이유가 없다.
    private const float CombatGateInterval = 0.5f;

    private readonly CovenantPalette.CauseDef _cause;
    private readonly ICovenantClauseHost      _host;
    private readonly Action<GameObject>       _fire;
    private readonly Action<int>              _onProximitySample;

    // 원인 런타임 상태
    private GameObject _streakTarget;
    private int   _streak;
    private int   _killStreak;
    private float _swapWindowEnd;
    private float _periodicTimer;
    private float _killHitWindowEnd;   // 사냥 개시
    private bool  _firstHitArmed;      // 선제
    private float _proximityTimer, _proximityCd;   // 포위
    private Vector3 _lastPos; private bool _movePrimed; private float _moveAccum;   // 행군
    private float _gateTimer, _gateOpenUntil;   // 전투 게이트

    /// <param name="fire">조건이 맞은 순간 — 대상형 원인은 그 적, 범위형은 null.</param>
    /// <param name="onProximitySample">포위 원인이 인접 수를 잴 때마다(「격노」 위험 변형이 포위가 풀리면 증폭을 끈다).</param>
    public CovenantCauseTracker(in CovenantPalette.CauseDef cause, ICovenantClauseHost host,
                                Action<GameObject> fire, Action<int> onProximitySample = null)
    {
        _cause = cause;
        _host  = host;
        _fire  = fire;
        _onProximitySample = onProximitySample;
    }

    /// <summary>전투 게이트가 열려 있는 시각(실측 도구가 읽는다).</summary>
    public float GateOpenUntil => _gateOpenUntil;

    public void OnAttackHit(GameObject target)
    {
        switch (_cause.trigger)
        {
            case CauseTriggerKind.OnHitStreakSameTarget:
                if (target == _streakTarget) _streak++;
                else { _streakTarget = target; _streak = 1; }
                if (_streak >= _cause.thresholdInt) { _streak = 0; _fire(target); }
                break;
            case CauseTriggerKind.OnWeaponSwapWindow:
                if (Time.time <= _swapWindowEnd) { _swapWindowEnd = 0f; _fire(target); }
                break;
            case CauseTriggerKind.OnKillThenHitWindow:
                if (Time.time <= _killHitWindowEnd) { _killHitWindowEnd = 0f; _fire(target); }
                break;
            case CauseTriggerKind.OnFirstHitInRoom:
                if (_firstHitArmed) { _firstHitArmed = false; _fire(target); }
                break;
        }
    }

    public void OnKill(GameObject target)
    {
        switch (_cause.trigger)
        {
            case CauseTriggerKind.OnKillStreak:
                _killStreak++;
                if (_killStreak >= _cause.thresholdInt) { _killStreak = 0; _fire(target); }
                break;
            case CauseTriggerKind.OnKillThenHitWindow:
                _killHitWindowEnd = Time.time + _cause.thresholdF;
                break;
        }
    }

    public void OnWeaponSwap()
    {
        if (_cause.trigger == CauseTriggerKind.OnWeaponSwapWindow)
            _swapWindowEnd = Time.time + _cause.thresholdF;
    }

    public void OnRoomEnter()
    {
        if (_cause.trigger == CauseTriggerKind.OnFirstHitInRoom) _firstHitArmed = true;
    }

    public void OnRoomClear()
    {
        if (_cause.trigger == CauseTriggerKind.OnRoomClear) _fire(null);
    }

    public void OnSkillUse()
    {
        if (_cause.trigger == CauseTriggerKind.OnSkillUse) _fire(null);
    }

    public void Tick(float deltaTime)
    {
        switch (_cause.trigger)
        {
            case CauseTriggerKind.Periodic:
                if (!CombatGateOpen(deltaTime)) break;
                _periodicTimer += deltaTime;
                if (_periodicTimer >= _cause.thresholdF) { _periodicTimer = 0f; _fire(null); }
                break;
            case CauseTriggerKind.OnProximity:
                _proximityTimer += deltaTime;
                if (_proximityTimer >= ProximityCheckInterval)
                {
                    _proximityTimer = 0f;
                    int near = CountNearbyEnemies(ProximityRadius);
                    _onProximitySample?.Invoke(near);

                    if (Time.time >= _proximityCd && near >= _cause.thresholdInt)
                    {
                        _proximityCd = Time.time + _cause.thresholdF;
                        _fire(null);
                    }
                }
                break;
            case CauseTriggerKind.OnMoveDistance:
                // 전투 밖에서 걸은 거리는 세지 않는다 — 누적만 막고 재진입 시 다시 재면
                // 복도를 지나온 것만으로 방에 들어서자마자 한 번 터지는 일이 생긴다.
                if (!CombatGateOpen(deltaTime)) { _movePrimed = false; _moveAccum = 0f; break; }
                if (_host.Context?.Player != null)
                {
                    Vector3 p = _host.PlayerPosition;
                    if (!_movePrimed) { _lastPos = p; _movePrimed = true; }
                    else
                    {
                        _moveAccum += Vector3.Distance(p, _lastPos);
                        _lastPos = p;
                        if (_moveAccum >= _cause.thresholdF) { _moveAccum = 0f; _fire(null); }
                    }
                }
                break;
        }
    }

    /// <summary>
    /// 상시 원인(심장박동·행군)은 <b>근처에 산 적이 있을 때만</b> 굴린다.
    ///
    /// 이 둘은 전투와 무관하게 흐르는 원인이라, <b>적이 없어도 발동이 성립하는 효과</b>
    /// (격노·박차·보호막·성역·결계·황금비)와 물리면 빈 방을 걸어다니거나 가만히 서 있는 것만으로
    /// 가동률이 100%가 된다 — 걷기 5m/s면 행군 12m는 2.4초, 심장박동은 4초 주기라
    /// 효과의 지속(4~6초)도 icd(3초)도 전부 넘겨버린다. 골드처럼 상한 없이 누적되는 효과라면
    /// 그대로 무한 수급이 된다.
    ///
    /// 적을 요구하는 효과(초신성·처형·화상…)는 대상 해석이 null을 돌려줘 이미 막히지만,
    /// 자기완결 효과는 막을 곳이 여기뿐이다. 짝을 하나씩 봉인하는 것과 달리 이 게이트는
    /// 원인·효과가 늘어나도 같이 따라온다.
    ///
    /// 유예(<see cref="CombatGateGrace"/>)를 두는 이유: 거리를 벌리는 카이팅이나 마지막 한 마리를
    /// 잡은 직후의 공백에서 발동이 뚝 끊기면 전투 중 조합까지 못 쓰게 된다.
    /// </summary>
    private bool CombatGateOpen(float deltaTime)
    {
        _gateTimer += deltaTime;
        if (_gateTimer >= CombatGateInterval)
        {
            _gateTimer = 0f;
            if (CountNearbyEnemies(CombatGateRadius) > 0)
                _gateOpenUntil = Time.time + CombatGateGrace;
        }
        return Time.time < _gateOpenUntil;
    }

    private int CountNearbyEnemies(float radius)
        => _host.Context?.Player == null ? 0 : CovenantQuery.CountLiveEnemies(_host.PlayerPosition, radius);
}
