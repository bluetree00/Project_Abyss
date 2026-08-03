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

    // 라벨 캐시 — 표시값이 바뀔 때만 문자열을 새로 만든다.
    private string _label = "여명";
    private int    _labelKey = int.MinValue;

    public event Action OnChanged;

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
            if (_phase != ZPhase.Noon) { _phase = ZPhase.Noon; _timer = 0f; _markActiveLast = false; OnChanged?.Invoke(); }
            else _timer = 0f;
            return;
        }

        float accel = _phase == ZPhase.Charging ? _chargeAccel : 0f;
        _timer += Time.deltaTime * (1f + accel);

        bool changed = false;
        if (_timer >= CurrentDuration()) { _timer = 0f; Advance(); changed = true; }

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
        ZPhase.Noon     => _noonTime,
        _               => _cooldownTime,
    };

    private void Advance()
    {
        _phase = _phase switch
        {
            ZPhase.Charging => ZPhase.Noon,
            ZPhase.Noon     => ZPhase.Cooldown,
            _               => ZPhase.Charging,
        };

        // ⚠️ 예전엔 여기서 Charging 진입 시 _chargeAccel을 0으로 지웠다.
        //    가속은 <b>충전 구간에서만</b> 쓰이는데 충전에 들어가는 순간 지워버렸으니,
        //    황혼에서 아무리 처치해도 단 1%도 반영되지 않았다 — 잔열 패시브가 100% 무효였다.
        //    가속은 '해를 앞당기는' 적립이므로 정오에 도달했을 때(=보상을 받았을 때) 소진한다.
        if (_phase == ZPhase.Noon) _chargeAccel = 0f;
    }

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

    /// <summary>[파츠] 영원한 정오 — on이면 즉시 정오로 진입해 그 구간에서 벗어나지 않는다(황혼·충전 제거).</summary>
    public void SetHoldNoon(bool on)
    {
        if (_holdNoon == on) return;
        _holdNoon = on;
        if (on) { _phase = ZPhase.Noon; _timer = 0f; _markActiveLast = false; }
        OnChanged?.Invoke();
    }

    // ── IRelicResource ──
    public float Fill => _phase switch
    {
        ZPhase.Charging => ChargeFill,
        ZPhase.Noon     => 1f - Mathf.Clamp01(_timer / Mathf.Max(0.01f, _noonTime)),
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
