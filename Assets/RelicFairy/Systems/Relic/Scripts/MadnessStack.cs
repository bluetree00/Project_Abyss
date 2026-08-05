using System;
using UnityEngine;

/// <summary>
/// 랜슬롯 광기(Madness) 스택 — 적중형 리소스. <see cref="IRelicResource"/> 구현.
/// 적중마다 +1(MAX), 일정 시간 미공격 시 초당 감쇠, MAX 도달 시 OnMaxReached.
/// 리워크: 빈틈(Falter) 제거 → MAX 도달 시 <b>광란(Frenzy) 상태</b> 진입(강화 구간, 심판 수동 활성),
/// 광란 종료 시 게이지 0 초기화. 무한 MAX 없음.
/// 스택 추가는 LancelotMadnessPassive(OnAttackHit)가 호출, 감쇠·광란 타이머는 자체 Update.
/// 수치는 RELIC_STAT_DATA(lancelot) 슬롯 구동.
/// </summary>
public sealed class MadnessStack : MonoBehaviour, IRelicResource
{
    private const string RelicKey = "lancelot";
    private const int V_MAX = 0, V_DECAY_DELAY = 1, V_DECAY_RATE = 2;

    // 서버(RELIC_STAT_DATA lancelot slot 0) 미도달 시 폴백. CSV와 같은 값으로 맞춰둔다
    // — 어긋나 있으면 CDN 업로드 전후로 광란 빈도가 통째로 달라져 밸런싱이 헛돈다.
    private int   _maxStacks  = 40;
    private float _decayDelay = 2f;
    private float _decayRate  = 1f;

    private int   _stacks;
    private float _lastAttackTime = -999f;
    private float _decayAccum;

    // 광란(Frenzy) 상태 — 빈틈 대체
    private bool  _frenzy;
    private float _frenzyEnd;
    private float _retainRatio;   // [파츠] 식지 않는 광기 — 광란 종료 후 남길 스택 비율(0 = 전부 초기화)

    // [파츠] 끝나지 않는 광란 — 광란 1회당 한 번 자동 재점화(스택 MAX 유지 → 공격 보너스 보존).
    private bool  _endlessFrenzy;
    private bool  _endlessUsed;         // 이번 광란에서 재점화를 이미 썼는가(무한 방지)
    private float _lastFrenzyDuration = 6f;

    // 라벨 캐시 — 표시값이 바뀔 때만 문자열을 새로 만든다(매 프레임 alloc 방지).
    private string _label = "광기 0%";
    private int    _labelKey = int.MinValue;

    public event Action OnChanged;
    public event Action OnMaxReached;
    /// <summary>광란 진입/종료 시 발행(VFX 상태 토글용).</summary>
    public event Action<bool> OnFrenzyChanged;

    public int   Stacks    => _stacks;
    public int   MaxStacks => _maxStacks;
    /// <summary>0~1 스택 비율(스탯 곡선용).</summary>
    public float Ratio => _maxStacks > 0 ? (float)_stacks / _maxStacks : 0f;

    public bool  IsFrenzy        => _frenzy;
    public float FrenzyRemaining => _frenzy ? Mathf.Max(0f, _frenzyEnd - Time.time) : 0f;

    public void Initialize()
    {
        var m = Managers.RelicStatData;
        if (m != null)
        {
            _maxStacks  = Mathf.Max(1, m.GetInt(RelicKey, V_MAX, _maxStacks));
            _decayDelay = m.Get(RelicKey, V_DECAY_DELAY, _decayDelay);
            _decayRate  = Mathf.Max(0.01f, m.Get(RelicKey, V_DECAY_RATE, _decayRate));
        }
        _stacks = 0; _lastAttackTime = -999f; _decayAccum = 0f; _frenzy = false;
        OnChanged?.Invoke();
    }

    public void AddStack(int n)
    {
        if (_frenzy) return;         // 광란 중엔 스택 동결(MAX 유지)
        _lastAttackTime = Time.time; // 적중 시 감쇠 리셋
        if (_stacks >= _maxStacks) return;
        _stacks = Mathf.Min(_maxStacks, _stacks + Mathf.Max(1, n));
        OnChanged?.Invoke();
        if (_stacks >= _maxStacks) OnMaxReached?.Invoke();
    }

    /// <summary>스택 직접 설정(디버그/복원).</summary>
    public void SetStacks(int to)
    {
        _stacks = Mathf.Clamp(to, 0, _maxStacks);
        _decayAccum = 0f;
        OnChanged?.Invoke();
    }

    /// <summary>MAX 도달 시 relic이 호출 — 광란 상태 duration초 진입.</summary>
    public void EnterFrenzy(float duration)
    {
        _frenzy    = true;
        _frenzyEnd = Time.time + Mathf.Max(0.1f, duration);
        _lastFrenzyDuration = Mathf.Max(0.1f, duration);
        _endlessUsed = false;          // 새 광란 진입 → 재점화 1회 재충전
        _decayAccum = 0f;
        OnChanged?.Invoke();
        OnFrenzyChanged?.Invoke(true);
    }

    /// <summary>광란 중 처치 등 — 지속 소폭 연장.</summary>
    public void ExtendFrenzy(float add)
    {
        if (!_frenzy) return;
        _frenzyEnd += Mathf.Max(0f, add);
        OnChanged?.Invoke();
    }

    /// <summary>[파츠] 식지 않는 광기 — 광란 종료 시 남길 스택 비율(0~1). 0이면 기존대로 전부 초기화.</summary>
    public void SetRetainRatio(float ratio) => _retainRatio = Mathf.Clamp01(ratio);

    /// <summary>[파츠] 끝나지 않는 광란 — on이면 광란이 끝날 때 1회 자동 재점화(지속을 한 번 더 연장).</summary>
    public void SetEndlessFrenzy(bool on) => _endlessFrenzy = on;

    private void ExitFrenzy()
    {
        _frenzy = false;
        _stacks = Mathf.Clamp(Mathf.RoundToInt(_maxStacks * _retainRatio), 0, _maxStacks);  // 파츠 시 절반 유지
        _decayAccum = 0f;
        _lastAttackTime = Time.time;
        OnChanged?.Invoke();
        OnFrenzyChanged?.Invoke(false);
    }

    private void Update()
    {
        if (_frenzy)
        {
            if (Time.time >= _frenzyEnd)
            {
                // [파츠] 끝나지 않는 광란 — 종료 직전 1회 자동 재점화(스택·상태 유지, 종료 이벤트 없음).
                if (_endlessFrenzy && !_endlessUsed)
                {
                    _endlessUsed = true;
                    _frenzyEnd = Time.time + _lastFrenzyDuration;
                    OnChanged?.Invoke();
                }
                else ExitFrenzy();
            }
            return;                 // 광란 중 감쇠 없음
        }
        if (_stacks <= 0) return;
        if (Time.time - _lastAttackTime < _decayDelay) return;

        _decayAccum += Time.deltaTime * _decayRate;
        if (_decayAccum >= 1f)
        {
            int dec = Mathf.FloorToInt(_decayAccum);
            _decayAccum -= dec;
            _stacks = Mathf.Max(0, _stacks - dec);
            OnChanged?.Invoke();
        }
    }

    // ── IRelicResource ──
    public float Fill => _frenzy ? 1f : Ratio;

    /// <summary>
    /// 게이지 라벨 — 평시엔 충전률(%), 광란 중엔 남은 시간.
    /// 매 프레임 문자열을 새로 만들면 GC가 쌓이므로 표시값이 바뀔 때만 다시 만든다
    /// (광란 잔여는 0.1초 단위로만 갱신 → 초당 10회).
    /// </summary>
    public string Label
    {
        get
        {
            int key = _frenzy ? -Mathf.CeilToInt(FrenzyRemaining * 10f) : _stacks;
            if (key != _labelKey)
            {
                _labelKey = key;
                _label = _frenzy
                    ? $"광란 {FrenzyRemaining:0.0}초"
                    : $"광기 {Mathf.RoundToInt(Ratio * 100f)}%";
            }
            return _label;
        }
    }

    // 단계는 MAX 대비 <b>비율</b>로 판정한다. 고정값(구 16/31)은 MAX가 50이던 시절 기준이라,
    // MAX를 25로 내리면 3·4단계에 영영 도달하지 못해 바 연출이 죽는다.
    public int Phase => _frenzy               ? 5
                      : _stacks >= _maxStacks ? 4
                      : Ratio   >= 0.6f       ? 3
                      : Ratio   >= 0.3f       ? 2
                      : _stacks >= 1          ? 1
                                              : 0;
    public RelicGaugeStyle Style => RelicGaugeStyle.Bar;   // 수평 광기 게이지
    public Color BarColor => _frenzy
        ? new Color(0.78f, 0.30f, 0.98f)                                                    // 광란 — 보라
        : Color.Lerp(new Color(0.55f, 0.22f, 0.20f), new Color(0.96f, 0.20f, 0.16f), Ratio); // 광기 — 붉은 강도
    public bool IsSkillReady => _frenzy;                    // 심판은 광란 중 수동 발동

    public void Tick(float deltaTime) { }                  // 감쇠·광란은 자체 Update
    public void OnAttackLanded(GameObject target) => AddStack(1);
    public void OnKill(GameObject target) { }

    public RelicResourceState Capture() => new RelicResourceState { fill = _stacks, phase = Phase, aux = FrenzyRemaining };
    public void Restore(RelicResourceState s)
    {
        _stacks = Mathf.Clamp((int)s.fill, 0, _maxStacks);
        if (s.aux > 0f) EnterFrenzy(s.aux); else { _frenzy = false; OnChanged?.Invoke(); }
    }
    public void ApplyConfig(RelicResourceConfig config)
    {
        if (config == null) return;
        _maxStacks = Mathf.Max(1, Mathf.RoundToInt(config.Get("max_stacks", _maxStacks)));
    }
}
