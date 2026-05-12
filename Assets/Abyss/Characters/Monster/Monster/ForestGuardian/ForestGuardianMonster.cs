using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// ForestGuardian 보스 MonoBehaviour.
///
/// ━━ 페이즈 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  1페이즈 (HP 100~50%) : TreantE 머티리얼, 이동속도 1.0x, 패턴 딜레이 0.8~1.8s
///  2페이즈 (HP 50~0%)   : TreantD 머티리얼, 이동속도 1.25x, 패턴 딜레이 0.2~0.7s
///
/// ━━ 그로기 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  일반 피격 시 강인도 8 감소.
///  BigAttackWindow 중 피격 시 추가 40 감소.
///  강인도 0 → GroggyState 진입, 3초 경직 후 강인도 복원.
///
/// ━━ 패턴 가중치 회복 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  패턴 실행 직후 weight=0, 5~10초에 걸쳐 원래값으로 복원.
/// </summary>
public class ForestGuardianMonster : MonsterBase, IBoss
{
    // ── 상수 ─────────────────────────────────────────────────
    private const string Phase2BodyMatAddress  = "ForestGuardian/Materials/TreantD";
    private const string Phase2LimbMatAddress  = "ForestGuardian/Materials/TreantDLimbs";
    private const float  Phase2SpeedMultiplier = 1.25f;
    private const float  Phase2AttackSpeedMult = 1.2f;
    private const float  Phase2BreakMin        = 0.2f;
    private const float  Phase2BreakMax        = 0.7f;
    private const float  Phase2HpThreshold     = 0.5f;
    private const float  WeightRecoveryMin     = 5f;
    private const float  WeightRecoveryMax     = 10f;

    // ── Inspector ────────────────────────────────────────────
    [Header("ForestGuardian — 렌더러")]
    [Tooltip("body 머티리얼(index 0)을 가진 Renderer 배열")]
    [SerializeField] private Renderer[] _bodyRenderers;
    [Tooltip("limbs 머티리얼(index 1)을 가진 Renderer 배열 (body와 동일 오브젝트여도 무방)")]
    [SerializeField] private Renderer[] _limbRenderers;

    // ── MonsterBase 추상 멤버 ─────────────────────────────────
    protected override string ConfigAddress  => "ForestGuardian/ForestGuardianConfig";
    protected override string DataAddress    => string.Empty;
    protected override string HeadBoneName   => null;
    protected override float  HPBarHeadOffset=> 0.3f;
    protected override bool   UseWorldHPBar  => false;

    // ── IBoss ─────────────────────────────────────────────────
    public float HpRatio =>
        (_runtime != null && _config != null && _config.stat.maxHp > 0)
        ? (float)_runtime.CurrentHp / _config.stat.maxHp
        : 1f;

    public BossAttackBlackboard Blackboard => _coreBB;

    // ── ForestGuardian 공개 접근 ──────────────────────────────
    public ForestGuardianBlackboard FGBlackboard => _fgBB;

    // ── 내부 필드 ─────────────────────────────────────────────
    private ForestGuardianBlackboard _fgBB;
    private BossAttackBlackboard     _coreBB;
    private BossPatternRunner        _runner;
    private BossPatternContext       _patternCtx;

    // ── 패턴 가중치 회복 ──────────────────────────────────────
    private struct WeightRecoveryEntry
    {
        public BossPatternSO Pattern;
        public float         OriginalWeight;
        public float         Timer;
        public float         Duration;
    }

    private readonly List<WeightRecoveryEntry> _weightRecoveries = new();

    // ── 커스텀 ICondition ─────────────────────────────────────

    private sealed class FGPhase2Condition : ICondition
    {
        private readonly ForestGuardianBlackboard _bb;
        public FGPhase2Condition(ForestGuardianBlackboard bb) => _bb = bb;
        public bool Evaluate(BossPatternContext ctx) => _bb.IsPhase2;
    }

    private sealed class FGGroggyCondition : ICondition
    {
        private readonly ForestGuardianBlackboard _bb;
        public FGGroggyCondition(ForestGuardianBlackboard bb) => _bb = bb;
        public bool Evaluate(BossPatternContext ctx) => _bb.IsGroggy;
    }

    private sealed class FGPhaseChangePendingCondition : ICondition
    {
        private readonly ForestGuardianBlackboard _bb;
        public FGPhaseChangePendingCondition(ForestGuardianBlackboard bb) => _bb = bb;
        public bool Evaluate(BossPatternContext ctx)
        {
            var fg = ctx.Boss as ForestGuardianMonster;
            return fg != null && fg.HpRatio <= 0.5f && !_bb.IsPhase2;
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 레이어드 FSM 상태 등록
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected override void RegisterStates()
    {
        base.RegisterStates();
        _fsm.RegisterAs<PatrolState>     (new FGIdleState());
        _fsm.RegisterAs<ChaseState>      (new FGChaseState());
        _fsm.RegisterAs<AttackReadyState>(new FGAttackReadyState());
        _fsm.RegisterAs<AttackState>     (new FGAttackState());
        _fsm.RegisterAs<GetHitState>     (new FGGetHitState());
        _fsm.RegisterAs<DieState>        (new FGDieState());
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 초기화
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected override void OnInitialized()
    {
        var bossConfig = _config as BossConfigSO;
        if (bossConfig == null)
        {
            Debug.LogError("[ForestGuardianMonster] Config 이 BossConfigSO 가 아닙니다!", this);
            return;
        }

        _fgBB   = new ForestGuardianBlackboard();
        _coreBB = new BossAttackBlackboard();

        _patternCtx = new BossPatternContext
        {
            Boss       = this,
            Ctx        = _ctx,
            Blackboard = _coreBB,
        };

        // Inspector 미할당 시 SkinnedMeshRenderer 자동 탐색
        if (_bodyRenderers == null || _bodyRenderers.Length == 0)
            _bodyRenderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        if (_limbRenderers == null || _limbRenderers.Length == 0)
            _limbRenderers = _bodyRenderers;

        BuildConditions(bossConfig);
        InitializePatterns(bossConfig);

        _runner = new BossPatternRunner(
            bossConfig,
            _patternCtx,
            isAlive:     () => _runtime != null && !_runtime.IsDead && !IsPlayerDead() && !(_fgBB?.IsGroggy ?? false),
            isInRange:   () => _runtime?.PlayerTarget != null,
            changeState: s  => ChangeState(s),
            onExecuted:  OnPatternExecuted);

        BindBossHud();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 매 프레임
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected override void Update()
    {
        base.Update();

        if (_fgBB == null || _coreBB == null) return;

        float dt = Time.deltaTime;

        // 블랙보드 틱 (빅어택 윈도우, 그로기 타이머)
        _fgBB.Tick(dt);
        _coreBB.TickCooldowns(dt);

        // 패턴 가중치 회복
        TickWeightRecoveries(dt);

        // 패턴 러너 NormalModeTimer
        if (_runner != null)
        {
            if (_runner.IsPatternActive)
                _coreBB.NormalModeTimer = 0f;
            else
                _coreBB.NormalModeTimer += dt;
        }

        _runner?.Tick(dt);

        // 페이즈 전환 체크 — 패턴 실행 중일 땐 패턴이 직접 트리거하도록 위임
        if (!_fgBB.IsPhase2 && HpRatio < Phase2HpThreshold && !IsInSpecialState)
            EnterPhase2Async().Forget();

        // 그로기 상태 진입
        if (_fgBB.IsGroggy && !IsInSpecialState)
            ChangeState<GetHitState>();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 피격 처리 (강인도)
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    public override void TakeDamage(float amount, UnityEngine.GameObject instigator,
        float knockbackMultiplier = 1f,
        ElementType element = ElementType.None,
        float elementAmount = 0f)
    {
        if (_fgBB != null && instigator != null)
        {
            bool isHeavy = _fgBB.IsBigWindowOpen;
            Vector3 dir  = instigator.transform.position - transform.position;
            _fgBB.SetHitDirection(dir, transform.forward, isHeavy);
        }
        base.TakeDamage(amount, instigator, knockbackMultiplier, element, elementAmount);
    }

    protected override void OnDamageTaken()
    {
        if (_fgBB == null) return;

        bool isHeavy = _fgBB.IsBigWindowOpen;

        // 강인도 데미지 (그로기 판정)
        _fgBB.ApplyToughnessDamage(ForestGuardianBlackboard.NormalHitDamage, isHeavy);

        if (_fgBB.IsGroggy)
        {
            // 그로기 우선 — 방어도 억제 없이 base의 ChangeState<GetHitState>() 에 위임
            return;
        }

        // 방어도 데미지 — 파괴되면 GetHit 방향 경직 발동, 그렇지 않으면 추적 유지
        bool poiseBroke = _fgBB.ApplyPoiseDamage(isHeavy);
        if (!poiseBroke)
            _suppressGetHitThisHit = true;   // base의 ChangeState<GetHitState>() 스킵
        // poiseBroke=true 이면 _suppressGetHitThisHit = false 유지 → base가 GetHitState 진입
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 풀 재사용
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected override void OnEnable()
    {
        base.OnEnable();
        _runner?.Reset();
        _coreBB?.Reset();
        _fgBB?.Reset();
        _weightRecoveries.Clear();
        _phase2Transitioning = false;
        BindBossHud();
    }

    protected override void OnDisable()
    {
        UnbindBossHudIfBound();
        base.OnDisable();
    }

    public void UnbindBossHudIfBoundPublic() => UnbindBossHudIfBound();

    /// <summary>2페이즈 전환을 패턴에서 직접 트리거할 때 사용. 머터리얼·속도 교체 포함.</summary>
    public void TriggerPhase2() => EnterPhase2Async().Forget();

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 보스룸 전용: 플레이어가 있으면 항상 추적
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    public override bool ShouldStartChase(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return false;
        return !IsPlayerDead();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 내부 헬퍼
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

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
            BossConditionKey.Phase2          => new HpBelowCondition(config.condPhase2HpThreshold),
            BossConditionKey.Dist_Close      => new MaxRangeCondition(config.condDistClose),
            BossConditionKey.Dist_Far        => new MinRangeCondition(config.condDistFar),
            BossConditionKey.TimePressure    => new NormalModeTimerCondition(config.condTimePressureSecs),
            BossConditionKey.FG_Phase1             => new HpAboveCondition(config.condPhase2HpThreshold),
            BossConditionKey.FG_Phase2             => new FGPhase2Condition(_fgBB),
            BossConditionKey.FG_PhaseChangePending => new FGPhaseChangePendingCondition(_fgBB),
            BossConditionKey.FG_IsPhase2           => new FGPhase2Condition(_fgBB),
            BossConditionKey.FG_IsGroggy           => new FGGroggyCondition(_fgBB),
            _                                      => new AlwaysTrue(),
        };
    }

    // ── 패턴 실행 콜백 ─────────────────────────────────────────
    private void OnPatternExecuted(BossPatternSO pattern)
    {
        _coreBB.LastPatternTag  = pattern.patternTag;
        _coreBB.NormalModeTimer = 0f;

        // 가중치 즉시 0, 5~10초에 걸쳐 복원 예약
        float original = pattern.weight;
        if (original <= 0f) return;

        pattern.weight = 0f;

        // 기존 회복 항목 제거 — 부분 회복 중 재발동 시 OriginalWeight가 계속 감소하는 버그 방지
        // 이전 항목에 더 높은 원래 값이 있으면 그 값으로 복원
        for (int i = _weightRecoveries.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(_weightRecoveries[i].Pattern, pattern)) continue;
            if (_weightRecoveries[i].OriginalWeight > original)
                original = _weightRecoveries[i].OriginalWeight;
            _weightRecoveries.RemoveAt(i);
        }

        _weightRecoveries.Add(new WeightRecoveryEntry
        {
            Pattern        = pattern,
            OriginalWeight = original,
            Timer          = 0f,
            Duration       = Random.Range(WeightRecoveryMin, WeightRecoveryMax),
        });
    }

    private void TickWeightRecoveries(float dt)
    {
        for (int i = _weightRecoveries.Count - 1; i >= 0; i--)
        {
            var e = _weightRecoveries[i];
            e.Timer += dt;
            float t = Mathf.Clamp01(e.Timer / e.Duration);
            e.Pattern.weight = Mathf.Lerp(0f, e.OriginalWeight, t);

            if (t >= 1f)
            {
                e.Pattern.weight = e.OriginalWeight;
                _weightRecoveries.RemoveAt(i);
            }
            else
            {
                _weightRecoveries[i] = e;
            }
        }
    }

    // ── 페이즈 전환 ────────────────────────────────────────────
    private bool _phase2Transitioning;

    private async UniTaskVoid EnterPhase2Async()
    {
        if (_phase2Transitioning) return;
        _phase2Transitioning = true;

        Debug.Log($"[FG] Phase2 시작 — HP={_runtime?.CurrentHp}/{EffectiveMaxHp} ratio={HpRatio:F2}", this);
        _fgBB.SetPhase2();

        // 이동속도, 애니메이션 속도 적용
        if (_runtime is not null)
            _runtime.SpeedMultiplier = Phase2SpeedMultiplier;

        if (_coreBB is not null)
            _coreBB.AttackSpeedMult = Phase2AttackSpeedMult;

        // BossConfigSO 패턴 딜레이 교체
        if (_config is BossConfigSO bossConfig)
        {
            bossConfig.patternBreakDurationMin = Phase2BreakMin;
            bossConfig.patternBreakDurationMax = Phase2BreakMax;
        }

        // 머티리얼 교체 (Addressable 로드)
        try
        {
            var matBody = await Managers.AddressableManager.LoadAssetAsync<Material>(Phase2BodyMatAddress);
            var matLimb = await Managers.AddressableManager.LoadAssetAsync<Material>(Phase2LimbMatAddress);

            Debug.Log($"[FG] 머티리얼 로드 — body={matBody?.name ?? "NULL"}, limb={matLimb?.name ?? "NULL"}, bodyRenderers={_bodyRenderers?.Length ?? 0}", this);

            if (!destroyCancellationToken.IsCancellationRequested)
            {
                ApplyMaterials(_bodyRenderers, matBody, 0);
                ApplyMaterials(_limbRenderers, matLimb, 1);
                Debug.Log("[FG] 머티리얼 교체 완료", this);
            }
            else
            {
                Debug.LogWarning("[FG] 머티리얼 교체 취소됨 (destroyCancellationToken)", this);
            }
        }
        catch (System.OperationCanceledException)
        {
            // 씬 전환 등으로 취소됨 — 정상 흐름
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[ForestGuardianMonster] 2페이즈 머티리얼 로드 실패: {e.Message}", this);
        }
    }

    private static void ApplyMaterials(Renderer[] renderers, Material mat, int matIndex = 0)
    {
        if (mat == null || renderers == null) return;
        foreach (var r in renderers)
        {
            if (r == null) continue;
            var mats = r.materials;
            if (matIndex < mats.Length)
            {
                mats[matIndex] = mat;
                r.materials = mats;
            }
        }
    }
}
}
