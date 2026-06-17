using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// DeathKnight 보스 MonoBehaviour.
///
/// ━━ 페이즈 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  1페이즈 (HP 100~40%): 기본 속도, 패턴 딜레이 2~4s
///  2페이즈 (HP 40%~0%) : 이동속도 1.2x, 패턴 딜레이 1~2.5s
///
/// ━━ 격노(Enrage) ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  HP 30% 이하 도달 시 1회 발동.
///  이동속도 1.3x, 애니메이션 속도 1.4x. 해제 없음.
/// </summary>
public class DeathKnightBossMonster : MonsterBase, IBoss, IBossEntrance
{
    // ── 상수 ─────────────────────────────────────────────
    private const float Phase2SpeedMult    = 1.2f;
    private const float Phase2BreakMin     = 1.0f;
    private const float Phase2BreakMax     = 2.5f;
    private const float EnrageSpeedMult    = 1.3f;
    private const float Phase2HpThreshold  = 0.4f;

    // ── Inspector ─────────────────────────────────────────
    [Header("DeathKnight — 렌더러")]
    [SerializeField] private Renderer[] _bodyRenderers;

    [Header("DeathKnight — 검")]
    [SerializeField] private DeathKnightSwordController _swordCtrl;

    [Header("DeathKnight — 콤보 공격 풀")]
    [SerializeField] private List<BossPatternSO> _attackPool;

    [Header("DeathKnight — 2페이즈 순간이동")]
    [SerializeField] private GameObject _teleportVfxPrefab;
    [SerializeField] private float      _teleportDistance    = 3f;
    [SerializeField] private float      _teleportVfxDuration = 0.5f;

    [Header("DeathKnight — 기본 공격 풀 (1·2페이즈 공용)")]
    [SerializeField] private List<BossPatternSO> _phase2BasicPool;

    [Header("DeathKnight — 2페이즈 광역 공격 풀")]
    [SerializeField] private List<BossPatternSO> _phase2AreaPool;

    [Header("DeathKnight — 1페이즈 고정 위치 앵커 (비워두면 초기 위치 자동 사용)")]
    [SerializeField] private Transform _phase1AnchorTransform;

    [Header("DeathKnight — 피라미드 슬래시 앵커 (플레이어 구역 중심, (0,0,-11) 오브젝트)")]
    [SerializeField] private Transform _pyramidStrikeAnchor;

    [Header("DeathKnight — 연출 종료 시 활성화할 장벽 오브젝트")]
    [SerializeField] private GameObject[] _entranceEndBarriers;

    [Header("DeathKnight — 등장 연출")]
    [Tooltip("클로즈업 카메라 오프셋 (보스 기준). 측면+정면 대각선 구도, Y>0 으로 바닥 클리핑 방지")]
    [SerializeField] private Vector3    _entranceCameraOffset          = new Vector3(1.5f, 1.0f, -1.5f);
    [Tooltip("카메라가 바라보는 지점 = 데스나이트 위치 + 이 오프셋")]
    [SerializeField] private Vector3    _entranceCameraLookOffset      = new Vector3(0f, 1.2f, 0f);
    [Tooltip("카메라 클로즈업 전환 시간 (초)")]
    [SerializeField] private float      _entranceCameraCloseUpDuration = 1.0f;
    [Tooltip("보스 이름 HUD 소멸 후 플레이어 카메라 복귀 시간 (초)")]
    [SerializeField] private float      _entranceCameraReturnDuration  = 1.2f;
    [Tooltip("보스 이름 HUD 등장과 함께 표시할 화면 전체 바람 이펙트 프리팹")]
    [SerializeField] private GameObject _entranceWindEffectPrefab;

    // ── MonsterBase 추상 멤버 ─────────────────────────────
    protected override string ConfigAddress   => "DeathKnightBoss/DeathKnightBossConfig";
    protected override string DataAddress     => string.Empty;
    protected override string HeadBoneName    => null;
    protected override float  HPBarHeadOffset => 0.3f;
    protected override bool   UseWorldHPBar   => false;

    // ── IBoss ─────────────────────────────────────────────
    public float HpRatio =>
        (_runtime != null && _config != null && _config.stat.maxHp > 0)
        ? (float)_runtime.CurrentHp / _config.stat.maxHp
        : 1f;

    public BossAttackBlackboard Blackboard => _coreBB;

    // ── DeathKnight 공개 접근 ─────────────────────────────
    public DeathKnightBossBlackboard DKBlackboard => _dkBB;
    public Transform SwordTransform          => _swordCtrl?.SwordTransform;
    public Transform PyramidStrikeAnchor     => _pyramidStrikeAnchor;
    public Vector3 EntranceCameraOffset          => _entranceCameraOffset;
    public Vector3 EntranceCameraLookOffset      => _entranceCameraLookOffset;
    public float   EntranceCameraCloseUpDuration => _entranceCameraCloseUpDuration;
    public float   EntranceCameraReturnDuration  => _entranceCameraReturnDuration;

    // ── 내부 필드 ─────────────────────────────────────────
    private DeathKnightBossBlackboard _dkBB;
    private BossAttackBlackboard      _coreBB;
    private MaterialPropertyBlock     _propBlock;
    private DKComboRunner             _runner;
    private BossPatternContext        _patternCtx;
    private bool                      _prevPatternActive;
    private bool                      _isStaggered;
    private DKDormantState            _dormantState;
    private bool                      _pendingTriggerEntrance;

    /// <summary>GetHitState 진입/종료 시 콤보 러너 차단 플래그.</summary>
    public void SetStagger(bool value) => _isStaggered = value;

    // ── 커스텀 ICondition ─────────────────────────────────

    private sealed class DKPhase1Condition : ICondition
    {
        private readonly DeathKnightBossBlackboard _bb;
        public DKPhase1Condition(DeathKnightBossBlackboard bb) => _bb = bb;
        public bool Evaluate(BossPatternContext ctx) => !_bb.IsPhase2;
    }

    private sealed class DKPhase2Condition : ICondition
    {
        private readonly DeathKnightBossBlackboard _bb;
        public DKPhase2Condition(DeathKnightBossBlackboard bb) => _bb = bb;
        public bool Evaluate(BossPatternContext ctx) => _bb.IsPhase2;
    }

    private sealed class DKEnragedCondition : ICondition
    {
        private readonly DeathKnightBossBlackboard _bb;
        public DKEnragedCondition(DeathKnightBossBlackboard bb) => _bb = bb;
        public bool Evaluate(BossPatternContext ctx) => _bb.IsEnraged;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 레이어드 FSM 상태 등록
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected override void RegisterStates()
    {
        base.RegisterStates();
        _fsm.RegisterAs<PatrolState>     (new DKIdleState());
        _fsm.RegisterAs<ChaseState>      (new DKChaseState());
        _fsm.RegisterAs<AttackReadyState>(new DKAttackReadyState());
        _fsm.RegisterAs<AttackState>     (new DKAttackState());
        _fsm.RegisterAs<GetHitState>     (new DKGetHitState());
        _fsm.RegisterAs<DieState>        (new DKDieState());
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 초기화
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected override void OnInitialized()
    {
        InitializeRoomContext();
        var bossConfig = _config as BossConfigSO;
        if (bossConfig == null)
        {
            Debug.LogError("[DeathKnightBossMonster] Config이 BossConfigSO가 아닙니다!", this);
            return;
        }

        _dkBB   = new DeathKnightBossBlackboard();
        _coreBB = new BossAttackBlackboard();

        // Phase1 고정 위치 등록 (앵커 없으면 현재 위치 사용)
        Vector3 phase1Pos = _phase1AnchorTransform != null
            ? _phase1AnchorTransform.position
            : transform.position;
        _dkBB.SetPhase1FixedPosition(phase1Pos);

        _patternCtx = new BossPatternContext
        {
            Boss       = this,
            Ctx        = _ctx,
            Blackboard = _coreBB,
        };

        if (_bodyRenderers == null || _bodyRenderers.Length == 0)
            _bodyRenderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);

        if (_swordCtrl == null)
            _swordCtrl = GetComponentInChildren<DeathKnightSwordController>(true);

        BuildConditions(bossConfig);
        InitializePatterns(bossConfig);
        InitializeAttackPool();
        InitializePhase2BasicPool();
        InitializePhase2AreaPool();

        // stateDecorator 에서 runner 를 참조하기 위한 클로저 트릭
        DKComboRunner runnerRef = null;

        _runner = new DKComboRunner(
            bossConfig,
            _patternCtx,
            _attackPool,        // 1페이즈 광역 풀
            _phase2BasicPool,   // 기본 공격 풀 (공용)
            _phase2AreaPool,    // 2페이즈 광역 풀
            isAlive:     () => _runtime != null && !_runtime.IsDead && !IsPlayerDead(),
            isInRange:   IsInEngagementRange,
            isPhase2:    () => _dkBB?.IsPhase2 ?? false,
            isStaggered: () => _isStaggered,
            changeState:    s => ChangeState(s),
            onExecuted:     OnPatternExecuted,
            stateDecorator: state =>
            {
                // 1·2페이즈 모두 텔레포트 적용
                // usePhase1Position=true → Phase1 고정 위치 (광역 공격)
                // usePhase1Position=false → 플레이어 근처 (기본 공격)
                Vector3? phase1Pos = null;
                if (runnerRef != null && runnerRef.CurrentComboUsePhase1Position
                    && _dkBB.HasPhase1Position)
                {
                    phase1Pos = _dkBB.Phase1FixedPosition;
                }

                return new DKPhase2TeleportState(
                    state, _teleportVfxPrefab, _teleportDistance, _teleportVfxDuration, phase1Pos);
            });

        runnerRef = _runner;

        BindBossHud();

        _dormantState = new DKDormantState();
        ChangeState(_dormantState);
        if (_pendingTriggerEntrance)
        {
            _pendingTriggerEntrance = false;
            _dormantState.TriggerEntrance(_ctx);
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 매 프레임
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected override void Update()
    {
        base.Update();

        if (_dkBB == null || _coreBB == null) return;
        if (_dormantState != null && _dormantState.IsActive) return;

        float dt = Time.deltaTime;

        _coreBB.TickCooldowns(dt);

        if (_runner != null)
        {
            bool active = _runner.IsPatternActive;

            if (active)
                _coreBB.NormalModeTimer = 0f;
            else
                _coreBB.NormalModeTimer += dt;

            // 패턴 시작 → 검 등장 / 패턴 종료 → 검 소멸
            if (active != _prevPatternActive)
            {
                if (active)
                {
                    // 등장 전 반드시 올바른 색상 머티리얼 세팅 (핑크 방지)
                    if (_dkBB != null) _swordCtrl?.SetSwordColor(_dkBB.SwordColor);
                    _swordCtrl?.ShowSword();
                }
                else
                {
                    _swordCtrl?.HideSword();
                }
                _prevPatternActive = active;
            }
        }

        _runner?.Tick(dt);

        // 페이즈 전환 체크
        if (!_dkBB.IsPhase2 && HpRatio <= Phase2HpThreshold && !IsInSpecialState)
            EnterPhase2();

        // 격노 체크
        if (!_dkBB.IsEnraged && HpRatio <= DeathKnightBossBlackboard.EnrageHpThreshold)
            TryEnrage();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 풀 재사용
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected override void OnEnable()
    {
        base.OnEnable();
        InitializeRoomContext();
        _runner?.Reset();
        _coreBB?.Reset();
        _dkBB?.Reset();
        _prevPatternActive = false;
        _isStaggered       = false;
        if (_dkBB != null) ApplyArmorTint(_dkBB.SwordColor);
        if (_attackPool != null)
            foreach (var p in _attackPool)
                p?.OnRecycled();
        if (_phase2BasicPool != null)
            foreach (var p in _phase2BasicPool)
                p?.OnRecycled();
        if (_phase2AreaPool != null)
            foreach (var p in _phase2AreaPool)
                p?.OnRecycled();
        BindBossHud();
        _pendingTriggerEntrance = false;
        if (_dormantState != null)
            ChangeState(_dormantState);
    }

    protected override void OnDisable()
    {
        UnbindBossHudIfBound();
        base.OnDisable();
    }

    public void UnbindBossHudIfBoundPublic() => UnbindBossHudIfBound();

    /// <summary>AttackReady 진입 시 애니메이션 전환이 끝날 때까지 패턴 대기 보장.</summary>
    public void EnsurePatternDelay(float minDuration) => _runner?.EnsureMinBreakCooldown(minDuration);

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 피격 처리 (아머)
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected override void OnDamageTaken()
    {
        if (_dkBB == null) return;

        bool armorBroke = _dkBB.ApplyArmorDamage(isHeavy: false);
        if (!armorBroke)
            _suppressGetHitThisHit = true;  // 아머 미파괴 → 경직 스킵

        // 피격 시 보스 몸 hit blink (화면 전체 플래시 대신 보스 자체가 깜빡임)
        float blinkDuration = armorBroke ? 0.15f : 0.06f;
        // 이전 blink 코루틴 중단 후 새로 시작
        if (_hitBlinkRoutine != null) StopCoroutine(_hitBlinkRoutine);
        _hitBlinkRoutine = StartCoroutine(HitBlinkRoutine(blinkDuration));
    }

    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int BaseColorId      = Shader.PropertyToID("_BaseColor");
    private Coroutine _hitBlinkRoutine;

    private IEnumerator HitBlinkRoutine(float duration)
    {
        if (_bodyRenderers == null || _bodyRenderers.Length == 0) yield break;
        if (_propBlock == null) _propBlock = new MaterialPropertyBlock();

        // 흰색으로 번쩍
        foreach (var r in _bodyRenderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(_propBlock);
            _propBlock.SetColor(BaseColorId,      Color.white);
            _propBlock.SetColor(EmissionColorId,  Color.white);
            r.SetPropertyBlock(_propBlock);
        }

        // WaitForSecondsRealtime: Time.timeScale 영향 없음 (혹시 외부에서 timeScale 변경해도 정상 작동)
        yield return new WaitForSecondsRealtime(duration);

        // 현재 검 색상으로 복원
        if (_dkBB != null) ApplyArmorTint(_dkBB.SwordColor);
        _hitBlinkRoutine = null;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 무적 처리 (피라미드 슬래시 패턴 중 데미지 차단)
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    public override void TakeDamage(float amount, UnityEngine.GameObject instigator,
                                    float knockbackMultiplier = 1f,
                                    bool isCrit = false)
    {
        if (_dkBB != null && _dkBB.IsInvincible) return;
        base.TakeDamage(amount, instigator, knockbackMultiplier, isCrit);
    }

    /// <summary>
    /// 검 색상 논리값만 반전한다.
    /// 실제 머티리얼 교체는 다음 ShowSword() 직전에 이루어지므로 핑크 검이 노출되지 않는다.
    /// </summary>
    public void FlipSwordColor()
    {
        if (_dkBB == null) return;
        _dkBB.FlipSwordColor();
        // 검이 숨겨진 상태일 때는 지금 바로 머티리얼 세팅 (다음 Show 때도 세팅되지만 안전하게)
        if (!_prevPatternActive)
            _swordCtrl?.SetSwordColor(_dkBB.SwordColor);
        ApplyArmorTint(_dkBB.SwordColor);
        ApplyBarrierTint(_dkBB.SwordColor);
    }

    private void ApplyBarrierTint(DKSwordColor color)
    {
        if (_entranceEndBarriers == null) return;
        Color tint, emission;
        if (color == DKSwordColor.White)
        {
            tint     = new Color(0.9f, 0.95f, 1.0f,  0.02f);
            emission = new Color(0.05f, 0.06f, 0.12f, 1f);
        }
        else
        {
            tint     = new Color(0.0f, 0.0f,  0.0f,  0.02f);
            emission = new Color(0.0f, 0.0f,  0.0f,  1f);
        }
        foreach (var b in _entranceEndBarriers)
        {
            if (b == null) continue;
            b.GetComponent<SoftBarrier>()?.SetTint(tint, emission);
        }
    }

    private void ApplyArmorTint(DKSwordColor color)
    {
        if (_bodyRenderers == null || _bodyRenderers.Length == 0) return;
        if (_propBlock == null) _propBlock = new MaterialPropertyBlock();

        Color baseTint = color == DKSwordColor.White
            ? new Color(0.9f,  0.9f,  1.0f, 1f)
            : new Color(0.08f, 0.08f, 0.12f, 1f);
        Color emission = color == DKSwordColor.White
            ? new Color(0.2f, 0.25f, 0.55f, 1f)
            : new Color(0.5f,  0.0f,  0.6f, 1f);

        _propBlock.SetColor("_BaseColor",      baseTint);
        _propBlock.SetColor("_EmissionColor",  emission);
        foreach (var r in _bodyRenderers)
            if (r != null) r.SetPropertyBlock(_propBlock);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 보스룸: 플레이어가 있으면 항상 추적
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    // 추적 없음 — 제자리 대기 전용 보스
    public override bool ShouldStartChase(MonsterContext ctx) => false;

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 내부 헬퍼
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private bool IsInEngagementRange()
    {
        return _runtime?.PlayerTarget != null && !IsPlayerDead();
    }

    private void InitializeRoomContext()
    {
        if (_runtime == null) return;
        Bounds floorBounds = DragonPatternFloorUtils.ResolveArenaBoundsXZ(_runtime.SpawnPosition, 15f);
        int width  = Mathf.Max(2, Mathf.RoundToInt(floorBounds.size.x / 2f));
        int height = Mathf.Max(2, Mathf.RoundToInt(floorBounds.size.z / 2f));
        Vector3 worldCenter = new Vector3(floorBounds.center.x, 0f, floorBounds.center.z);
        DKBossRoomContext.Initialize(width, height, 2f, worldCenter);
    }

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

    private void InitializeAttackPool()
    {
        if (_attackPool == null) return;
        foreach (var p in _attackPool)
            p?.Initialize(_patternCtx);
    }

    private void InitializePhase2BasicPool()
    {
        if (_phase2BasicPool == null) return;
        foreach (var p in _phase2BasicPool)
            p?.Initialize(_patternCtx);
    }

    private void InitializePhase2AreaPool()
    {
        if (_phase2AreaPool == null) return;
        foreach (var p in _phase2AreaPool)
            p?.Initialize(_patternCtx);
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
            BossConditionKey.Phase2       => new HpBelowCondition(config.condPhase2HpThreshold),
            BossConditionKey.Dist_Close   => new MaxRangeCondition(config.condDistClose),
            BossConditionKey.Dist_Far     => new MinRangeCondition(config.condDistFar),
            BossConditionKey.TimePressure => new NormalModeTimerCondition(config.condTimePressureSecs),
            BossConditionKey.DK_IsPhase1  => new DKPhase1Condition(_dkBB),
            BossConditionKey.DK_IsPhase2  => new DKPhase2Condition(_dkBB),
            BossConditionKey.DK_IsEnraged => new DKEnragedCondition(_dkBB),
            _                             => new AlwaysTrue(),
        };
    }

    private void OnPatternExecuted(BossPatternSO pattern)
    {
        _coreBB.LastPatternTag  = pattern.patternTag;
        _coreBB.NormalModeTimer = 0f;
    }

    // ── 페이즈 전환 ────────────────────────────────────────
    private void EnterPhase2()
    {
        _dkBB.SetPhase2();

        if (_runtime != null)
            _runtime.SpeedMultiplier = Phase2SpeedMult;

        if (_config is BossConfigSO bossConfig)
        {
            bossConfig.patternBreakDurationMin = Phase2BreakMin;
            bossConfig.patternBreakDurationMax = Phase2BreakMax;
        }

        Debug.Log($"[DK] Phase2 진입 — HP={HpRatio:F2}", this);
    }

    // ── 격노 ───────────────────────────────────────────────
    private void TryEnrage()
    {
        if (!_dkBB.TrySetEnraged()) return;

        if (_runtime != null)
            _runtime.SpeedMultiplier = EnrageSpeedMult;

        Debug.Log($"[DK] Enrage 발동 — HP={HpRatio:F2}", this);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // IBossEntrance
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    public override bool HasEntranceAnimation => true;

    public event Action OnEntranceRequested;
    public event Action OnCombatReady;

    internal void FireEntranceRequest() => OnEntranceRequested?.Invoke();

    internal void FireCombatReady()
    {
        foreach (var b in _entranceEndBarriers)
            if (b != null) b.SetActive(true);
        ApplyBarrierTint(_dkBB?.SwordColor ?? DKSwordColor.White);
        _runner?.EnsureMinBreakCooldown(3f);
        OnCombatReady?.Invoke();
        RaiseBossCombatReady();
    }

    public void TriggerEntrance()
    {
        if (_dormantState != null)
            _dormantState.TriggerEntrance(_ctx);
        else
            _pendingTriggerEntrance = true;
    }

    public GameObject SpawnEntranceWindVfx()
        => _entranceWindEffectPrefab != null ? Instantiate(_entranceWindEffectPrefab) : null;
}
}
