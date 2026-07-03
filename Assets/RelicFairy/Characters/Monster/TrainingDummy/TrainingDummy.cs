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

    private const float DpsWindow        = 3f;
    private const float AnalysisResetIdle = 4f;  // 이 시간 이상 무타격이면 다음 타격 때 분석 리셋(교전 단위 집계)
    private const float LabelRefreshInterval = 0.1f;

    private static readonly int HitHash = Animator.StringToHash("Hit");

    private MonsterHPBar _hpBar;

    private float _currentHp;
    private float _lastHitTime;

    private readonly Queue<(float time, float damage)> _damageLog = new();
    private float _damageInWindow;

    // 실시간 데미지 분석 집계 (교전 단위)
    private float _lastHit;
    private bool  _lastHitCrit;
    private float _maxHit;
    private int   _hitCount;
    private int   _critCount;
    private float _labelTimer;

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

        PruneDpsWindow();

        // 라벨 문자열 조립은 매 프레임 GC를 피해 저주기로만 갱신.
        _labelTimer -= Time.deltaTime;
        if (_labelTimer <= 0f)
        {
            _labelTimer = LabelRefreshInterval;
            RefreshAnalysisLabel();
        }
    }

    // ── Public Methods (IDamageable) ─────────────────────────────────

    public void TakeDamage(float amount, GameObject instigator, float knockbackMultiplier = 1f, bool isCrit = false)
    {
        if (amount <= 0f) return;

        float now = Time.time;
        // 오래 쉬었다가 다시 때리면 새 교전으로 보고 집계 리셋 → 매 세션 깨끗한 분석.
        if (now - _lastHitTime > AnalysisResetIdle) ResetAnalysis();

        _currentHp   = Mathf.Max(0f, _currentHp - amount);
        _lastHitTime = now;

        _lastHit     = amount;
        _lastHitCrit = isCrit;
        if (amount > _maxHit) _maxHit = amount;
        _hitCount++;
        if (isCrit) _critCount++;

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

    private void PruneDpsWindow()
    {
        float now = Time.time;
        while (_damageLog.Count > 0 && now - _damageLog.Peek().time > DpsWindow)
        {
            var (_, d) = _damageLog.Dequeue();
            _damageInWindow -= d;
        }
    }

    private void ResetAnalysis()
    {
        _lastHit    = 0f;
        _maxHit     = 0f;
        _hitCount   = 0;
        _critCount  = 0;
        _damageLog.Clear();
        _damageInWindow = 0f;
    }

    /// <summary>HP바 서브라벨에 실시간 데미지 분석을 멀티라인으로 표시한다.
    /// DPS(3초) / 최근타(크리 표기) / 최대타 / 타수·크리율.</summary>
    private void RefreshAnalysisLabel()
    {
        if (_hpBar == null) return;

        if (_hitCount == 0)
        {
            _hpBar.SetSubLabel(string.Empty);
            return;
        }

        float dps      = _damageInWindow / DpsWindow;
        float critRate = 100f * _critCount / _hitCount;

        _hpBar.SetSubLabel(
            $"DPS {dps:F0}\n" +
            $"최근 {_lastHit:F0}{(_lastHitCrit ? " CRIT" : string.Empty)}\n" +
            $"최대 {_maxHit:F0}\n" +
            $"타수 {_hitCount}  크리 {critRate:F0}%");
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
