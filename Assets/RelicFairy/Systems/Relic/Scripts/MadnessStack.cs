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

    private int   _maxStacks  = 50;
    private float _decayDelay = 2f;
    private float _decayRate  = 1f;

    private int   _stacks;
    private float _lastAttackTime = -999f;
    private float _decayAccum;

    // 광란(Frenzy) 상태 — 빈틈 대체
    private bool  _frenzy;
    private float _frenzyEnd;

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

    private void ExitFrenzy()
    {
        _frenzy = false;
        _stacks = 0;               // 게이지 초기화
        _decayAccum = 0f;
        _lastAttackTime = Time.time;
        OnChanged?.Invoke();
        OnFrenzyChanged?.Invoke(false);
    }

    private void Update()
    {
        if (_frenzy)
        {
            if (Time.time >= _frenzyEnd) ExitFrenzy();
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
    public string Label => _frenzy ? "광란!" : "광기 " + _stacks;
    public int Phase => _frenzy ? 5 : (_stacks >= _maxStacks ? 4 : (_stacks >= 31 ? 3 : (_stacks >= 16 ? 2 : (_stacks >= 1 ? 1 : 0))));
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
