using System;
using UnityEngine;

/// <summary>
/// 랜슬롯 광기(Madness) 스택 — 적중형 리소스. <see cref="IRelicResource"/> 구현.
/// 적중마다 +1(MAX), 일정 시간 미공격 시 초당 감쇠, MAX 도달 시 OnMaxReached(자동 발동).
/// 스택 추가는 LancelotMadnessPassive(OnAttackHit)가 호출, 감쇠는 자체 Update.
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
    // 배신의 대가 빈틈
    private float _falterEnd;
    private int   _falterRestart;
    private bool  _faltering;

    public event Action OnChanged;
    public event Action OnMaxReached;

    public int Stacks    => _stacks;
    public int MaxStacks => _maxStacks;
    /// <summary>0~1 스택 비율(스탯 곡선용).</summary>
    public float Ratio => _maxStacks > 0 ? (float)_stacks / _maxStacks : 0f;

    public void Initialize()
    {
        var m = Managers.RelicStatData;
        if (m != null)
        {
            _maxStacks  = Mathf.Max(1, m.GetInt(RelicKey, V_MAX, _maxStacks));
            _decayDelay = m.Get(RelicKey, V_DECAY_DELAY, _decayDelay);
            _decayRate  = Mathf.Max(0.01f, m.Get(RelicKey, V_DECAY_RATE, _decayRate));
        }
        _stacks = 0; _lastAttackTime = -999f; _decayAccum = 0f;
        OnChanged?.Invoke();
    }

    public void AddStack(int n)
    {
        if (_faltering) return;      // 빈틈 중엔 스택 미적립(종료 후 재시작)
        _lastAttackTime = Time.time; // 적중 시 감쇠 리셋
        if (_stacks >= _maxStacks) return;
        _stacks = Mathf.Min(_maxStacks, _stacks + Mathf.Max(1, n));
        OnChanged?.Invoke();
        if (_stacks >= _maxStacks) OnMaxReached?.Invoke();
    }

    /// <summary>심판 일격 후/빈틈 종료 등 — 스택 설정.</summary>
    public void SetStacks(int to)
    {
        _stacks = Mathf.Clamp(to, 0, _maxStacks);
        _decayAccum = 0f;
        OnChanged?.Invoke();
    }

    public bool IsFaltering => _faltering;

    /// <summary>심판 일격 후 빈틈 진입 — 스택 소비, duration초 후 restartStacks로 재시작.</summary>
    public void StartFalter(float duration, int restartStacks)
    {
        _stacks = 0;
        _faltering = true;
        _falterEnd = Time.time + Mathf.Max(0f, duration);
        _falterRestart = Mathf.Clamp(restartStacks, 0, _maxStacks);
        OnChanged?.Invoke();
    }

    /// <summary>빈틈 중 처치 → 즉시 해제(재시작 스택 적용).</summary>
    public void ClearFalter() { if (_faltering) EndFalter(); }

    private void EndFalter()
    {
        _faltering = false;
        _stacks = _falterRestart;
        _decayAccum = 0f;
        _lastAttackTime = Time.time;
        OnChanged?.Invoke();
    }

    private void Update()
    {
        if (_faltering)
        {
            if (Time.time >= _falterEnd) EndFalter();
            return; // 빈틈 중 감쇠 없음
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
    public float Fill => Ratio;
    public string Label => "광기 " + _stacks;
    public int Phase => _stacks >= _maxStacks ? 4 : (_stacks >= 31 ? 3 : (_stacks >= 16 ? 2 : (_stacks >= 1 ? 1 : 0)));
    public bool IsSkillReady => false; // 자동 발동(수동 불가)

    public void Tick(float deltaTime) { }                 // 감쇠는 자체 Update
    public void OnAttackLanded(GameObject target) => AddStack(1); // (라우팅 시 사용) — 현재는 패시브가 호출
    public void OnKill(GameObject target) { }

    public RelicResourceState Capture() => new RelicResourceState { fill = _stacks, phase = Phase, aux = 0f };
    public void Restore(RelicResourceState s) { _stacks = Mathf.Clamp((int)s.fill, 0, _maxStacks); OnChanged?.Invoke(); }
    public void ApplyConfig(RelicResourceConfig config)
    {
        if (config == null) return;
        _maxStacks = Mathf.Max(1, Mathf.RoundToInt(config.Get("max_stacks", _maxStacks)));
    }
}
