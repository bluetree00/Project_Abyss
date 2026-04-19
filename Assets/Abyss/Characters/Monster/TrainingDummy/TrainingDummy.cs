using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 공격 테스트용 허수아비.
/// 데미지를 받으면 HP가 줄어들고, 일정 시간 후 자동 회복.
/// 절대 죽지 않음 (IKillable.IsDead = false).
/// 원소 발동 시 ELEMENT_EFFECT_DATA 테이블 기반 효과 적용.
/// </summary>
public class TrainingDummy : MonoBehaviour, IDamageable, IKillable
{
    // ── Constants ───────────────────────────────────────────────────
    private static readonly string[] ElementLabels = { "⚡", "💧", "🔥", "🌿", "🪨" };

    private struct ActiveEffect
    {
        public ElementEffectEntry Data;
        public float RemainingDuration;
        public float TickTimer;
        public bool  IsActive;
    }

    // ── [SerializeField] ────────────────────────────────────────────
    [Header("Stats")]
    [SerializeField] private float maxHp = 500f;
    [SerializeField] private float regenDelay = 3f;
    [SerializeField] private float regenPerSecond = 100f;

    [Header("Element")]
    [SerializeField] private float accumulationThreshold = 100f;
    [SerializeField] private float accumDecayPerSecond = 10f;

    [Header("UI")]
    [SerializeField] private Slider hpBar;
    [SerializeField] private TMPro.TMP_Text hpText;
    [SerializeField] private TMPro.TMP_Text damageText;
    [SerializeField] private TMPro.TMP_Text elementAccumText;

    [Header("Hit Animation")]
    [SerializeField] private Animator animator;

    [Header("Hit Color Flash")]
    [SerializeField] private Renderer[] renderers;

    // ── Private ─────────────────────────────────────────────────────
    private static readonly int HitHash = Animator.StringToHash("Hit");
    private static readonly int ColorID = Shader.PropertyToID("_BaseColor");

    private float _currentHp;
    private float _lastHitTime;
    private float _totalDamageShown;
    private float _lastTriggerDamage;
    private Color _originalColor = Color.white;
    private MaterialPropertyBlock _mpb;

    private readonly float[]        _elementAccum  = new float[ElementTypeUtil.Count];
    private readonly ActiveEffect[] _activeEffects  = new ActiveEffect[ElementTypeUtil.Count];

    // 효과 누적 상태 배율
    private float _incomingDamageMultiplier  = 1f;  // Grass poison
    private float _lightningAccumMultiplier  = 1f;  // Water wet
    private bool  _isPetrified;                     // Earth petrify

    // ── Properties ──────────────────────────────────────────────────
    public bool IsDead => false;

    // ── Lifecycle ───────────────────────────────────────────────────
    private void Awake()
    {
        _currentHp = maxHp;
        _mpb = new MaterialPropertyBlock();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();
        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<Renderer>();

        UpdateUI();
    }

    private void Update()
    {
        ProcessActiveEffects();

        bool regenActive = !_isPetrified
                           && Time.time - _lastHitTime > regenDelay
                           && _currentHp < maxHp;
        if (regenActive)
        {
            _currentHp = Mathf.Min(maxHp, _currentHp + regenPerSecond * Time.deltaTime);
            _totalDamageShown = 0f;
        }

        // 피격 공백 후 누적치 자연 감소
        if (Time.time - _lastHitTime > regenDelay)
        {
            for (int i = 0; i < _elementAccum.Length; i++)
                _elementAccum[i] = Mathf.Max(0f, _elementAccum[i] - accumDecayPerSecond * Time.deltaTime);
        }

        UpdateUI();

        if (hpBar != null)
        {
            var cam = Camera.main;
            if (cam != null)
                hpBar.transform.parent.rotation = cam.transform.rotation;
        }
    }

    // ── Public Methods ───────────────────────────────────────────────
    public void TakeDamage(float amount, GameObject instigator,
                           float knockbackMultiplier = 1f,
                           ElementType element = ElementType.None,
                           float elementAmount = 0f)
    {
        if (_isPetrified) return;
        if (amount <= 0f) return;

        // Grass poison — 받는 피해 증가
        amount *= _incomingDamageMultiplier;

        _currentHp = Mathf.Max(0f, _currentHp - amount);
        _lastHitTime = Time.time;
        _lastTriggerDamage = amount;
        _totalDamageShown += amount;

        // 원소 누적치
        if (element.IsValid() && elementAmount > 0f)
        {
            int idx = element.ToIndex();

            // Water wet — Lightning 누적치 증가
            float accum = elementAmount;
            if (element == ElementType.Lightning && _activeEffects[(int)ElementType.Water].IsActive)
                accum *= _lightningAccumMultiplier;

            _elementAccum[idx] = Mathf.Min(_elementAccum[idx] + accum, accumulationThreshold * 2f);

            if (_elementAccum[idx] >= accumulationThreshold)
            {
                TriggerElement(element);
                _elementAccum[idx] = 0f;
            }
        }

        if (animator != null)
            animator.SetTrigger(HitHash);

        StopAllCoroutines();
        StartCoroutine(HitColorFlash());

        UpdateUI();

        if (_currentHp <= 0f)
            ResetDummy();
    }

    // ── Private Methods ──────────────────────────────────────────────
    private void TriggerElement(ElementType element)
    {
        var entry = Managers.ElementEffectData?.Get(element);
        if (entry == null)
        {
            Debug.LogWarning($"[Dummy] ElementEffectData 없음: {element}");
            return;
        }

        int idx = element.ToIndex();

        // 기존 효과 만료 처리 후 새 효과 적용
        if (_activeEffects[idx].IsActive)
            ClearEffectModifiers(element, _activeEffects[idx].Data);

        _activeEffects[idx] = new ActiveEffect
        {
            Data              = entry,
            RemainingDuration = entry.duration,
            TickTimer         = entry.tick_interval > 0f ? entry.tick_interval : float.MaxValue,
            IsActive          = true,
        };

        ApplyEffectModifiers(element, entry);

        // Lightning — 즉시 체인 데미지
        if (element == ElementType.Lightning)
            ApplyLightningChain(entry);

        Debug.Log($"[Dummy] 원소 발동! {element} → {entry.effect_id} ({entry.description})");
    }

    private void ApplyEffectModifiers(ElementType element, ElementEffectEntry entry)
    {
        if (element == ElementType.Grass)
            _incomingDamageMultiplier = 1f + entry.magnitude_b;   // 15% 증가
        else if (element == ElementType.Water)
            _lightningAccumMultiplier = entry.magnitude_b;         // 2배
        else if (element == ElementType.Earth)
            _isPetrified = true;
    }

    private void ClearEffectModifiers(ElementType element, ElementEffectEntry entry)
    {
        if (element == ElementType.Grass)
            _incomingDamageMultiplier = 1f;
        else if (element == ElementType.Water)
            _lightningAccumMultiplier = 1f;
        else if (element == ElementType.Earth)
            _isPetrified = false;
    }

    private void ProcessActiveEffects()
    {
        for (int i = 0; i < _activeEffects.Length; i++)
        {
            if (!_activeEffects[i].IsActive) continue;

            _activeEffects[i].RemainingDuration -= Time.deltaTime;

            if (_activeEffects[i].RemainingDuration <= 0f)
            {
                ClearEffectModifiers((ElementType)i, _activeEffects[i].Data);
                _activeEffects[i] = default;
                continue;
            }

            // DoT 틱
            if (_activeEffects[i].Data.tick_interval > 0f)
            {
                _activeEffects[i].TickTimer -= Time.deltaTime;
                if (_activeEffects[i].TickTimer <= 0f)
                {
                    ApplyDoTTick((ElementType)i, _activeEffects[i].Data);
                    _activeEffects[i].TickTimer = _activeEffects[i].Data.tick_interval;
                }
            }
        }
    }

    private void ApplyDoTTick(ElementType element, ElementEffectEntry entry)
    {
        float damage = element switch
        {
            ElementType.Fire  => maxHp * entry.magnitude_a,   // 최대 HP 5%
            ElementType.Grass => entry.magnitude_a,            // 고정 5 데미지
            _                 => 0f,
        };
        if (damage <= 0f) return;

        _currentHp = Mathf.Max(0f, _currentHp - damage);
        _totalDamageShown += damage;
        _lastHitTime = Time.time;

        Debug.Log($"[Dummy] DoT tick: {element} -{damage:F1}  HP={_currentHp:F0}/{maxHp}");
        UpdateUI();
    }

    private void ApplyLightningChain(ElementEffectEntry entry)
    {
        float radius     = entry.magnitude_b;   // 3m
        float chainRatio = entry.magnitude_a;   // 0.3 = 30%
        float chainDmg   = _lastTriggerDamage * chainRatio;
        if (chainDmg <= 0f) return;

        var hits = Physics.OverlapSphere(transform.position, radius);
        foreach (var col in hits)
        {
            if (col.gameObject == gameObject) continue;
            if (col.TryGetComponent<IDamageable>(out var target))
            {
                target.TakeDamage(chainDmg, gameObject, 1f, ElementType.None, 0f);
                Debug.Log($"[Dummy] Lightning chain → {col.name} ({chainDmg:F1})");
            }
        }
    }

    private void ResetDummy()
    {
        _currentHp = maxHp;
        _totalDamageShown = 0f;
        System.Array.Clear(_elementAccum, 0, _elementAccum.Length);
        System.Array.Clear(_activeEffects, 0, _activeEffects.Length);
        _incomingDamageMultiplier = 1f;
        _lightningAccumMultiplier = 1f;
        _isPetrified = false;
        Debug.Log("[Dummy] Reset! Full HP restored.");
        UpdateUI();
    }

    private void UpdateUI()
    {
        if (hpBar != null)
        {
            hpBar.maxValue = maxHp;
            hpBar.value = _currentHp;
        }

        if (hpText != null)
            hpText.text = $"{_currentHp:F0} / {maxHp:F0}";

        if (damageText != null)
            damageText.text = _totalDamageShown > 0f ? $"DMG: {_totalDamageShown:F0}" : "";

        UpdateElementUI();
    }

    private void UpdateElementUI()
    {
        if (elementAccumText == null) return;

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < ElementTypeUtil.Count; i++)
        {
            var   elemType = (ElementType)i;
            float accum    = _elementAccum[i];
            float ratio    = accum / accumulationThreshold;
            string bar     = BuildBar(ratio, 10);

            string effectTag = "";
            if (_activeEffects[i].IsActive)
            {
                float rem = _activeEffects[i].RemainingDuration;
                effectTag = $" <color=yellow>[{_activeEffects[i].Data.effect_id} {rem:F1}s]</color>";
            }

            sb.AppendLine($"{ElementLabels[i]} [{bar}] {accum:F0}/{accumulationThreshold:F0}{effectTag}");
        }

        if (_isPetrified)
            sb.AppendLine("<color=#aaaaaa>[석화 - 무적]</color>");

        elementAccumText.text = sb.ToString().TrimEnd();
    }

    private static string BuildBar(float ratio, int width)
    {
        int filled = Mathf.RoundToInt(Mathf.Clamp01(ratio) * width);
        return new string('■', filled) + new string('□', width - filled);
    }

    private System.Collections.IEnumerator HitColorFlash()
    {
        SetColor(Color.red);
        yield return new WaitForSeconds(0.08f);
        SetColor(_originalColor);
    }

    private void SetColor(Color color)
    {
        if (renderers == null || _mpb == null) return;
        _mpb.SetColor(ColorID, color);
        foreach (var r in renderers)
            if (r != null) r.SetPropertyBlock(_mpb);
    }
}
