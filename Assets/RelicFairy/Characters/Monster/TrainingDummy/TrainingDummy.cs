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

    [Header("등급")]
    // 유물/아이템 중엔 '상대의 격'으로 보상이 갈리는 것들이 있다(랜슬롯 광기 수급 등).
    // 더미가 항상 Common이면 보스 상대 수급을 여기서 검증할 수 없다 → 등급을 지정 가능하게 둔다.
    [Tooltip("이 더미를 어떤 격의 상대로 취급할지. 광기 스택 등 등급 기반 효과가 이 값을 읽는다.")]
    [SerializeField] private MonsterGrade grade = MonsterGrade.Common;

    [Header("Hit Animation")]
    [SerializeField] private Animator animator;
    // 컨트롤러마다 피격 표현 방식이 다르다.
    //  · 허수아비(DummyAnimator) : "Hit" Trigger 파라미터 보유 → SetTrigger
    //  · 몬스터(예: Skeleton)    : 파라미터가 아예 없고 상태 이름을 직접 재생 → CrossFade
    // 둘 다 지원해야 아무 몬스터나 더미로 세울 수 있다.
    [Tooltip("이 이름의 Trigger 파라미터가 Animator에 있으면 그걸 쓴다.")]
    [SerializeField] private string hitTrigger = "Hit";
    [Tooltip("Trigger가 없는 컨트롤러용 — 피격 시 직접 재생할 상태 이름(예: GetHit). 비우면 피격 모션 없음.")]
    [SerializeField] private string hitStateName = "";
    [Tooltip("피격 모션 후 되돌아갈 대기 상태 이름(예: IdleNormal). 비우면 되돌리지 않는다.")]
    [SerializeField] private string idleStateName = "";
    [Tooltip("피격 모션을 이 시간(초) 재생한 뒤 대기 상태로 복귀한다.")]
    [SerializeField] private float hitReturnDelay = 0.8f;

    // ── Private ─────────────────────────────────────────────────────

    private const float DpsWindow        = 3f;
    private const float AnalysisResetIdle = 4f;  // 이 시간 이상 무타격이면 다음 타격 때 분석 리셋(교전 단위 집계)
    private const float LabelRefreshInterval = 0.1f;

    private MonsterHPBar _hpBar;

    private int   _hitTriggerHash;
    private bool  _hasHitTrigger;   // Animator에 hitTrigger 파라미터가 실제로 있는지(없으면 SetTrigger가 경고를 뱉는다)
    private bool  _inHitAnim;
    private float _hitAnimEnd;

    private float _currentHp;
    private float _lastHitTime;

    private readonly Queue<(float time, float damage)> _damageLog = new();
    private float _damageInWindow;

    // 디버프 아이콘 행
    private readonly List<BuffViewItem> _statusUiBuf = new();
    private bool _statusUiWasEmpty = true;

    // 실시간 데미지 분석 집계 (교전 단위)
    private float _lastHit;
    private bool  _lastHitCrit;
    private float _maxHit;
    private int   _hitCount;
    private int   _critCount;
    private float _labelTimer;

    // ── Properties ──────────────────────────────────────────────────

    public bool IsDead => false;

    /// <summary>이 더미를 어떤 격의 상대로 취급할지(랜슬롯 광기 등 등급 기반 효과가 읽는다).</summary>
    public MonsterGrade Grade => grade;

    // ── Lifecycle ───────────────────────────────────────────────────

    private void Awake()
    {
        _currentHp = maxHp;

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        ApplyHitLayer();
        ResolveHitTrigger();
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

        // 상태 이름으로 피격을 재생한 경우, 일정 시간 뒤 대기 상태로 되돌린다
        // (Trigger 방식과 달리 컨트롤러가 알아서 복귀시켜 주지 않는다 → 그냥 두면 피격 포즈로 굳는다).
        if (_inHitAnim && Time.time >= _hitAnimEnd)
        {
            _inHitAnim = false;
            if (animator != null && !string.IsNullOrEmpty(idleStateName))
                animator.CrossFade(idleStateName, 0.15f);
        }

        PruneDpsWindow();

        // 라벨 문자열 조립은 매 프레임 GC를 피해 저주기로만 갱신.
        _labelTimer -= Time.deltaTime;
        if (_labelTimer <= 0f)
        {
            _labelTimer = LabelRefreshInterval;
            RefreshAnalysisLabel();
            RefreshStatusUi();
        }
    }

    /// <summary>
    /// 더미에 걸린 상태이상을 HP바 디버프 행에 표시한다.
    /// 더미는 MonsterBase가 아니라 상태 수신기가 없다 — 지금 붙을 수 있는 건 화상(독립 MonoBehaviour)뿐.
    /// </summary>
    private void RefreshStatusUi()
    {
        if (_hpBar == null) return;

        _statusUiBuf.Clear();
        if (TryGetComponent<MonsterBurnHandler>(out var burn) && burn.Remaining > 0f)
            _statusUiBuf.Add(RelicFairy.Monster.MonsterStatusReceiver.MakeItem(
                "burn", 1, burn.Remaining01, burn.Remaining));

        if (_statusUiBuf.Count == 0 && _statusUiWasEmpty) return;
        _statusUiWasEmpty = _statusUiBuf.Count == 0;

        _hpBar.SetStatuses(_statusUiBuf);
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

        // 속성·DoT 등 2차 피해는 넉백 0으로 들어온다
        // (CombatQuery.DealSynergyDamage / MonsterBurnHandler.DealDot의 비-MonsterBase 폴백).
        // 실제 몬스터는 이 경로가 TakeSynergyDamage라 GetHitState 전환이 없다 —
        // 더미도 똑같이 피격 모션을 내지 않아야 화상 틱마다 경직처럼 보이지 않는다.
        if (knockbackMultiplier > 0f)
            PlayHitReaction();

        _hpBar?.UpdateHP((int)_currentHp, (int)maxHp);

        // 데미지 팝업 (허수아비도 일관 표시)
        DamagePopupSpawner.Spawn(transform.position + Vector3.up * (hpBarHeadOffset + 0.9f), amount, isCrit, GetInstanceID());
    }

    // ── Private Methods ──────────────────────────────────────────────

    /// <summary>
    /// 콜라이더를 MonsterHit 레이어로 옮긴다.
    ///
    /// 플레이어 근접 판정(ColliderInstance)은 환경 콜라이더가 질의 버퍼를 채우는 걸 막으려고
    /// <b>MonsterHit 레이어만</b> 훑는다. 몬스터는 MonsterBase가 런타임에 자동으로 얹어주지만
    /// 더미는 MonsterBase가 아니다 — 여기서 직접 얹지 않으면 <b>때려도 아예 안 맞는다</b>.
    /// </summary>
    private void ApplyHitLayer()
    {
        int hitLayer = LayerMask.NameToLayer("MonsterHit");
        if (hitLayer < 0) return;   // 레이어 미정의 → 기존 레이어 유지(회귀 0)

        var cols = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
            cols[i].gameObject.layer = hitLayer;
    }

    /// <summary>
    /// Animator에 hitTrigger 파라미터가 실제로 존재하는지 미리 확인해 캐싱한다.
    /// 없는데 SetTrigger를 부르면 Unity가 매 타격마다 경고를 뱉는다(몬스터 컨트롤러는 대개 파라미터가 없다).
    /// </summary>
    private void ResolveHitTrigger()
    {
        _hasHitTrigger = false;
        if (animator == null || string.IsNullOrEmpty(hitTrigger)) return;

        _hitTriggerHash = Animator.StringToHash(hitTrigger);

        var ps = animator.parameters;
        for (int i = 0; i < ps.Length; i++)
        {
            if (ps[i].type == AnimatorControllerParameterType.Trigger && ps[i].nameHash == _hitTriggerHash)
            {
                _hasHitTrigger = true;
                return;
            }
        }
    }

    /// <summary>피격 반응 — Trigger가 있으면 그걸, 없으면 상태 이름을 직접 재생한다.</summary>
    private void PlayHitReaction()
    {
        if (animator == null) return;

        if (_hasHitTrigger)
        {
            animator.SetTrigger(_hitTriggerHash);
            return;
        }

        if (string.IsNullOrEmpty(hitStateName)) return;

        animator.CrossFade(hitStateName, 0.05f, 0, 0f);   // 연타 시 처음부터 다시
        _inHitAnim  = true;
        _hitAnimEnd = Time.time + hitReturnDelay;
    }

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
