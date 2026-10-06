using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.UI;
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
///
/// ━━ 해방 페이지(악몽기, 09-28 설계 확정 §5 「왕을 벤 검」) ━━━━━━━━━━
///  위 전투 전체가 1페이지다(<see cref="BossPages"/> — 1페이지 동안 HpRatio는 1페이지 기준 1→0).
///  1페이지 체력이 다 깎이면 전환 연출(<see cref="BossPageTransitionPatternSO"/>)을 직접 건다 — 콤보 러너는 강제 항목도
///  콤보·휴식이 끝나야 내므로 기다리지 않는다. 전환 전경에서 플레이어 구역 가운데 3×3이 무너지고(영구 · 지속 피해),
///  2페이지에선 수동 공격(영혼 창 · 환영 돌진)과 영혼 소환 회복이 멈춘다.
///  2페이지 패턴은 설정 SO의 Page_2 항목(직접 패턴)으로 고른다 — 배치 사본(Arena_Boss_Ch3, 약 250 MB)의 풀은 건드리지 않는다.
///  간판 「원탁의 무덤」은 2페이지 50%에서 한 번, 역시 직접 건다.
///  악몽 모드 2페이지엔 특성 「지휘」 — 환영 기수가 서 있는 동안 쉬는 시간 ×0.85 · 피해 +15%(<see cref="DKCommandTrait"/>).
/// </summary>
public class DeathKnightBossMonster : MonsterBase, IBoss, IBossEntrance, IPagedBoss, IBossHudSource
{
    // ── 상수 ─────────────────────────────────────────────
    private const float Phase2SpeedMult    = 1.2f;
    private const float Phase2BreakMin     = 1.0f;
    private const float Phase2BreakMax     = 2.5f;
    private const float EnrageSpeedMult    = 1.3f;
    private const float Phase2HpThreshold  = 0.4f;
    private const float SoulGateRatio      = 0.5f;   // 영혼 소환 전까지 HP를 붙잡는 1페이지 비율

    // ── 2페이지 무대(가운데 3×3 붕괴) ─────────────────────
    private static readonly Color CollapseColor = new Color(0.22f, 0.20f, 0.26f, 0.62f);
    private const float CollapseDamagePerTick   = 0.12f;  // 0.5초마다 공격력 × 이 배율
    private const float PostTransitionBreak     = 1.5f;   // 전환이 끝나고 첫 패턴까지
    private static readonly Color Page2WindowBase     = new Color(0.30f, 0.30f, 0.34f, 0.4f);
    private static readonly Color Page2WindowEmission = new Color(0.32f, 0.32f, 0.36f, 1f);
    private const float Page2EmissionBoost = 2.5f;   // 2페이지 — 갑옷 발광 배율(검 색 알림은 그대로, 09-29)

    // ── 검 색 규칙 가르치기(10-03 개선 2-2) ───────────────
    private const float  SwordCueFlashSeconds = 0.35f;  // 갑옷 번쩍 — 잠깐만
    private const float  SwordCueFlashBoost   = 2f;     // 번쩍 순간 갑옷 발광 배율(2페이지 발광 배율과 곱해진다)
    private const float  SwordCueRingScale    = 1.6f;   // 발밑 고리(런 공용 Ring) 크기
    private const string FloorRuleHint        = "검과 같은 색의 바닥이 베인다";
    private const string FlipToBlackHint      = "검이 검게 물들었다";
    private const string FlipToWhiteHint      = "검이 하얗게 물들었다";
    private const string CrossFlipHint        = "색이 뒤집힌다 — 마지막 색의 반대로";
    private const string TreasonColorHint     = "검과 같은 색의 환영만 벤다 — 다른 색 칸으로";

    // ── Inspector ─────────────────────────────────────────
    [Header("DeathKnight — 표시")]
    [Tooltip("보스 체력바 이름. config.monsterName은 퀘스트 처치 키라 바꾸지 않는다")]
    [SerializeField] private string _bossDisplayName = "죽음의 기사";

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
    /// <summary>페이즈 · 소환 · 광폭 경계용 비율 — 1페이지 동안 1페이지 기준(1→0), 2페이지에선 기존 체력 기준(0.4→0).</summary>
    public float HpRatio =>
        (_runtime != null && _config != null && _config.stat.maxHp > 0)
        ? Pages.PhaseRatio(_runtime.CurrentHp, _config.stat.maxHp)
        : 1f;

    public BossAttackBlackboard Blackboard => _coreBB;

    // ── 2페이지(해방) ─────────────────────────────────────
    /// <summary>
    /// 2페이지 상태. 풀 재사용(OnEnable)마다 악몽기 여부를 다시 읽어 새로 만든다.
    /// MonsterBase가 초기화 중 최대 체력(<see cref="MonsterBase.EffectiveMaxHp"/>)을 읽으므로 없으면 바로 만든다.
    /// </summary>
    public BossPages Pages => _pages ??= CreatePages();
    public string StoryBossId => StoryProgress.DeathKnight;

    protected override float BossHpScale    => Pages.HpScale;
    protected override int   DamageHpFloor  => Mathf.Max(Pages.HpFloor(base.DamageHpFloor), SoulGatePending ? SoulGateHp : 0);

    /// <summary>영혼 소환 전 — 체력이 1페이지 50%(<see cref="SoulGateHp"/>) 아래로 안 내려간다(일반 · 시너지 · 지속 피해 모두 하한으로).</summary>
    private bool SoulGatePending => _dkBB != null && !_dkBB.IsPhase2 && !_soulGateCleared;
    /// <summary>영혼 소환 하한 — 내림이라 소환 조건(HpRatio ≤ 0.5)을 언제나 만족한다.</summary>
    private int  SoulGateHp      => Pages.Page2Hp + Mathf.FloorToInt(Pages.Page1Hp * SoulGateRatio);
    /// <summary>하한에 닿았다 — 영혼 소환까지 「막힘」(피해 숫자 · 피격 경직 없음 → 지금 공격이 끝나면 바로 소환, 10-06 실측 10초 → 짧게).</summary>
    private bool SoulGateHolding => SoulGatePending && _runtime != null && _runtime.CurrentHp <= SoulGateHp;

    /// <summary>1페이지(= 기존 전투) 최대 체력 — 영혼 소환 회복량 등 기존 비율의 기준. 봉인기엔 최대 체력 그대로.</summary>
    public int PhaseMaxHp => Pages.Page1Hp;

    /// <summary>2페이지 플레이어 구역(유리벽 앞 격자) — 전환 때 한 번 잰다.</summary>
    public DKPage2Zone Page2Zone => _page2Zone ??= DKPage2Zone.Resolve(this);

    // ── IBossHudSource ────────────────────────────────────
    public float[] HudPageMarkers  => Pages.HudPageMarkers;
    public int     HudPage         => Pages.HudPage;
    public bool    HudInvulnerable => IsDamageImmuneNow;
    public event Action<bool>       HudInvulnerableChanged;
    public event Action<int, float> HudPageRefill;
    public event Action             HudPageMarkersChanged;
    /// <summary>기사는 무방비 창을 알리지 않는다(간판의 약점은 보스 몸이 아니라 무덤 기둥).</summary>
    public event Action<float>      HudVulnerableWindow { add { } remove { } }

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
    private GameObject                _page2AuraInstance;   // 2페이지 — 반대 빛깔 오라를 하나 더(흑백이 함께 피어오른다)
    private GameObject                _page2AuraPrefab;
    private float                     _page2Glow;           // 0 = 1페이지 · 1 = 2페이지 갑옷 발광
    private DKP2PassiveAttackRunner   _passiveRunner;
    private CancellationTokenSource   _passiveCts;
    private CancellationTokenSource   _healCts;
    private bool                      _soulGateCleared;
    private BossPages                 _pages;
    private DKPage2Zone               _page2Zone;
    private BossPageTransitionPatternSO _pageTransition;
    private BossPatternSO             _pageSignature;
    private BossStageHazard           _stageHazard;
    private bool                      _lastHudInvulnerable;
    private float                     _swordCueGlow;        // 0~1 — 검 색 신호 때 갑옷 발광이 잠깐 세진다(10-03)
    private bool                      _floorHintShown;      // 규칙 자막 — 전투마다 한 번씩
    private bool                      _flipHintShown;
    private bool                      _crossFlipHintShown;
    private bool                      _treasonHintShown;
    private DKCommandTrait            _command;             // 악몽 특성 「지휘」 — 환영 기수(10-02)

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
        ResolvePagePatterns(bossConfig);
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
        _command = new DKCommandTrait(this);

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

        if (IsDead) return;   // 사망 뒤 연속기 대기열이 다음 패턴을 쏴 사망 상태에서 끌려나오던 결함
        if (_dkBB == null || _coreBB == null) return;
        if (_dormantState != null && _dormantState.IsActive) return;

        TickHudInvulnerable();
        _command?.Tick(_ctx);   // 악몽 「지휘」 — 전환 · 간판이 이 프레임을 끊기 전에

        // 2페이지 전환 · 간판 — 콤보 러너보다 먼저(러너는 강제 항목도 콤보·휴식이 끝나야 낸다).
        // 둘 다 특수 상태라 도는 동안 러너는 새 패턴을 내지 않는다.
        if (TryBeginPageTransition()) return;
        if (TryBeginPageSignature()) return;

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

        // 영혼 소환은 한 번만 — 기둥을 못 깨 회복돼도 게이트를 다시 잠그지 않는다(10-01 감사 D3).
        // 예전엔 55%를 넘으면 다시 잠가 50%에서 또 소환했고, 기둥을 못 깨는 빌드는 실패 → 회복 → 소환을 끝없이 되풀이했다.

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
        // 2페이지 여부를 먼저 정한다 — base.OnEnable이 HP를 최대 체력(페이지 배율 포함)으로 채운다.
        ResetPages();
        base.OnEnable();
        InitializeRoomContext();
        _page2Zone = null;
        DestroyStageHazard();
        _command?.Reset("전투 초기화", true);
        _lastHudInvulnerable = false;
        _passiveCts?.Cancel();
        _passiveCts?.Dispose();
        _passiveCts    = null;
        _passiveRunner = null;
        _runner?.Reset();
        _runner?.SetBreakRange(-1f, -1f);   // 풀 재사용 — 1페이즈 쉬는 시간(설정 값)으로
        _coreBB?.Reset();
        _dkBB?.Reset();
        _prevPatternActive = false;
        _isStaggered       = false;
        _soulGateCleared   = false;
        _page2Glow         = 0f;
        _swordCueGlow      = 0f;
        _floorHintShown    = _flipHintShown = _crossFlipHintShown = _treasonHintShown = false;
        if (_dkBB != null) ApplyArmorTint(_dkBB.SwordColor);
        ApplyWindowTint(_dkBB?.SwordColor ?? DKSwordColor.White);
        ApplyAuraColor(_dkBB?.SwordColor ?? DKSwordColor.White);
        SyncPage2Aura();
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
        HudPageMarkersChanged?.Invoke();   // 같은 보스 재바인딩은 눈금을 다시 읽지 않는다 — 새 페이지 상태를 알린다
        _pendingTriggerEntrance = false;
        if (_dormantState != null)
            ChangeState(_dormantState);
    }

    protected override void OnDisable()
    {
        DestroyStageHazard();   // 죽지 않고 비활성(런 종료 등)돼도 붕괴 구역이 남아 플레이어를 치지 않게
        _command?.Reset("기사 비활성", true);   // 환영 기수도 — 남으면 플레이어 구역에 서 있는다
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

    /// <summary>
    /// 처치 — 수동 공격(영혼 창 · 환영 돌진)과 남은 연속기를 먼저 끊는다.
    /// 끊지 않으면 처치 뒤 약 2초간 플레이어가 맞았다(09-19 감사).
    /// </summary>
    protected override void OnFatalDamage()
    {
        _passiveCts?.Cancel();
        _passiveCts?.Dispose();
        _passiveCts    = null;
        _passiveRunner = null;
        _healCts?.Cancel();
        _healCts?.Dispose();
        _healCts = null;
        _runner?.Reset();
        _command?.Reset("기사 쓰러짐", false);   // 환영 기수는 흩어진다
        base.OnFatalDamage();
    }

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
        // TakeDamage를 우회해도 페이지 경계(1페이지 동안 2페이지 몫)는 넘지 않는다 — 봉인기엔 바닥 0 그대로
        _runtime.CurrentHp = Mathf.Max(DamageHpFloor, _runtime.CurrentHp - amount);
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
        if (Pages.IsPage2 || Pages.Transitioning) return;   // 2페이지엔 영혼 소환 회복이 없다(설계 §5)
        _runtime.CurrentHp = Mathf.Min(_runtime.CurrentHp + amount, EffectiveMaxHp);
        NotifyHpChanged();
    }

    public void SoulPillarHealBossGradual(int total, float duration)
    {
        if (_runtime == null || _runtime.IsDead || total <= 0) return;
        if (Pages.IsPage2 || Pages.Transitioning) return;
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

    /// <summary>영혼 소환·방패 구간의 자체 무적도 「막힘」으로 읽히게 한다(공용 규약).</summary>
    public override bool IsDamageImmuneNow => base.IsDamageImmuneNow || (_dkBB != null && _dkBB.IsInvincible) || SoulGateHolding;

    public override void TakeDamage(float amount, UnityEngine.GameObject instigator,
                                    float knockbackMultiplier = 1f,
                                    bool isCrit = false)
    {
        if (_dkBB != null && _dkBB.IsInvincible) return;
        if (SoulGateHolding) return;   // 영혼 소환 대기 — 막힘(CombatDamage가 막힘 표시를 띄운다). 하한은 DamageHpFloor가 지킨다
        base.TakeDamage(amount, instigator, knockbackMultiplier, isCrit);
    }

    public void NotifySoulSummonCompleted() => _soulGateCleared = true;

    /// <summary>
    /// 검 색상을 반전하고 갑옷 · 장벽 · 창문 · 오라를 맞춘다.
    /// 보이는 검도 지금 바꾼다 — 바뀌는 순간이 검에 보여야 한다(10-03 개선 2-2). 디졸브 중일 때만 다음 ShowSword()에 맡긴다(핑크 검 방지).
    /// 이번 전투 첫 전환엔 바뀐 색을 자막으로도 알린다.
    /// </summary>
    public void FlipSwordColor()
    {
        if (_dkBB == null) return;
        _dkBB.FlipSwordColor();
        _swordCtrl?.ApplySwordColorNow(_dkBB.SwordColor);
        ApplyArmorTint(_dkBB.SwordColor);
        ApplyBarrierTint(_dkBB.SwordColor);
        ApplyWindowTint(_dkBB.SwordColor);
        ApplyAuraColor(_dkBB.SwordColor);
        SwordCueFlashAsync().Forget();

        if (_flipHintShown) return;
        _flipHintShown = true;
        UI_BossBark.Show(_dkBB.SwordColor == DKSwordColor.Black ? FlipToBlackHint : FlipToWhiteHint,
                         BossBarkType.PatternAnnounce);
    }

    /// <summary>
    /// 검 색 바닥 패턴이 타일을 까는 순간(패턴마다 한 번) — 갑옷이 잠깐 번쩍이고 발밑에서 검 색 고리가 퍼진다.
    /// 「검과 같은 색 바닥이 베인다」를 기사 몸과 바닥으로 잇는다. 이번 전투 첫 번째엔 규칙 자막도(10-03 개선 2-2).
    /// 검 렌더러는 디졸브가 재질을 갈아 끼우므로 건드리지 않는다 — 검 색 알림은 갑옷 발광이 맡아 왔다.
    /// </summary>
    public void CueSwordFloor()
    {
        if (_dkBB == null) return;
        SwordCueFlashAsync().Forget();
        RunFx.Play(RunFxSlot.Ring, transform.position + Vector3.up * 0.1f, SwordCueRingScale,
                   _dkBB.SwordColor == DKSwordColor.White ? Color.white : DKGridPatternHelper.DarkReadableTint);

        if (_floorHintShown) return;
        _floorHintShown = true;
        UI_BossBark.Show(FloorRuleHint, BossBarkType.PatternAnnounce);
    }

    /// <summary>2페이지 KL3 「반역의 환영」 — 이번 전투 첫 번째에만 규칙 자막(10-03 S3).</summary>
    public void HintTreasonColor()
    {
        if (_treasonHintShown) return;
        _treasonHintShown = true;
        UI_BossBark.Show(TreasonColorHint, BossBarkType.PatternAnnounce);
    }

    /// <summary>2페이지 KL1 「흑백 교차」 — 이번 전투 첫 번째에만 규칙 자막(10-03 개선 2-2).</summary>
    public void HintCrossFlip()
    {
        if (_crossFlipHintShown) return;
        _crossFlipHintShown = true;
        UI_BossBark.Show(CrossFlipHint, BossBarkType.PatternAnnounce);
    }

    /// <summary>갑옷 발광을 잠깐 세웠다가 되돌린다 — 피격 깜빡임 중엔 그쪽이 끝날 때 복원한다.</summary>
    private async UniTaskVoid SwordCueFlashAsync()
    {
        var ct = destroyCancellationToken;
        try
        {
            for (float t = 0f; t < SwordCueFlashSeconds; t += Time.deltaTime)
            {
                _swordCueGlow = 1f - t / SwordCueFlashSeconds;
                if (_dkBB != null && _hitBlinkRoutine == null) ApplyArmorTint(_dkBB.SwordColor);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException) { return; }
        _swordCueGlow = 0f;
        if (_dkBB != null && _hitBlinkRoutine == null) ApplyArmorTint(_dkBB.SwordColor);
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
        SyncPage2Aura();
    }

    /// <summary>2페이지 반대 빛깔 오라 — 검 색이 바뀌면 같이 바뀐다(늘 흑백 한 쌍).</summary>
    private void SyncPage2Aura()
    {
        GameObject want = _page2Glow > 0f
            ? (_currentAuraPrefab == _whiteAuraPrefab ? _blackAuraPrefab : _whiteAuraPrefab)
            : null;
        if (want == _page2AuraPrefab && (want == null || _page2AuraInstance != null)) return;
        if (_page2AuraInstance != null)
        {
            BossEffectPool.Release(_page2AuraInstance);
            _page2AuraInstance = null;
        }
        _page2AuraPrefab = want;
        if (want == null) return;

        Transform anchor = _auraAnchor != null ? _auraAnchor : transform;
        _page2AuraInstance = BossEffectPool.Spawn(want, anchor.position, Quaternion.identity, anchor);
        if (_page2AuraInstance != null)
        {
            _page2AuraInstance.transform.localPosition = new Vector3(0f, -1.5f, 0f);
            _page2AuraInstance.transform.localRotation = Quaternion.identity;
        }
    }

    /// <summary>전환 — 바가 차오르는 <paramref name="seconds"/> 동안 갑옷 발광이 세지고, 반대 빛깔 오라가 함께 피어오른다(09-29).</summary>
    private async UniTaskVoid Page2GlowAsync(float seconds)
    {
        var ct = destroyCancellationToken;
        _page2Glow = 0.01f;
        SyncPage2Aura();
        try
        {
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                if (_page2Glow <= 0f) return;   // 도중에 초기화(비활성 → 새 전투)
                _page2Glow = Mathf.Max(0.01f, t / seconds);
                if (_dkBB != null && _hitBlinkRoutine == null) ApplyArmorTint(_dkBB.SwordColor);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException) { return; }
        if (_page2Glow <= 0f) return;
        _page2Glow = 1f;
        if (_dkBB != null && _hitBlinkRoutine == null) ApplyArmorTint(_dkBB.SwordColor);
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
        if (_page2Glow > 0f) emission *= 1f + (Page2EmissionBoost - 1f) * _page2Glow;
        if (_swordCueGlow > 0f) emission *= 1f + (SwordCueFlashBoost - 1f) * _swordCueGlow;

        _propBlock.SetColor("_BaseColor",      baseTint);
        _propBlock.SetColor("_EmissionColor",  emission);
        foreach (var r in _bodyRenderers)
            if (r != null) r.SetPropertyBlock(_propBlock);
    }

    private void ApplyWindowTint(DKSwordColor color)
    {
        if (_windowRenderers == null || _windowRenderers.Length == 0) return;

        // 2페이지 — 흑백이 섞인 회색 빛(전환 전경부터). 검 색은 갑옷 · 오라가 계속 알린다.
        if (_stageHazard != null)
        {
            if (_propBlock == null) _propBlock = new MaterialPropertyBlock();
            _propBlock.SetColor(BaseColorId,     Page2WindowBase);
            _propBlock.SetColor(EmissionColorId, Page2WindowEmission);
            foreach (var r in _windowRenderers)
                if (r != null) r.SetPropertyBlock(_propBlock);
            return;
        }

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
        // 2페이지 공용 키(Page_1 · Page_2 · 전환 · 간판) — 기본 분기(AlwaysTrue)보다 먼저
        if (BossPageCondition.TryBuild(key, this, () => Pages, out var pageCondition))
            return pageCondition;

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

        _runner?.SetBreakRange(Phase2BreakMin, Phase2BreakMax);   // 공용 설정 SO에 쓰지 않는다(10-01 감사)

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
    // 2페이지(해방) — IPagedBoss
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>전환 전경 — 플레이어 구역 가운데 3×3이 무너진다(영구 · 지속 피해) · 창문 빛이 회색으로.</summary>
    public void OnPageStageChange(float seconds)
    {
        DestroyStageHazard();
        var zone = Page2Zone;
        _stageHazard = BossStageHazard.CreateRect(this, zone.CollapseRect, zone.FloorY, seconds,
                                                  CollapseColor, null, 1f, CollapseDamagePerTick);
        ApplyWindowTint(_dkBB?.SwordColor ?? DKSwordColor.White);
        Page2GlowAsync(seconds).Forget();
        BossImpactFeedback.TriggerCameraShake(0.14f, Mathf.Max(0.4f, seconds * 0.5f));
        Debug.Log($"[DK] 2페이지 무대 — 가운데 3×3 붕괴 중심={zone.Center} 앞줄={zone.FrontRow} 구역 z={zone.MinZ}~{zone.MaxZ}", this);
    }

    /// <summary>
    /// 2페이지 진입 — 수동 공격(영혼 창 · 환영 돌진)과 영혼 소환 회복을 멈추고, 1페이지 경계가 다시 켜지지 않게 닫는다.
    /// (2페이지 HpRatio는 0.4→0이라 페이즈 2 · 광폭 · 55% 소환 재개 조건이 그대로면 다시 켜질 수 있다)
    /// </summary>
    public void OnPage2Entered()
    {
        StopPassiveAttacks();
        CancelGradualHeal();
        if (_dkBB == null) return;

        _dkBB.SetPhase2();          // 페이즈 2 진입 · 영혼 소환 · 55% 재개 모두 닫힘
        _soulGateCleared = true;    // 50% 클램프 해제
        TryEnrage();                // 1페이지에서 이미 켜졌다 — 2페이지 도중에 켜져 속도가 바뀌지 않게
        _dkBB.SetInvincible(false);
        _runner?.SetBreakRange(Phase2BreakMin, Phase2BreakMax);
    }

    /// <summary>전환이 끝나면 패턴 대기 상태로 — 첫 패턴 전에 한숨 돌린다.</summary>
    public void ReturnToCombat()
    {
        ChangeState<AttackReadyState>();
        _runner?.EnsureMinBreakCooldown(PostTransitionBreak);
    }

    /// <summary>유리벽(연출 종료 때 켜지는 장벽) 콜라이더까지의 거리 — 2페이지 구역의 앞줄을 잴 때.</summary>
    internal bool TryFindGlass(Vector3 origin, Vector3 dir, float maxDistance, out float distance)
    {
        distance = float.MaxValue;
        if (_entranceEndBarriers == null) return false;
        var ray = new Ray(origin, dir);
        foreach (var barrier in _entranceEndBarriers)
        {
            if (barrier == null) continue;
            foreach (var col in barrier.GetComponentsInChildren<Collider>())
                if (col != null && col.Raycast(ray, out var hit, maxDistance) && hit.distance < distance)
                    distance = hit.distance;
        }
        return distance < float.MaxValue;
    }

    private BossPages CreatePages()
    {
        var pages = new BossPages(this, BossPages.ResolveEnabled());
        pages.HudPageRefill         += RelayHudPageRefill;
        pages.HudPageMarkersChanged += RelayHudPageMarkersChanged;
        return pages;
    }

    /// <summary>풀 재사용 — 이번 전투의 악몽기 여부를 다시 읽는다(HUD 구독은 보스 이벤트라 그대로 이어진다).</summary>
    private void ResetPages()
    {
        if (_pages != null)
        {
            _pages.HudPageRefill         -= RelayHudPageRefill;
            _pages.HudPageMarkersChanged -= RelayHudPageMarkersChanged;
        }
        _pages = CreatePages();
    }

    /// <summary>설정 SO 항목에서 전환 · 간판 패턴을 찾는다(둘 다 콤보 러너 대신 이 보스가 직접 건다).</summary>
    private void ResolvePagePatterns(BossConfigSO config)
    {
        _pageTransition = null;
        _pageSignature  = null;
        if (config.patternEntries == null) return;
        foreach (var entry in config.patternEntries)
        {
            if (entry?.patterns == null) continue;
            foreach (var p in entry.patterns)
            {
                if (p is BossPageTransitionPatternSO t && _pageTransition == null) _pageTransition = t;
                if (p is DKRoundTableTombPatternSO s && _pageSignature == null) _pageSignature = s;
            }
        }
        if (Pages.Enabled && _pageTransition == null)
            Debug.LogWarning("[DK] 2페이지 전환 패턴(BossPageTransitionPatternSO)이 설정에 없다 — 연출 없이 바로 넘긴다", this);
    }

    /// <summary>1페이지 체력이 다 깎였으면 콤보를 끊고 전환 연출로. 전환 패턴이 없으면 연출 없이 바로 2페이지.</summary>
    private bool TryBeginPageTransition()
    {
        if (!Pages.TransitionDue(CurrentHp)) return false;

        StopPassiveAttacks();
        CancelGradualHeal();
        ResetRunnerKeepSword();

        if (_pageTransition == null || _pageTransition.GetRuntimeState() == null)
        {
            Pages.BeginTransition();
            Pages.BeginRefill(0.5f);
            OnPageStageChange(0.5f);
            Pages.CompleteTransition();
            OnPage2Entered();
            ReturnToCombat();
            return true;
        }

        ChangeState(_pageTransition.GetRuntimeState());
        return true;
    }

    /// <summary>2페이지 50% — 지금 패턴이 끝나면 간판 「원탁의 무덤」을 바로 건다.</summary>
    private bool TryBeginPageSignature()
    {
        if (_pageSignature == null || IsInSpecialState || _isStaggered) return false;
        if (!_pageSignature.CanExecute(_patternCtx)) return false;
        var state = _pageSignature.GetRuntimeState();
        if (state == null) return false;

        ResetRunnerKeepSword();
        ChangeState(state);
        return true;
    }

    /// <summary>
    /// 콤보 대기열을 비운다. 러너의 「패턴 중」 표시가 꺼지므로 검 표시 기억도 같이 끈다 —
    /// 안 끄면 다음 프레임에 검을 디졸브로 감췄다가 바로 다시 꺼내(디졸브 재진입) 검이 깨진다.
    /// 보이던 검은 그대로 두고, 새 특수 상태가 러너에 잡히는 프레임에 ShowSword(이미 보이면 무시)만 탄다.
    /// </summary>
    private void ResetRunnerKeepSword()
    {
        _runner?.Reset();
        _prevPatternActive = false;
    }

    private void TickHudInvulnerable()
    {
        bool inv = HudInvulnerable;
        if (inv == _lastHudInvulnerable) return;
        _lastHudInvulnerable = inv;
        HudInvulnerableChanged?.Invoke(inv);
    }

    private void StopPassiveAttacks()
    {
        _passiveCts?.Cancel();
        _passiveCts?.Dispose();
        _passiveCts    = null;
        _passiveRunner = null;
    }

    private void DestroyStageHazard()
    {
        if (_stageHazard == null) return;
        Destroy(_stageHazard.gameObject);
        _stageHazard = null;
    }

    private void RelayHudPageRefill(int page, float seconds) => HudPageRefill?.Invoke(page, seconds);
    private void RelayHudPageMarkersChanged() => HudPageMarkersChanged?.Invoke();

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // IBossEntrance
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    public override bool HasEntranceAnimation => true;

    public override string BossName => _bossDisplayName;

    public event Action OnEntranceRequested;
    public event Action OnCombatReady;

    internal void FireEntranceRequest() => OnEntranceRequested?.Invoke();

    internal void FireCombatReady()
    {
        foreach (var b in _entranceEndBarriers)
            if (b != null) b.SetActive(true);
        ApplyBarrierTint(_dkBB?.SwordColor ?? DKSwordColor.White);

        // [근접 기회 A안] 유리 앞 공명 성흔석 — 유리 너머 기사를 근접 빌드도 칠 수 있게(유리가 켜진 뒤에 거리를 잰다)
        var playerT = GameRunBootstrapper.Instance?.Run?.Player?.transform;
        Vector3 playerSide = _pyramidStrikeAnchor != null ? _pyramidStrikeAnchor.position
                           : playerT != null ? playerT.position
                           : transform.position - transform.forward * 10f;
        DKResonanceStone.SpawnSetAsync(this, _entranceEndBarriers, playerSide, destroyCancellationToken).Forget();

        LoadGuideMaterialsAsync(destroyCancellationToken).Forget();   // 예고 데칼 재질 · 검 색 고리(10-03)
        GameCameraController.Instance?.ActivateDKPlayerOrbit(1.0f);
        _runner?.EnsureMinBreakCooldown(3f);
        OnCombatReady?.Invoke();
        RaiseBossCombatReady();
    }

    /// <summary>
    /// 예고 데칼 재질(원 · 화살표)을 넣는다 — 다른 보스처럼 정적 주입이지만 기사 프리팹엔 재질 칸이 없어 런 공용 목록(리치와 같은 재질)에서 받는다.
    /// 안 넣으면 Ch3로 바로 들어올 때 KL2 · KL4 빔 예고가 무늬 없는 판으로, 앞 보스를 거치면 그 보스 재질로 그려졌다(10-03 개선 2-1).
    /// 같은 목록의 고리 이펙트는 <see cref="CueSwordFloor"/>가 쓴다.
    /// </summary>
    private async UniTaskVoid LoadGuideMaterialsAsync(CancellationToken ct)
    {
        try
        {
            await RunFx.LoadAsync();
            if (ct.IsCancellationRequested || RunFx.GuideCircle == null) return;
            PatternGuideHelper.SetMaterials(RunFx.GuideCircle, RunFx.GuideArrow);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { Debug.LogWarning($"[DK] 런 공용 이펙트 목록 로드 예외 — 예고는 기본 도형으로: {e.Message}", this); }
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
