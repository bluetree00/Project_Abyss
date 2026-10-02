using System;
using UnityEngine;

/// <summary>
/// 가웨인 정오 게이지(Zenith) — 시간 자동 사이클. <see cref="IRelicResource"/> 구현.
/// 충전(Charging) → 정오(Noon, 스킬 가능) → 쿨다운(Cooldown) → 반복.
/// 수치는 RELIC_STAT_DATA(gawain) 슬롯 구동(서버 권위). 자체 Update로 진행.
///
/// OnChanged는 **구간 전환·각인 임계 교차 시에만** 발행(매 프레임 ✗) — 버프 재적용 비용 최소화.
/// HUD는 Fill을 매 프레임 폴링.
/// </summary>
public sealed class ZenithGauge : MonoBehaviour, IRelicResource
{
    public enum ZPhase { Charging = 0, Noon = 1, Cooldown = 2 }

    private const string RelicKey = "gawain";
    private const int V_CHARGE = 0, V_NOON = 1, V_COOLDOWN = 2, V_MARK_THRESH = 7;

    // 잔열 가속 상한 — 3배속까지. 없으면 학살 구간에서 정오가 끊기지 않고 도는 무한 루프가 된다.
    private const float MaxChargeAccel = 2f;

    private float _chargeTime = 20f, _noonTime = 10f, _cooldownTime = 15f, _markThreshold = 0.8f;

    private ZPhase _phase = ZPhase.Charging;
    private float  _timer;        // 현재 구간 경과
    private float  _chargeAccel;  // 잔열 — 처치로 적립되는 충전 가속(0 = 정속)
    private bool   _markActiveLast;
    private bool   _holdNoon;     // [파츠] 영원한 정오 — true면 정오 구간에 고정(황혼·충전 제거)

    // 유물 성장 v2(10-02) — 하루를 손대는 손잡이. 아무것도 켜지 않으면 기존 순환 그대로다.
    private const float HoldDawnMax = 10f;   // 「새벽이 길다」 — 여명 끝에서 해를 붙드는 최대 시간
    private bool   _holdDawn;     // 여명 공명 4가 켰다
    private bool   _holding;      // 지금 여명 끝에서 붙들고 있다
    private float  _holdTimer;
    private float  _noonExtra;    // 이번 정오에 더해진 초(해시계)
    private bool   _shortNoonPending;   // 「밤이 오지 않는다」 — 황혼 끝에 짧은 정오 예약
    private bool   _shortNoon;          // 지금 정오가 짧은 정오
    private float  _shortNoonTime = 3f;
    private bool   _paused;       // 일식 — 해의 시간이 멈춘다

    // 라벨 캐시 — 표시값이 바뀔 때만 문자열을 새로 만든다.
    private string _label = "여명";
    private int    _labelKey = int.MinValue;

    public event Action OnChanged;

    /// <summary>구간이 바뀌었다(이전, 새). 유물 성장 v2 허브가 시간대 사건(여명 · 정오 · 정오 끝 · 황혼)으로 쓴다.</summary>
    public event Action<ZPhase, ZPhase> PhaseChanged;

    // ── 타입 접근(유물 로직용) ──
    public ZPhase CurrentPhase => _phase;
    public bool IsNoon => _phase == ZPhase.Noon;
    /// <summary>충전 구간 게이지 0~1.</summary>
    public float ChargeFill => _phase == ZPhase.Charging
        ? Mathf.Clamp01(_timer / Mathf.Max(0.01f, _chargeTime))
        : (_phase == ZPhase.Noon ? 1f : 0f);
    /// <summary>각인 발동(충전 중 게이지 임계 이상).</summary>
    public bool IsMarkReady => _phase == ZPhase.Charging && ChargeFill >= _markThreshold;

    public void Initialize()
    {
        var m = Managers.RelicStatData;
        if (m != null)
        {
            _chargeTime    = m.Get(RelicKey, V_CHARGE,      _chargeTime);
            _noonTime      = m.Get(RelicKey, V_NOON,        _noonTime);
            _cooldownTime  = m.Get(RelicKey, V_COOLDOWN,    _cooldownTime);
            _markThreshold = m.Get(RelicKey, V_MARK_THRESH, _markThreshold);
        }
        _phase = ZPhase.Charging; _timer = 0f; _chargeAccel = 0f; _markActiveLast = false;
        OnChanged?.Invoke();
    }

    private void Update()
    {
        // 해는 던전 안에서만 돈다. 예전엔 무조건 흘러서 베이스캠프에 서 있기만 해도
        // 정오가 왔다 갔다 하며 실제 전투에 쓸 사이클을 허공에 버렸다.
        // (차단형 팝업 구간은 timeScale=0이라 deltaTime이 0 → 별도 처리 불필요)
        if (!IsRunActive) return;

        // [파츠] 영원한 정오 — 정오에 고정하고 사이클을 멈춘다.
        if (_holdNoon)
        {
            if (_phase != ZPhase.Noon) { var old = _phase; _phase = ZPhase.Noon; _timer = 0f; _markActiveLast = false; OnChanged?.Invoke(); PhaseChanged?.Invoke(old, _phase); }
            else _timer = 0f;
            return;
        }

        // [v2 일식] 해의 시간이 멈춘다
        if (_paused) return;

        // [v2 새벽이 길다] 여명 끝에서 해를 붙든다 — 낙일로 정오를 열거나, 최대 시간이 지나면 저절로 열린다
        if (_holding)
        {
            _holdTimer += Time.deltaTime;
            if (_holdTimer < HoldDawnMax) return;
            _holding = false;
            _timer = 0f; Advance(); OnChanged?.Invoke();
            return;
        }

        float accel = _phase == ZPhase.Charging ? _chargeAccel : 0f;
        _timer += Time.deltaTime * (1f + accel);

        bool changed = false;
        if (_timer >= CurrentDuration())
        {
            if (_phase == ZPhase.Charging && _holdDawn)
            {
                _holding = true; _holdTimer = 0f; _timer = _chargeTime;
                OnChanged?.Invoke();
                return;
            }
            _timer = 0f; Advance(); changed = true;
        }

        bool markNow = IsMarkReady;
        if (markNow != _markActiveLast) { _markActiveLast = markNow; changed = true; }

        if (changed) OnChanged?.Invoke();
    }

    /// <summary>런(던전)이 실제로 진행 중인가. 허브·런 종료 구간에서는 사이클을 멈춘다.</summary>
    private static bool IsRunActive
        => GameRunBootstrapper.Instance?.Run?.IsRunning ?? false;

    private float CurrentDuration() => _phase switch
    {
        ZPhase.Charging => _chargeTime,
        ZPhase.Noon     => (_shortNoon ? _shortNoonTime : _noonTime) + _noonExtra,
        _               => _cooldownTime,
    };

    private void Advance()
    {
        var old = _phase;
        bool wasShort = _shortNoon;
        _shortNoon = false;
        _phase = _phase switch
        {
            ZPhase.Charging => ZPhase.Noon,
            ZPhase.Noon     => wasShort ? ZPhase.Charging : ZPhase.Cooldown,   // 짧은 정오 뒤엔 황혼 없이 새벽
            _               => _shortNoonPending ? ZPhase.Noon : ZPhase.Charging,
        };
        if (old == ZPhase.Cooldown && _phase == ZPhase.Noon) { _shortNoon = true; _shortNoonPending = false; }
        if (_phase == ZPhase.Noon) _noonExtra = 0f;

        // ⚠️ 예전엔 여기서 Charging 진입 시 _chargeAccel을 0으로 지웠다.
        //    가속은 <b>충전 구간에서만</b> 쓰이는데 충전에 들어가는 순간 지워버렸으니,
        //    황혼에서 아무리 처치해도 단 1%도 반영되지 않았다 — 잔열 패시브가 100% 무효였다.
        //    가속은 '해를 앞당기는' 적립이므로 정오에 도달했을 때(=보상을 받았을 때) 소진한다.
        if (_phase == ZPhase.Noon) _chargeAccel = 0f;
        PhaseChanged?.Invoke(old, _phase);
    }

    // ── 유물 성장 v2 손잡이 ─────────────────────────────────

    /// <summary>「새벽이 길다」(여명 공명 4) — 여명 끝에서 해를 붙든다. 끄면 붙들고 있던 해를 바로 놓는다.</summary>
    public bool HoldDawn
    {
        get => _holdDawn;
        set
        {
            _holdDawn = value;
            if (!value && _holding) { _holding = false; _timer = 0f; Advance(); OnChanged?.Invoke(); }
        }
    }

    /// <summary>지금 여명 끝에서 해를 붙들고 있는가(낙일 버튼이 정오를 연다).</summary>
    public bool IsHoldingDawn => _holding;

    /// <summary>여명이면 즉시 정오를 연다(붙듦 해제 · 여명의 맹세 ③ 축열 가득). 그 밖엔 아무것도 안 한다.</summary>
    public void OpenNoonNow()
    {
        if (_phase != ZPhase.Charging) return;
        _holding = false;
        _timer = 0f; Advance(); OnChanged?.Invoke();
    }

    /// <summary>정오면 이번 정오를 늘린다(해시계).</summary>
    public void ExtendNoon(float seconds)
    {
        if (_phase != ZPhase.Noon || seconds <= 0f) return;
        _noonExtra += seconds;
        OnChanged?.Invoke();
    }

    /// <summary>해를 앞당긴다 — 여명 · 황혼의 남은 시간을 줄인다(아침 사냥 · 저무는 해 ③ · 노을로). 끝나면 다음 프레임에 넘어간다.</summary>
    public void AdvanceTime(float seconds)
    {
        if (seconds <= 0f || _holding) return;
        if (_phase == ZPhase.Charging || _phase == ZPhase.Cooldown)
            _timer = Mathf.Min(_timer + seconds, CurrentDuration());
    }

    /// <summary>「밤이 오지 않는다」(황혼 공명 4) — 이번 황혼이 끝나면 짧은 정오를 한 번 더.</summary>
    public void QueueShortNoon(float seconds)
    {
        _shortNoonPending = true;
        _shortNoonTime = Mathf.Max(0.5f, seconds);
    }

    /// <summary>지금 정오가 「밤이 오지 않는다」의 짧은 정오인가.</summary>
    public bool IsShortNoon => _phase == ZPhase.Noon && _shortNoon;

    /// <summary>일식 — 해의 시간이 멈춘다.</summary>
    public bool Paused { get => _paused; set => _paused = value; }

    /// <summary>구간 기본 길이(초) — HUD 해시계 호의 크기. 정오는 늘어난 몫 · 짧은 정오를 뺀 기본 길이.</summary>
    public float DurationOf(ZPhase p) => p switch
    {
        ZPhase.Charging => _chargeTime,
        ZPhase.Noon     => _noonTime,
        _               => _cooldownTime,
    };

    /// <summary>지금 구간의 진행 0~1(붙듦 중엔 1) — HUD 해시계 바늘.</summary>
    public float PhaseProgress01 => _holding ? 1f : Mathf.Clamp01(_timer / Mathf.Max(0.01f, CurrentDuration()));

    /// <summary>지금 구간의 남은 초(붙듦 중엔 0).</summary>
    public float PhaseRemaining => _holding ? 0f : Mathf.Max(0f, CurrentDuration() - _timer);

    /// <summary>
    /// 잔열 — 처치로 <b>다음 해를 앞당긴다</b>. 황혼·충전 중 적립되고, 충전 속도에 곱해진다.
    /// (게이지를 '채우는' 게 아니라 시간을 '당기는' 것 — 스택 누적형 유물과 구조가 다르다.)
    /// </summary>
    public void AddChargeAccel(float pct)
    {
        if (_phase == ZPhase.Cooldown || _phase == ZPhase.Charging)
            _chargeAccel = Mathf.Min(_chargeAccel + Mathf.Max(0f, pct), MaxChargeAccel);
    }

    /// <summary>현재 충전 가속(0 = 정속, 1 = 2배속). HUD/디버그 표시용.</summary>
    public float ChargeAccel => _chargeAccel;

    /// <summary>[파츠] 영원한 정오가 정오를 붙잡고 있는가.</summary>
    public bool IsHoldNoon => _holdNoon;

    /// <summary>한 주기(여명 + 정오 + 황혼) 초 — 정오 고정 중 낙일(Q) 재사용 간격.</summary>
    public float CycleSeconds => _chargeTime + _noonTime + _cooldownTime;

    /// <summary>[파츠] 영원한 정오 — on이면 즉시 정오로 진입해 그 구간에서 벗어나지 않는다(황혼·충전 제거).</summary>
    public void SetHoldNoon(bool on)
    {
        if (_holdNoon == on) return;
        _holdNoon = on;
        var old = _phase;
        if (on) { _phase = ZPhase.Noon; _timer = 0f; _markActiveLast = false; }
        OnChanged?.Invoke();
        if (old != _phase) PhaseChanged?.Invoke(old, _phase);
    }

    // ── IRelicResource ──
    public float Fill => _phase switch
    {
        ZPhase.Charging => ChargeFill,
        ZPhase.Noon     => 1f - Mathf.Clamp01(_timer / Mathf.Max(0.01f, CurrentDuration())),
        _               => Mathf.Clamp01(_timer / Mathf.Max(0.01f, _cooldownTime)),
    };
    /// <summary>
    /// 게이지 라벨. 잔열 가속이 붙어 있으면 배속을 함께 보여준다 —
    /// 처치가 해를 앞당긴다는 사실이 화면에 보이지 않으면 플레이어는 그런 게 있는지도 모른다.
    /// 표시값이 바뀔 때만 문자열을 새로 만든다(매 프레임 alloc 방지).
    /// </summary>
    public string Label
    {
        get
        {
            int key = (int)_phase * 100 + Mathf.RoundToInt(_chargeAccel * 10f);
            if (key != _labelKey)
            {
                _labelKey = key;
                _label = _phase switch
                {
                    ZPhase.Noon     => "정오!",
                    ZPhase.Charging => _chargeAccel > 0.01f ? $"여명 ×{1f + _chargeAccel:0.0}" : "여명",
                    _               => _chargeAccel > 0.01f ? $"황혼 ×{1f + _chargeAccel:0.0}" : "황혼",
                };
            }
            return _label;
        }
    }
    public int Phase => (int)_phase;
    public RelicGaugeStyle Style => RelicGaugeStyle.Sun;   // 정오 태양 위젯(열림/닫힘 + 내부 감소)
    public Color BarColor => _phase switch
    {
        ZPhase.Noon     => new Color(1.00f, 0.85f, 0.25f),                                   // 정오 — 밝은 금
        ZPhase.Charging => IsMarkReady ? new Color(1.00f, 0.62f, 0.18f)                      // 각인 — 주황금
                                       : new Color(0.72f, 0.58f, 0.24f),                     // 충전 — 어두운 금
        _               => new Color(0.42f, 0.42f, 0.48f),                                   // 황혼 — 회색
    };
    public bool IsSkillReady => _phase == ZPhase.Noon;

    public void Tick(float deltaTime) { }          // 시간형: 자체 Update로 진행
    public void OnAttackLanded(GameObject target) { }
    public void OnKill(GameObject target) { }       // 잔열 가속은 relic이 황혼 판정 후 AddChargeAccel 호출

    public RelicResourceState Capture() => new RelicResourceState { fill = _timer, phase = (int)_phase, aux = _chargeAccel };
    public void Restore(RelicResourceState s)
    {
        _phase = (ZPhase)s.phase; _timer = s.fill; _chargeAccel = s.aux; _markActiveLast = IsMarkReady;
        OnChanged?.Invoke();
    }

    public void ApplyConfig(RelicResourceConfig config)
    {
        if (config == null) return;
        _chargeTime   = config.Get("charge_time",   _chargeTime);
        _noonTime     = config.Get("noon_time",     _noonTime);
        _cooldownTime = config.Get("cooldown_time", _cooldownTime);
    }
}
