using System;
using UnityEngine;

/// <summary>
/// 원소별 독립 누적치 게이지 관리 컴포넌트.
/// 각 원소가 자신만의 누적치를 가지며, 해당 원소의 activation_gauge × monster.max_accumulation 에
/// 도달하면 그 원소 효과 발동. 누적 후 해당 원소의 게이지는 0으로 리셋되나 다른 원소 게이지는 유지.
/// decay는 원소별 개별 타이머로 동작 — 해당 원소를 decayDelay 초 동안 안 맞히면 그 게이지만 서서히 감소.
/// UI 표기용 프로퍼티(Accum/Threshold/Ratio)는 "가장 최근에 피격된 원소" 기준으로 값을 반환한다.
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
    [Tooltip("몬스터별 누적치 배율. 실제 임계치 = element.activation_gauge × 이 값.")]
    [SerializeField] private float monsterMaxAccumulationScale = 1f;
    [SerializeField] private float decayPerSecond = 10f;
    [SerializeField] private float decayDelay = 3f;

    // ── Private ─────────────────────────────────────────────────────
    private IElementTarget _target;

    private readonly float[]        _accums        = new float[ElementTypeUtil.Count];
    private readonly float[]        _lastHitTimes  = new float[ElementTypeUtil.Count];
    private readonly ActiveEffect[] _active        = new ActiveEffect[ElementTypeUtil.Count];

    private ElementType _last      = ElementType.None;
    private float       _lastDamage;
    private int         _poisonStacks; // Grass 스택형 전용 상태

    // ── Properties ──────────────────────────────────────────────────
    public ElementType LastElement       => _last;
    public float       LastTriggerDamage => _lastDamage;
    public int         PoisonStacks      => _poisonStacks;

    /// <summary>UI 바인딩용 — 최근 피격 원소의 누적치.</summary>
    public float Accum => GetAccum(_last);

    /// <summary>UI 바인딩용 — 최근 피격 원소의 임계치 (activation_gauge × monster scale).</summary>
    public float Threshold => GetThreshold(_last);

    /// <summary>UI 바인딩용 — 최근 피격 원소의 진행 비율.</summary>
    public float Ratio
    {
        get
        {
            float t = Threshold;
            return t <= 0f ? 0f : Mathf.Clamp01(Accum / t);
        }
    }

    public float GetAccum(ElementType e) => e.IsValid() ? _accums[(int)e] : 0f;

    public float GetThreshold(ElementType e)
    {
        if (!e.IsValid()) return 0f;
        var entry = Managers.ElementEffectData?.Get(e);
        if (entry == null || entry.activation_gauge <= 0f) return 0f;
        return entry.activation_gauge * Mathf.Max(0.01f, monsterMaxAccumulationScale);
    }

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
        float now = Time.time;

        // 원소별 개별 decay — 해당 원소를 decayDelay 초 이상 안 맞히면 그 게이지만 감소
        for (int i = 0; i < _accums.Length; i++)
        {
            if (_accums[i] <= 0f) continue;
            if (now - _lastHitTimes[i] > decayDelay)
                _accums[i] = Mathf.Max(0f, _accums[i] - decayPerSecond * Time.deltaTime);
        }

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

    /// <summary>몬스터 스펙에서 읽은 max_accumulation 배율을 주입 (MonsterBase가 Config 로드 후 호출).</summary>
    public void SetMonsterMaxAccumulationScale(float scale)
    {
        monsterMaxAccumulationScale = Mathf.Max(0.01f, scale);
    }

    /// <summary>피격 시 호출 — 원소가 None이면 lastDamage만 갱신하고 누적 없음.</summary>
    public void AddBuildup(ElementType element, float buildupAmount, float lastDamage = 0f)
    {
        if (lastDamage > 0f) _lastDamage = lastDamage;

        if (!element.IsValid() || buildupAmount <= 0f) return;

        int idx = (int)element;
        _lastHitTimes[idx] = Time.time;
        _last = element;

        _accums[idx] += buildupAmount;

        float threshold = GetThreshold(element);
        if (threshold > 0f && _accums[idx] >= threshold)
        {
            Trigger(element);
            _accums[idx] = 0f;
        }
    }

    /// <summary>Grass 스택 추가 (무한 중첩).</summary>
    public void AddPoisonStack()
    {
        _poisonStacks++;
    }

    /// <summary>Grass 효과 종료 시 스택 리셋.</summary>
    public void ResetPoisonStacks()
    {
        _poisonStacks = 0;
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

        for (int i = 0; i < _accums.Length; i++)
        {
            _accums[i] = 0f;
            _lastHitTimes[i] = 0f;
        }

        _last          = ElementType.None;
        _lastDamage    = 0f;
        _poisonStacks  = 0;
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

        // 기존 효과가 살아있으면 Clear 후 덮어쓰기 (duration 갱신)
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

        // VFX는 대상 Transform에 부착해 이동에도 자연스럽게 따라오게.
        ElementEffectRunner.SpawnVFXAttached(entry.vfx_key, _target.Transform, Mathf.Max(entry.duration, 1f));

        OnTriggered?.Invoke(element, entry);

        // 연결 검증용 — 발동 시 실제 임계치 확인
        Debug.Log($"[ElementBuildup:{gameObject.name}] 발동 → {element} ({entry.effect_id}) | gauge={entry.activation_gauge:F0} × scale={monsterMaxAccumulationScale:F2} = threshold={entry.activation_gauge * monsterMaxAccumulationScale:F0} | dur={entry.duration:F1}s");
    }
}
