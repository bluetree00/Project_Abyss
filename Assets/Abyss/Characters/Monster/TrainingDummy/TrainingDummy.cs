using Cysharp.Threading.Tasks;
using System.Threading;
using UnityEngine;

/// <summary>
/// 공격 테스트용 허수아비.
/// MonsterHPBar 풀을 그대로 사용하며, 데미지를 받으면 HP가 줄고 일정 시간 후 자동 회복한다.
/// 절대 죽지 않음 (IKillable.IsDead = false).
/// </summary>
[RequireComponent(typeof(ElementBuildup))]
public class TrainingDummy : MonoBehaviour, IDamageable, IKillable, IElementTarget
{
    // ── [SerializeField] ────────────────────────────────────────────

    [Header("Stats")]
    [SerializeField] private float maxHp          = 500f;
    [SerializeField] private float regenDelay      = 3f;
    [SerializeField] private float regenPerSecond  = 100f;

    [Header("UI")]
    [SerializeField] private string displayName    = "허수아비";
    [SerializeField] private float  hpBarHeadOffset = 0.3f;

    [Header("Hit Animation")]
    [SerializeField] private Animator animator;

    // ── Private ─────────────────────────────────────────────────────

    private static readonly int HitHash = Animator.StringToHash("Hit");

    private ElementBuildup        _buildup;
    private ElementVisualFeedback _visualFeedback;
    private MonsterHPBar          _hpBar;

    private float _currentHp;
    private float _lastHitTime;

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
        _currentHp      = maxHp;
        _buildup        = GetComponent<ElementBuildup>();
        _visualFeedback = GetComponent<ElementVisualFeedback>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();
    }

    private void Start()
    {
        InitHPBarAsync(this.GetCancellationTokenOnDestroy()).Forget();
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

    private void OnDestroy()
    {
        if (_hpBar != null)
        {
            Managers.MonsterHPBar?.ReturnHPBar(_hpBar);
            _hpBar = null;
        }
    }

    private void Update()
    {
        bool regenActive = Time.time - _lastHitTime > regenDelay && _currentHp < maxHp;
        if (regenActive)
            _currentHp = Mathf.Min(maxHp, _currentHp + regenPerSecond * Time.deltaTime);

        _hpBar?.UpdateHP((int)_currentHp, (int)maxHp);

        if (_buildup != null && _hpBar != null)
            _hpBar.UpdateElement(_buildup.Ratio, _buildup.Accum, _buildup.Threshold, _buildup.LastElement, _buildup.PoisonStacks);
    }

    // ── Public Methods (IDamageable) ─────────────────────────────────

    public void TakeDamage(float amount, GameObject instigator,
                           float knockbackMultiplier = 1f,
                           ElementType element       = ElementType.None,
                           float elementAmount       = 0f)
    {
        if (amount <= 0f) return;

        amount      *= _incomingDamageMultiplier;
        _currentHp   = Mathf.Max(0f, _currentHp - amount);
        _lastHitTime = Time.time;

        if (_buildup != null)
            _buildup.AddBuildup(element, elementAmount, amount);

        if (animator != null)
            animator.SetTrigger(HitHash);

        _visualFeedback?.FlashHit(Color.red);
        _hpBar?.UpdateHP((int)_currentHp, (int)maxHp);
    }

    // ── Public Methods (IElementTarget) ──────────────────────────────

    public void TakeElementalDoT(float damage, ElementType source)
    {
        if (damage <= 0f) return;

        _currentHp   = Mathf.Max(0f, _currentHp - damage);
        _lastHitTime = Time.time;

        DamagePopupSpawner.Spawn(transform.position + Vector3.up * 1.5f, damage, false, source);
        _hpBar?.UpdateHP((int)_currentHp, (int)maxHp);
    }

    public void SetIncomingDamageMultiplier(float multi) => _incomingDamageMultiplier = multi;
    public void SetMovementMultiplier(float multi)       => _movementMultiplier       = multi;
    public void SetAttackSpeedMultiplier(float multi)    => _attackSpeedMultiplier    = multi;
    public void SetDefenseMultiplier(float multi)        => _defenseMultiplier        = multi;

    // ── Private Methods ──────────────────────────────────────────────

    private async UniTaskVoid InitHPBarAsync(CancellationToken ct)
    {
        try
        {
            _hpBar = await Managers.MonsterHPBar.RequestHPBarAsync(
                this, (int)_currentHp, (int)maxHp, null, hpBarHeadOffset);
            _hpBar?.SetMonsterName(displayName);
        }
        catch (System.OperationCanceledException) { }
    }

    // ── Event Handlers ───────────────────────────────────────────────

    private void HandleTriggered(ElementType element, ElementEffectEntry entry)
    {
        Debug.Log($"[Dummy] 원소 발동! {element} → {entry.effect_id}");
    }

    private void HandleExpired(ElementType element, ElementEffectEntry entry)
    {
        Debug.Log($"[Dummy] 원소 효과 만료: {element} → {entry.effect_id}");
    }
}
