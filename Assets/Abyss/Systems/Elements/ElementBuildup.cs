using System;
using UnityEngine;

/// <summary>
/// 단일 공유 누적치 게이지를 관리하는 컴포넌트.
/// 모든 원소 공격이 같은 _accum에 쌓이며, 임계값 도달 시 마지막에 때린 원소의 효과를 발동한다.
/// 효과 동작 자체는 ElementBehavior(원소 측)에 위임.
/// IElementTarget 을 같은 GameObject에 함께 부착해야 한다.
/// </summary>
[DisallowMultipleComponent]
public class ElementBuildup : MonoBehaviour
{
    // ── Constants ───────────────────────────────────────────────────
    private struct ActiveEffect
    {
        public ElementBehavior     Behavior;
        public ElementEffectEntry  Data;
        public float               Remaining;
        public float               TickTimer;
        public bool                IsActive;
    }

    // ── [SerializeField] ────────────────────────────────────────────
    [Header("Buildup")]
    [SerializeField] private float threshold = 100f;
    [SerializeField] private float decayPerSecond = 10f;
    [SerializeField] private float decayDelay = 3f;

    // ── Private ─────────────────────────────────────────────────────
    private IElementTarget _target;

    private float       _accum;
    private ElementType _last       = ElementType.None;
    private float       _lastHitTime;
    private float       _lastDamage;
    private float       _accumGainMultiplier = 1f;

    private readonly ActiveEffect[] _active = new ActiveEffect[ElementTypeUtil.Count];

    // ── Properties ──────────────────────────────────────────────────
    public float       Accum                 => _accum;
    public float       Threshold             => threshold;
    public float       Ratio                 => threshold <= 0f ? 0f : Mathf.Clamp01(_accum / threshold);
    public ElementType LastElement           => _last;
    public float       LastTriggerDamage     => _lastDamage;
    public float       AccumGainMultiplier   => _accumGainMultiplier;

    public bool  IsEffectActive(ElementType e) => e.IsValid() && _active[(int)e].IsActive;
    public float GetRemaining(ElementType e)   => e.IsValid() ? _active[(int)e].Remaining : 0f;
    public ElementEffectEntry GetActiveEntry(ElementType e) => e.IsValid() ? _active[(int)e].Data : null;

    // ── Events ──────────────────────────────────────────────────────
    public event Action<ElementType, ElementEffectEntry> OnTriggered;
    public event Action<ElementType, ElementEffectEntry> OnExpired;
    public event Action<ElementType, ElementEffectEntry> OnTick;

    // ── Lifecycle ───────────────────────────────────────────────────
    private void Awake()
    {
        _target = GetComponent<IElementTarget>();
        if (_target == null)
            Debug.LogError($"[ElementBuildup] IElementTarget 컴포넌트가 같은 GameObject에 없습니다: {gameObject.name}");
    }

    private void Update()
    {
        // 누적치 자연 감소
        if (Time.time - _lastHitTime > decayDelay)
            _accum = Mathf.Max(0f, _accum - decayPerSecond * Time.deltaTime);

        // 액티브 효과 처리
        for (int i = 0; i < _active.Length; i++)
        {
            if (!_active[i].IsActive) continue;

            _active[i].Remaining -= Time.deltaTime;

            if (_active[i].Remaining <= 0f)
            {
                var behavior = _active[i].Behavior;
                var data     = _active[i].Data;
                behavior.Clear(_target, this, data);
                _active[i] = default;
                OnExpired?.Invoke((ElementType)i, data);
                continue;
            }

            if (_active[i].Data.tick_interval > 0f)
            {
                _active[i].TickTimer -= Time.deltaTime;
                if (_active[i].TickTimer <= 0f)
                {
                    _active[i].Behavior.Tick(_target, this, _active[i].Data);
                    OnTick?.Invoke((ElementType)i, _active[i].Data);
                    _active[i].TickTimer = _active[i].Data.tick_interval;
                }
            }
        }
    }

    // ── Public Methods ───────────────────────────────────────────────
    /// <summary>피격 시 호출 — 원소가 None이면 마지막 피격 시간만 갱신.</summary>
    public void AddBuildup(ElementType element, float buildupAmount, float lastDamage = 0f)
    {
        _lastHitTime = Time.time;
        if (lastDamage > 0f) _lastDamage = lastDamage;

        if (!element.IsValid() || buildupAmount <= 0f) return;

        float gain = buildupAmount * _accumGainMultiplier;
        _accum = Mathf.Min(threshold * 2f, _accum + gain);
        _last  = element;

        if (_accum >= threshold)
        {
            Trigger(element);
            _accum = 0f;
        }
    }

    /// <summary>Behavior가 누적치 증가 배율을 조정할 때 사용 (Water).</summary>
    public void SetAccumGainMultiplier(float multi)
    {
        _accumGainMultiplier = Mathf.Max(0f, multi);
    }

    /// <summary>모든 누적치/액티브 효과 초기화.</summary>
    public void ResetAll()
    {
        for (int i = 0; i < _active.Length; i++)
        {
            if (_active[i].IsActive)
                _active[i].Behavior.Clear(_target, this, _active[i].Data);
            _active[i] = default;
        }
        _accum                = 0f;
        _last                 = ElementType.None;
        _accumGainMultiplier  = 1f;
        _lastDamage           = 0f;
    }

    // ── Private Methods ──────────────────────────────────────────────
    private void Trigger(ElementType element)
    {
        var entry = Managers.ElementEffectData?.Get(element);
        if (entry == null)
        {
            Debug.LogWarning($"[ElementBuildup] ElementEffectData 없음: {element}");
            return;
        }

        var behavior = ElementRegistry.Get(element);
        if (behavior == null)
        {
            Debug.LogWarning($"[ElementBuildup] ElementBehavior 미등록: {element}");
            return;
        }

        int idx = (int)element;

        // 기존 효과 덮어쓰기
        if (_active[idx].IsActive)
            _active[idx].Behavior.Clear(_target, this, _active[idx].Data);

        _active[idx] = new ActiveEffect
        {
            Behavior  = behavior,
            Data      = entry,
            Remaining = entry.duration,
            TickTimer = entry.tick_interval > 0f ? entry.tick_interval : float.MaxValue,
            IsActive  = true,
        };

        behavior.Apply(_target, this, entry);
        ElementEffectRunner.SpawnVFX(entry.vfx_key, _target.Transform.position, Mathf.Max(entry.duration, 1f));

        OnTriggered?.Invoke(element, entry);
    }
}
