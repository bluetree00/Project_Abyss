using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 공격 테스트용 허수아비.
/// 데미지를 받으면 HP가 줄어들고, 일정 시간 후 자동 회복.
/// 절대 죽지 않음 (IKillable.IsDead = false).
/// 원소 효과는 ElementBuildup 컴포넌트가 처리하며, IElementTarget 콜백으로 본 클래스가 상태를 받는다.
/// </summary>
[RequireComponent(typeof(ElementBuildup))]
public class TrainingDummy : MonoBehaviour, IDamageable, IKillable, IElementTarget
{
    // ── Constants ───────────────────────────────────────────────────
    private static readonly string[] ElementLabels = { "⚡", "💧", "🔥", "🌿", "🪨" };

    private static readonly Color[] ElementColors =
    {
        new(1.00f, 0.92f, 0.23f, 1f), // Lightning - yellow
        new(0.13f, 0.59f, 0.95f, 1f), // Water     - blue
        new(0.96f, 0.26f, 0.21f, 1f), // Fire      - red
        new(0.30f, 0.69f, 0.31f, 1f), // Grass     - green
        new(0.55f, 0.43f, 0.39f, 1f), // Earth     - brown
    };

    // ── [SerializeField] ────────────────────────────────────────────
    [Header("Stats")]
    [SerializeField] private float maxHp = 500f;
    [SerializeField] private float regenDelay = 3f;
    [SerializeField] private float regenPerSecond = 100f;

    [Header("UI")]
    [SerializeField] private Slider hpBar;
    [SerializeField] private TMPro.TMP_Text hpText;
    [SerializeField] private TMPro.TMP_Text damageText;
    [SerializeField] private TMPro.TMP_Text elementAccumText;

    [Header("Element Gauge (HP 위 단일 공유 게이지)")]
    [SerializeField] private Image elementGaugeFill;
    [SerializeField] private TMPro.TMP_Text elementGaugeLabel;

    [Header("Hit Animation")]
    [SerializeField] private Animator animator;

    // ── Private ─────────────────────────────────────────────────────
    private static readonly int HitHash = Animator.StringToHash("Hit");

    private ElementBuildup _buildup;
    private ElementVisualFeedback _visualFeedback;

    private float _currentHp;
    private float _lastHitTime;
    private float _totalDamageShown;

    // IElementTarget 콜백으로 갱신되는 상태
    private float _incomingDamageMultiplier = 1f;
    private float _movementMultiplier       = 1f;
    private float _attackSpeedMultiplier    = 1f;
    private float _defenseMultiplier        = 1f;

    // ── Properties ──────────────────────────────────────────────────
    public bool       IsDead     => false;
    public Transform  Transform  => transform;
    public GameObject GameObject => gameObject;
    public float      MaxHp      => maxHp;

    // ── Lifecycle ───────────────────────────────────────────────────
    private void Awake()
    {
        _currentHp = maxHp;

        _buildup        = GetComponent<ElementBuildup>();
        _visualFeedback = GetComponent<ElementVisualFeedback>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        UpdateUI();
    }

    private void OnEnable()
    {
        if (_buildup != null)
        {
            _buildup.OnTriggered += HandleTriggered;
            _buildup.OnExpired   += HandleExpired;
        }
    }

    private void OnDisable()
    {
        if (_buildup != null)
        {
            _buildup.OnTriggered -= HandleTriggered;
            _buildup.OnExpired   -= HandleExpired;
        }
    }

    private void Update()
    {
        bool regenActive = Time.time - _lastHitTime > regenDelay
                           && _currentHp < maxHp;
        if (regenActive)
        {
            _currentHp = Mathf.Min(maxHp, _currentHp + regenPerSecond * Time.deltaTime);
            _totalDamageShown = 0f;
        }

        UpdateUI();

        if (hpBar != null)
        {
            var cam = Camera.main;
            if (cam != null)
                hpBar.transform.parent.rotation = cam.transform.rotation;
        }
    }

    // ── Public Methods (IDamageable) ─────────────────────────────────
    public void TakeDamage(float amount, GameObject instigator,
                           float knockbackMultiplier = 1f,
                           ElementType element = ElementType.None,
                           float elementAmount = 0f)
    {
        if (amount <= 0f) return;

        amount *= _incomingDamageMultiplier;

        _currentHp = Mathf.Max(0f, _currentHp - amount);
        _lastHitTime = Time.time;
        _totalDamageShown += amount;

        if (_buildup != null)
            _buildup.AddBuildup(element, elementAmount, amount);

        if (animator != null)
            animator.SetTrigger(HitHash);

        // 일반 히트 플래시 (원소 무관) — 시각 피드백 컴포넌트로 위임
        _visualFeedback?.FlashHit(Color.red);

        UpdateUI();

        if (_currentHp <= 0f)
            ResetDummy();
    }

    // ── Public Methods (IElementTarget) ──────────────────────────────
    public void TakeElementalDoT(float damage, ElementType source)
    {
        if (damage <= 0f) return;
        _currentHp = Mathf.Max(0f, _currentHp - damage);
        _totalDamageShown += damage;
        _lastHitTime = Time.time;

        // DoT 데미지도 원소 색 팝업으로 표시
        DamagePopupSpawner.Spawn(transform.position + Vector3.up * 1.5f, damage, false, source);

        Debug.Log($"[Dummy] DoT tick: {source} -{damage:F1}  HP={_currentHp:F0}/{maxHp}");
        UpdateUI();
    }

    public void SetIncomingDamageMultiplier(float multi) => _incomingDamageMultiplier = multi;

    public void SetMovementMultiplier(float multi) => _movementMultiplier = multi;

    public void SetAttackSpeedMultiplier(float multi) => _attackSpeedMultiplier = multi;

    public void SetDefenseMultiplier(float multi) => _defenseMultiplier = multi;

    // ── Private Methods ──────────────────────────────────────────────
    private void ResetDummy()
    {
        // HP만 리필 — 누적치/액티브 효과/석화 상태는 그대로 유지
        _currentHp = maxHp;
        _totalDamageShown = 0f;
        Debug.Log("[Dummy] HP refilled (누적치·효과 유지)");
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
        if (_buildup == null) return;

        var lastElement = _buildup.LastElement;

        if (elementGaugeFill != null)
        {
            elementGaugeFill.fillAmount = _buildup.Ratio;
            elementGaugeFill.color = ColorOf(lastElement);
        }

        if (elementGaugeLabel != null)
        {
            string label = lastElement.IsValid()
                ? $"{ElementLabels[(int)lastElement]} {_buildup.Accum:F0}/{_buildup.Threshold:F0}"
                : $"- {_buildup.Accum:F0}/{_buildup.Threshold:F0}";

            if (lastElement == ElementType.Grass && _buildup.PoisonStacks > 0)
                label += $" x{_buildup.PoisonStacks}";

            elementGaugeLabel.text = label;
        }

        if (elementAccumText != null)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < ElementTypeUtil.Count; i++)
            {
                var e = (ElementType)i;
                if (!_buildup.IsEffectActive(e)) continue;
                var entry = _buildup.GetActiveEntry(e);
                float rem = _buildup.GetRemaining(e);
                sb.AppendLine($"<color=yellow>{ElementLabels[i]} {entry.effect_id} {rem:F1}s</color>");
            }
            if (_movementMultiplier <= 0f)
                sb.AppendLine("<color=#aaaaaa>[이동 정지]</color>");
            if (_attackSpeedMultiplier <= 0f)
                sb.AppendLine("<color=#aaaaaa>[공격 정지]</color>");
            if (_defenseMultiplier < 1f)
                sb.AppendLine($"<color=#aaaaaa>[방어력 x{_defenseMultiplier:F2}]</color>");
            elementAccumText.text = sb.ToString().TrimEnd();
        }
    }

    private static Color ColorOf(ElementType element)
    {
        if (!element.IsValid()) return Color.gray;
        int idx = (int)element;
        if (idx < 0 || idx >= ElementColors.Length) return Color.gray;
        return ElementColors[idx];
    }

    // ── Event Handlers ───────────────────────────────────────────────
    private void HandleTriggered(ElementType element, ElementEffectEntry entry)
    {
        Debug.Log($"[Dummy] 원소 발동! {element} → {entry.effect_id} ({entry.description})");
    }

    private void HandleExpired(ElementType element, ElementEffectEntry entry)
    {
        Debug.Log($"[Dummy] 원소 효과 만료: {element} → {entry.effect_id}");
    }
}
