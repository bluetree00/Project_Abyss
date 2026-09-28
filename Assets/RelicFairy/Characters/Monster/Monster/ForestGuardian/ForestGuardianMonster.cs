using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Random = UnityEngine.Random;

namespace RelicFairy.Monster
{
/// <summary>
/// ForestGuardian 보스 MonoBehaviour.
///
/// ━━ 페이즈 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  1페이즈 (HP 100~50%) : TreantE 머티리얼
///  2페이즈 (HP 50~0%)   : TreantD 머티리얼 (속도/딜레이 변화 없음)
///
/// ━━ 그로기 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  일반 피격 시 강인도 8 감소.
///  BigAttackWindow 중 피격 시 추가 40 감소.
///  강인도 0 → GroggyState 진입, 3초 경직 후 강인도 복원.
///
/// ━━ 패턴 가중치 회복 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  패턴 실행 직후 weight=0, 5~10초에 걸쳐 원래값으로 복원.
///
/// ━━ 2페이지 「고목의 분노」 (악몽기만 — BossPages, 09-28 설계 확정 §3) ━━━━━━━━━━
///  위의 1·2페이즈 전투 전부가 1페이지다. 체력 = 기존 × 1.4, 1페이지 몫이 다 깎이면 전환 패턴(무적) →
///  아레나 가장자리 3.5 m 가시 뿌리 띠(영구) · 몸이 붉게 → 2페이지 새 패턴 FL1~FL4 + 간판 FL-S(2페이지 50%).
///  HpRatio는 1페이지 동안 1페이지 기준(1→0)이라 50% 페이즈 경계가 그대로 돌고, 2페이지에선 0.4→0이라 다시 켜지지 않는다.
///  봉인기(Pages.Enabled=false)엔 체력 · 바 · 패턴 전부 지금과 같다.
/// </summary>
public class ForestGuardianMonster : MonsterBase, IBoss, IBossEntrance, IPagedBoss, IBossHudSource
{
    // ── 상수 ─────────────────────────────────────────────────
    private const string Phase2BodyMatAddress  = "ForestGuardian/Materials/TreantD";
    private const string Phase2LimbMatAddress  = "ForestGuardian/Materials/TreantDLimbs";
    private const float  Phase2HpThreshold     = 0.5f;
    private const float  WeightRecoveryMin     = 5f;
    private const float  WeightRecoveryMax     = 10f;
    private const float  ArenaFallbackHalfSize = 14f;     // 바닥 상자를 못 찾을 때 — Ch1 아레나 28×28
    private const float  DirectHitMinInterval  = 0.1f;    // 한 번 휘두름의 다단 판정을 「한 대」로 센다(간판 약점)
    private const string HeartGroggyStatusId   = "fg_heart";

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    /// <summary>패턴 가이드 채움 색 — 숲 테마(호박빛). 선은 공통 예고색(노랑)→판정색(빨강).</summary>
    internal static readonly Color GuideFlow = new Color(1f, 0.55f, 0.12f);

    /// <summary>안전 표시(흰색) — 보스 공통 색 규약(리치와 같은 값). 뿌리 감옥 틈 · 뿌리 회오리 안쪽.</summary>
    internal static readonly Color GuideSafe = new Color(0.95f, 0.95f, 0.90f);

    // ── Inspector ────────────────────────────────────────────
    [Header("ForestGuardian — 표시")]
    [Tooltip("보스 체력바 이름. config.monsterName은 퀘스트 처치 키라 바꾸지 않는다")]
    [SerializeField] private string _bossDisplayName = "숲의 수호자";

    [Header("ForestGuardian — 패턴 가이드 (SkillIndicator)")]
    [Tooltip("원형·부채꼴 가이드 머티리얼 — 리치 가이드를 숲 문양으로 바꾼 것. 비우면 프리미티브로 폴백")]
    [SerializeField] private Material _circleGuideMaterial;
    [Tooltip("직선 가이드 머티리얼(브레스). 비우면 프리미티브로 폴백")]
    [SerializeField] private Material _arrowGuideMaterial;

    [Header("ForestGuardian — 렌더러")]
    [Tooltip("body 머티리얼(index 0)을 가진 Renderer 배열")]
    [SerializeField] private Renderer[] _bodyRenderers;
    [Tooltip("limbs 머티리얼(index 1)을 가진 Renderer 배열 (body와 동일 오브젝트여도 무방)")]
    [SerializeField] private Renderer[] _limbRenderers;

    [Header("ForestGuardian — 목소리 사운드 (Voice)")]
    [Tooltip("랜덤 간격으로 재생할 목소리 클립 (Voice1~3)")]
    [SerializeField] private AudioClip[] _voiceClips;
    [Tooltip("패턴 비활성 상태에서 목소리 사운드를 재생하는 최소 간격 (초)")]
    [SerializeField] private float _voiceSfxIntervalMin = 8f;
    [Tooltip("패턴 비활성 상태에서 목소리 사운드를 재생하는 최대 간격 (초)")]
    [SerializeField] private float _voiceSfxIntervalMax = 15f;

    [Header("ForestGuardian — 등장 연출 카메라")]
    [Tooltip("카메라 오프셋 (보스 기준). 우측아래에서 올려다보는 구도. Ch2/Ch3 참고: DK=(1.5,1,-1.5), Dragon=(7,0.5,-2)")]
    [SerializeField] private Vector3 _entranceCamOffset     = new Vector3(4f, 1f, -3f);
    [Tooltip("카메라가 바라볼 지점 = 보스 위치 + 이 오프셋. Y를 높이면 얼굴 방향을 바라본다")]
    [SerializeField] private Vector3 _entranceCamLookOffset = new Vector3(0f, 3f, 0f);
    [Tooltip("플레이어→보스 대각선 이동 시간 (초)")]
    [SerializeField] private float   _entranceCamMoveDuration   = 1.0f;
    [Tooltip("대각선 구도에서 보스를 보여주는 홀드 시간 (초)")]
    [SerializeField] private float   _entranceCamHoldDuration   = 1.5f;
    [Tooltip("보스 대각선→플레이어 복귀 이동 시간 (초)")]
    [SerializeField] private float   _entranceCamReturnDuration = 1.2f;

    [Header("ForestGuardian — 발걸음 사운드 (Walk)")]
    [Tooltip("발이 땅에 닿을 때 랜덤 재생할 발걸음 클립 (Walk1~3)")]
    [SerializeField] private AudioClip[] _footstepClips;
    [Tooltip("걷기 애니메이션 상태 이름 (Animator 상태명과 일치)")]
    [SerializeField] private string _walkStateName = "Walk";
    [Tooltip("걷기 클립 내 발이 땅에 닿는 시점 (normalizedTime). 108프레임 기준 20·48·75·102프레임")]
    [SerializeField] private float[] _footstepPhases = { 0.185f, 0.444f, 0.694f, 0.944f };

    [Header("ForestGuardian — 2페이지 무대 (가시 뿌리 띠, 악몽기만)")]
    [Tooltip("아레나 가장자리에서 이 폭(m)만큼 가시 뿌리가 영구히 덮는다")]
    [SerializeField] private float      _thornBandWidth      = 3.5f;
    [Tooltip("가시 뿌리 띠 바닥 색(반투명)")]
    [SerializeField] private Color      _thornBandColor      = new Color(0.55f, 0.10f, 0.06f, 0.45f);
    [Tooltip("띠를 따라 늘어놓을 가시 이펙트(시각 전용). 비우면 바닥 띠만")]
    [SerializeField] private GameObject _thornBandVfxPrefab;
    [SerializeField] private float      _thornBandVfxScale   = 1f;
    [Tooltip("띠 안에 있으면 0.5초마다 attackPower × 이 배율 피해")]
    [SerializeField] private float      _thornBandDamageMult = 0.12f;
    [Tooltip("2페이지 몸 색 — 초록빛이 붉게(머티리얼 _BaseColor에 곱한다)")]
    [SerializeField] private Color      _page2BodyTint       = new Color(1f, 0.55f, 0.45f);

    // ── MonsterBase 추상 멤버 ─────────────────────────────────
    protected override string ConfigAddress  => "ForestGuardian/ForestGuardianConfig";
    protected override string DataAddress    => string.Empty;
    protected override string HeadBoneName   => null;
    protected override float  HPBarHeadOffset=> 0.3f;
    protected override bool   UseWorldHPBar  => false;

    // ── IBoss ─────────────────────────────────────────────────
    /// <summary>
    /// 페이즈 경계용 체력 비율 — 2페이지가 있으면 1페이지 동안 1페이지 기준(1→0), 2페이지에선 0.4→0(BossPages.PhaseRatio).
    /// 봉인기엔 예전 공식(현재 / config 최대) 그대로.
    /// </summary>
    public float HpRatio =>
        (_runtime != null && _config != null && _config.stat.maxHp > 0)
        ? Pages.PhaseRatio(_runtime.CurrentHp, _config.stat.maxHp)
        : 1f;

    public BossAttackBlackboard Blackboard => _coreBB;

    // ── 2페이지 (IPagedBoss) ──────────────────────────────────
    /// <summary>페이지 부품 — MonsterBase가 초기화 중 EffectiveMaxHp를 읽을 때 처음 만들어진다(악몽기 여부를 그때 읽는다).</summary>
    public BossPages Pages => _pages ??= CreatePages();

    protected override float BossHpScale    => Pages.HpScale;
    protected override int   DamageHpFloor  => Pages.HpFloor(base.DamageHpFloor);

    // ── IBossHudSource (페이지 바 — BossPages에 위임) ─────────────
    public float[] HudPageMarkers  => Pages.HudPageMarkers;
    public int     HudPage         => Pages.HudPage;
    public bool    HudInvulnerable => IsInvulnerableNow;
    public event Action<bool>       HudInvulnerableChanged;
    public event Action<float>      HudVulnerableWindow;
    public event Action             HudPageMarkersChanged;
    public event Action<int, float> HudPageRefill;

    private bool IsInvulnerableNow =>
        _fsm != null && (_fsm.CurrentConstraints & SpecialStateConstraint.Invincible) != 0;

    // ── ForestGuardian 공개 접근 ──────────────────────────────
    public ForestGuardianBlackboard FGBlackboard => _fgBB;

    public Vector3 EntranceCamOffset       => _entranceCamOffset;
    public Vector3 EntranceCamLookOffset   => _entranceCamLookOffset;
    public float   EntranceCamMoveDuration   => _entranceCamMoveDuration;
    public float   EntranceCamHoldDuration   => _entranceCamHoldDuration;
    public float   EntranceCamReturnDuration => _entranceCamReturnDuration;

    // ── 2페이지 패턴이 읽는 무대 정보 ─────────────────────────────
    /// <summary>아레나 바닥 XZ 경계(가시 띠 · 간판 채널링 자리).</summary>
    internal Bounds  ArenaBoundsXZ       { get { EnsureArena(); return _arenaBounds; } }
    internal float   ArenaFloorY         { get { EnsureArena(); return _arenaFloorY; } }
    internal Vector3 ArenaCenter         { get { EnsureArena(); return new Vector3(_arenaBounds.center.x, _arenaFloorY, _arenaBounds.center.z); } }
    internal Color   ThornBandColor      => _thornBandColor;
    internal float   ThornBandDamageMult => _thornBandDamageMult;

    /// <summary>플레이어의 직접 타격(실제로 체력이 깎인 것) 누적 수 — 간판 「숲의 심장」이 시작 값과의 차로 센다.</summary>
    internal int DirectHitCount => _directHitCount;

    // ── 내부 필드 ─────────────────────────────────────────────
    private ForestGuardianBlackboard _fgBB;
    private BossAttackBlackboard     _coreBB;
    private BossPatternRunner        _runner;
    private BossPatternContext       _patternCtx;
    private FGDormantState _dormantState;

    // 2페이지
    private BossPages       _pages;
    private BossStageHazard _stageHazard;
    private Bounds          _arenaBounds;
    private float           _arenaFloorY;
    private bool            _arenaResolved;
    private bool            _thornWidened;
    private bool            _page2Tinted;
    private bool            _lastHudInvulnerable;
    private int             _directHitCount;
    private float           _lastDirectHitTime = -1f;

    // 목소리 사운드
    private float _voiceSfxTimer;
    private float _voiceSfxNextInterval;

    // 발걸음 사운드
    private int   _walkStateHash;
    private int   _footstepStateHash;
    private float _footstepPrevTime;

    // ── 패턴 가중치 회복 ──────────────────────────────────────
    private struct WeightRecoveryEntry
    {
        public BossPatternSO Pattern;
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

    /// <summary>
    /// 페이즈 체력 범위 — 공용 HpBelow/HpAbove(현재 / config 최대)는 2페이지 체력 배율(×1.4)을 모르므로
    /// 페이지 기준 <see cref="HpRatio"/>로 본다. 봉인기엔 두 공식이 같다.
    /// </summary>
    private sealed class FGHpRatioCondition : ICondition
    {
        private readonly ForestGuardianMonster _fg;
        private readonly float                 _threshold;
        private readonly bool                  _below;
        public FGHpRatioCondition(ForestGuardianMonster fg, float threshold, bool below)
        {
            _fg        = fg;
            _threshold = threshold;
            _below     = below;
        }
        public bool Evaluate(BossPatternContext ctx)
            => _below ? _fg.HpRatio <= _threshold : _fg.HpRatio > _threshold;
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

        // 가이드는 정적 주입 — 보스마다 자기 것을 넣는다(리치 뒤에 오면 리치 것이 남아 있다)
        PatternGuideHelper.SetMaterials(_circleGuideMaterial, _arrowGuideMaterial);

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

        _walkStateHash         = Animator.StringToHash(_walkStateName);
        _voiceSfxNextInterval  = Random.Range(_voiceSfxIntervalMin, _voiceSfxIntervalMax);

        BindBossHud();

        _dormantState = new FGDormantState();
        ChangeState(_dormantState);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 매 프레임
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected override void Update()
    {
        base.Update();

        if (IsDead) return;   // 사망 뒤 그로기 · 페이즈 판정이 사망 모션을 피격으로 덮던 결함
        if (_fgBB == null || _coreBB == null) return;
        TickHudInvulnerable();
        if (_dormantState != null && _dormantState.IsActive) return;

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

        UpdateVoiceSfx();
        UpdateFootstepSound();

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

    public override void TakeDamage(float amount, UnityEngine.GameObject instigator, float knockbackMultiplier = 1f, bool isCrit = false)
    {
        if (_fgBB != null && instigator != null)
        {
            bool isHeavy = _fgBB.IsBigWindowOpen;
            Vector3 dir  = instigator.transform.position - transform.position;
            _fgBB.SetHitDirection(dir, transform.forward, isHeavy);
        }

        if (_fgBB != null)
        {
            if (_fgBB.IsTransitioning)
                amount *= 0.01f;          // 변신 연출 중: 99% 감소
            else if (_fgBB.IsPhase2)
                amount *= 0.7f;           // 2페이즈: 30% 감소
        }

        int hpBefore = CurrentHp;
        base.TakeDamage(amount, instigator, knockbackMultiplier, isCrit);
        if (CurrentHp < hpBefore) CountDirectHit();   // 무적 · 사망 가드에 막힌 타격은 세지 않는다
    }

    protected override void OnDamageTaken()
    {
        if (_fgBB == null) return;

        bool isHeavy = _fgBB.IsBigWindowOpen;

        // 그로기는 2페이즈에서만 발동
        if (_fgBB.IsPhase2)
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
        // 2페이지 부품은 base가 EffectiveMaxHp(= 페이지 체력 배율)로 체력을 채우기 전에 비운다 — 이번 전투의 악몽기 여부를 다시 읽게.
        ResetPages();
        _arenaResolved = false;
        base.OnEnable();
        _runner?.Reset();
        _coreBB?.Reset();
        _fgBB?.Reset();
        _weightRecoveries.Clear();
        _thornWidened        = false;
        _directHitCount      = 0;
        _lastDirectHitTime   = -1f;
        _lastHudInvulnerable = false;
        RestoreBodyTint();
        _phase2Transitioning    = false;
        _voiceSfxTimer          = 0f;
        _voiceSfxNextInterval = Random.Range(_voiceSfxIntervalMin, _voiceSfxIntervalMax);
        if (_dormantState != null)
            ChangeState(_dormantState);
        BindBossHud();
    }

    protected override void OnDisable()
    {
        UnbindBossHudIfBound();
        ClearStageHazard();   // 가시 띠는 보스가 죽으면 스스로 사라지지만, 죽지 않고 풀로 돌아가면 남는다
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
        // 2페이지 키(Page_1 · Page_2 · Page_TransitionDue · Page_SignatureDue) — 아래 기본 분기가 AlwaysTrue라 먼저 거른다
        if (BossPageCondition.TryBuild(key, this, () => Pages, out var pageCondition))
            return pageCondition;

        return key switch
        {
            BossConditionKey.Phase2          => new FGHpRatioCondition(this, config.condPhase2HpThreshold, below: true),
            BossConditionKey.Dist_Close      => new MaxRangeCondition(config.condDistClose),
            BossConditionKey.Dist_Far        => new MinRangeCondition(config.condDistFar),
            BossConditionKey.TimePressure    => new NormalModeTimerCondition(config.condTimePressureSecs),
            BossConditionKey.FG_Phase1             => new FGHpRatioCondition(this, config.condPhase2HpThreshold, below: false),
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

        // 가중치 배율 즉시 0, 5~10초에 걸쳐 1로 복원 예약.
        // 배율은 러너에만 둔다 — 패턴 SO에 직접 쓰면 공용 에셋에 값이 남아 다음 전투까지 따라간다
        // (그렇게 굳은 값이 커밋돼 있었다: Smash 1.4e-8 · Punch 0.045 …).
        if (pattern.weight <= 0f) return;

        _runner?.SetWeightScale(pattern, 0f);

        // 기존 회복 항목 제거 — 부분 회복 중 재발동하면 처음부터 다시 센다
        for (int i = _weightRecoveries.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(_weightRecoveries[i].Pattern, pattern)) continue;
            _weightRecoveries.RemoveAt(i);
        }

        _weightRecoveries.Add(new WeightRecoveryEntry
        {
            Pattern  = pattern,
            Timer    = 0f,
            Duration = Random.Range(WeightRecoveryMin, WeightRecoveryMax),
        });
    }

    private void TickWeightRecoveries(float dt)
    {
        for (int i = _weightRecoveries.Count - 1; i >= 0; i--)
        {
            var e = _weightRecoveries[i];
            e.Timer += dt;
            float t = Mathf.Clamp01(e.Timer / e.Duration);
            _runner?.SetWeightScale(e.Pattern, t);

            if (t >= 1f)
            {
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
                if (_page2Tinted) SetBodyTint(_page2BodyTint);   // 2페이지에 들어선 뒤 로드가 끝났으면 새 머티리얼에도 붉은 빛
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

    /// <summary>패턴 비활성 상태에서 랜덤 간격으로 Voice1~3 중 하나를 재생해 "살아있는 보스" 느낌을 준다.</summary>
    private void UpdateVoiceSfx()
    {
        if (_runtime == null || _runtime.IsDead) return;
        if (_voiceClips == null || _voiceClips.Length == 0) return;

        if (_runner != null && _runner.IsPatternActive)
        {
            _voiceSfxTimer = 0f;
            return;
        }

        _voiceSfxTimer += Time.deltaTime;
        if (_voiceSfxTimer < _voiceSfxNextInterval) return;

        _voiceSfxTimer        = 0f;
        _voiceSfxNextInterval = Random.Range(_voiceSfxIntervalMin, _voiceSfxIntervalMax);
        var clip = _voiceClips[Random.Range(0, _voiceClips.Length)];
        Managers.Sound?.PlayEffectAt(clip, transform.position);
    }

    /// <summary>Walk 애니메이션 재생 중 발이 땅에 닿는 시점(normalizedTime)을 지날 때마다 Walk1~3 중 하나를 랜덤 재생한다.</summary>
    private void UpdateFootstepSound()
    {
        if (_footstepClips == null || _footstepClips.Length == 0
            || _footstepPhases == null || _footstepPhases.Length == 0
            || _animator == null) return;

        var info = _animator.GetCurrentAnimatorStateInfo(0);
        int hash = info.shortNameHash;

        if (hash != _walkStateHash)
        {
            _footstepStateHash = 0;
            return;
        }

        if (_footstepStateHash != hash)
        {
            _footstepStateHash = hash;
            _footstepPrevTime  = info.normalizedTime;
            return;
        }

        float currentTime = info.normalizedTime;
        foreach (float phase in _footstepPhases)
        {
            int prevCycle = Mathf.FloorToInt(_footstepPrevTime - phase);
            int curCycle  = Mathf.FloorToInt(currentTime - phase);
            if (curCycle != prevCycle)
            {
                var clip = _footstepClips[Random.Range(0, _footstepClips.Length)];
                Managers.Sound?.PlayEffectAt(clip, transform.position);
                break;
            }
        }
        _footstepPrevTime = currentTime;
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

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 2페이지 「고목의 분노」 — IPagedBoss · 무대 · 간판 보상
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>전환 전경 — 아레나 가장자리 띠가 가시 뿌리로 뒤덮인다(영구, <paramref name="seconds"/> 동안 자라남).</summary>
    public void OnPageStageChange(float seconds)
    {
        ClearStageHazard();
        EnsureArena();
        _stageHazard = BossStageHazard.CreateEdgeBand(this, _arenaBounds, _arenaFloorY, _thornBandWidth, seconds,
                                                      _thornBandColor, _thornBandVfxPrefab, _thornBandVfxScale, _thornBandDamageMult);
        Debug.Log($"[FG] 2페이지 무대 — 가시 뿌리 띠 {_thornBandWidth:F1} m · 아레나 {_arenaBounds.size.x:F0}×{_arenaBounds.size.z:F0} m", this);
    }

    /// <summary>
    /// 전환 끝 — 2페이지. 몸이 붉게 물든다.
    /// 한 방에 1페이지를 건너뛰어 1페이지의 2페이즈(머티리얼 · 속도 · 강인도)를 못 거쳤으면 지금 켠다 —
    /// 2페이지는 2페이즈 위에 얹히고, 1페이지 페이즈 전환 패턴(FG_Phase2Entry)이 2페이지에 끼어들지 않는다.
    /// </summary>
    public void OnPage2Entered()
    {
        if (_fgBB != null && !_fgBB.IsPhase2) TriggerPhase2();
        _page2Tinted = true;
        SetBodyTint(_page2BodyTint);
    }

    /// <summary>전환 · 간판이 끝나면 추격으로 — 다른 패턴과 같은 복귀.</summary>
    public void ReturnToCombat()
    {
        if (IsDead) return;
        ChangeState<ChaseState>();
    }

    /// <summary>간판 실패 — 가시 띠를 영구히 넓힌다(한 번만, 설계 §6 「무대 변화는 영구 · 상한」).</summary>
    internal void WidenThornBand(float extra)
    {
        if (_thornWidened || _stageHazard == null) return;
        _thornWidened = true;
        _stageHazard.Widen(extra);
    }

    /// <summary>간판 성공 — 그로기 <paramref name="seconds"/>초 + 받는 피해 증가. HUD에 무방비 창을 알린다.</summary>
    internal void BeginHeartGroggy(float seconds, float damageTakenAmp)
    {
        _fgBB?.ForceGroggy(seconds);
        if (damageTakenAmp > 0f) ApplyDamageTakenAmp(damageTakenAmp, seconds, HeartGroggyStatusId);
        HudVulnerableWindow?.Invoke(seconds);
    }

    /// <summary>HUD에 칠 수 있는 창을 알린다 — 간판 약점 채널링.</summary>
    internal void NotifyVulnerableWindow(float seconds) => HudVulnerableWindow?.Invoke(seconds);

    private BossPages CreatePages()
    {
        var pages = new BossPages(this, BossPages.ResolveEnabled());
        pages.HudPageRefill         += HandlePageRefill;
        pages.HudPageMarkersChanged += HandlePageMarkersChanged;
        return pages;
    }

    private void ResetPages()
    {
        if (_pages == null) return;
        _pages.HudPageRefill         -= HandlePageRefill;
        _pages.HudPageMarkersChanged -= HandlePageMarkersChanged;
        _pages = null;
    }

    /// <summary>아레나 바닥 상자(브레스와 같은 탐색, 스폰 지점 기준). 바닥 높이는 보스가 선 스폰 높이.</summary>
    private void EnsureArena()
    {
        if (_arenaResolved) return;
        _arenaResolved = true;
        Vector3 refPos = _runtime != null ? _runtime.SpawnPosition : transform.position;
        _arenaBounds = DragonPatternFloorUtils.ResolveArenaBoundsXZ(refPos, ArenaFallbackHalfSize);
        _arenaFloorY = refPos.y;
    }

    private void ClearStageHazard()
    {
        if (_stageHazard != null) Destroy(_stageHazard.gameObject);
        _stageHazard = null;
    }

    private void CountDirectHit()
    {
        float now = Time.time;
        if (now - _lastDirectHitTime < DirectHitMinInterval) return;
        _lastDirectHitTime = now;
        _directHitCount++;
    }

    private void TickHudInvulnerable()
    {
        bool inv = IsInvulnerableNow;
        if (inv == _lastHudInvulnerable) return;
        _lastHudInvulnerable = inv;
        HudInvulnerableChanged?.Invoke(inv);
    }

    private void RestoreBodyTint()
    {
        if (!_page2Tinted) return;
        _page2Tinted = false;
        SetBodyTint(Color.white);
    }

    /// <summary>몸 · 팔다리 머티리얼(인스턴스)의 _BaseColor. MPB는 피격 플래시(VictimHitFeedback)가 매번 비우므로 쓰지 않는다.</summary>
    private void SetBodyTint(Color tint)
    {
        TintRenderers(_bodyRenderers, tint);
        if (!ReferenceEquals(_limbRenderers, _bodyRenderers)) TintRenderers(_limbRenderers, tint);
    }

    private static void TintRenderers(Renderer[] renderers, Color tint)
    {
        if (renderers == null) return;
        foreach (var r in renderers)
        {
            if (r == null) continue;
            foreach (var m in r.materials)
                if (m != null && m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, tint);
        }
    }

    private void HandlePageRefill(int page, float seconds) => HudPageRefill?.Invoke(page, seconds);
    private void HandlePageMarkersChanged()                => HudPageMarkersChanged?.Invoke();

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // IBossEntrance
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    public override bool HasEntranceAnimation => true;

    public override string BossName => _bossDisplayName;

    // OnEntranceRequested 는 발행하지 않는다 — BRC 카메라 팬 개입 방지
    public event Action OnEntranceRequested;
    public event Action OnCombatReady;

    internal void FireCombatReady()
    {
        _runner?.EnsureMinBreakCooldown(3f);
        OnCombatReady?.Invoke();
        RaiseBossCombatReady();
    }

    // BRC 호출용 — 이 보스는 Enter 에서 직접 시작하므로 실질적으로 호출되지 않음
    public void TriggerEntrance()
    {
        _dormantState?.TriggerEntrance(_ctx);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 등장 연출 전용 메서드 (FGDormantState 에서 호출)
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    internal void PlayEntranceVoice()
    {
        if (_voiceClips == null || _voiceClips.Length == 0) return;
        var clip = _voiceClips[Random.Range(0, _voiceClips.Length)];
        Managers.Sound?.PlayEffectAt(clip, transform.position);
    }
}
}
