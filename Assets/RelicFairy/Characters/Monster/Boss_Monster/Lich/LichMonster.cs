using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.UI;
using UnityEngine;
using UnityEngine.Rendering;

namespace RelicFairy.Monster
{
/// <summary>
/// 리치 (Lich) 보스 MonoBehaviour.
/// Chapter 4 최종 보스 — 멀린의 육체를 탈취한 외부 존재.
///
/// ━━ 페이즈 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  Phase 1 (HP 100~40%) : 봉인 상태. 마법 투사체 / 순간이동 타격 패턴.
///  Phase 2 (HP 40~0%)   : 완전 해방. 데스레이 / 영혼 흡수 강화 패턴.
///
/// ━━ 조건 키 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  Lich_Phase1       : HpAboveCondition — 봉인 상태 (HP > 40%)
///  Lich_IsPhase2     : LichBlackboard.IsPhase2 flag — 해방 상태 진입 완료
///  Lich_Phase2Pending: HP ≤ 40% && !IsPhase2 — 페이즈 전환 대기
/// </summary>
public class LichMonster : MonsterBase, IBoss, IBossEntrance
{
    // ── 상수 ─────────────────────────────────────────────────
    private const float Phase2HpThreshold    = 0.4f;
    private const float Phase2SpeedMult      = 1.3f;
    private const float Phase2AttackMult     = 1.25f;
    private const float Phase2HpRestoreRatio = 0.12f; // Phase 2 진입 시 최대 HP의 12% 회복
    private const int   Phase2UnlockAt       = 3;     // (구·디버그) 조우 횟수 기반 해금 임계. 출시 게이트는 BossSealService.
    private const string SealBossId          = "lich"; // 봉인 서비스 키(메타 영구 페이즈2 해금)
    private const float RetreatDuration      = 2.5f;
    private const float Phase2PreviewDuration = 12f; // 2차 조우 Phase 2 미리보기 노출 시간 (GDD: 10~20s)

    // ── MonsterBase 추상 멤버 ─────────────────────────────────
    public  const  string PrefabAddress    = "Lich/Lich";
    protected override string ConfigAddress   => "Lich/LichConfig";
    protected override string DataAddress     => string.Empty;
    protected override string HeadBoneName    => null;
    protected override float  HPBarHeadOffset => 0.25f;
    protected override bool   UseWorldHPBar   => false;

    // ── IBoss ─────────────────────────────────────────────────
    public float HpRatio =>
        (_runtime != null && _config != null && _config.stat.maxHp > 0)
        ? (float)_runtime.CurrentHp / _config.stat.maxHp
        : 1f;

    public BossAttackBlackboard Blackboard => _lichBB;

    // ── 공개 접근 ─────────────────────────────────────────────
    public LichBlackboard LichBB => _lichBB;

    // ── 내부 필드 ─────────────────────────────────────────────
    private LichBlackboard         _lichBB;
    private BossPatternRunner      _runner;
    private BossPatternContext     _patternCtx;
    private LichFormController     _formController;
    private LichMovementController _movementController;
    private LichDormantState       _dormantState;
    private bool                   _pendingTriggerEntrance;
    private bool                   _prevPatternActive; // 패턴 종료 감지용
    private GameObject             _spawnedFog;
    private CancellationTokenSource _atmosphereCts;
    private bool                   _lightingChanged;
    private AmbientMode            _originalAmbientMode;
    private Color                  _originalAmbientColor;
    private float                  _originalMainLightIntensity;
    private Color                  _originalMainLightColor;

    public LichMovementController MovementController => _movementController;

    /// <summary>스폰 지점 Y — 해골 소환 등 지면 높이 추정에 사용.</summary>
    public float SpawnGroundY => _movementController != null ? _movementController.GroundY : transform.position.y;

    [Header("── 등장 연출 ──────────────────────────────────")]
    [Tooltip("Appear 애니메이션 종료 후 전투 진입까지 대기 시간 (초). Appear 클립 길이와 맞춘다.")]
    [SerializeField] private float _entranceDuration = 4f;
    [Tooltip("플레이어 감지 반경 (m). 방 입장 시 자연스럽게 감지되도록 방 크기에 맞게 설정한다.")]
    [SerializeField] private float _detectionRange = 20f;

    [Header("── 주변 연출 ──────────────────────────────────")]
    [Tooltip("보스 중심 바닥에 독립 스폰할 포그 프리팹. 보스와 함께 이동하지 않고 월드에 고정된다.")]
    [SerializeField] private GameObject _groundFogPrefab;

    [Header("── 연출 — 보스방 라이팅 ─────────────────────────")]
    [Tooltip("등장 시 전환할 주변광. 어둡고 강렬한 보라색 분위기.")]
    [SerializeField] private Color _bossAmbientColor   = new Color(0.04f, 0.01f, 0.07f);
    [Tooltip("주 조명 강도 배율. 0에 가까울수록 더 어두워짐.")]
    [SerializeField, Range(0f, 1f)] private float _mainLightMult = 0.25f;
    [Tooltip("주 조명 색상 틴트 (어두운 보라).")]
    [SerializeField] private Color _mainLightTint      = new Color(0.55f, 0.25f, 1.0f);
    [Tooltip("라이팅 전환 시간 (초).")]
    [SerializeField] private float _lightingTransition = 2.5f;

    [Header("── 패턴 가이드 (SkillIndicator) ──────────────")]
    [Tooltip("원형/AoE 텔레그래프 머티리얼 (taecg/SkillIndicator/Circle). 비우면 프리미티브로 폴백.")]
    [SerializeField] private Material _circleGuideMaterial;
    [Tooltip("직선 빔 텔레그래프 머티리얼 (taecg/SkillIndicator/Arrow). 비우면 프리미티브로 폴백.")]
    [SerializeField] private Material _arrowGuideMaterial;

#if UNITY_EDITOR
    [Header("── 테스트 전용 (빌드 제외) ──────────────────")]
    [SerializeField] private bool _debugOverrideEncounter;
    [Tooltip("시작 조우 횟수. 3 이상이면 Phase 2 해금. 전투가 끝날 때마다 자동으로 +1됨.")]
    [SerializeField] private int  _debugEncounterCount = 1;
    [Tooltip("true: DormantState를 건너뛰고 즉시 전투 진입. BossRoomController 없는 단독 테스트에 사용.")]
    [SerializeField] private bool _debugSkipEntrance;
    [Tooltip("true: 전투 시작 즉시 Phase2 상태로 강제 진입 (DarkRain 등 Phase2 패턴 테스트용). _debugSkipEntrance가 true일 때만 동작.")]
    [SerializeField] private bool _debugForcePhase2;
    private int _debugSessionCount; // 플레이 중 자동 진행되는 세션 카운터
#endif

    /// <summary>
    /// 3차+ 조우부터 Phase 2가 영구 해금됨. (Lich_Phase2Pending 조건의 게이트)
    ///
    /// ── 분기 지점 ───────────────────────────────────────────────────
    ///  • 테스트(에디터): _debugOverrideEncounter 켜고 _debugEncounterCount로 제어.
    ///      3 이상 → 처음부터 Phase2 노출 / 1~2 → 봉인(Phase1)만, 사망 시 자동 +1로 전 페이즈 순환.
    ///  • 출시(빌드): 아래 BackendGameData.lichEncounterCount >= Phase2UnlockAt 게이트가 적용됨.
    ///      해금 조건(횟수/시점)을 바꾸려면 Phase2UnlockAt 또는 이 반환식을 조정한다.
    /// </summary>
    public bool IsPhase2Unlocked
    {
        get
        {
#if UNITY_EDITOR
            if (_debugOverrideEncounter) return _debugSessionCount >= Phase2UnlockAt;
#endif
            // 출시 게이트 — 초회 클리어 시 봉인이 해제되면 이후 조우부터 페이즈2 해금(메타 영구).
            return BossSealService.IsSealBroken(SealBossId);
        }
    }

    // ── 커스텀 ICondition ─────────────────────────────────────

    // BuiltConditions는 공유 ScriptableObject에 저장되므로 생성 시 _lichBB를 캡처하면
    // 마지막으로 BuildConditions()를 호출한 풀 인스턴스의 블랙보드를 참조하게 된다.
    // 평가 시점에 ctx.Boss로 활성 Lich를 조회해 항상 올바른 블랙보드를 사용한다.
    private sealed class LichPhase1Condition : ICondition
    {
        // Phase2 미진입(봉인) 상태이면 Phase1 패턴을 허용한다.
        // Phase2 전환은 HP ≤ 40%(Lich_Phase2Pending) 조건이 전담하며 SealBreaker와는 무관하다.
        public bool Evaluate(BossPatternContext ctx)
            => !((ctx.Boss as LichMonster)?.LichBB?.IsPhase2 ?? false);
    }

    private sealed class LichPhase2Condition : ICondition
    {
        public bool Evaluate(BossPatternContext ctx)
            => (ctx.Boss as LichMonster)?.LichBB?.IsPhase2 ?? false;
    }

    private sealed class LichPhase2PendingCondition : ICondition
    {
        // HP 40% 이하 도달 시 Phase2Entry 발동. SealBreaker와 완전히 독립된 조건.
        // Phase2는 해금(3차+ 조우, 디버그 시 _debugSessionCount) 이후에만 전환된다.
        // 해금 전(1·2차)에는 봉인 상태(Phase1)로만 싸우고 HP 0에서 퇴각한다.
        public bool Evaluate(BossPatternContext ctx)
        {
            var lich = ctx.Boss as LichMonster;
            return lich != null
                && lich.IsPhase2Unlocked
                && lich.HpRatio <= Phase2HpThreshold
                && !(lich.LichBB?.IsPhase2 ?? false);
        }
    }

    // LichMovementController가 이동을 전담하므로 ChaseState 추적 로직은 불필요.
    // 애니메이션만 재생하고 이동은 완전히 LichMovementController에 위임한다.
    private sealed class LichCombatState : IMonsterState
    {
        public void Enter(MonsterContext ctx)
        {
            var a = ctx.Animation;
            if (ctx.Animator != null && !string.IsNullOrEmpty(a.chaseStateName))
                ctx.Animator.CrossFade(a.chaseStateName, a.crossFadeDuration);
        }

        public void Update(MonsterContext ctx) { }
        public void Exit(MonsterContext ctx)   { }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 초기화
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected override void RegisterStates()
    {
        base.RegisterStates();
        _fsm.RegisterAs<ChaseState>(new LichCombatState());
    }

    protected override void OnInitialized()
    {
        var bossConfig = _config as BossConfigSO;
        if (bossConfig == null)
        {
            Debug.LogError("[LichMonster] Config이 BossConfigSO가 아닙니다!", this);
            return;
        }

        // 패턴 텔레그래프 비주얼 주입 (미할당 시 PatternGuideHelper가 프리미티브로 폴백)
        PatternGuideHelper.SetMaterials(_circleGuideMaterial, _arrowGuideMaterial);

        _formController = GetComponentInChildren<LichFormController>();
        _formController?.HideAll(); // 등장 연출 전 숨김 — TriggerEntrance()에서 디졸브 인

        // 공중 이동 컨트롤러 초기화 (NavMeshAgent 비활성화 후 직접 Transform 제어)
        _movementController = GetComponent<LichMovementController>();
        if (_movementController == null)
            Debug.LogWarning("[LichMonster] LichMovementController 컴포넌트가 없습니다. 프리팹에 추가하세요.", this);

        if (TryGetComponent<UnityEngine.AI.NavMeshAgent>(out var navAgent))
            navAgent.enabled = false;

        _lichBB = new LichBlackboard();

        _patternCtx = new BossPatternContext
        {
            Boss       = this,
            Ctx        = _ctx,
            Blackboard = _lichBB,
        };

        BuildConditions(bossConfig);
        InitializePatterns(bossConfig);

        _runner = new BossPatternRunner(
            bossConfig,
            _patternCtx,
            isAlive:     () => _runtime != null && !_runtime.IsDead && !IsPlayerDead(),
            isInRange:   () => _runtime?.PlayerTarget != null,
            changeState: s  => ChangeState(s),
            onExecuted:  p  => { _lichBB.LastPatternTag = p.patternTag; _lichBB.NormalModeTimer = 0f; });

        _movementController?.Init(_lichBB);

        BindBossHud();

#if UNITY_EDITOR
        if (_debugOverrideEncounter)
        {
            _debugSessionCount = _debugEncounterCount;
            OnDied += Editor_HandleDied;
            Debug.Log($"[LichMonster] 테스트 모드 시작 — {_debugSessionCount}차 조우 (Phase2Unlocked={IsPhase2Unlocked})");
        }
#endif

        // 등장 대기 상태로 진입 — Appear 애니메이션은 TriggerEntrance() 호출 시 시작
        _dormantState = new LichDormantState(_entranceDuration, _detectionRange);

#if UNITY_EDITOR
        if (_debugSkipEntrance)
        {
            Debug.Log("[LichMonster] debugSkipEntrance — DormantState 생략, ChaseState 즉시 진입");
            _dormantState = null; // IsActive 가드가 Update를 차단하지 않도록 null 처리
            _formController?.ApplyForm(LichForm.Phase1); // 디버그: 등장 연출 없이 즉시 표시
            ChangeState<ChaseState>();
            if (_debugForcePhase2)
            {
                ApplyPhase2Buffs(); // 내부에서 IsPhase2 가드로 중복 적용 방지
                // HP를 임계값 아래로 설정 — Phase1 엔트리(HP > 40%) 조건이 false가 되도록
                if (_runtime != null && _config != null)
                    _runtime.CurrentHp = Mathf.RoundToInt(_config.stat.maxHp * (Phase2HpThreshold - 0.05f));
                Debug.Log("[LichMonster] debugForcePhase2 — Phase2 강제 적용 완료");
            }
            return;
        }
#endif

        ChangeState(_dormantState);

        // InitAsync 완료 전에 BossSpawner가 TriggerEntrance()를 호출한 경우 즉시 적용
        if (_pendingTriggerEntrance)
        {
            _pendingTriggerEntrance = false;
            _dormantState.TriggerEntrance(_ctx);
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 매 프레임
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected override void Update()
    {
        base.Update();

        if (_lichBB == null) return;
        if (_runtime != null && _runtime.IsDead) return;

        // 등장 연출 중에는 패턴 러너와 무브먼트 완전 정지
        if (_dormantState != null && _dormantState.IsActive) return;

        float dt = Time.deltaTime;

        _lichBB.TickCooldowns(dt);

        if (_runner != null)
        {
            if (_runner.IsPatternActive)
                _lichBB.NormalModeTimer = 0f;
            else
                _lichBB.NormalModeTimer += dt;
        }

        _movementController?.Tick(dt, _runtime?.PlayerTarget);

        // 패턴이 active → inactive 로 전환된 시점에 취약 구간 알림
        bool nowPattern = _runner?.IsPatternActive ?? false;
        if (_prevPatternActive && !nowPattern)
            _movementController?.NotifyPatternEnded();
        _prevPatternActive = nowPattern;

        _runner?.Tick(dt);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 풀 재사용
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected override void OnEnable()
    {
        base.OnEnable();
        _runner?.Reset();
        _lichBB?.Reset();
        _movementController?.OnRecycled();
        _pendingTriggerEntrance = false;
        _lightingChanged        = false;

        // 풀 재사용: 숨김 후 등장 연출 재진입 (TriggerEntrance에서 다시 디졸브 인)
        _formController?.HideAll();
        if (_dormantState != null)
            ChangeState(_dormantState);

        BindBossHud();
    }

    protected override void OnDisable()
    {
        UnbindBossHudIfBound();
        if (_spawnedFog != null) { Destroy(_spawnedFog); _spawnedFog = null; }
        _atmosphereCts?.Cancel();
        _atmosphereCts?.Dispose();
        _atmosphereCts = null;
        RestoreLighting();
        base.OnDisable();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 보스룸 전용: 플레이어가 있으면 항상 추적
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    public override bool ShouldStartChase(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return false;
        return !IsPlayerDead();
    }

    // Lich는 BossPatternRunner 전용 — MonsterBase 기본 근접 공격 완전 비활성화
    public override bool ShouldEnterAttackReady(MonsterContext ctx) => false;

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // IBossEntrance
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>LichDormantState가 플레이어를 감지했을 때 발행 — BossRoomController가 카메라 팬을 시작한다.</summary>
    public override bool HasEntranceAnimation => true;

    public event System.Action OnEntranceRequested;

    /// <summary>Appear 연출이 끝나고 전투가 시작되기 직전 발행 — 플레이어 입력 복구 등에 사용한다.</summary>
    public event System.Action OnCombatReady;

    /// <summary>LichDormantState가 감지 직후 호출 — OnEntranceRequested 이벤트 발행.</summary>
    internal void FireEntranceRequest() => OnEntranceRequested?.Invoke();

    /// <summary>LichDormantState가 ChaseState 전환 직전 호출 — OnCombatReady 이벤트 발행.</summary>
    internal void FireCombatReady()
    {
        _runner?.EnsureMinBreakCooldown(3f);
        OnCombatReady?.Invoke();
        RaiseBossCombatReady();
    }

    /// <summary>BossRoomController가 카메라 팬 완료 후 호출 — Appear 애니메이션 + 보스 이름 UI 시작.</summary>
    public void TriggerEntrance()
    {
        if (_dormantState != null)
            _dormantState.TriggerEntrance(_ctx);
        else
            _pendingTriggerEntrance = true; // InitAsync 완료 전 호출된 경우 OnInitialized에서 적용
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 페이즈 2 진입
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>LichPhase2EntryPatternSO 완료 후 호출. 속도·공격 속도·패턴 딜레이 적용.</summary>
    public void ApplyPhase2Buffs()
    {
        if (_lichBB == null || _lichBB.IsPhase2) return;

        _lichBB.SetPhase2();

        if (_runtime != null)
            _runtime.SpeedMultiplier = Phase2SpeedMult;

        _lichBB.AttackSpeedMult = Phase2AttackMult;

        // HP 회복 (최대 HP의 12%)
        if (_runtime != null && _config != null)
        {
            int restore = Mathf.RoundToInt(_config.stat.maxHp * Phase2HpRestoreRatio);
            _runtime.CurrentHp = Mathf.Min(_runtime.CurrentHp + restore, _config.stat.maxHp);
        }

        UI_BossBark.Show("영혼이여. 너도 결국 복사될 것이다.", BossBarkType.PhaseAnnounce);

        // Phase2 전환: 책·의복 즉시 숨김, 낫 디졸브 인
        if (_formController != null)
            _formController.DissolveInFormAsync(LichForm.Phase2, destroyCancellationToken).Forget();

        _lichBB.BreakDurationMinOverride = 0.3f;
        _lichBB.BreakDurationMaxOverride = 0.8f;

        Debug.Log($"[Lich] Phase 2 해방 — HP={_runtime?.CurrentHp} ratio={HpRatio:F2}", this);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 내부 헬퍼
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>HP 0 도달 시 호출. 조우 횟수에 따라 퇴각 or 사망.</summary>
    protected override void OnFatalDamage()
    {
        // 전투 종료 — 보스보다 오래 남는 생존 해골 정리(퇴각·사망 공통).
        LichSkeletonMonster.DespawnAll();

        if (!IsPhase2Unlocked)
        {
            // 봉인 상태 격파 = 초회 클리어 → 봉인 해제(메타 영구). 이후 조우부터 페이즈2가 열린다.
            // (해제는 다음 조우부터 반영되므로 지금 판정 흐름은 그대로 '봉인 퇴각' 연출로 진행)
            BossSealService.BreakSeal(SealBossId);

            // 봉인 상태: 퇴각(사라짐) 연출 후 보스방 완료
            DoRetreatAsync(destroyCancellationToken).Forget();
            return;
        }
        // 3차+ 조우 완전 격파 — 멀린 내레이션 후 실제 사망 처리
        UI_BossBark.Show("리치가 쓰러졌다. 하지만... 이건 끝이 아니야.", BossBarkType.MerlinNarration);
        base.OnFatalDamage();
    }

    private async UniTaskVoid DoRetreatAsync(System.Threading.CancellationToken ct)
    {
        // 진행 중이던 패턴 특수 상태를 빠져나가 Exit(가이드·빔·VFX 정리)를 보장한다.
        // 사망 시 Update가 정지하므로 패턴이 스스로 종료하지 못해 월드 오브젝트가 잔존하는 문제 방지.
        ChangeState<ChaseState>();

        _movementController?.SetLocked(true);
        _runner?.Reset();

#if UNITY_EDITOR
        int count = _debugOverrideEncounter
            ? _debugSessionCount
            : BackendGameData.Instance?.Data?.lichEncounterCount ?? 1;
#else
        int count = BackendGameData.Instance?.Data?.lichEncounterCount ?? 1;
#endif

        // 2차 조우: Phase 2 형태 잠깐 노출 후 재봉인 (GDD §8.3)
        if (count == 2)
        {
            UI_BossBark.Show("균열이 심화됐다. 다음엔 막지 못할 것이다.", BossBarkType.MerlinNarration);
            if (await DoPhase2PreviewAsync(ct) == false) return;
        }
        else
        {
            string bark = count <= 1 ? "봉인에 균열이 생겼어." : "균열이 심화됐다. 다음엔 막지 못할 것이다.";
            UI_BossBark.Show(bark, BossBarkType.MerlinNarration);
        }

        _ctx.Animator?.CrossFade("Die", 0.2f);

        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(RetreatDuration), cancellationToken: ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

#if UNITY_EDITOR
        if (_debugOverrideEncounter)
        {
            Editor_AdvanceAndRestart(ct);
            return;
        }
#endif
        RaiseDied();
        gameObject.SetActive(false);
    }

    /// <summary>
    /// 2차 조우 전용 — Phase 2 비주얼 폼으로 잠깐 전환 후 재봉인.
    /// 취소 시 false 반환.
    /// </summary>
    private async UniTask<bool> DoPhase2PreviewAsync(System.Threading.CancellationToken ct)
    {
        // Phase 2 외형만 전환 (블랙보드·스탯은 건드리지 않음)
        _formController?.ApplyForm(LichForm.Phase2);
        _ctx.Animator?.CrossFade("Phase2Entry", 0.1f);
        UI_BossBark.Show("...다음엔, 이 균열이 봉인을 삼킬 것이다.", BossBarkType.MerlinNarration);

        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(Phase2PreviewDuration), cancellationToken: ct);
        }
        catch (OperationCanceledException)
        {
            _formController?.ApplyForm(LichForm.Phase1);
            return false;
        }

        // 재봉인 — Phase 1 외형으로 복귀
        _formController?.ApplyForm(LichForm.Phase1);
        _ctx.Animator?.CrossFade("Die", 0.1f);
        return true;
    }

    /// <summary>LichDormantState.TriggerEntrance에서 호출 — Appear 애니메이션 시점에 Phase1 장비 디졸브 인.</summary>
    public void ShowPhase1Form()
    {
        if (_formController == null) return;
        _formController.DissolveInFormAsync(LichForm.Phase1, destroyCancellationToken).Forget();
    }

    /// <summary>LichDormantState.TriggerEntrance에서 호출 — 포그 스폰 + 라이팅 전환.</summary>
    internal void TriggerEntranceAtmosphere()
    {
        _atmosphereCts?.Cancel();
        _atmosphereCts?.Dispose();
        _atmosphereCts = CancellationTokenSource.CreateLinkedTokenSource(
            destroyCancellationToken, ActivationToken);
        EntranceAtmosphereAsync(_atmosphereCts.Token).Forget();
    }

    private async UniTaskVoid EntranceAtmosphereAsync(CancellationToken ct)
    {
        // ── 포그 스폰 ──────────────────────────────────────────
        if (_groundFogPrefab != null && _spawnedFog == null)
        {
            var fogPos = new Vector3(transform.position.x, _groundFogPrefab.transform.position.y, transform.position.z);
            _spawnedFog = Instantiate(_groundFogPrefab, fogPos, _groundFogPrefab.transform.rotation);

            // VFXLossyTransformBinder.Target이 null이면 파티클이 월드 원점에 스폰됨
            // → Lich Transform으로 연결해 올바른 위치에 스폰
            foreach (var binder in _spawnedFog.GetComponentsInChildren<INab.CommonVFX.VFXLossyTransformBinder>(true))
                binder.Target = transform;

            foreach (var vfx in _spawnedFog.GetComponentsInChildren<UnityEngine.VFX.VisualEffect>(true))
                vfx.Play();
            foreach (var ps in _spawnedFog.GetComponentsInChildren<ParticleSystem>(true))
                ps.Play(withChildren: true);
        }

        // ── 라이팅 원본 저장 ────────────────────────────────────
        _originalAmbientMode      = RenderSettings.ambientMode;
        _originalAmbientColor     = RenderSettings.ambientLight;
        var sun = RenderSettings.sun;
        _originalMainLightIntensity = sun != null ? sun.intensity : 1f;
        _originalMainLightColor     = sun != null ? sun.color    : Color.white;
        _lightingChanged = true;

        // Flat 모드로 전환해야 ambientLight 직접 제어 가능
        RenderSettings.ambientMode = AmbientMode.Flat;

        // ── 라이팅 어둡게 전환 ──────────────────────────────────
        float t = 0f;
        try
        {
            while (t < _lightingTransition)
            {
                t += Time.deltaTime;
                float f = Mathf.Clamp01(t / _lightingTransition);
                RenderSettings.ambientLight = Color.Lerp(_originalAmbientColor, _bossAmbientColor, f);
                if (sun != null)
                {
                    sun.intensity = Mathf.Lerp(_originalMainLightIntensity,
                                               _originalMainLightIntensity * _mainLightMult, f);
                    sun.color     = Color.Lerp(_originalMainLightColor, _mainLightTint, f);
                }
                await UniTask.Yield(ct);
            }
            RenderSettings.ambientLight = _bossAmbientColor;
            if (sun != null)
            {
                sun.intensity = _originalMainLightIntensity * _mainLightMult;
                sun.color     = _mainLightTint;
            }
        }
        catch (OperationCanceledException)
        {
            RestoreLighting();
        }
    }

    private void RestoreLighting()
    {
        if (!_lightingChanged) return;
        _lightingChanged = false;
        RenderSettings.ambientMode  = _originalAmbientMode;
        RenderSettings.ambientLight = _originalAmbientColor;
        var sun = RenderSettings.sun;
        if (sun != null)
        {
            sun.intensity = _originalMainLightIntensity;
            sun.color     = _originalMainLightColor;
        }
    }

    /// <summary>LichDormantState.Enter에서 호출 — 플레이어가 실제 보스방에 진입한 시점에 조우 기록.</summary>
    public void StartEncounterRecord() =>
        RecordEncounterAsync(destroyCancellationToken).Forget();

    private async UniTaskVoid RecordEncounterAsync(System.Threading.CancellationToken ct)
    {
#if UNITY_EDITOR
        if (_debugOverrideEncounter)
        {
            Debug.Log($"[LichMonster] 테스트 모드 — 조우 기록 생략 (sessionCount={_debugSessionCount})");
            return;
        }
#endif
        if (BackendGameData.Instance == null) return;
        try
        {
            await BackendGameData.Instance.RecordLichEncounterAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Debug.LogWarning($"[LichMonster] 조우 기록 저장 실패: {e.Message}");
        }
    }

#if UNITY_EDITOR
    private void Editor_HandleDied(MonsterBase _)
    {
        // Phase 2 실제 사망 경로 — DieState에서 RaiseDied() 호출 시 진입
        OnDied -= Editor_HandleDied;
        Editor_DelayedAdvanceAsync(destroyCancellationToken).Forget();
    }

    private async UniTaskVoid Editor_DelayedAdvanceAsync(System.Threading.CancellationToken ct)
    {
        try { await UniTask.Delay(TimeSpan.FromSeconds(3f), cancellationToken: ct); }
        catch (OperationCanceledException) { return; }
        Editor_AdvanceAndRestart(ct);
    }

    private void Editor_AdvanceAndRestart(System.Threading.CancellationToken ct)
    {
        int prev = _debugSessionCount;
        _debugSessionCount++;
        Debug.Log($"[LichMonster] 테스트 — {prev}차 조우 완료 → {_debugSessionCount}차 시작 (Phase2Unlocked={IsPhase2Unlocked})");
        Editor_ResetFight();
    }

    /// <summary>에디터 전용 — 보스 상태를 초기화해 현재 세션 카운터 기준으로 재시작.</summary>
    [ContextMenu("테스트: 전투 리셋 (카운터 유지)")]
    private void Editor_ResetFight()
    {
        if (!Application.isPlaying) return;
        OnDied -= Editor_HandleDied;

        _runner?.Reset();
        _lichBB?.Reset();
        _movementController?.OnRecycled();
        if (_runtime != null)
        {
            _runtime.IsDead    = false;
            _runtime.CurrentHp = _config?.stat.maxHp ?? 100;
        }
        _formController?.ApplyForm(LichForm.Phase1);
        gameObject.SetActive(true);
        ChangeState<ChaseState>();

        OnDied += Editor_HandleDied;
        Debug.Log($"[LichMonster] 전투 리셋 — {_debugSessionCount}차 조우 (Phase2Unlocked={IsPhase2Unlocked})");
    }

    /// <summary>에디터 전용 — 세션 카운터를 Inspector 초기값으로 되돌리고 재시작.</summary>
    [ContextMenu("테스트: 세션 카운터 리셋 (1차부터)")]
    private void Editor_ResetSession()
    {
        if (!Application.isPlaying) return;
        _debugSessionCount = _debugEncounterCount;
        Editor_ResetFight();
        Debug.Log($"[LichMonster] 세션 리셋 — {_debugSessionCount}차부터 재시작");
    }
#endif

    private void InitializePatterns(BossConfigSO config)
    {
        if (config.patternEntries == null) return;
        foreach (var entry in config.patternEntries)
        {
            if (entry?.patterns == null) continue;
            foreach (var p in entry.patterns)
                p?.Initialize(_patternCtx);
        }
    }

    private void BuildConditions(BossConfigSO config)
    {
        if (config.patternEntries == null) return;
        foreach (var entry in config.patternEntries)
        {
            if (entry == null) continue;
            if (entry.conditions == null || entry.conditions.Count == 0)
            {
                entry.BuiltConditions = null;
                continue;
            }

            var built = new ICondition[entry.conditions.Count];
            for (int i = 0; i < entry.conditions.Count; i++)
                built[i] = BuildSingleCondition(entry.conditions[i], config);
            entry.BuiltConditions = built;
        }
    }

    private ICondition BuildSingleCondition(BossConditionKey key, BossConfigSO config)
    {
        return key switch
        {
            BossConditionKey.Phase2              => new HpBelowCondition(config.condPhase2HpThreshold),
            BossConditionKey.Dist_Close          => new MaxRangeCondition(config.condDistClose),
            BossConditionKey.Dist_Far            => new MinRangeCondition(config.condDistFar),
            BossConditionKey.TimePressure        => new NormalModeTimerCondition(config.condTimePressureSecs),
            BossConditionKey.Lich_Phase1         => new LichPhase1Condition(),
            BossConditionKey.Lich_IsPhase2       => new LichPhase2Condition(),
            BossConditionKey.Lich_Phase2Pending  => new LichPhase2PendingCondition(),
            _                                    => new AlwaysTrue(),
        };
    }
}
}
