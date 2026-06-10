using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using System.Threading;
using UnityEngine;

/// <summary>
/// 공격 테스트용 허수아비.
/// MonsterHPBar 풀을 그대로 사용하며, 데미지를 받으면 HP가 줄고 일정 시간 후 자동 회복한다.
/// 절대 죽지 않음 (IKillable.IsDead = false).
/// </summary>
public class TrainingDummy : MonoBehaviour, IDamageable, IKillable
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

    private const float DpsWindow = 3f;

    private static readonly int HitHash = Animator.StringToHash("Hit");

    private MonsterHPBar _hpBar;

    private float _currentHp;
    private float _lastHitTime;

    private readonly Queue<(float time, float damage)> _damageLog = new();
    private float _damageInWindow;

    // ── Properties ──────────────────────────────────────────────────

    public bool IsDead => false;

    // ── Lifecycle ───────────────────────────────────────────────────

    private void Awake()
    {
        _currentHp = maxHp;

        if (animator == null)
            animator = GetComponentInChildren<Animator>();
    }

    private void Start()
    {
        InitHPBarAsync(this.GetCancellationTokenOnDestroy()).Forget();
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

        UpdateDps();
    }

    // ── Public Methods (IDamageable) ─────────────────────────────────

    public void TakeDamage(float amount, GameObject instigator, float knockbackMultiplier = 1f, bool isCrit = false)
    {
        if (amount <= 0f) return;

        _currentHp   = Mathf.Max(0f, _currentHp - amount);
        _lastHitTime = Time.time;
        LogDamage(amount);

        if (animator != null)
            animator.SetTrigger(HitHash);

        _hpBar?.UpdateHP((int)_currentHp, (int)maxHp);

        // 데미지 팝업 (허수아비도 일관 표시)
        DamagePopupSpawner.Spawn(transform.position + Vector3.up * (hpBarHeadOffset + 0.9f), amount, isCrit);
    }

    // ── Private Methods ──────────────────────────────────────────────

    private void LogDamage(float amount)
    {
        _damageLog.Enqueue((Time.time, amount));
        _damageInWindow += amount;
    }

    private void UpdateDps()
    {
        float now = Time.time;
        while (_damageLog.Count > 0 && now - _damageLog.Peek().time > DpsWindow)
        {
            var (_, d) = _damageLog.Dequeue();
            _damageInWindow -= d;
        }

        if (_hpBar == null) return;
        float dps = _damageInWindow / DpsWindow;
        _hpBar.SetSubLabel(dps >= 1f ? $"DPS  {dps:F0}" : string.Empty);
    }

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
}
