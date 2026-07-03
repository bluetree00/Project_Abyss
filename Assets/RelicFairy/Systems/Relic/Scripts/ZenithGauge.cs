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

    private float _chargeTime = 20f, _noonTime = 10f, _cooldownTime = 15f, _markThreshold = 0.8f;

    private ZPhase _phase = ZPhase.Charging;
    private float  _timer;        // 현재 구간 경과
    private float  _chargeAccel;  // 잔열(황혼 처치) 충전 가속 — 후속
    private bool   _markActiveLast;

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
        float accel = _phase == ZPhase.Charging ? _chargeAccel : 0f;
        _timer += Time.deltaTime * (1f + accel);

        bool changed = false;
        if (_timer >= CurrentDuration()) { _timer = 0f; Advance(); changed = true; }

        bool markNow = IsMarkReady;
        if (markNow != _markActiveLast) { _markActiveLast = markNow; changed = true; }

        if (changed) OnChanged?.Invoke();
    }

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
        if (_phase == ZPhase.Charging) _chargeAccel = 0f;
    }

    /// <summary>잔열: 황혼/충전 중 다음 충전 가속(처치 보상). 후속 패시브에서 호출.</summary>
    public void AddChargeAccel(float pct)
    {
        if (_phase == ZPhase.Cooldown || _phase == ZPhase.Charging)
            _chargeAccel += Mathf.Max(0f, pct);
    }

    // ── IRelicResource ──
    public float Fill => _phase switch
    {
        ZPhase.Charging => ChargeFill,
        ZPhase.Noon     => 1f - Mathf.Clamp01(_timer / Mathf.Max(0.01f, _noonTime)),
        _               => Mathf.Clamp01(_timer / Mathf.Max(0.01f, _cooldownTime)),
    };
    public string Label => _phase switch { ZPhase.Charging => "정오 충전", ZPhase.Noon => "정오!", _ => "황혼" };
    public int Phase => (int)_phase;
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
