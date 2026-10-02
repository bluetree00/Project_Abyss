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
/// ━━ 두 전투 (모드는 전투 시작 때 세계 단계로 정한다) ━━━━━━━━━━━━━━━━━━━━
///  A 봉인된 리치 (봉인기) : P1 대마법 (100→40) → T1 사슬의 각성 → P2′ 사슬에 묶인 낫 (40→0)
///                           → HP 0 = 봉인 → 붕괴 → 퇴각
///  B 해방된 리치 (악몽기) : P1⁺ (100→60) → T2 봉인은 없다 → P2 대마법+낫 (60→25)
///                           → T3 최후의 원 → P3 영혼 복제 (25→0) → 사망 (첫 처치 = 엔딩)
///  페이지 임계에선 HP가 더 내려가지 않는다(<see cref="DamageHpFloor"/>) — 한 방에 페이지를 건너뛰지 않게.
///
/// ━━ 조건 키 (페이지 키 + 모드 키 조합으로 풀을 가른다) ━━━━━━━━━━━━━━━━━━
///  Lich_Phase1 / Lich_IsPhase2 / Lich_Phase3 : 현재 페이지 1 / 2 / 3
///  Lich_Phase2Pending / Lich_Phase3Pending    : 다음 페이지로 넘어갈 HP에 닿음 (전환 패턴 강제 발동)
///  Lich_Sealed / Lich_Nightmare               : 이번 전투의 모드
///
/// 설계: 바탕화면 기획 「리치보스_완전설계_봉인기_악몽기」.
/// </summary>
public class LichMonster : MonsterBase, IBoss, IBossEntrance, IBossHudSource
{
    // ── 상수 ─────────────────────────────────────────────────
    private const int    Phase2UnlockAt       = 3;                  // 에디터 옛 조우 횟수 디버그 전용 — 이 횟수부터 악몽기로 본다
    private const string DeathDeepDialogueKey = "Lich_Death_Deep";  // 엔딩 뒤 처치
    private const string CastBoneName         = "hand_r";           // 마법이 나가는 손
    private const string BookMaterialName     = "MI_Book";          // 책 발광 머티리얼(인스턴스는 이름 뒤에 (Instance))
    private const float  AuraFloorLift        = 0.05f;
    private const float  ArenaOrbitBlend      = 1.2f;
    private const float  BlockedFxGap         = 0.12f;   // 막힘 표시 최소 간격(연타로 겹치지 않게)
    private const float  NightmareHpScale     = 1.1f;    // 악몽 모드 총 체력(레벨디자인 설계서 §6) — 이미 3줄 + 최종 마법이라 다른 보스(×1.15)보다 낮게

    // 패링(연출·UX 시나리오 §12-3) — 낫이 빛나는 창 안에 맞받아치면 그 공격을 튕겨낸다.
    private const float  ParryRange           = 6f;      // 원거리 저격 패링 방지(수평 거리)
    private const float  ParryStaggerDuration = 1.2f;
    private const float  ParrySlowScale       = 0.3f;
    private const float  ParrySlowSeconds     = 0.15f;
    private const float  DeathBurstScale      = 0.55f;   // 사망 폭발 — 근접 구도에서도 화면을 덮지 않게
    private const float  DeathImpactDelay     = 0.8f;    // 사망 → 코어에 떨어지는 충격
    private const string ParryHintText        = "낫이 빛나는 순간 — 맞받아쳐라";

    private static readonly Color ParryGlintTint = new Color(1f, 0.85f, 0.35f, 1f);

    private static readonly int BookGlowId = Shader.PropertyToID("_EmissiveBoots2");

    /// <summary>제단 구도 한 벌 — FreeLook 세 리그의 (높이, 반경).</summary>
    [Serializable]
    private struct ArenaOrbit
    {
        public Vector2 top;
        public Vector2 middle;
        public Vector2 bottom;

        public ArenaOrbit(Vector2 top, Vector2 middle, Vector2 bottom)
        {
            this.top    = top;
            this.middle = middle;
            this.bottom = bottom;
        }
    }

    // ── MonsterBase 추상 멤버 ─────────────────────────────────
    public  const  string PrefabAddress    = "Lich/Lich";
    protected override string ConfigAddress   => "Lich/LichConfig";
    protected override string DataAddress     => string.Empty;
    protected override string HeadBoneName    => null;
    protected override float  HPBarHeadOffset => 0.25f;
    protected override bool   UseWorldHPBar   => false;
    protected override float  BossHpScale     => StoryProgress.IsNightmareMode ? NightmareHpScale : 1f;

    // ── IBoss ─────────────────────────────────────────────────
    public float HpRatio =>
        (_runtime != null && EffectiveMaxHp > 0)
        ? (float)_runtime.CurrentHp / EffectiveMaxHp
        : 1f;

    public BossAttackBlackboard Blackboard => _lichBB;

    // ── 공개 접근 ─────────────────────────────────────────────
    public LichBlackboard LichBB => _lichBB;

    // ── IBossHudSource ───────────────────────────────────────
    public float[] HudPageMarkers  => PageThresholds;
    public int     HudPage         => _hudPage;
    public bool    HudInvulnerable => IsInvulnerableNow;
    public event Action<bool>       HudInvulnerableChanged;
    public event Action<float>      HudVulnerableWindow;
    public event Action             HudPageMarkersChanged;
    public event Action<int, float> HudPageRefill;

    private bool IsInvulnerableNow =>
        _fsm != null && (_fsm.CurrentConstraints & SpecialStateConstraint.Invincible) != 0;

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
    private Transform              _castPoint;
    private Renderer[]             _bodyRenderers;
    private GameObject             _auraMarker;
    private bool                   _bodyVisible = true;
    private bool                   _lastInvulnerable;
    private bool                   _arenaCameraOn;
    private Renderer               _bookRenderer;
    private Material               _bookMaterial;
    private float                  _bookBaseGlow = -1f;
    private float                  _bookPulse;
    private float                  _bookPulseSeconds = 0.6f;
    private float                  _lastBlockedFx = -1f;
    private float                  _parryWindowEnd = -1f;
    private bool                   _parried;
    private bool                   _parryHintShown;
    private int                    _hudPage = 1;   // HP바가 보여 주는 페이즈 — 전환 연출이 바를 채우기 시작할 때 넘어간다
    private LichSwingDriver        _swing;         // 낫 휘두름을 판정 순간에 맞춘다(처음 쓸 때 만든다)
    private float                  _damageTakenMult = 1f;   // 그로기 등 — 받는 피해 배율

    public LichMovementController MovementController => _movementController;

    private LichSwingDriver Swing
    {
        get
        {
            if (_swing == null && _animator != null)
            {
                _swing = new LichSwingDriver(_animator);
                _swing.StrikeStarted += HandleSwingStrike;
            }
            return _swing;
        }
    }

    /// <summary>지금 패링 창이 열려 있는가(낫이 빛나는 중).</summary>
    public bool IsParryWindowOpen => !_parried && Time.time <= _parryWindowEnd;

    /// <summary>마법이 나가는 손(오른손 본). 없으면 리치 자신.</summary>
    public Transform CastPoint => _castPoint != null ? _castPoint : transform;

    /// <summary>스폰 지점 Y — 해골 소환 등 지면 높이 추정에 사용.</summary>
    public float SpawnGroundY => _movementController != null ? _movementController.GroundY : transform.position.y;

    [Header("── 페이지 · 모드 ──────────────────────────────")]
    [Tooltip("봉인기(봉인된 리치) 페이지 전환 HP 비율 — 내림차순. [40%] = P1 → T1 → P2′")]
    [SerializeField] private float[] _sealedPageThresholds    = { 0.4f };
    [Tooltip("악몽기(해방된 리치) 페이지 전환 HP 비율 — 내림차순. [60%, 25%] = P1⁺ → T2 → P2 → T3 → P3")]
    [SerializeField] private float[] _nightmarePageThresholds = { 0.6f, 0.25f };
    [SerializeField] private string  _sealedBossName          = "봉인된 리치";
    [SerializeField] private string  _nightmareBossName       = "해방된 리치";

    [Header("── 등장 연출 ──────────────────────────────────")]
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
    [Tooltip("2페이즈 무대 변화 — 주변광(핏빛 보라).")]
    [SerializeField] private Color _page2AmbientColor  = new Color(0.10f, 0.01f, 0.05f);
    [Tooltip("2페이즈 무대 변화 — 주 조명 색(진홍).")]
    [SerializeField] private Color _page2LightTint     = new Color(1.0f, 0.22f, 0.35f);
    [Tooltip("2페이즈 무대 변화 — 주 조명 강도 배율(원래 강도 기준).")]
    [SerializeField, Range(0f, 1f)] private float _page2LightMult = 0.35f;

    [Header("── 전투 카메라 「제단 구도」 (연출·UX 시나리오 §10-4) ──")]
    [Tooltip("기본 게임 구도(중간 리그 높이 7.3 · 반경 4)보다 높고 멀리 — 60 m 제단의 리치와 바닥 예고를 화면에 담는다")]
    [SerializeField] private ArenaOrbit _altarOrbitWide   = new ArenaOrbit(new Vector2(12.0f, 4.6f), new Vector2(10.0f, 5.6f), new Vector2(6.6f, 5.2f));
    [Tooltip("악몽기 2페이지 — 교전 거리가 가까워져 조금 당긴다")]
    [SerializeField] private ArenaOrbit _altarOrbitNear   = new ArenaOrbit(new Vector2(11.0f, 4.2f), new Vector2(9.0f, 5.0f), new Vector2(6.0f, 4.8f));
    [Tooltip("3페이지 — 코어 20 m를 꽉 채우는 근접 구도(설계서 §6)")]
    [SerializeField] private ArenaOrbit _altarOrbitClose  = new ArenaOrbit(new Vector2(9.8f, 3.8f), new Vector2(8.2f, 4.6f), new Vector2(5.4f, 4.4f));
    [Tooltip("봉인 의식 — 봉인석 넷이 다 보이게 높이")]
    [SerializeField] private ArenaOrbit _altarOrbitRitual = new ArenaOrbit(new Vector2(15.0f, 6.0f), new Vector2(13.0f, 7.0f), new Vector2(9.0f, 6.5f));

    [Header("── 몸 발광 · 발밑 표식 ─────────────────────────")]
    [Tooltip("시전 순간 책 발광 배율(머티리얼 기본값 대비)")]
    [SerializeField] private float _bookPulseBoost = 3.5f;
    [Tooltip("날고 있는 리치의 바닥 위치 표식 배율(BossAura 칸)")]
    [SerializeField] private float _auraMarkerScale = 0.6f;

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
    private bool _debugAutoPatternsOff; // 테스트 — 패턴 러너 정지(강제 실행만)
#endif

    /// <summary>
    /// 세계가 악몽기인가 = 이번 전투를 해방된 리치(B)로 치르는가. 전투 시작 때 <see cref="ResolveMode"/>가 읽어
    /// 블랙보드에 고정한다(전투 도중 붕괴가 일어나도 그 전투의 모드는 바뀌지 않는다).
    ///
    /// ── 분기 지점 ───────────────────────────────────────────────────
    ///  • 출시: <see cref="StoryProgress.IsLiberated"/> — 봉인기에 리치를 봉인하는 순간 붕괴가 일어나 해방기로(악몽 모드 포함).
    ///  • 테스트: 메뉴 RelicFairy/Test Run/Story/Override (저장 안 함). 인스펙터의 옛 조우 횟수 디버그도 남아 있다.
    /// </summary>
    public bool IsPhase2Unlocked
    {
        get
        {
#if UNITY_EDITOR
            if (_debugOverrideEncounter) return _debugSessionCount >= Phase2UnlockAt;
#endif
            // 출시 게이트 — 봉인기에 리치를 봉인하는 순간 붕괴가 일어나고, 해방기부터 해방된 리치(3줄)(메타 영구).
            return StoryProgress.IsLiberated;
        }
    }

    // ── 커스텀 ICondition ─────────────────────────────────────

    // BuiltConditions는 공유 ScriptableObject에 저장되므로 생성 시 _lichBB를 캡처하면
    // 마지막으로 BuildConditions()를 호출한 풀 인스턴스의 블랙보드를 참조하게 된다.
    // 평가 시점에 ctx.Boss로 활성 Lich를 조회해 항상 올바른 블랙보드를 사용한다.
    private sealed class LichPhase1Condition : ICondition
    {
        // 첫 전환 전 — 두 모드 공통. 전환은 Lich_Phase2Pending이 전담하며 결계(SealBreaker)와는 무관하다.
        public bool Evaluate(BossPatternContext ctx)
            => ((ctx.Boss as LichMonster)?.LichBB?.Page ?? 1) == 1;
    }

    private sealed class LichPhase2Condition : ICondition
    {
        // 정확히 2페이지 — 3페이지에서 2페이지 풀이 섞이지 않게(낫 패턴 게이트 IsPhase2는 2 이상).
        public bool Evaluate(BossPatternContext ctx)
            => ((ctx.Boss as LichMonster)?.LichBB?.Page ?? 1) == 2;
    }

    private sealed class LichPhase2PendingCondition : ICondition
    {
        // 1페이지에서 첫 임계에 닿음 — 봉인기 T1 / 악몽기 T2 (모드 키로 가른다).
        public bool Evaluate(BossPatternContext ctx)
            => (ctx.Boss as LichMonster)?.IsPageTransitionDue(1) ?? false;
    }

    private sealed class LichPhase3Condition : ICondition
    {
        public bool Evaluate(BossPatternContext ctx)
            => ((ctx.Boss as LichMonster)?.LichBB?.Page ?? 1) == 3;
    }

    private sealed class LichPhase3PendingCondition : ICondition
    {
        // 2페이지에서 둘째 임계에 닿음 — 임계가 둘인 악몽기만 해당(T3).
        public bool Evaluate(BossPatternContext ctx)
            => (ctx.Boss as LichMonster)?.IsPageTransitionDue(2) ?? false;
    }

    private sealed class LichFinalMagicPendingCondition : ICondition
    {
        // 3페이지 · HP 임계 · 아직 안 막음 — F4 「최후의 대마법」 강제.
        public bool Evaluate(BossPatternContext ctx)
            => (ctx.Boss as LichMonster)?.IsFinalMagicDue ?? false;
    }

    private sealed class LichModeCondition : ICondition
    {
        private readonly bool _nightmare;
        public LichModeCondition(bool nightmare) => _nightmare = nightmare;

        public bool Evaluate(BossPatternContext ctx)
        {
            var bb = (ctx.Boss as LichMonster)?.LichBB;
            return bb != null && bb.IsNightmare == _nightmare;
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

        _castPoint     = FindDeep(transform, CastBoneName);
        _bodyRenderers = (_formController != null ? _formController.transform : transform)
                         .GetComponentsInChildren<Renderer>(true);
        LichVfx.LoadAsync().Forget();   // 패턴 이펙트 목록 — 없어도 패턴은 돈다
        LichSfx.LoadAsync().Forget();   // 패턴 소리 목록 — 없어도 패턴은 돈다
        FindBookRenderer();

        // 공중 이동 컨트롤러 초기화 (NavMeshAgent 비활성화 후 직접 Transform 제어)
        _movementController = GetComponent<LichMovementController>();
        if (_movementController == null)
            Debug.LogWarning("[LichMonster] LichMovementController 컴포넌트가 없습니다. 프리팹에 추가하세요.", this);

        if (TryGetComponent<UnityEngine.AI.NavMeshAgent>(out var navAgent))
            navAgent.enabled = false;

        _lichBB = new LichBlackboard();
        ResolveMode();   // HUD 이름(BindBossHud)이 모드를 읽는다 — 먼저 정한다

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
            onExecuted:  p  => { _lichBB.LastPatternTag = p.patternTag; _lichBB.NormalModeTimer = 0f; LichPatternProbe.BeginPattern(p.name); });

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
        _dormantState = new LichDormantState(_detectionRange);

#if UNITY_EDITOR
        if (_debugSkipEntrance)
        {
            Debug.Log("[LichMonster] debugSkipEntrance — DormantState 생략, ChaseState 즉시 진입");
            _dormantState = null; // IsActive 가드가 Update를 차단하지 않도록 null 처리
            _formController?.ApplyForm(LichForm.Phase1); // 디버그: 등장 연출 없이 즉시 표시
            ChangeState<ChaseState>();
            if (_debugForcePhase2)
            {
                // 2페이지로 바로 — 전환 연출 없이 페이지·폼만. HP는 첫 임계 조금 아래로.
                EnterPageCore(2, _lichBB.IsNightmare ? LichForm.Phase2 : LichForm.Phase2_Bound, 1f, 1f, -1f, -1f);
                if (_runtime != null)
                    _runtime.CurrentHp = Mathf.RoundToInt(EffectiveMaxHp * (PageThresholds[0] - 0.05f));
                Debug.Log("[LichMonster] debugForcePhase2 — 2페이지 강제 적용 완료");
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
        _swing?.Tick(Time.deltaTime);

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
        TickPresentation(dt);

        // 패턴이 active → inactive 로 전환된 시점에 취약 구간 알림
        bool nowPattern = _runner?.IsPatternActive ?? false;
        if (_prevPatternActive && !nowPattern)
            _movementController?.NotifyPatternEnded();
        _prevPatternActive = nowPattern;

#if UNITY_EDITOR
        if (_debugAutoPatternsOff) return;
#endif
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
        ResolveMode();
        _movementController?.OnRecycled();
        _pendingTriggerEntrance = false;
        _lightingChanged        = false;

        // 풀 재사용: 숨김 후 등장 연출 재진입 (TriggerEntrance에서 다시 디졸브 인)
        _formController?.HideAll();
        SetBodyVisible(true);   // 순간이동 도중 비활성화됐어도 몸은 돌아온다
        _lastInvulnerable = false;
        _parryWindowEnd   = -1f;
        _parried          = false;
        _parryHintShown   = false;
        if (_dormantState != null)
            ChangeState(_dormantState);

        BindBossHud();
        ArenaTileGrid.TileRestoring += HandleTileRestoring;
    }

    protected override void OnDisable()
    {
        ArenaTileGrid.TileRestoring -= HandleTileRestoring;
        UnbindBossHudIfBound();
        LichHazards.Clear();
        LichCrack.ClearAll();
        LichVfx.Stop(ref _auraMarker);
        LichStageShift.Clear();
        LichSealShard.ClearAll();
        LichSoulCopy.ClearAll();
        LichSealChainBolt.ClearAll();
        SetArenaCamera(false);
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

    /// <summary>
    /// 리치는 맞을 때마다 움찔하지 않는다 — 활강·패턴 리듬을 피격이 끊지 않게(다른 보스의 경직 게이트와 같은 역할).
    /// 무방비는 패턴이 정한 휘청 창(<see cref="NotifyVulnerableWindow"/>)으로만 준다.
    /// </summary>
    protected override void OnDamageTaken()
    {
        base.OnDamageTaken();
        _suppressGetHitThisHit = true;
    }

    private void TryParry(GameObject instigator)
    {
        if (_parried || Time.time > _parryWindowEnd || instigator == null) return;
        var player = _runtime?.PlayerTarget;
        if (player == null) return;

        var src = instigator.transform;
        if (src != player && !src.IsChildOf(player)) return;
        if (LichPatternUtil.FlatDistance(transform.position, player.position) > ParryRange) return;

        _parried        = true;
        _parryWindowEnd = -1f;
        Debug.Log("[Lich] 패링 — 낫을 튕겨냈다", this);

        Vector3 at = Vector3.Lerp(CastPoint.position, player.position + Vector3.up, 0.5f);
        LichVfx.Play(LichVfxSlot.ParryClash, at, Quaternion.identity);
        LichSfx.Play(LichSfxSlot.ParryClash, at);
        LichCinematics.Flash(Color.white, 0.12f, 0.15f);   // 패링 — 자주 나오니 옅게(09-20 화면 실측: 선형 합성이라 0.3도 진하다)
        LichCinematics.SlowMo(ParrySlowScale, ParrySlowSeconds);
        LichPatternUtil.Impact(LichImpact.Medium, false);
    }

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
        SetArenaCamera(true);
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
    // 모드 · 페이지
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    public override string BossName =>
        _lichBB != null && _lichBB.IsNightmare ? _nightmareBossName : _sealedBossName;

    /// <summary>이번 전투의 페이지 전환 임계(내림차순). 개수 + 1 = 페이지 수.</summary>
    public float[] PageThresholds =>
        _lichBB != null && _lichBB.IsNightmare ? _nightmarePageThresholds : _sealedPageThresholds;

    /// <summary>
    /// 다음 페이지 전환 임계에서 HP를 붙잡는다 — 전환 연출을 거치기 전엔 그 아래로 내려가지 않는다.
    /// 마지막 페이지에선 제한 없음(처치 가능).
    /// </summary>
    protected override int DamageHpFloor
    {
        get
        {
            int floor = base.DamageHpFloor;
            if (_lichBB == null) return floor;
            var thresholds = PageThresholds;
            int next = _lichBB.Page - 1;
            if (thresholds == null || next >= thresholds.Length)
            {
                // 마지막 페이지 — 최후의 대마법(F4)을 막기 전엔 그 임계 아래로 깎이지 않는다.
                if (_lichBB.IsNightmare && _lichBB.Page >= 3 && !_lichBB.FinalMagicDone)
                    return Mathf.Max(floor, Mathf.CeilToInt(FinalMagicThreshold * EffectiveMaxHp));
                return floor;
            }
            return Mathf.Max(floor, Mathf.CeilToInt(thresholds[next] * EffectiveMaxHp));
        }
    }

    /// <summary>최후의 대마법(F4) 임계 — 3페이지에서 HP 비율이 이 아래면 강제 발동.</summary>
    private const float FinalMagicThreshold = 0.10f;

    /// <summary>3페이지 최후의 대마법(F4)을 쓸 때가 됐는가 — 임계에 닿았고 아직 막지 않았다.</summary>
    public bool IsFinalMagicDue
        => _lichBB != null && _lichBB.IsNightmare && _lichBB.Page >= 3 && !_lichBB.FinalMagicDone
           && HpRatio <= FinalMagicThreshold + 0.0001f;

    /// <summary><paramref name="fromPage"/>에서 다음 페이지로 넘어갈 HP에 닿았는가 (전환 패턴 강제 발동 조건).</summary>
    public bool IsPageTransitionDue(int fromPage)
    {
        if (_lichBB == null || _lichBB.Page != fromPage) return false;
        var thresholds = PageThresholds;
        int idx = fromPage - 1;
        return thresholds != null && idx < thresholds.Length && HpRatio <= thresholds[idx];
    }

    /// <summary>LichPhase2EntryPatternSO(페이지 전환) 완료 시 호출 — 페이지를 올리고 그 페이지의 버프·폼을 건다.</summary>
    public void EnterPage(LichPhase2EntryPatternSO transition)
    {
        if (transition == null) return;
        EnterPageCore(transition.targetPage, transition.form,
                      transition.speedMultiplier, transition.attackSpeedMultiplier,
                      transition.breakDurationMin, transition.breakDurationMax);
    }

    /// <summary>
    /// 페이즈 전환 컷신 — 다음 페이즈 HP바가 <paramref name="seconds"/> 동안 차오르기 시작한다(HUD가 받아 그린다).
    /// HP 자체는 페이즈 경계에 붙잡혀 있고, 바는 그 경계부터 다음 경계까지를 한 줄로 보여 준다.
    /// </summary>
    public void BeginPageRefill(int page, float seconds)
    {
        _hudPage = page;
        HudPageRefill?.Invoke(page, seconds);
    }

    /// <summary>폼 전환(디졸브) — 봉인 의식·붕괴 컷신이 쓴다.</summary>
    internal void DissolveToForm(LichForm form)
    {
        if (_formController != null)
            _formController.DissolveInFormAsync(form, destroyCancellationToken).Forget();
    }

    /// <summary>세계 단계를 읽어 이번 전투의 모드를 블랙보드에 정한다(HP바 눈금도 모드를 따른다).</summary>
    private void ResolveMode()
    {
        _lichBB?.SetMode(IsPhase2Unlocked);
        _hudPage = 1;
        HudPageMarkersChanged?.Invoke();
    }

    /// <summary>결계 패턴 설정 — 봉인 의식이 주기 공격·방해 해골 값을 빌린다. 없으면 null(기본값).</summary>
    private LichSealBreakerPatternSO FindWardPattern()
    {
        if (_config is not BossConfigSO boss || boss.patternEntries == null) return null;
        foreach (var entry in boss.patternEntries)
        {
            if (entry?.patterns == null) continue;
            foreach (var pattern in entry.patterns)
                if (pattern is LichSealBreakerPatternSO ward) return ward;
        }
        return null;
    }

    private void EnterPageCore(int page, LichForm form, float speedMult, float attackSpeedMult,
                               float breakMin, float breakMax)
    {
        if (_lichBB == null || page <= _lichBB.Page) return;

        _lichBB.SetPage(page);

        if (_runtime != null)
            _runtime.SpeedMultiplier = speedMult;
        _lichBB.MoveSpeedMult   = speedMult;   // 이동 컨트롤러가 읽는다(10-01)
        _lichBB.AttackSpeedMult = attackSpeedMult;

        // 휴식 덮어쓰기는 블랙보드에만 — 공용 BossConfig SO를 건드리지 않는다. 악몽 모드면 ×0.8.
        if (breakMin >= 0f) _lichBB.BreakDurationMinOverride = breakMin * _lichBB.BreakScale;
        if (breakMax >= 0f) _lichBB.BreakDurationMaxOverride = breakMax * _lichBB.BreakScale;

        // 전환 컷신이 폼을 이미 드러냈으면(포효 순간) 다시 디졸브하지 않는다.
        if (_formController != null && _formController.CurrentForm != form)
            _formController.DissolveInFormAsync(form, destroyCancellationToken).Forget();

        if (_hudPage < page)
        {
            _hudPage = page;
            HudPageMarkersChanged?.Invoke();
        }

        if (_arenaCameraOn) ApplyArenaOrbit(OrbitForPage());

        Debug.Log($"[Lich] {page}페이지 진입 ({(_lichBB.IsNightmare ? "해방" : "봉인")}) — HP={_runtime?.CurrentHp} ratio={HpRatio:F2} 폼={form}", this);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 내부 헬퍼
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>HP 0 도달 시 호출(마지막 페이지에서만 — 그 전엔 HP가 임계에 붙잡힌다). 모드에 따라 봉인 or 사망.</summary>
    protected override void OnFatalDamage()
    {
        // 전투 종료 — 보스보다 오래 남는 생존 해골·장판 정리(퇴각·사망 공통).
        LichSkeletonMonster.DespawnAll();
        LichHazards.Clear();
        LichSoulCopy.ClearAll();
        LichSealChainBolt.ClearAll();
        LichVfx.Stop(ref _auraMarker, 0.5f);

        if (_lichBB == null || !_lichBB.IsNightmare)
        {
            // 봉인기 — 봉인 의식을 거쳐 리치를 봉인하는 순간 봉인이 버티지 못하고 무너진다(붕괴).
            // 세계 기록(모든 보스 봉인 해제 → 악몽기)은 의식이 완성될 때 남긴다. 반영은 다음 조우부터.
            RunSealedEndAsync(destroyCancellationToken).Forget();
            return;
        }

        // 악몽기 완전 격파 — 첫 처치는 엔딩(대사·카드는 보스 클리어 흐름이 튼다), 이후는 짧은 바크.
        if (!StoryProgress.MarkEnding())
            UI_BossBark.ShowDialogue(DeathDeepDialogueKey);
        PlayDeathPresentation();
        SetArenaCamera(false);
        base.OnFatalDamage();
    }

    /// <summary>
    /// 봉인된 리치의 끝 — 봉인 의식 → 붕괴 컷신(<see cref="LichSealRitual"/>) → 퇴장 → 보스방 완료.
    /// 골드·사망 이펙트 없음, <see cref="MonsterBase.RaiseDied"/>로 방 클리어.
    /// </summary>
    private async UniTaskVoid RunSealedEndAsync(CancellationToken ct)
    {
        // 진행 중이던 패턴 특수 상태를 빠져나가 Exit(가이드·빔·VFX 정리)를 보장한다.
        // 사망 시 Update가 정지하므로 패턴이 스스로 종료하지 못해 월드 오브젝트가 잔존하는 문제 방지.
        ChangeState<ChaseState>();

        _movementController?.SetLocked(true);
        _runner?.Reset();

        try
        {
            await new LichSealRitual(this, _ctx, FindWardPattern()).RunAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception e)
        {
            // 의식이 깨져도 방은 끝나야 한다 — 여기서 멈추면 출구 없는 보스방에 갇힌다.
            Debug.LogError($"[Lich] 봉인 의식 예외 — 방 클리어로 넘어간다: {e}", this);
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
    /// LichDormantState.TriggerEntrance에서 호출(컨트롤러 팬·폴백 팬 공통 경로) — 이번 전투의 모드를 확정한다.
    /// 스폰 뒤 세계 단계가 바뀌었을 수 있다(테스트 오버라이드 등).
    /// </summary>
    internal void BeginEncounter()
    {
        ResolveMode();
        ApplyNightmareTempo();
        Debug.Log($"[Lich] 전투 시작 — {BossName} · 페이지 임계 [{string.Join(", ", PageThresholds)}]", this);
    }

    /// <summary>
    /// 악몽 모드 — 다른 보스와 같은 쉬는 시간 ×0.8(BossPages.NightmareBreakScale). 리치는 IPagedBoss가 아니라 러너가 페이지 배율을
    /// 곱하지 않으므로 블랙보드 덮어쓰기로 건다: 1페이지는 설정 값 × 배율, 2 · 3페이지는 전환 에셋 값 × 배율(EnterPageCore).
    /// </summary>
    private void ApplyNightmareTempo()
    {
        if (_lichBB == null) return;
        _lichBB.BreakScale = StoryProgress.IsNightmareMode ? BossPages.NightmareBreakScale : 1f;
        if (_lichBB.Page == 1 && _lichBB.BreakScale < 1f && _config is BossConfigSO boss)
        {
            _lichBB.BreakDurationMinOverride = boss.patternBreakDurationMin * _lichBB.BreakScale;
            _lichBB.BreakDurationMaxOverride = boss.patternBreakDurationMax * _lichBB.BreakScale;
        }
    }

    /// <summary>
    /// 몸(모델·장비)을 그리거나 감춘다 — 순간이동 패턴용. 렌더러의 켜짐 상태는 폼 컨트롤러가 쥐고 있으므로
    /// 건드리지 않고 <see cref="Renderer.forceRenderingOff"/>만 바꾼다.
    /// </summary>
    public void SetBodyVisible(bool visible)
    {
        _bodyVisible = visible;
        if (_bodyRenderers == null) return;
        foreach (var r in _bodyRenderers)
            if (r != null) r.forceRenderingOff = !visible;
    }

    /// <summary>무적에 막혔다 — 「맞았다」 대신 막힘 표시(결계 돔 · 전환 · 사라짐).</summary>
    public override void NotifyBlockedHit(Vector3 hitPoint)
    {
        if (Time.time - _lastBlockedFx < BlockedFxGap) return;
        _lastBlockedFx = Time.time;
        LichVfx.PlayTinted(LichVfxSlot.ArcaneOrbBurst, hitPoint, Quaternion.identity, 0.35f, new Color(0.8f, 0.9f, 1f, 1f));
        LichSfx.Play(LichSfxSlot.Blocked, hitPoint, 0.8f);
    }

    /// <summary>
    /// 패링 판정 — 창이 열린 동안 플레이어의 타격이 닿으면 이번 낫 공격을 튕겨낸다.
    /// 그 타격의 피해는 평소대로 들어간다(맞받아친 보상은 휘청으로 준다).
    /// </summary>
    public override void TakeDamage(float amount, GameObject instigator, float knockbackMultiplier = 1f, bool isCrit = false)
    {
        TryParry(instigator);
        base.TakeDamage(amount * _damageTakenMult, instigator, knockbackMultiplier, isCrit);
    }

    /// <summary>받는 피해 배율 — 최후의 대마법을 막은 그로기 동안 올린다. 1이면 보통.</summary>
    public void SetDamageTakenMultiplier(float mult) => _damageTakenMult = Mathf.Max(0f, mult);

    /// <summary>
    /// 낫 공격 판정 직전에 연다 — 낫이 금빛으로 빛나고 「쨍」 소리가 난다. 판정 순간 <see cref="ConsumeParried"/>로 닫는다.
    /// </summary>
    public void OpenParryWindow(float seconds)
    {
        _parried        = false;
        _parryWindowEnd = Time.time + Mathf.Max(0.05f, seconds);

        Vector3 glint = CastPoint.position;
        LichVfx.PlayTinted(LichVfxSlot.ParryGlint, glint, Quaternion.identity, 1f, ParryGlintTint);
        LichSfx.Play(LichSfxSlot.ParryCue, glint);

        if (_parryHintShown) return;
        _parryHintShown = true;
        UI_BossBark.Show(ParryHintText, BossBarkType.PatternAnnounce);
    }

    /// <summary>
    /// 낫 휘두름 — <paramref name="contactIn"/>초 뒤 판정 순간에 클립의 접촉 프레임이 오도록 튼다
    /// (느린 준비 → 빠른 베기 → 접촉 멈칫). 휙 소리는 드라이버가 베기 구간에 낸다.
    /// </summary>
    public void PlaySwing(LichSwing swing, float contactIn, float holdSeconds = 0.07f)
        => Swing?.Play(swing, contactIn, holdSeconds);

    /// <summary>휘두름 드라이버를 멈춘다(애니메이터 속도 1).</summary>
    public void StopSwing() => _swing?.Cancel();

    /// <summary>시전 박자 — <paramref name="releaseIn"/>초 뒤 방출 프레임(충전 자세로 버티다 내뻗는다).</summary>
    public void PlayCastBeat(LichCast cast, float releaseIn, float snapSpeed = 2f, float holdSeconds = 0.1f)
        => Swing?.PlayBeat(cast, releaseIn, snapSpeed, holdSeconds);

    /// <summary>판정 순간 패턴이 부른다 — 튕겨냈으면 true(이 공격의 피해를 주지 말 것). 창은 닫힌다.</summary>
    public bool ConsumeParried()
    {
        bool parried    = _parried;
        _parried        = false;
        _parryWindowEnd = -1f;
        return parried;
    }

    /// <summary>튕겨낸 뒤의 휘청 — 몸이 젖혀지고 HP바가 금빛으로 맥동한다. 패턴은 돌려받은 시간만큼 멈춰 있는다.</summary>
    public float BeginParryStagger()
    {
        _swing?.Cancel();
        if (_animator != null) _animator.CrossFade("GetHit", 0.05f);
        NotifyVulnerableWindow(ParryStaggerDuration);
        return ParryStaggerDuration;
    }

    /// <summary>무방비 창이 열렸다 — HP바가 금빛으로 맥동한다(패턴의 반격창 · 휘청에서 부른다).</summary>
    public void NotifyVulnerableWindow(float seconds)
    {
        if (seconds > 0f) HudVulnerableWindow?.Invoke(seconds);
    }

    /// <summary>시전 순간 — 책이 잠깐 밝게 빛난다.</summary>
    public void PulseBook(float seconds = 0.6f)
    {
        _bookPulse        = 1f;
        _bookPulseSeconds = Mathf.Max(0.05f, seconds);
    }

    /// <summary>봉인 의식 동안 제단 전체를 담는 높은 구도(false면 페이지 구도로 돌아간다).</summary>
    internal void SetRitualCamera(bool on)
    {
        if (!_arenaCameraOn) return;
        ApplyArenaOrbit(on ? _altarOrbitRitual : OrbitForPage());
    }

    /// <summary>손에 든 낫을 숨기거나(투척 중) 현재 폼대로 되돌린다.</summary>
    public void SetScytheVisible(bool visible) => _formController?.SetScytheVisible(visible);

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

    /// <summary>
    /// 2페이즈 무대 변화 — 조명을 핏빛 보라로 <paramref name="seconds"/> 동안 옮긴다. 원래 조명은 전투가 끝날 때 그대로 되돌린다.
    /// </summary>
    public void ShiftToPage2Lighting(float seconds)
    {
        if (!_lightingChanged) return;
        ShiftLightingAsync(seconds, destroyCancellationToken).Forget();
    }

    private async UniTaskVoid ShiftLightingAsync(float seconds, CancellationToken ct)
    {
        var   sun        = RenderSettings.sun;
        Color fromAmb    = RenderSettings.ambientLight;
        Color fromTint   = sun != null ? sun.color : Color.white;
        float fromInt    = sun != null ? sun.intensity : 1f;
        float toInt      = _originalMainLightIntensity * _page2LightMult;
        float t          = 0f;
        try
        {
            while (t < seconds)
            {
                t += Time.deltaTime;
                float f = Mathf.Clamp01(t / Mathf.Max(0.01f, seconds));
                RenderSettings.ambientLight = Color.Lerp(fromAmb, _page2AmbientColor, f);
                if (sun != null)
                {
                    sun.color     = Color.Lerp(fromTint, _page2LightTint, f);
                    sun.intensity = Mathf.Lerp(fromInt, toInt, f);
                }
                await UniTask.Yield(ct);
            }
        }
        catch (OperationCanceledException) { }
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
    public void StartEncounterRecord()
    {
        StoryProgress.MarkLichMet();   // 리치가 멀린의 이름을 부르는 순간 — 대사창 표시명 공개
        RecordEncounterAsync(destroyCancellationToken).Forget();
    }

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
    /// <summary>[테스트] 패턴 자동 선택을 켜고 끈다 — 끄면 강제 실행한 패턴만 돈다. 새 값을 돌려준다.</summary>
    public bool Editor_ToggleAutoPatterns()
    {
        _debugAutoPatternsOff = !_debugAutoPatternsOff;
        return !_debugAutoPatternsOff;
    }

    /// <summary>
    /// [테스트] 설정에 든 패턴을 타입 이름으로 찾아 지금 바로 실행한다(쿨다운·패턴 조건 무시).
    /// 같은 타입이 여러 풀에 있으면(봉인판·해방판 에셋) 지금 조건이 맞는 풀의 것을 먼저 고른다.
    /// 다른 패턴이 진행 중이거나 등장 연출 중이면 거절한다. 메뉴 RelicFairy/Test Run/Boss Room/9 Force Lich Pattern.
    /// </summary>
    public bool Editor_ForcePattern(string patternTypeName)
    {
        if (!Application.isPlaying || _lichBB == null || IsInSpecialState) return false;
        if (_dormantState != null && _dormantState.IsActive) return false;
        if (_config is not BossConfigSO boss || boss.patternEntries == null) return false;

        BossPatternSO found = null;
        for (int pass = 0; pass < 2 && found == null; pass++)
        {
            foreach (var entry in boss.patternEntries)
            {
                if (entry?.patterns == null) continue;
                if (pass == 0 && !entry.EvaluateConditions(_patternCtx)) continue;
                foreach (var pattern in entry.patterns)
                {
                    if (pattern == null || pattern.GetType().Name != patternTypeName) continue;
                    found = pattern;
                    break;
                }
                if (found != null) break;
            }
        }
        if (found == null) return false;

        var state = found.GetRuntimeState();
        if (state == null) return false;
        LichPatternProbe.BeginPattern(found.name);
        ChangeState(state);
        _lichBB.LastPatternTag = found.patternTag;
        Debug.Log($"[Lich] 테스트 강제 실행 — {found.name}", this);
        return true;
    }

    /// <summary>[테스트] 패턴 자동 선택이 켜져 있는가.</summary>
    public bool Editor_AutoPatternsEnabled => !_debugAutoPatternsOff;

    /// <summary>[테스트] 패턴 · 전환 · 등장 연출 중인가(강제 실행이 거절되는 동안).</summary>
    public bool Editor_IsBusy => IsInSpecialState || (_dormantState != null && _dormantState.IsActive);

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
            BossConditionKey.Lich_Phase3         => new LichPhase3Condition(),
            BossConditionKey.Lich_Phase3Pending  => new LichPhase3PendingCondition(),
            BossConditionKey.Lich_FinalMagicPending => new LichFinalMagicPendingCondition(),
            BossConditionKey.Lich_Sealed         => new LichModeCondition(nightmare: false),
            BossConditionKey.Lich_Nightmare      => new LichModeCondition(nightmare: true),
            _                                    => new AlwaysTrue(),
        };
    }

    // ── 연출: 무적 표시 · 발밑 표식 · 책 발광 · 전투 구도 ──────────

    private void TickPresentation(float dt)
    {
        bool inv = IsInvulnerableNow;
        if (inv != _lastInvulnerable)
        {
            _lastInvulnerable = inv;
            HudInvulnerableChanged?.Invoke(inv);
        }

        TickAuraMarker();
        TickBookGlow(dt);
    }

    /// <summary>날고 있는 리치의 바닥 위치 — 카메라가 내려다봐서 떠 있는 몸만으론 위치가 안 읽힌다(시나리오 §3 U7).</summary>
    private void TickAuraMarker()
    {
        if (!_bodyVisible || _movementController == null)
        {
            if (_auraMarker != null) LichVfx.Stop(ref _auraMarker, 0.2f);
            return;
        }

        Vector3 floor = transform.position;
        floor.y = _movementController.FloorY + AuraFloorLift;
        if (_auraMarker == null)
        {
            if (LichVfx.Has(LichVfxSlot.BossAura))
                _auraMarker = LichVfx.PlayLoop(LichVfxSlot.BossAura, floor, Quaternion.identity, _auraMarkerScale);
            return;
        }
        _auraMarker.transform.position = floor;
    }

    private void FindBookRenderer()
    {
        if (_bodyRenderers == null) return;
        foreach (var r in _bodyRenderers)
        {
            if (r == null) continue;
            foreach (var m in r.sharedMaterials)
            {
                if (m == null || !m.name.StartsWith(BookMaterialName, StringComparison.Ordinal)) continue;
                _bookRenderer = r;
                return;
            }
        }
    }

    private void TickBookGlow(float dt)
    {
        if (_bookPulse <= 0f || _bookRenderer == null) return;

        // 디졸브가 머티리얼을 잠시 바꿔 끼우므로 매번 책 머티리얼인지 확인하고 인스턴스를 잡는다.
        var shared = _bookRenderer.sharedMaterial;
        if (shared == null || !shared.name.StartsWith(BookMaterialName, StringComparison.Ordinal)) return;
        if (_bookMaterial != shared)
        {
            _bookMaterial = _bookRenderer.material;
            _bookBaseGlow = _bookMaterial.HasProperty(BookGlowId) ? _bookMaterial.GetFloat(BookGlowId) : -1f;
        }
        if (_bookBaseGlow < 0f)
        {
            _bookPulse = 0f;
            return;
        }

        _bookPulse = Mathf.MoveTowards(_bookPulse, 0f, dt / _bookPulseSeconds);
        float boost = 1f + (_bookPulseBoost - 1f) * _bookPulse;
        _bookMaterial.SetFloat(BookGlowId, Mathf.Max(0.5f, _bookBaseGlow) * boost);
    }

    private ArenaOrbit OrbitForPage()
    {
        int page = _lichBB?.Page ?? 1;
        if (page >= 3) return _altarOrbitClose;
        if (page == 2 && _lichBB != null && _lichBB.IsNightmare) return _altarOrbitNear;
        return _altarOrbitWide;
    }

    /// <summary>등장 연출이 카메라를 돌려주기 직전 — 전투 구도를 먼저 깔아 두면 인계 블렌드가 곧장 그 구도에 내려앉는다.</summary>
    internal void PrepareArenaCamera() => SetArenaCamera(true, 0.01f);

    private void SetArenaCamera(bool on, float blend = ArenaOrbitBlend)
    {
        var cam = GameCameraController.Instance;
        if (cam == null || _arenaCameraOn == on) return;
        _arenaCameraOn = on;
        if (on) ApplyArenaOrbit(OrbitForPage(), blend);
        else    cam.DeactivateArenaOrbit(blend);
    }

    private static void ApplyArenaOrbit(ArenaOrbit o, float blend = ArenaOrbitBlend)
        => GameCameraController.Instance?.ActivateArenaOrbit(o.top, o.middle, o.bottom, blend);

    /// <summary>
    /// 악몽기 사망 — 슬로모 · 흰 플래시 · 사망 폭발 → 낫이 부서진다 → 0.8초 뒤 코어에 떨어지는 충격(설계서 §4-7).
    /// 폭발은 작게 — 3페이지 근접 구도에서 배율 1이면 화면을 덮고, 곧 뜨는 엔딩 대사창의 일시정지에 그대로 얼어붙는다(09-19 실측).
    /// </summary>
    private void PlayDeathPresentation()
    {
        Vector3 body = transform.position + Vector3.up * 1.2f;
        LichVfx.Play(LichVfxSlot.DeathBurst, body, Quaternion.identity, DeathBurstScale);
        LichVfx.Play(LichVfxSlot.SkeletonDeath, body, Quaternion.identity, 1.2f);   // 부서진 낫 · 뼛가루
        SetScytheVisible(false);
        LichSfx.Play(LichSfxSlot.SealComplete, body);
        LichCinematics.SlowMo(0.4f, 0.8f);
        LichCinematics.Flash(Color.white, 0.25f, 0.35f);   // 사망 — 절정(0.6은 화면·HUD가 다 하얗게 지워졌다, 09-20 실측)
        LichPatternUtil.Impact(LichImpact.Transition);
        DeathImpactAsync(new Vector3(transform.position.x, FloorYForDeath(), transform.position.z), destroyCancellationToken).Forget();
    }

    /// <summary>사망 +0.8초 — 리치가 코어에 떨어지는 충격(작은 충격파 · 떨림).</summary>
    private static async UniTaskVoid DeathImpactAsync(Vector3 floor, CancellationToken ct)
    {
        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(DeathImpactDelay), cancellationToken: ct);
        }
        catch (OperationCanceledException) { return; }
        LichVfx.Play(LichVfxSlot.SlamImpact, floor, Quaternion.identity, LichPatternUtil.NovaScale(4f));
        LichSfx.Play(LichSfxSlot.SlamImpact, floor);
        ArenaTileGrid.Active?.Tremble(floor, 6f, 0.5f);
    }

    private float FloorYForDeath()
    {
        var grid = ArenaTileGrid.Active;
        return grid != null && grid.TryGetWorldCenter(out var c) ? c.y : transform.position.y;
    }

    private static Transform FindDeep(Transform root, string name)
    {
        foreach (Transform child in root)
        {
            if (child.name == name) return child;
            var found = FindDeep(child, name);
            if (found != null) return found;
        }
        return null;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 이벤트 핸들러
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>부서진 바닥이 떠오른다 — 청록 복구 빛과 소리(연출·UX 시나리오 §12-4).</summary>
    private void HandleSwingStrike() => LichSfx.Play(LichSfxSlot.ScytheSwing, transform.position);

    private void HandleTileRestoring(Vector3 cellCenter, float seconds)
    {
        LichVfx.Play(LichVfxSlot.TileRestore, cellCenter, Quaternion.identity);
        LichSfx.Play(LichSfxSlot.TileRestore, cellCenter, 0.7f);
    }
}
}
