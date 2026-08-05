using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
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

    [Header("DeathKnight — 전신 오라 (검 색상 연동)")]
    [Tooltip("오라를 붙일 기준 위치. 비워두면 보스 루트 사용")]
    [SerializeField] private Transform  _auraAnchor;
    [Tooltip("SwordColor.White일 때 재생할 오라 프리팹 (Aura_Light_LWRP)")]
    [SerializeField] private GameObject _whiteAuraPrefab;
    [Tooltip("SwordColor.Black일 때 재생할 오라 프리팹 (Aura_Dark_LWRP)")]
    [SerializeField] private GameObject _blackAuraPrefab;

    [Header("DeathKnight — 스테인드 글라스 (검 색상 연동)")]
    [Tooltip("SM_GlassWindowCathedral_01a_2 (1)(2)(3) — Black 시 보라로 틴트, White 시 원본 복원")]
    [SerializeField] private Renderer[] _windowRenderers;

    [Header("DeathKnight — 콤보 공격 풀")]
    [SerializeField] private List<BossPatternSO> _attackPool;

    [Header("DeathKnight — 2페이즈 순간이동")]
    [SerializeField] private GameObject _teleportVfxPrefab;
    [SerializeField] private float      _teleportDistance    = 3f;
    [SerializeField] private float      _teleportVfxDuration = 0.5f;
    [SerializeField] private AudioClip  _teleportInSfx;
    [SerializeField] private AudioClip  _teleportOutSfx;

    [Header("DeathKnight — 기본 공격 풀 (1·2페이즈 공용)")]
    [SerializeField] private List<BossPatternSO> _phase2BasicPool;

    [Header("DeathKnight — 2페이즈 광역 공격 풀")]
    [SerializeField] private List<BossPatternSO> _phase2AreaPool;

    [Header("DeathKnight — 2페이즈 패시브 공격 패턴")]
    [SerializeField] private DKSoulSpearPatternSO   _passiveSoulSpear;
    [SerializeField] private DKPhantomRushPatternSO _passivePhantomRush;

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
    [Tooltip("Attack1 스윙 적중 시점에 재생할 슬래시 사운드")]
    [SerializeField] private AudioClip  _entranceSlashSfx;
    [Tooltip("프롭(의자 등)이 날아갈 때 재생할 사운드")]
    [SerializeField] private AudioClip  _entrancePropFlySfx;
    [Tooltip("검 충격 시점에 보스 발 위치에서 원형으로 퍼지는 VFX")]
    [SerializeField] private GameObject _entranceRadialVfxPrefab;
    [Tooltip("원형 VFX와 동시에 재생할 Zone 사운드")]
    [SerializeField] private AudioClip  _entranceZoneSfx;

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
    public GameObject WhiteAuraPrefab        => _whiteAuraPrefab;
    public GameObject BlackAuraPrefab        => _blackAuraPrefab;
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
    private GameObject                _auraInstance;
    private GameObject                _currentAuraPrefab;
    private DKP2PassiveAttackRunner   _passiveRunner;
    private CancellationTokenSource   _passiveCts;
    private CancellationTokenSource   _healCts;
    private bool                      _soulGateCleared;

    public bool SoulGateCleared => _soulGateCleared;

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
            stateDecorator: null);

        BindBossHud();

        _dormantState = new DKDormantState();
        ChangeState(_dormantState);
        if (_pendingTriggerEntrance)
        {
            _pendingTriggerEntrance = false;
            _dormantState.TriggerEntrance(_ctx);
        }
    }

    /// <summary>검을 등장시킨다 (패턴 시작 / 등장 연출 스윙 등 외부 호출용).</summary>
    public void ShowSwordVisual()
    {
        // 등장 전 반드시 올바른 색상 머티리얼 세팅 (핑크 방지)
        if (_dkBB != null) _swordCtrl?.SetSwordColor(_dkBB.SwordColor);
        _swordCtrl?.ShowSword();
    }

    /// <summary>검을 소멸시킨다 (등장 연출 종료 등 외부 호출용).</summary>
    public void HideSwordVisual() => _swordCtrl?.HideSword();

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

            if (active != _prevPatternActive)
            {
                if (active)
                    ShowSwordVisual();
                else
                    _swordCtrl?.HideSword();
                _prevPatternActive = active;
            }
        }

        _runner?.Tick(dt);

        // SoulSummon 완료 후 HP가 55% 이상 회복되면 다음 사이클을 위해 플래그 초기화
        // (기둥을 파괴하지 않아 회복된 경우 → HP 클램프 재활성화 → 재발동 허용)
        if (_soulGateCleared && !_dkBB.IsPhase2 && HpRatio > 0.55f)
            _soulGateCleared = false;

        // 페이즈 전환 체크: SoulSummon 완료(_soulGateCleared) 후에만 진입 허용
        if (!_dkBB.IsPhase2 && _soulGateCleared && HpRatio <= Phase2HpThreshold && !IsInSpecialState)
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
        _passiveCts?.Cancel();
        _passiveCts?.Dispose();
        _passiveCts    = null;
        _passiveRunner = null;
        _runner?.Reset();
        _coreBB?.Reset();
        _dkBB?.Reset();
        _prevPatternActive = false;
        _isStaggered       = false;
        _soulGateCleared   = false;
        if (_dkBB != null) ApplyArmorTint(_dkBB.SwordColor);
        ApplyWindowTint(_dkBB?.SwordColor ?? DKSwordColor.White);
        ApplyAuraColor(_dkBB?.SwordColor ?? DKSwordColor.White);
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
        _passiveCts?.Cancel();
        _passiveCts?.Dispose();
        _passiveCts    = null;
        _passiveRunner = null;
        _healCts?.Cancel();
        _healCts?.Dispose();
        _healCts = null;
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
    // 영혼 기둥 HP 연동 (무적 우회 — 기둥 피격 → 보스 HP 직접 변경)
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    public void SoulPillarApplyDamage(int amount)
    {
        if (_runtime == null || _runtime.IsDead) return;
        // 기둥을 부순 건 플레이어의 직접 타격이다 — 이 경로로 죽여도 막타 히트스톱이 살아야 한다.
        // (TakeDamage를 우회하므로 피해 종류가 갱신되지 않아 직전 DoT 틱이 남을 수 있다)
        _lastDamageKind = DamageKind.Normal;
        _runtime.CurrentHp = Mathf.Max(0, _runtime.CurrentHp - amount);
        NotifyHpChanged();
        if (_runtime.CurrentHp <= 0)
        {
            _runtime.CurrentHp = 0;
            _runtime.IsDead    = true;
            OnFatalDamage();
        }
    }

    public void SoulPillarHealBoss(int amount)
    {
        if (_runtime == null || _runtime.IsDead) return;
        _runtime.CurrentHp = Mathf.Min(_runtime.CurrentHp + amount, EffectiveMaxHp);
        NotifyHpChanged();
    }

    public void SoulPillarHealBossGradual(int total, float duration)
    {
        if (_runtime == null || _runtime.IsDead || total <= 0) return;
        _healCts?.Cancel();
        _healCts?.Dispose();
        _healCts = new CancellationTokenSource();
        HealGradualAsync(total, duration, _healCts.Token).Forget();
    }

    public void CancelGradualHeal()
    {
        _healCts?.Cancel();
        _healCts?.Dispose();
        _healCts = null;
    }

    private async UniTaskVoid HealGradualAsync(int total, float duration, CancellationToken ct)
    {
        try
        {
            if (_runtime == null || _runtime.IsDead) return;
            int startHp  = _runtime.CurrentHp;
            int targetHp = Mathf.Min(startHp + total, EffectiveMaxHp);
            int actual   = targetHp - startHp;
            if (actual <= 0) return;

            float elapsed  = 0f;
            const float tickInterval = 0.05f;

            while (elapsed < duration)
            {
                await UniTask.Delay(System.TimeSpan.FromSeconds(tickInterval), cancellationToken: ct);
                elapsed += tickInterval;
                if (_runtime == null || _runtime.IsDead) return;
                _runtime.CurrentHp = startHp + Mathf.RoundToInt(actual * Mathf.Clamp01(elapsed / duration));
                NotifyHpChanged();
            }

            _runtime.CurrentHp = targetHp;
            NotifyHpChanged();
        }
        catch (System.OperationCanceledException) { }
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

        // SoulSummon 완료 전까지 HP를 50%에서 클램프
        if (_dkBB != null && !_dkBB.IsPhase2 && !_soulGateCleared && _runtime != null)
        {
            int minHp = Mathf.CeilToInt(EffectiveMaxHp * 0.5f);
            if (_runtime.CurrentHp < minHp)
            {
                _runtime.CurrentHp = minHp;
                NotifyHpChanged();
            }
        }
    }

    public void NotifySoulSummonCompleted() => _soulGateCleared = true;

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
        ApplyWindowTint(_dkBB.SwordColor);
        ApplyAuraColor(_dkBB.SwordColor);
    }

    /// <summary>전신 오라 프리팹을 검 색상에 맞춰 교체한다 (보스 루트/지정 앵커에 인스턴스화).</summary>
    private void ApplyAuraColor(DKSwordColor color)
    {
        GameObject auraPrefab = color == DKSwordColor.White ? _whiteAuraPrefab : _blackAuraPrefab;
        if (auraPrefab == null) return;
        if (auraPrefab == _currentAuraPrefab) return;

        if (_auraInstance != null)
        {
            BossEffectPool.Release(_auraInstance);
            _auraInstance = null;
        }

        Transform anchor = _auraAnchor != null ? _auraAnchor : transform;
        _auraInstance = BossEffectPool.Spawn(auraPrefab, anchor.position, Quaternion.identity, anchor);
        if (_auraInstance != null)
        {
            // 바닥 장판 잔여물이 바닥 아래로 가려지도록 살짝 낮춤 (뜨는 입자는 위로 올라가므로 영향 없음)
            _auraInstance.transform.localPosition = new Vector3(0f, -1.5f, 0f);
            _auraInstance.transform.localRotation = Quaternion.identity;
        }
        _currentAuraPrefab = auraPrefab;
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
            tint     = new Color(0.0f, 0.0f,  0.0f,  0.1f);
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

    private void ApplyWindowTint(DKSwordColor color)
    {
        if (_windowRenderers == null || _windowRenderers.Length == 0) return;

        if (color == DKSwordColor.White)
        {
            foreach (var r in _windowRenderers)
                if (r != null) r.SetPropertyBlock(null);
            return;
        }

        if (_propBlock == null) _propBlock = new MaterialPropertyBlock();
        _propBlock.SetColor(BaseColorId,     new Color(0.15f, 0f, 0.25f, 0.4f));
        _propBlock.SetColor(EmissionColorId, new Color(0.5f,  0f, 0.6f,  1f));
        foreach (var r in _windowRenderers)
            if (r != null) r.SetPropertyBlock(_propBlock);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 보스룸: 플레이어가 있으면 항상 추적
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    // 추적 없음 — 제자리 대기 전용 보스
    public override bool ShouldStartChase(MonsterContext ctx) => false;
    protected override bool LocksNavPosition => true;

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
        _dkBB.SetInvincible(true);
        Phase2InvincibleAsync(this.GetCancellationTokenOnDestroy()).Forget();

        if (_config is BossConfigSO bossConfig)
        {
            bossConfig.patternBreakDurationMin = Phase2BreakMin;
            bossConfig.patternBreakDurationMax = Phase2BreakMax;
        }

        // 2페이즈 패시브 공격 루프 시작
        if (_passiveSoulSpear != null || _passivePhantomRush != null)
        {
            _passiveCts?.Cancel();
            _passiveCts?.Dispose();
            _passiveCts    = new CancellationTokenSource();
            _passiveRunner = new DKP2PassiveAttackRunner(_ctx, _passiveSoulSpear, _passivePhantomRush);
            _passiveRunner.Start(_passiveCts.Token);
        }

        Debug.Log($"[DK] Phase2 진입 — HP={HpRatio:F2}", this);
    }

    private async Cysharp.Threading.Tasks.UniTaskVoid Phase2InvincibleAsync(System.Threading.CancellationToken ct)
    {
        try
        {
            await Cysharp.Threading.Tasks.UniTask.Delay(System.TimeSpan.FromSeconds(2f), cancellationToken: ct);
            _dkBB?.SetInvincible(false);
        }
        catch (System.OperationCanceledException) { }
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
        GameCameraController.Instance?.ActivateDKPlayerOrbit(1.0f);
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
        => _entranceWindEffectPrefab != null
            ? BossEffectPool.Spawn(_entranceWindEffectPrefab, Vector3.zero, Quaternion.identity)
            : null;

    /// <summary>등장 연출 Attack1 스윙 적중 시점에 슬래시 사운드를 재생한다.</summary>
    public void PlayEntranceSlashSfx()
        => Managers.Sound?.PlayEffect(_entranceSlashSfx);

    /// <summary>프롭이 날아가는 시점에 Soul 사운드를 재생한다.</summary>
    public void PlayEntrancePropFlySfx()
        => Managers.Sound?.PlayEffect(_entrancePropFlySfx);

    /// <summary>검 충격 시점에 바닥 위치에서 원형 VFX를 스폰하고 Zone 사운드를 재생한다.</summary>
    public void SpawnEntranceRadialVfx()
    {
        if (_entranceRadialVfxPrefab != null)
        {
            float floorY = DKBossRoomContext.CellToWorld(0, 0, 0f).y;
            Vector3 spawnPos = new Vector3(transform.position.x, floorY, transform.position.z);
            BossEffectPool.SpawnOneShot(_entranceRadialVfxPrefab, spawnPos, Quaternion.identity, fallbackLifetime: 3f);
        }
        Managers.Sound?.PlayEffect(_entranceZoneSfx);
    }
}
}
