using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;


namespace RelicFairy.Monster
{
/// <summary>
/// 몬스터 시스템의 추상 기반 클래스.
///
/// ━━━ 설계 원칙 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  • Inspector에 SO를 직접 할당하지 않는다.
///  • ConfigAddress 주소 하나로 Addressables에서 MonsterConfigSO를 로드.
///  • FSM 상태는 CreateStates()를 오버라이드해 교체 가능.
/// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///
/// 새 몬스터 추가 방법:
///   1) MonsterBase를 상속한 MonoBehaviour 클래스 작성
///   2) ConfigAddress 프로퍼티에 해당 SO의 Addressable 주소 반환
///   3) 빈 오브젝트에 해당 클래스 + Rigidbody + NavMeshAgent 추가
///   4) SO .asset 파일들 생성 후 Addressables 등록
/// </summary>
public abstract class MonsterBase : MonoBehaviour, IDamageable
{
    // ── 추상 멤버 (파생 클래스가 구현) ────────────────────
    /// <summary>Addressables에 등록된 MonsterConfigSO 주소.</summary>
    protected abstract string ConfigAddress { get; }

    /// <summary>
    /// 서버(JSON) 데이터 파일의 Addressables 주소.
    /// null 또는 빈 문자열이면 JSON 로드를 건너뛰고 SO 기본값 사용.
    /// </summary>
    protected abstract string DataAddress { get; }

    /// <summary>
    /// MONSTER_ELEMENT_STAT_DATA 테이블의 monster_id. 서버 스탯 오버라이드에 사용.
    /// 기본 구현은 ConfigAddress 의 '/' 앞 부분을 사용 (예: "WormMonster/WormMonsterConfig" → "WormMonster").
    /// 매핑이 다르면 파생 클래스에서 오버라이드.
    /// </summary>
    protected virtual string ServerStatId
    {
        get
        {
            if (string.IsNullOrEmpty(ConfigAddress)) return string.Empty;
            int idx = ConfigAddress.IndexOf('/');
            return idx > 0 ? ConfigAddress.Substring(0, idx) : ConfigAddress;
        }
    }

    /// <summary>
    /// HP 바 위치의 기준이 될 Head 본 이름.
    /// 파생 클래스에서 실제 본 이름으로 오버라이드.
    /// null이면 콜라이더 상단 기준 폴백.
    /// </summary>
    protected virtual string HeadBoneName => "Head";
    protected virtual string HPBarAnchorName => "UI_HPAnchor";

    /// <summary>Head 본 위에서 추가로 올릴 오프셋 (m).</summary>
    protected virtual float HPBarHeadOffset => 0.1f;

    /// <summary>월드 스페이스 HP 바 사용 여부. 보스처럼 HUD에서 체력을 표시하는 경우 false로 오버라이드.</summary>
    protected virtual bool UseWorldHPBar => true;

    // ── 설정 SO 캐시 (주소별 1개 인스턴스, JSON 적용 완료 상태로 보관) ──
    // 동종 몬스터가 여러 마리여도 Instantiate 1회 + JSON 적용 1회만 수행.
    // 플레이 세션마다 초기화되므로 에디터 SO 원본은 절대 오염되지 않는다.
    static readonly Dictionary<string, MonsterConfigSO> _configCache = new();

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ClearConfigCache() => _configCache.Clear();

    // ── 적 가시성 표기용 레이어 (외곽선/실루엣 Render Objects 피처의 필터 대상) ──
    // 몹 비주얼 렌더러만 Monster 레이어로 올린다. 콜라이더/루트는 그대로 → 물리·타격(IDamageable)·NavMesh 무영향.
    // -2 = 미조회, -1 = 프로젝트에 Monster 레이어 없음(스킵).
    private const string MonsterVisibilityLayerName = "Monster";
    private static int s_monsterVisibilityLayer = -2;

    // ── 타격 판정용 레이어(MonsterHit) ────────────────────
    // 몬스터 '콜라이더'를 전용 레이어로 올린다. 물리 질의가 레이어 마스크로 몬스터만 골라낼 수 있게 하려는 것.
    //
    // 왜 필요한가: 예전엔 몬스터 콜라이더가 Default라, 광역 질의가 `~0`(전 레이어)로 훑을 수밖에 없었다.
    // 그러면 바닥·벽·소품·VFX 트리거까지 전부 걸려서 NonAlloc 버퍼가 환경 콜라이더로 가득 차고
    // 몬스터가 한 마리도 안 잡히는 사고가 났다("이펙트는 나가는데 데미지 0").
    //
    // 가시성 레이어(Monster=렌더러)와 별개다 — 렌더러/콜라이더가 서로 간섭하지 않게 분리했다.
    private const string MonsterHitLayerName = "MonsterHit";
    private static int s_monsterHitLayer = -2;

    /// <summary>몬스터 콜라이더 레이어 마스크. 광역 질의가 몬스터만 고르는 데 쓴다. 레이어 미정의면 ~0(전부).</summary>
    public static int HitLayerMask
    {
        get
        {
            if (s_monsterHitLayer == -2)
                s_monsterHitLayer = LayerMask.NameToLayer(MonsterHitLayerName);
            return s_monsterHitLayer >= 0 ? (1 << s_monsterHitLayer) : ~0;
        }
    }

    // ── 내부 필드 ─────────────────────────────────────────
    protected MonsterConfigSO    _config;
    protected MonsterFSM         _fsm;
    protected MonsterContext     _ctx;
    protected MonsterRuntimeData _runtime;
    protected NavMeshAgent       _agent;
    protected Animator           _animator;
    private   MonsterHPBar       _hpBar;
    private   bool               _hpBarRequesting;
    private   bool               _worldHPBarSuppressed;

    // ── 이동/전투 캐시 ────────────────────────────────────
    private float                  _baseAgentSpeed;
    private float                  _baseDefense;
    // 챕터 난이도 배율(ChapterDataSO.difficultyScale) — HP/공격력 스케일. 보스는 1 고정. 스폰/풀재사용마다 갱신.
    private float                  _difficultyScale = 1f;
    private float                  _incomingDamageMulti = 1f;
    // 받는 피해 증폭 디버프(낙인/취약/분쇄 등) — statusId별 다중 슬롯. 시한부, 만료 시 슬롯 무시.
    // 과거 단일 float 1슬롯이라 서로 다른 출처(룬 분쇄/유물 낙인/아이템 취약)가 덮어써 1개만 유효했다.
    // 이제 활성 슬롯들의 증폭을 합연산(1 + Σamp)한다.
    private struct DmgTakenAmpSlot { public float amp; public float expire; public float total; }
    private readonly Dictionary<string, DmgTakenAmpSlot> _dmgTakenAmpSlots = new();
    private float                  _defenseMulti        = 1f;
    private float                  _attackSpeedMulti    = 1f;
    // 마지막으로 받은 피해의 종류 — 막타 히트스톱(DieState)이 "플레이어의 직접 타격이었나"를 판정하는 근거.
    // TakeDamage/TakeSynergyDamage를 우회해 HP를 깎는 보스 기믹은 직접 세팅해야 한다(영혼 기둥 등).
    protected DamageKind           _lastDamageKind      = DamageKind.Normal;
    // 상태이상 통합 수신기(ST) — CC(스턴/빙결)·Slow(서리)·DoT(점화/독)를 한 틀로. 풀-안전 plain class.
    private readonly MonsterStatusReceiver _status = new();

    // 디버프 아이콘 행 갱신 — 매 프레임 돌 필요가 없다(잔여 게이지는 0.15초 계단이면 충분히 부드럽다).
    private const float StatusUiInterval = 0.15f;
    private readonly List<BuffViewItem> _statusUiBuf = new();
    private float _statusUiTimer;
    private bool  _statusUiWasEmpty = true;
    private bool                   _statusCcActive;     // CC로 정지 중 → 해제 시 agent 1회 복원
    private bool                   _statusSlowActive;   // 슬로우로 속도 override 중 → 해제 시 base 1회 복원
    // 공격 windup(예고) 노출 — 아이템 저스트가드/공격캔슬(ColliderInstance가 소비).
    private bool                   _telegraphing;
    private bool                   _attackCanceled;
    // 풀 재사용 race 방어용 lifecycle 카운터 — OnEnable마다 증가하여 외부 콜백(dissolve onComplete 등)이
    // 자기 세대 값과 비교해 이전 인스턴스에 대한 호출을 무시할 수 있도록 한다.
    // 기존 _worldHPBarSuppressed/_hpBarRequesting과 계층이 달라(외부 vs 내부 UI) 겹치지 않음.
    private int                    _generationId;
    // OnDisable에서 취소되어 DissolveEffect의 복원 콜백을 차단한다.
    private CancellationTokenSource _activationCts;

    /// <summary>외부 비동기 콜백이 풀 재사용 이전 세대에 대한 것인지 검증할 때 비교하는 값. OnEnable마다 +1.</summary>
    public int GenerationId => _generationId;

    /// <summary>이 활성화 수명 동안 유효한 토큰. OnDisable에서 취소 — 풀 반환 시 DissolveEffect 복원 차단에 사용.</summary>
    public CancellationToken ActivationToken => _activationCts?.Token ?? CancellationToken.None;

    /// <summary>공격 상태/어빌리티가 쿨다운 계산 시 곱할 배율. 0 = 공격 불가.</summary>
    public float AttackSpeedMultiplier => _attackSpeedMulti;

    /// <summary>유효 공격력.</summary>
    public float EffectiveAttackPower => _config != null ? _config.stat.attackPower : 0f;

    /// <summary>유효 최대 HP. 챕터 난이도 배율(difficultyScale) 반영.</summary>
    public int EffectiveMaxHp => _config != null ? Mathf.RoundToInt(_config.stat.maxHp * _difficultyScale) : 0;

    /// <summary>몬스터 등급(Common/Rare/Elite/Boss). 대상 수가 아니라 '상대의 격'으로 보상을 정할 때 쓴다.</summary>
    public MonsterGrade Grade => _config != null ? _config.grade : MonsterGrade.Common;

    /// <summary>유효 공격 속도.</summary>
    public float EffectiveAttackRate => _config != null ? _config.stat.attackRate : 0f;

    // ── HP 변경 이벤트 (보스 UI 등 외부에서 구독) ─────────
    /// <summary>HP가 변경될 때마다 발행. (currentHp, maxHp)</summary>
    public event System.Action<int, int> OnHPChanged;

    /// <summary>몬스터 사망 시 1회 발행. RoomClearController 등 외부 수명주기가 구독.</summary>
    public event System.Action<MonsterBase> OnDied;

    /// <summary>보스 등장 연출 완료 후 1회 발행. HUD가 보스 패널을 이 시점에 표시.</summary>
    public event System.Action OnBossCombatReady;
    protected void RaiseBossCombatReady() => OnBossCombatReady?.Invoke();

    /// <summary>true면 등장 연출이 끝날 때까지 HUD 보스 패널을 억제한다.</summary>
    public virtual bool HasEntranceAnimation => false;

    /// <summary>DieState.Enter에서 호출. 외부 구독자가 사망을 감지할 수 있도록 이벤트 래핑.</summary>
    public void RaiseDied()
    {
        OnDied?.Invoke(this);
        QuestEvents.ReportKill(_config?.monsterName ?? "Unknown");

        // 기억의 제단 할인 조건용 집계. 보스는 GameRunSession.NotifyBossRoomCleared가 세므로
        // 여기서는 정예만 — 둘 다 세면 보스 처치가 이중 계상된다.
        var run = GameRunBootstrapper.Instance?.Run;
        run?.ReportKill();
        if (Grade == MonsterGrade.Elite) run?.ReportEliteKill();
    }

    /// <summary>보스 HP 바 초기화용. Config 로드 후 유효.</summary>
    public int CurrentHp => _runtime != null ? _runtime.CurrentHp : 0;
    /// <summary>사망 처리됨(런타임). 서약 등 외부 타겟팅에서 시체 제외용.</summary>
    public bool IsDead => _runtime != null && _runtime.IsDead;
    public int    BossMaxHp => EffectiveMaxHp;

    /// <summary>파생 클래스가 HP바 부가 표기에 접근하기 위한 읽기 전용 핸들(허수아비 DPS 분석 등). 아직 없으면 null.</summary>
    protected MonsterHPBar HpBar => _hpBar;
    public string BossName  => _config != null ? _config.monsterName : string.Empty;

    // ── 특수 상태 인스턴스 (SO 데이터로 자동 생성) ────────
    private readonly List<SpecialStateBase> _specialStates = new();

    // ── HP 트리거 발동 추적 (oneShot 트리거 중복 방지) ────
    private readonly HashSet<int> _firedHpTriggers = new();

    /// <summary>인덱스로 특수 상태 인스턴스를 가져온다. StateOverrideSO 내부 상태에서 접근 가능.</summary>
    public SpecialStateBase GetSpecialState(int index)
        => index < _specialStates.Count ? _specialStates[index] : null;

    // ── OnEnable 콜백 (StateOverrideSO 가 쿨다운 리셋 등을 등록) ─
    private readonly List<System.Action> _onEnabledCallbacks = new();

    // ── GetHit 억제 플래그 (파생 클래스에서 OnDamageTaken 안에 true 설정 → GetHitState 전환 스킵) ─
    protected bool _suppressGetHitThisHit;

    /// <summary>풀 재사용(OnEnable) 시 호출할 콜백을 등록한다. StateOverrideSO.RegisterOverrides() 에서 사용.</summary>
    public void RegisterOnEnabledCallback(System.Action callback) => _onEnabledCallbacks.Add(callback);

    // ── 캐싱 ──────────────────────────────────────────────
    private Rigidbody  _rb;
    private Collider[] _cachedColliders;

    // Inspector 디버그용
    [SerializeField] private string _debugState;
    private float     _diagTimer;
    private Transform _hpBarAnchor;
    private Transform _headBone;   // HeadBoneName으로 탐색한 본

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 초기화
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>전 몬스터 플레이어 탐색 범위 배율. 1이면 미적용. detectionRange/chaseGiveUpRange에 곱해진다.
    /// 차트 기본값(detectionRange 5m)에 1.5배는 방 크기에 비해 너무 좁아, 플레이어가 안 오는 몹을
    /// 일일이 찾아다니는 피로가 컸다 → 3배로 올려 방에 들어서면 대부분 스스로 붙게 한다.</summary>
    private const float DetectionRangeMultiplier = 3.0f;

    private async void Awake()
    {
        await InitAsync();
    }

    protected virtual async UniTask InitAsync()
    {
        // 0. 적 가시성 표기(외곽선) 레이어 부여는 OnEnable의 RevealVisibilityMarkupAsync로 이관 —
        //    디졸브 등장이 끝난 뒤 켜지도록 지연(미완성 본체에 외곽선이 겹쳐 보이는 것 방지).

        // 1. MonsterConfigSO 로드 (주소별 캐시 — 동종 몬스터는 Instantiate·JSON 적용을 1회만 수행)
        if (!_configCache.TryGetValue(ConfigAddress, out _config))
        {
            var loaded = await Managers.AddressableManager.TryLoadAssetAsync<MonsterConfigSO>(ConfigAddress);

            // await 복귀 시점에 오브젝트가 파괴되어 있을 수 있다 (씬 전환 타이밍 등).
            if (this == null) return;

            if (loaded == null)
            {
                Debug.LogError($"[MonsterBase] ConfigSO 로드 실패: {ConfigAddress}", this);
                return;
            }

            // await 중 동종 몬스터가 먼저 캐시를 채웠을 수 있으므로 재확인
            if (!_configCache.TryGetValue(ConfigAddress, out _config))
            {
                // 원본 SO 를 보호하기 위해 복사본 생성 후 JSON 적용
                _config = UnityEngine.Object.Instantiate(loaded);
                _configCache[ConfigAddress] = _config;

                // 2. JSON 데이터 로드 후 복사본에 덮어쓰기 (캐시 등록 전에 1회만 실행)
                await LoadAndApplyJsonDataAsync();

                if (this == null) return;

                // 2-1. 서버 CDN으로 수치 오버라이드 (Addressable JSON 위에 덮어쓰기)
                // ApplyServerStatOverride(); // 의도적 동결 — 되살리려면 밸런스 재조정이 선행돼야 한다.
                //   MONSTER_ELEMENT_STAT_DATA는 정상 데이터가 들어 있지만 현재 SO 수치와 축이 다르다.
                //   실측(2026-08-08): Slime HP 10→40 / ATK 30→5, Orc ATK 54→12, DemonKing HP 300→900.
                //   지금 켜면 전 몬스터 HP·공격력이 배수로 뒤집힌다. 차트를 정본으로 삼기로 결정하는
                //   시점에 CSV 쪽을 현재 SO 기준으로 재작성한 뒤 함께 켤 것.

                // 2-2. 전 몬스터 플레이어 탐색 범위 상향. 캐시 채움 시(타입당 1회)만 적용 → 인스턴스 누적 없음.
                ApplyDetectionRangeBoost(_config);
            }
        }

        // 2-2. base 스탯 캐싱
        _baseDefense = _config.stat.defense;

        // 3. NavMeshAgent 설정
        _agent = GetComponent<NavMeshAgent>();
        if (_agent == null)
        {
            Debug.LogWarning("[MonsterBase] NavMeshAgent가 없어 자동 추가합니다.", this);
            _agent = gameObject.AddComponent<NavMeshAgent>();
        }
        _agent.speed            = _config.stat.moveSpeed;
        _agent.stoppingDistance = _config.stat.attackRange;
        _baseAgentSpeed         = _config.stat.moveSpeed;

        // 3-1. 비-보스 몬스터 시각 크기 정규화 (핵앤슬래시 가독성).
        //      전역 상수 배율(구 NonBossVisualScale 0.8) 대신 SO의 visualHeightMeters를 목표로
        //      "실측 대비 배율"을 구해 적용한다 → 모델 원본 크기가 제각각이어도 실루엣이 밴드에 맞는다.
        //      콜라이더/발밑그림자/HP바/외곽선은 lossyScale로 자동 추종되며,
        //      NavMeshAgent radius/height만 월드값이라 함께 곱해 정합을 맞춘다.
        //      InitAsync는 인스턴스당 1회만 실행 → 풀 재사용(OnEnable) 시 중복 적용 없음.
        if (_config.grade != MonsterGrade.Boss)
            ApplyVisualHeightScale();

        // 3-2. 피격 시각 피드백(빨강 플래시) 자동 부착 — 프리팹에 명시 부착 안 된 몬스터도 적용.
        //      VictimHitFeedback은 프로필 미배정 시 공유 기본 프로필(빨강 더블블링크)을 사용한다.
        //      NavMeshAgent 자동추가와 동일 패턴, 인스턴스당 1회.
        if (!TryGetComponent<VictimHitFeedback>(out _))
            gameObject.AddComponent<VictimHitFeedback>();

        // Agent 를 가장 가까운 NavMesh 로 스냅. baseOffset=0 + voxel 오차로
        // isOnNavMesh=false 로 시작하는 경우를 방지한다. SamplePosition 기반 재시도.
        TrySnapAgentToNavMesh();

        // NavMeshAgent가 위치를 제어하므로 Rigidbody는 kinematic 유지
        _rb = GetComponent<Rigidbody>();
        if (_rb != null) _rb.isKinematic = true;

        // 4. Animator 설정 (Addressables에서 AnimatorController 로드)
        _animator = GetComponentInChildren<Animator>();
        if (_animator == null)
            Debug.LogWarning($"[MonsterBase] {name}: Animator 컴포넌트를 찾을 수 없습니다. 프리팹 구조를 확인하세요.", this);
        await LoadAnimatorControllerAsync();

        // Collider 배열 캐싱 (OnEnable에서 GetComponentsInChildren 반복 방지)
        _cachedColliders = GetComponentsInChildren<Collider>(true);

        // 4-0. 발밑 가짜 그림자 — 등급별 진하기/링. 콜라이더 반경으로 발자국 크기 산출.
        EnsureGroundShadow();

        // 4-1. Head 본 탐색
        if (!string.IsNullOrEmpty(HPBarAnchorName))
            _hpBarAnchor = FindBoneRecursive(transform, HPBarAnchorName);

        if (!string.IsNullOrEmpty(HeadBoneName))
            _headBone = FindBoneRecursive(transform, HeadBoneName);

        // 5. 런타임 데이터 초기화 — 챕터 난이도 배율 반영(HP scale + 공격력 = AttackMultiplier).
        ResolveDifficultyScale();
        _runtime = new MonsterRuntimeData
        {
            CurrentHp        = EffectiveMaxHp,
            AttackMultiplier = _difficultyScale,
            SpawnPosition    = transform.position,
            PatrolDirection  = 1,
        };

        // 6. 컨텍스트 조립
        _ctx = new MonsterContext
        {
            Monster   = this,
            Agent     = _agent,
            Animator  = _animator,
            Config    = _config,
            Runtime   = _runtime,
        };

        // 7. 플레이어 타깃 등록
        if (Managers.Player.PlayerTransform != null)
            SetPlayerTarget(Managers.Player.PlayerTransform);
        else
            Managers.Player.OnPlayerSpawned += OnPlayerSpawned;

        // 8. FSM 초기화
        _fsm = new MonsterFSM(_ctx);
        RegisterStates();
        _fsm.ChangeState<PatrolState>();

        // 9. HP 바 요청 (보스 등 UseWorldHPBar == false면 건너뜀)
        if (UseWorldHPBar && gameObject.activeInHierarchy && !_worldHPBarSuppressed)
        {
            _hpBar = await Managers.MonsterHPBar.RequestHPBarAsync(this, _runtime.CurrentHp, EffectiveMaxHp, _hpBarAnchor != null ? _hpBarAnchor : _headBone, HPBarHeadOffset);
            _hpBar?.SetMonsterInfo(_config.monsterName);
        }

        OnInitialized();
    }

    /// <summary>초기화 완료 후 파생 클래스에서 추가 처리가 필요한 경우 오버라이드.</summary>
    protected virtual void OnInitialized() { }

    /// <summary>전 몬스터 탐색 범위 일괄 상향. 캐시된 config 복사본에 1회만 곱한다(원본 SO 불변, 인스턴스 누적 없음).
    /// detectionRange와 chaseGiveUpRange를 같은 배율로 곱해 "감지 &lt; 포기" 순서를 유지한다.
    /// 보스 등 별도 탐색 필드(DragonBoss._detectionRange)를 쓰는 커스텀 로직은 영향받지 않는다.</summary>
    private static void ApplyDetectionRangeBoost(MonsterConfigSO config)
    {
        if (DetectionRangeMultiplier == 1f || config == null || config.detection == null) return;
        config.detection.detectionRange   *= DetectionRangeMultiplier;
        config.detection.chaseGiveUpRange *= DetectionRangeMultiplier;
    }

    /// <summary>SO의 visualHeightMeters(등급 배율 포함)를 목표로 루트 스케일을 보정한다.
    /// 값이 0(미지정)이거나 렌더러가 없으면 프리팹 원본 크기를 그대로 둔다.
    /// InitAsync에서 인스턴스당 1회만 호출된다.</summary>
    private void ApplyVisualHeightScale()
    {
        float target = _config.ResolveVisualHeight();
        if (target <= 0f) return;

        float measured = MeasureModelHeight();
        if (measured <= 0.001f) return;

        float k = target / measured;
        if (Mathf.Approximately(k, 1f)) return;

        transform.localScale *= k;
        _agent.radius        *= k;
        _agent.height        *= k;
    }

    /// <summary>현재 월드 스케일이 반영된 모델 실루엣 높이(m). 파티클/트레일은 크기가 들쭉날쭉해 제외한다.
    /// SkinnedMeshRenderer.bounds는 메시에 구워진 로컬 바운즈 기반이라 애니메이션 포즈와 무관하게 안정적이다.</summary>
    private float MeasureModelHeight()
    {
        var renderers = GetComponentsInChildren<Renderer>(true);
        bool  any = false;
        float min = 0f, max = 0f;

        foreach (var r in renderers)
        {
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;

            var b = r.bounds;
            if (!any) { min = b.min.y; max = b.max.y; any = true; }
            else
            {
                if (b.min.y < min) min = b.min.y;
                if (b.max.y > max) max = b.max.y;
            }
        }

        return any ? max - min : 0f;
    }

    /// <summary>현재 런/챕터의 난이도 배율을 _difficultyScale에 반영. 보스는 1 고정(자체 밸런스 유지).
    /// 인스턴스별 적용이라 공유 config를 오염시키지 않으며, 멀티챕터 런에서 OnEnable마다 재호출된다.</summary>
    private void ResolveDifficultyScale()
    {
        if (_config != null && _config.grade == MonsterGrade.Boss) { _difficultyScale = 1f; return; }
        var run = AppBootstrapper.Instance?.CurrentRun;
        _difficultyScale = run != null ? run.CurrentDifficultyScale : 1f;
    }

    /// <summary>
    /// 공용 상태를 FSM에 등록한다.
    /// </summary>
    protected virtual void RegisterStates()
    {
        // ── 1단계: 기본 공용 상태 등록 ────────────────────────────
        _fsm.RegisterAs<PatrolState>     (new PatrolState());
        _fsm.RegisterAs<ChaseState>      (new ChaseState());
        _fsm.RegisterAs<AttackReadyState>(new AttackReadyState());
        _fsm.RegisterAs<AttackState>     (new AttackState());
        _fsm.RegisterAs<GetHitState>     (new GetHitState());
        _fsm.RegisterAs<DieState>        (new DieState());

        // ── 특수 상태: 엔트리의 state SO → 런타임 인스턴스 생성 ───
        _specialStates.Clear();
        if (_config.specialStates != null)
            foreach (var entry in _config.specialStates)
                _specialStates.Add(entry?.state?.CreateState());

        // ── 2단계: 상태 오버라이드 (특수 상태 생성 이후에 실행) ───
        // 각 SO가 RegisterAs<T>()로 필요한 공용 상태만 덮어씌운다.
        if (_config.stateOverrides != null)
            foreach (var ovr in _config.stateOverrides)
                ovr?.RegisterOverrides(_fsm, this);

    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 매 프레임
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected virtual void Update()
    {
        if (_config == null || _runtime == null || _runtime.IsDead) return;

        // PlayerManager에서 최신 타깃을 O(1)로 갱신 (FindObjectsOfType 제거)
        var latestTarget = Managers.Player.PlayerTransform;
        if (latestTarget != _runtime.PlayerTarget)
            SetPlayerTarget(latestTarget);

        // 거리를 1회만 계산해 런타임에 저장 → 각 상태에서 재계산 불필요
        _runtime.DistToPlayer = _runtime.PlayerTarget != null
            ? Vector3.Distance(transform.position, _runtime.PlayerTarget.position)
            : float.MaxValue;

        // 상태이상 수신기 틱(CC/Slow/DoT/공격디버프). DoT가 이 프레임에 처치할 수 있으므로 직후 사망 가드.
        _status.Tick(Time.deltaTime, this);
        if (_runtime.IsDead) return;

        RefreshStatusUi();   // HP바 아래 디버프 아이콘 행(저주기 갱신)
        _attackSpeedMulti = _status.AttackSpeedMultiplier;   // 풀 간파/지배 적 공격 디버프

        // CC(스턴/빙결): 이동·FSM 정지. 해제 시 1회 복원.
        if (_status.IsCcActive)
        {
            _statusCcActive = true;
            if (_agent != null && _agent.isOnNavMesh && _agent.isActiveAndEnabled && !_agent.isStopped)
                _agent.isStopped = true;
            return;
        }
        if (_statusCcActive)
        {
            _statusCcActive = false;
            if (!LocksNavPosition && _agent != null && _agent.isOnNavMesh && _agent.isActiveAndEnabled)
                _agent.isStopped = false;
        }

        _fsm?.Update();

        // Slow(서리): FSM이 설정한 agent.speed 위에 덮어쓴다. 해제 시 base 1회 복원.
        float moveMult = _status.MoveSpeedMultiplier;
        if (moveMult < 0.999f)
        {
            _statusSlowActive = true;
            if (_agent != null) _agent.speed = _baseAgentSpeed * moveMult;
        }
        else if (_statusSlowActive)
        {
            _statusSlowActive = false;
            if (_agent != null) _agent.speed = _baseAgentSpeed;
        }

#if UNITY_EDITOR
        _debugState = _fsm?.CurrentType?.Name;
#endif
    }

    private void LateUpdate()
    {
        if (LocksNavPosition) return;
        if (_runtime == null || _runtime.IsDead || _runtime.PlayerTarget == null) return;
        if (_agent == null || !_agent.isOnNavMesh || !_agent.isActiveAndEnabled || _agent.isStopped) return;

        const float playerRadius = 0.35f;
        Vector3 toMonster = transform.position - _runtime.PlayerTarget.position;
        toMonster.y = 0f;
        float minSep = _agent.radius + playerRadius;

        // sqrMagnitude로 먼저 체크 — 겹치지 않는 대부분의 경우 sqrt 생략
        float sqrDist = toMonster.sqrMagnitude;
        if (sqrDist >= minSep * minSep || sqrDist < 0.001f * 0.001f) return;

        float dist = Mathf.Sqrt(sqrDist);
        _agent.nextPosition = transform.position + (toMonster / dist) * (minSep - dist);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 공개 API (상태 클래스 → 몬스터 베이스 콜백)
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>공용 상태 전환 — 타입으로 조회.</summary>
    public void ChangeState<T>() where T : IMonsterState => _fsm?.ChangeState<T>();

    /// <summary>특수 상태 전환 — 인스턴스 직접 전달.</summary>
    public void ChangeState(IMonsterState state) => _fsm?.ChangeState(state);

    /// <summary>현재 FullLockState 등 특수 상태(Constraints != None)가 실행 중이면 true.</summary>
    public bool IsInSpecialState
        => _fsm != null && _fsm.CurrentConstraints != SpecialStateConstraint.None;

    // ── 조건 훅 (공용 상태가 위임, 파생 클래스에서 오버라이드 가능) ────────

    /// <summary>true면 근접 공격(ColliderInstance) 판정을 무시한다. 원거리 투사체(BasicArrow 등)는 영향 없음.</summary>
    public virtual bool IsMeleeImmuneNow => false;

    /// <summary>true면 NavMeshAgent 이동을 영구 잠금한다. CC 해제·LateUpdate push 모두 차단.</summary>
    protected virtual bool LocksNavPosition => false;

    /// <summary>Patrol → Chase 전환 조건. 오버라이드로 몬스터별 감지 로직 교체.</summary>
    public virtual bool ShouldStartChase(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return false;
        if (IsPlayerDead()) return false;

        // If we cannot move on NavMesh, allow engage only when target is already in melee stop range.
        if (ctx.Agent == null || !ctx.Agent.isOnNavMesh)
            return ctx.Runtime.DistToPlayer <= GetCombatStopDistance(ctx);

        return ctx.Runtime.DistToPlayer <= ctx.Detection.detectionRange;
    }

    /// <summary>Chase → Patrol 전환 조건. 오버라이드로 포기 거리·조건 교체.</summary>
    public virtual bool ShouldGiveUpChase(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null || IsPlayerDead()) return true;

        if (ctx.Agent == null || !ctx.Agent.isOnNavMesh)
            return ctx.Runtime.DistToPlayer > GetCombatStopDistance(ctx);

        return ctx.Runtime.DistToPlayer > ctx.Detection.chaseGiveUpRange;
    }

    /// <summary>Chase → AttackReady 전환 조건. 오버라이드로 공격 진입 거리 교체.</summary>
    public virtual bool ShouldEnterAttackReady(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return false;
        return ctx.Runtime.DistToPlayer <= GetCombatStopDistance(ctx);
    }

    public virtual float GetCombatStopDistance(MonsterContext ctx)
    {
        if (ctx?.Agent != null && ctx.Agent.stoppingDistance > 0.05f)
            return ctx.Agent.stoppingDistance;

        return ctx.Stat.attackRange;
    }

    public virtual float GetCombatHitDistance(MonsterContext ctx)
        => Mathf.Max(GetCombatStopDistance(ctx), ctx.Stat.attackRadius);

    /// <summary>
    /// 데미지를 받아 HP가 감소한 직후, GetHitState 전환 전에 호출된다.
    /// 파생 클래스에서 오버라이드해 HP 임계값 기반 특수 상태 진입을 구현한다.
    /// 이 메서드 안에서 ChangeState(specialState) 를 호출하면
    /// 이후 GetHitState 전환이 자동으로 스킵된다.
    /// </summary>
    /// <summary>HP가 0 이하가 됐을 때 호출. 기본 동작은 DieState 전환. 보스에서 퇴각 등으로 오버라이드 가능.</summary>
    protected virtual void OnFatalDamage() => ChangeState<DieState>();

    /// <summary>파생 클래스에서 _runtime.CurrentHp를 직접 수정한 후 HP바·이벤트를 갱신한다.</summary>
    protected void NotifyHpChanged()
    {
        if (_runtime == null) return;
        int effMax = EffectiveMaxHp;
        _hpBar?.UpdateHP(_runtime.CurrentHp, effMax);
        OnHPChanged?.Invoke(_runtime.CurrentHp, effMax);
    }

    protected virtual void OnDamageTaken()
    {
        if (_config.specialStates == null || _config.specialStates.Count == 0) return;

        for (int i = 0; i < _config.specialStates.Count; i++)
        {
            var entry = _config.specialStates[i];
            if (entry == null || entry.state == null) continue;

            // conditions 가 비어있으면 코드 직접 발동 전용 — OnDamageTaken 에서 스킵
            if (entry.conditions == null || entry.conditions.Count == 0) continue;

            if (entry.oneShot && _firedHpTriggers.Contains(i)) continue;

            bool allMet = true;
            foreach (var cond in entry.conditions)
            {
                if (cond != null && !cond.Evaluate(_ctx)) { allMet = false; break; }
            }
            if (!allMet) continue;

            var state = GetSpecialState(i);
            if (state == null) continue;

            if (entry.oneShot) _firedHpTriggers.Add(i);
            ChangeState(state);
            return;
        }
    }

    /// <summary>
    /// 공격 히트 판정: 반경 내 플레이어에게 데미지 + 넉백 적용.
    /// AttackState 또는 MonsterAnimEventReceiver에서 호출.
    /// </summary>
    public void DealDamageToPlayer()
    {
        if (_runtime?.PlayerTarget == null || _config == null) return;
        if (_runtime.AttackHitDealt) return;

        int   damage         = (int)(_config.stat.attackPower * _runtime.AttackMultiplier);
        float knockbackForce = _config.stat.knockbackForce;

        // attackShape SO가 있으면 위임, 없으면 기본 구체 판정
        if (_config.stat.attackShape != null)
        {
            _config.stat.attackShape.Execute(_ctx, damage, knockbackForce);
            return;
        }

        float dist = Vector3.Distance(transform.position, _runtime.PlayerTarget.position);
        if (dist > GetCombatHitDistance(_ctx)) return;

        var player = _runtime.PlayerTarget.GetComponent<PlayerController>();
        if (player == null) return;

        SpawnHitVfx();
        player.TakeDamage(damage, gameObject);

        Vector3 dir = (_runtime.PlayerTarget.position - transform.position).normalized;
        dir.y = 0.3f;
        player.ApplyKnockback(dir.normalized * knockbackForce);
    }

    private void SpawnHitVfx()
    {
        var prefab = _config?.stat?.hitVfxPrefab;
        if (prefab == null) return;

        var pooler = Managers.ObjectPooler;
        if (pooler == null) return;

        Vector3 pos = transform.position + _config.stat.hitVfxOffset;
        var go = pooler.SpawnFromPrefab(prefab, ObjectPoolerManager.PoolType.Effect, pos, Quaternion.identity);
        if (go == null) return;

        go.transform.localScale = Vector3.one * Mathf.Max(0.001f, _config.stat.hitVfxScale);

        var systems = go.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < systems.Length; i++)
        {
            var main = systems[i].main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        }

        var ps = go.GetComponent<ParticleSystem>() ?? go.GetComponentInChildren<ParticleSystem>();
        float lifetime = ps != null ? ps.main.duration + ps.main.startLifetimeMultiplier + 0.3f : 3f;

        // Destroy 대신 풀 반환(수명 후 자동 Despawn). 스케일은 매 스폰 절대 설정 → 재사용 정합.
        var vfx = go.GetComponent<PooledOneShotVfx>() ?? go.AddComponent<PooledOneShotVfx>();
        vfx.Play(lifetime);
    }

    /// <summary>애니메이션 이벤트에서 호출 (MonsterAnimEventReceiver 경유).</summary>
    public void OnAnimAttackHit()
    {
        if (_runtime == null) return;

        // 방어·특수 상태(MovementLocked 이상) 진입 후 CrossFade 블렌딩 중에
        // 이전 Attack 애니메이션의 AnimEvent가 실행되는 것을 차단한다.
        var constraints = _fsm?.CurrentConstraints ?? SpecialStateConstraint.None;
        if (constraints != SpecialStateConstraint.None) return;

        int maxHits = _config?.combat.maxHitsPerAttack ?? 0;
        if (maxHits > 0 && _runtime.AttackHitCount >= maxHits) return;

        _runtime.AttackHitCount++;

        // 발사마다 현재 플레이어 방향으로 재조준
        if (_runtime.PlayerTarget != null)
        {
            Vector3 dir = _runtime.PlayerTarget.position - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(dir);
        }

        DealDamageToPlayer();
    }

    /// <summary>플레이어가 사망했는지 확인.</summary>
    public bool IsPlayerDead()
    {
        var pc = _runtime?.CachedPlayer;
        if (pc == null) return true;
        if (pc.RuntimeStats == null) return false;
        return pc.RuntimeStats.Hp <= 0;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // IDamageable — 플레이어 공격에 맞을 때 호출됨
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>피해 출처가 플레이어(또는 그 자식 무기/투사체)인지. 서약 통보를 플레이어발 피해로 한정.</summary>
    private static bool IsPlayerInstigator(GameObject g)
        => g != null && g.GetComponentInParent<PlayerController>() != null;

    /// <summary>
    /// 받는 피해 증폭 디버프 부여(심판 낙인 등 적 낙인). ampPercent=0.2 → 받는 피해 ×1.2, duration초 후 자동 해제.
    /// statusId별로 독립 슬롯에 저장돼 서로 다른 출처(룬 분쇄 shatter / 유물·아이템 낙인 brand / 취약 vulnerable)가
    /// 덮어쓰지 않고 합연산(1 + Σamp)으로 누적된다. 같은 statusId 재부여는 해당 슬롯만 갱신.
    /// </summary>
    public void ApplyDamageTakenAmp(float ampPercent, float duration, string statusId = "brand")
    {
        _dmgTakenAmpSlots[statusId] = new DmgTakenAmpSlot
        {
            amp    = Mathf.Max(0f, ampPercent),
            expire = Time.time + duration,
            total  = Mathf.Max(0.01f, duration),   // 게이지 비율용
        };
        GuidelineVisual.StatusApplied(transform, statusId, duration);   // [가이드라인 비주얼]
    }

    /// <summary>
    /// 디버프 아이콘 행 갱신. 매 프레임 딕셔너리 4개를 훑을 필요는 없으므로 저주기로 돈다.
    /// 상태가 하나도 없으면 첫 호출에서 행을 숨긴 뒤 더 이상 아무 일도 하지 않는다(정상 상태의 비용 ≈ 0).
    /// </summary>
    private void RefreshStatusUi()
    {
        if (_hpBar == null) return;

        _statusUiTimer -= Time.deltaTime;
        if (_statusUiTimer > 0f) return;
        _statusUiTimer = StatusUiInterval;

        _statusUiBuf.Clear();
        CollectStatuses(_statusUiBuf);

        // 상태가 계속 없으면 SetStatuses(빈 리스트)조차 부를 필요가 없다.
        if (_statusUiBuf.Count == 0 && _statusUiWasEmpty) return;
        _statusUiWasEmpty = _statusUiBuf.Count == 0;

        _hpBar.SetStatuses(_statusUiBuf);
    }

    /// <summary>
    /// 지금 이 몬스터에게 걸려 있는 <b>모든</b> 상태이상을 UI 모델로 뽑는다(디버프 표시 단일 진입점).
    ///
    /// 상태가 세 곳에 흩어져 있다 — 상태수신기(CC/슬로우/DoT/공속), 받피증폭 슬롯, 그리고
    /// 독립 MonoBehaviour인 화상 핸들러. UI가 세 곳을 각각 알 필요가 없도록 여기서 합친다.
    /// </summary>
    public void CollectStatuses(List<BuffViewItem> into)
    {
        if (into == null) return;

        _status.CollectStatuses(into);

        // 받피증폭(낙인·분쇄·취약·저주) — 상태수신기를 안 거치는 별도 슬롯
        float now = Time.time;
        foreach (var kv in _dmgTakenAmpSlots)
        {
            float left = kv.Value.expire - now;
            if (left <= 0f) continue;
            into.Add(MonsterStatusReceiver.MakeItem(kv.Key, 1, left / kv.Value.total, left));
        }

        // 화상 — MonsterStatusReceiver를 타지 않는 독립 계통(가웨인 태양)
        if (TryGetComponent<MonsterBurnHandler>(out var burn) && burn.Remaining > 0f)
            into.Add(MonsterStatusReceiver.MakeItem("burn", 1, burn.Remaining01, burn.Remaining));
    }

    /// <summary>현재 활성 받피증폭 디버프 합산 배율(1 + 만료되지 않은 슬롯들의 amp 합). 슬롯 없으면 1.</summary>
    private float CurrentDamageTakenMult()
    {
        if (_dmgTakenAmpSlots.Count == 0) return 1f;
        float now = Time.time;
        float sum = 0f;
        foreach (var kv in _dmgTakenAmpSlots)   // Dictionary struct enumerator — alloc 없음
            if (now < kv.Value.expire) sum += kv.Value.amp;
        return 1f + sum;
    }

    /// <summary>
    /// 시너지(룬) 즉발 피해(DM) — 방어 경감을 defenseIgnore(0~1)만큼 우회한다(기본 1=완전 무시).
    /// %기반 즉발/DoT 수치가 방어의 max(1,...) 감산에 무력화되지 않게 하는 경로.
    /// GetHit 경직·넉백 없음(DoT/체인 연타로 인한 스턴락 방지). 처치 판정은 일반 피해와 동일.
    /// </summary>
    /// <param name="kind">데미지 숫자 색 구분용. DoT 틱은 Dot을 넘긴다(기본은 즉발 시너지).</param>
    public void TakeSynergyDamage(float amount, GameObject instigator, float defenseIgnore = 1f, bool isCrit = false,
                                  DamageKind kind = DamageKind.Synergy, RuneElement? element = null)
    {
        if (_runtime == null || _runtime.IsDead || amount <= 0f) return;

        var constraints = _fsm?.CurrentConstraints ?? SpecialStateConstraint.None;
        if ((constraints & SpecialStateConstraint.Invincible) != 0) return;

        _lastDamageKind = kind;

        float defense = _baseDefense * _defenseMulti * Mathf.Clamp01(1f - defenseIgnore);
        float actual  = Mathf.Max(1f, (amount - defense) * _runtime.DamageMultiplier * _incomingDamageMulti * CurrentDamageTakenMult());
        _runtime.CurrentHp -= (int)actual;
        if (HpFloorMin1 && _runtime.CurrentHp < 1) _runtime.CurrentHp = 1; // [인트로] 이길 수 없는 연출: 처치 방지

        DamagePopupSpawner.Spawn(transform.position + Vector3.up * 1.2f, actual, isCrit, GetInstanceID(), kind, element);

        // [가이드라인 비주얼] 시너지 '즉발' 피해만 플래시. DoT는 제외 —
        // 화상·독은 0.5~1초마다 틱이라 구체가 지속 내내 깜빡여 화면을 어지럽힌다.
        // DoT는 데미지 숫자(속성색) + 속성 몸 이펙트로 이미 충분히 읽힌다.
        if (kind != DamageKind.Dot)
            GuidelineVisual.SynergyDamage(transform.position + Vector3.up * 1.2f, isCrit);

        int effMax = EffectiveMaxHp;
        _hpBar?.UpdateHP(_runtime.CurrentHp, effMax);
        OnHPChanged?.Invoke(_runtime.CurrentHp, effMax);

        if (_runtime.CurrentHp <= 0)
        {
            _runtime.CurrentHp = 0;
            _runtime.IsDead    = true;
            if (IsPlayerInstigator(instigator))
            {
                GameRunBootstrapper.Instance?.Run?.CovenantHandler?.OnKill(gameObject);
                GameRunBootstrapper.Instance?.Run?.EffectManager?.OnKill(gameObject);   // 아이템 처치 효과
                instigator.GetComponentInParent<PlayerController>()
                    ?.FirePassive(PassiveTrigger.OnKill, new PassiveContext { target = gameObject });  // [유물] 처치 패시브
            }
            OnFatalDamage();
        }
    }

    /// <summary>마지막으로 받은 피해의 종류. 치명타(사망) 직후엔 곧 '무엇이 죽였는가'가 된다 —
    /// DieState가 막타 히트스톱을 걸지 말지 판정하는 데 쓴다.</summary>
    public DamageKind LastDamageKind => _lastDamageKind;

    /// <summary>상태이상 통합 수신기(ST). 룬·아이템 효과가 CC/Slow/DoT를 부여하는 진입점.</summary>
    public MonsterStatusReceiver Status { get { _status.AttachOwner(this); return _status; } }

    // ── 공격 windup(예고) 노출 — 아이템 저스트가드/공격캔슬(ColliderInstance가 소비) ──
    /// <summary>현재 근접 공격 windup(예고) 중인가. 저스트가드(+피해)·섬광(캔슬) 판정용.</summary>
    public bool IsTelegraphingAttack => _telegraphing;

    /// <summary>AttackState가 데미지 적용 전 windup 진입 시 호출.</summary>
    public void BeginAttackTelegraph() { _telegraphing = true; _attackCanceled = false; }

    /// <summary>windup 종료(데미지 적용/상태 이탈) 시 호출.</summary>
    public void EndAttackTelegraph() { _telegraphing = false; }

    /// <summary>아이템 효과가 windup 중인 공격을 취소(섬광의 순간).</summary>
    public void CancelTelegraphedAttack() { if (_telegraphing) _attackCanceled = true; }

    /// <summary>AttackState가 데미지 직전 호출 — 취소되었으면 true(데미지 스킵).</summary>
    public bool ConsumeAttackCancel() { bool c = _attackCanceled; _attackCanceled = false; return c; }

    /// <summary>
    /// 시너지 상태이상: 감전/마비(CC) — duration초간 이동·FSM 정지. 누적 시 더 긴 만료 시각 유지.
    /// (아이템 Stun 효과도 이 경로를 공유. 일반 진입점은 Status.ApplyCc.)
    /// </summary>
    public void ApplyStun(float duration)
    {
        if (_runtime == null || _runtime.IsDead) return;
        _status.ApplyCc("stun", duration);
    }

    /// <summary>인트로 등 "이길 수 없는" 연출용 — true면 피해로 HP가 1 미만으로 떨어지지 않아 처치되지 않는다. 기본 false(무변화).</summary>
    public bool HpFloorMin1 { get; set; }

    public virtual void TakeDamage(float amount, GameObject instigator, float knockbackMultiplier = 1f, bool isCrit = false)
    {
        if (_runtime == null || _runtime.IsDead) return;

        var constraints = _fsm?.CurrentConstraints ?? SpecialStateConstraint.None;

        // 무적 상태 — 데미지 자체 무시
        if ((constraints & SpecialStateConstraint.Invincible) != 0) return;

        _lastDamageKind = DamageKind.Normal;   // 주 피해 경로 = 플레이어 직접 타격

        // [서약] 출력 피해 변조는 여기서 하지 않는다 — 소유자는 CombatDamage 파이프라인 ③ 한 곳뿐이다.
        // 여기서 또 걸면 주 피해가 파이프라인 ③ + 여기로 두 번 곱해져 배율이 제곱되고,
        // 관통 경로(TakeSynergyDamage)만 한 번 적용돼 히트마다 배율이 달라진다.

        // 방어력 + 데미지 배율 + 받는 데미지 배율 + 디버프 증폭(statusId별 합연산) (최소 1 데미지)
        float defense = _baseDefense * _defenseMulti;
        float actual = Mathf.Max(1f, (amount - defense) * _runtime.DamageMultiplier * _incomingDamageMulti * CurrentDamageTakenMult());
        _runtime.CurrentHp -= (int)actual;
        if (HpFloorMin1 && _runtime.CurrentHp < 1) _runtime.CurrentHp = 1; // [인트로] 이길 수 없는 연출: 처치 방지

        // 데미지 팝업 — 모든 데미지 소스에 일관 표시 (각 호출처에서 별도 호출 불필요)
        DamagePopupSpawner.Spawn(transform.position + Vector3.up * 1.2f, actual, isCrit, GetInstanceID());

        int effMax = EffectiveMaxHp;
        _hpBar?.UpdateHP(_runtime.CurrentHp, effMax);
        OnHPChanged?.Invoke(_runtime.CurrentHp, effMax);

        if (_runtime.CurrentHp <= 0)
        {
            _runtime.CurrentHp = 0;
            _runtime.IsDead    = true;
            // [서약] 플레이어 처치 통보
            if (IsPlayerInstigator(instigator))
            {
                GameRunBootstrapper.Instance?.Run?.CovenantHandler?.OnKill(gameObject);
                GameRunBootstrapper.Instance?.Run?.EffectManager?.OnKill(gameObject);   // 아이템 처치 효과
                instigator.GetComponentInParent<PlayerController>()
                    ?.FirePassive(PassiveTrigger.OnKill, new PassiveContext { target = gameObject });  // [유물] 처치 패시브
            }
            OnFatalDamage();
        }
        else
        {
            // 중단 불가 상태 — 데미지는 들어오되 상태 전환·넉백 없음
            if ((constraints & SpecialStateConstraint.UnInterruptible) != 0) return;

            // HP 임계값 특수 상태 진입 훅 — 파생 클래스에서 ChangeState(special) 호출 가능
            // 파생 클래스가 _suppressGetHitThisHit = true 를 설정하면 GetHitState 전환을 스킵한다
            _suppressGetHitThisHit = false;
            OnDamageTaken();

            // 특수 상태(포효 등)로 전환됐으면 넉백·GetHitState 모두 스킵
            // (isKinematic을 false로 두지 않아야 특수 상태 중 물리 이탈을 막는다)
            if (IsInSpecialState) return;

            // 방어도 등 파생 클래스가 GetHit 억제 요청 시 스킵
            if (_suppressGetHitThisHit) return;

            if ((_runtime.IsDormant || _runtime.IsReturning) && _runtime.HasBeenAttacked)
            {
                ChangeState<ChaseState>();
                return;
            }

            // 먼저 GetHitState로 전환해 NavMeshAgent를 끈 뒤 Rigidbody 넉백을 적용한다.
            // Agent 활성 중에는 Agent가 위치를 제어해 임펄스가 무효화되고
            // "Setting linear velocity of kinematic body" 경고가 발생하므로, 전환→임펄스 순서가 필수.
            ChangeState<GetHitState>();

            if (instigator != null && _rb != null && (_agent == null || !_agent.isActiveAndEnabled)
                && Grade != MonsterGrade.Boss)
            {
                _rb.isKinematic = false;
                Vector3 dir = (transform.position - instigator.transform.position).normalized;
                _rb.AddForce(dir * 3f * knockbackMultiplier, ForceMode.Impulse);
            }
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 내부 헬퍼
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private static Transform FindBoneRecursive(Transform parent, string boneName)
    {
        foreach (Transform child in parent)
        {
            if (child.name == boneName) return child;
            var found = FindBoneRecursive(child, boneName);
            if (found != null) return found;
        }
        return null;
    }

    private async UniTask LoadAndApplyJsonDataAsync()
    {
        if (string.IsNullOrEmpty(DataAddress)) return;

        var textAsset = await Managers.AddressableManager.LoadAssetAsync<UnityEngine.TextAsset>(DataAddress);
        if (textAsset == null)
        {
            Debug.LogWarning($"[MonsterBase] JSON 데이터 로드 실패: {DataAddress}", this);
            return;
        }

        var data = UnityEngine.JsonUtility.FromJson<MonsterJsonData>(textAsset.text);
        if (data == null)
        {
            Debug.LogWarning($"[MonsterBase] JSON 파싱 실패: {DataAddress}", this);
            return;
        }

        data.ApplyToConfig(_config);
    }

    private void ApplyServerStatOverride()
    {
        var mgr = Managers.ServerMonsterStat;
        if (mgr == null || !mgr.IsInitialized) return;

        var id = ServerStatId;
        if (string.IsNullOrEmpty(id)) return;

        var entry = mgr.GetById(id);
        if (entry == null)
        {
            Debug.LogWarning($"[MonsterBase] 서버 스탯 없음 — id='{id}' (SO 기본값 유지)", this);
            return;
        }

        if (entry.max_hp > 0)         _config.stat.maxHp       = entry.max_hp;
        if (entry.base_attack > 0f)   _config.stat.attackPower = entry.base_attack;
        if (entry.base_defense >= 0f) _config.stat.defense      = entry.base_defense;

        Debug.Log($"[MonsterBase] 서버 스탯 적용: {id} ({entry.monster_name}) | HP={entry.max_hp} ATK={entry.base_attack} DEF={entry.base_defense}");
    }

    // Animator Controller는 프리팹에 직접 할당 — 런타임 로드 불필요
    private async UniTask LoadAnimatorControllerAsync()
    {
        if (_animator == null || _config == null)
            return;

        string address = _config.animation.animatorControllerAddress;
        if (string.IsNullOrWhiteSpace(address))
            return;

        var controllerObject = await Managers.AddressableManager.TryLoadAssetAsync<UnityEngine.Object>(address);
        var controller = controllerObject as RuntimeAnimatorController;
        if (controller == null)
        {
            Debug.LogWarning(
                $"[MonsterBase] {name}: AnimatorController load skipped. Invalid address or type: {address}",
                this);
            return;
        }

        if (_animator.runtimeAnimatorController == controller)
            return;

        _animator.runtimeAnimatorController = controller;
        _animator.applyRootMotion = false;
        _animator.Rebind();
        _animator.Update(0f);
    }

    private void SetPlayerTarget(Transform player)
    {
        if (_runtime == null) return;
        _runtime.PlayerTarget = player;
        _runtime.CachedPlayer = player != null ? player.GetComponent<PlayerController>() : null;
        if (player != null) IgnorePlayerCollision(player);
    }

    /// <summary>외곽선 표기 on/off 토글. on=Monster 레이어(외곽선/실루엣 Render Objects 필터 대상),
    /// off=루트 레이어로 복원(디졸브 등장 중 숨김 + 풀 재사용 시 레이어 잔존으로 외곽선이 재-디졸브에 새는 것 방지).
    /// 콜라이더/루트 레이어는 그대로 → 물리·타격·NavMesh 무영향. ~헬퍼(발밑그림자)는 외곽선 대상 아님.</summary>
    /// <summary>
    /// 이 몬스터의 <b>콜라이더</b>들을 MonsterHit 레이어로 올린다(렌더러는 건드리지 않는다).
    /// 광역 질의가 레이어 마스크로 몬스터만 골라낼 수 있게 하는 것이 목적 — 환경 콜라이더가
    /// NonAlloc 버퍼를 채워 몬스터가 안 잡히는 사고를 구조적으로 막는다.
    /// 프리팹을 수정하지 않도록 런타임에 적용한다.
    /// </summary>
    private void ApplyHitLayer()
    {
        if (s_monsterHitLayer == -2)
            s_monsterHitLayer = LayerMask.NameToLayer(MonsterHitLayerName);
        if (s_monsterHitLayer < 0) return;   // 레이어 미정의 — 스킵(질의는 ~0 폴백)

        var colliders = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            var c = colliders[i];
            if (c == null) continue;
            c.gameObject.layer = s_monsterHitLayer;
        }
    }

    private void SetVisibilityMarkup(bool on)
    {
        if (s_monsterVisibilityLayer == -2)
            s_monsterVisibilityLayer = LayerMask.NameToLayer(MonsterVisibilityLayerName);
        if (s_monsterVisibilityLayer < 0) return; // 레이어 미정의 — 스킵

        // off 복원 대상은 루트 레이어(콜라이더/루트가 쓰는 원본 레이어) — 비주얼 렌더러는 본래 루트와 동일 레이어.
        int target = on ? s_monsterVisibilityLayer : gameObject.layer;

        var renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null) continue;
            if (!(r is SkinnedMeshRenderer || r is MeshRenderer)) continue;
            var n = r.gameObject.name;
            if (n.Length > 0 && n[0] == '~') continue; // "~" 헬퍼(발밑그림자 등) 제외 — 외곽선 대상 아님

            // 콜라이더가 같이 붙은 GO는 건드리지 않는다 — 여긴 타격 판정 레이어(MonsterHit)라
            // 여기서 Monster 레이어로 바꿔버리면 광역 질의의 레이어 마스크가 이 몹을 놓친다(외곽선 < 타격).
            if (r.TryGetComponent<Collider>(out _)) continue;

            r.gameObject.layer = target;
        }
    }

    // 디졸브 등장(일반 1.2s / 보스 1.5s)이 끝난 뒤 외곽선을 켜기 위한 지연. 디졸브 호출처가 Spawner/Boss/Lich 등
    // 여러 곳이라 단일 onComplete를 쓰지 않고, 최장(보스 1.5s)+마진의 지연으로 본체가 완전 불투명이 된 뒤에만 켠다.
    private const float MarkupRevealDelay = 1.6f;

    /// <summary>디졸브 등장 동안 외곽선이 미완성 본체에 겹쳐 보이지 않도록 지연 후 켠다(off→대기→on).
    /// OnEnable에서 먼저 off로 내려 풀 재사용 시 잔존 레이어를 리셋 → 매 스폰의 재-디졸브 동안에도 외곽선이 안 샌다.
    /// ActivationToken에 묶여 풀 반환/파괴 시 취소(이 경우 off 상태 유지).</summary>
    private async UniTaskVoid RevealVisibilityMarkupAsync()
    {
        // 콜라이더를 타격 판정 레이어(MonsterHit)로 — 광역 질의가 몬스터만 골라낼 수 있게.
        // 풀 재사용 시에도 매 스폰 재적용(레이어 잔존/유실 방지).
        ApplyHitLayer();

        SetVisibilityMarkup(false); // 스폰 즉시 끔(디졸브 중 숨김 + 풀 재사용 레이어 리셋)
        try
        {
            await UniTask.Delay(System.TimeSpan.FromSeconds(MarkupRevealDelay),
                                ignoreTimeScale: true, cancellationToken: ActivationToken);
        }
        catch (System.OperationCanceledException) { return; }
        SetVisibilityMarkup(true);
    }

    /// <summary>발밑 가짜 그림자(MonsterGroundShadow)를 런타임 부착·구성. 콜라이더(Capsule) 반경으로 발자국 크기 산출.
    /// 인스턴스당 1회면 충분 — 풀 재사용 시 자식 그림자는 유지된다.</summary>
    private void EnsureGroundShadow()
    {
        if (_config == null) return;

        float radius = 0.5f;
        if (_cachedColliders != null)
        {
            for (int i = 0; i < _cachedColliders.Length; i++)
            {
                if (_cachedColliders[i] is CapsuleCollider cap)
                {
                    var ls = cap.transform.lossyScale;
                    radius = cap.radius * Mathf.Max(ls.x, ls.z);
                    break;
                }
            }
        }

        var shadow = GetComponent<MonsterGroundShadow>();
        if (shadow == null) shadow = gameObject.AddComponent<MonsterGroundShadow>();
        shadow.Configure(_config.grade, radius);
    }

    private void IgnorePlayerCollision(Transform player)
    {
        if (_cachedColliders == null) return;
        var playerColliders = player.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < _cachedColliders.Length; i++)
        {
            if (_cachedColliders[i] == null) continue;
            for (int j = 0; j < playerColliders.Length; j++)
            {
                if (playerColliders[j] == null) continue;
                Physics.IgnoreCollision(_cachedColliders[i], playerColliders[j], true);
            }
        }
    }

    private void OnPlayerSpawned(Transform player)
    {
        SetPlayerTarget(player);
        Managers.Player.OnPlayerSpawned -= OnPlayerSpawned;
    }

    protected void BindBossHud()
    {
        if (_config == null || _runtime == null) return;
        // 풀 프리웜된 비활성 인스턴스가 자기 자신을 바인딩해 실제 스폰된 보스의 OnHPChanged를 가로채지 않도록 가드
        if (!gameObject.activeInHierarchy) return;

        var presenter = FindAnyObjectByType<HudPresenter>(FindObjectsInactive.Include);
        if (presenter == null) return;

        presenter.BindBoss(this);
        NotifyHPChanged();
    }

    protected void UnbindBossHudIfBound()
    {
        var presenter = FindAnyObjectByType<HudPresenter>(FindObjectsInactive.Include);
        if (presenter == null) return;
        if (!ReferenceEquals(presenter.BoundBoss, this)) return;

        presenter.UnbindBoss();
    }

    private void OnDestroy()
    {
        if (Managers.Player != null)
            Managers.Player.OnPlayerSpawned -= OnPlayerSpawned;
    }


    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 풀 재사용 — OnEnable/OnDisable 콜백
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected virtual void OnEnable()
    {
        // 세대 카운터는 _config 유무와 무관하게 먼저 증가 — 아직 InitAsync 미완 상태에서도
        // 외부 콜백이 풀 재사용을 감지할 수 있도록.
        _generationId++;

        // 활성화 토큰 갱신 — 이전 DissolveEffect 복원 태스크를 차단
        _activationCts?.Cancel();
        _activationCts?.Dispose();
        _activationCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);

        // 적 가시성 표기(외곽선) — 디졸브 등장이 끝난 뒤 레이어 부여(외곽선 켜짐). 매 스폰마다 재시도(전 스폰 경로 커버).
        RevealVisibilityMarkupAsync().Forget();

        if (_config == null || _runtime == null) return;

        // 풀 재사용 시 현재 챕터의 난이도 배율 재적용(멀티챕터 런 대응).
        ResolveDifficultyScale();

        _runtime.CurrentHp           = EffectiveMaxHp;
        _runtime.IsDead              = false;
        _runtime.SpawnPosition       = transform.position;
        _runtime.PatrolDirection     = 1;
        _runtime.IsWaitingAtWaypoint = false;
        _runtime.PatrolWaitTimer     = 0f;
        _runtime.StateTimer          = 0f;
        _runtime.AttackHitDealt      = false;
        _runtime.IsFirstAttack       = true;
        _runtime.HasBeenAttacked     = false;
        _runtime.IsDormant           = false;
        _runtime.IsReturning         = false;
        _runtime.TargetCleared       = true;
        _runtime.SpeedMultiplier     = 1f;
        _runtime.AttackMultiplier    = _difficultyScale;
        _runtime.DamageMultiplier    = 1f;

        if (_agent != null)
        {
            _agent.enabled = true;
            TrySnapAgentToNavMesh();
        }

        if (_rb != null)
        {
            if (!_rb.isKinematic)
            {
                _rb.linearVelocity  = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
            _rb.isKinematic = true;
        }

        if (_cachedColliders != null)
            foreach (var col in _cachedColliders)
                if (col != null) col.enabled = true;

        _firedHpTriggers.Clear();

        _incomingDamageMulti = 1f;
        _dmgTakenAmpSlots.Clear();
        _defenseMulti        = 1f;
        _attackSpeedMulti    = 1f;
        _lastDamageKind      = DamageKind.Normal;
        _status.Reset();

        // 화상은 MonsterStatusReceiver를 타지 않는 독립 컴포넌트라 _status.Reset()으로 지워지지 않는다.
        // 불타는 도중 죽어 풀로 돌아가면 남은 시간이 그대로 얼어붙었다가, 재사용된 몬스터가
        // 활성화되는 즉시 이어서 틱을 때린다 — 스폰하자마자 즉사하던 원인.
        //
        // Destroy 만으로는 부족하다 — 실제 제거는 프레임 끝이라 그 전에 Update 가 한 번 더 돌거나
        // 같은 프레임에 재스폰되면 화상이 그대로 살아 있다. 상태를 먼저 비우고 제거한다.
        if (TryGetComponent<MonsterBurnHandler>(out var burn)) burn.CancelForPooling();
        _statusCcActive      = false;
        _statusSlowActive    = false;
        _telegraphing        = false;
        _attackCanceled      = false;
        if (_agent != null) _agent.speed = _baseAgentSpeed;

        // 풀 재사용 시 이전 Die에서 설정된 HP바 숨김 플래그/요청중 플래그를 리셋 —
        // 리셋 없이는 RequestHPBarAsync가 조기 탈출해 HP바가 영구 미표시됨.
        _worldHPBarSuppressed = false;
        _hpBarRequesting      = false;

        foreach (var cb in _onEnabledCallbacks) cb?.Invoke();
        _fsm?.ChangeState<PatrolState>();

        RequestHPBarAsync().Forget();
    }

    protected virtual void OnDisable()
    {
        // 비활성화 시 활성화 토큰을 취소 — 풀 반환 중 남아있는 DissolveEffect 복원 태스크를 차단한다.
        _activationCts?.Cancel();
        _activationCts?.Dispose();
        _activationCts = null;

        _hpBarRequesting = false;
        if (_hpBar != null)
        {
            // Managers 가 먼저 파괴된 경우 (씬 종료/플레이 종료) 로 NRE 방지
            Managers.MonsterHPBar?.ReturnHPBar(_hpBar);
            _hpBar = null;
        }
    }

    /// <summary>런타임 HP 변경(회복 등) 후 HP 바를 동기화한다. 특수 상태에서 호출.</summary>
    public void NotifyHPChanged()
    {
        int effMax = EffectiveMaxHp;
        _hpBar?.UpdateHP(_runtime.CurrentHp, effMax);
        OnHPChanged?.Invoke(_runtime.CurrentHp, effMax);
    }

    /// <summary>월드 HP 바를 즉시 숨긴다. 잠복 상태 등에서 사용.</summary>
    public void HideWorldHPBar()
    {
        if (!UseWorldHPBar) return;
        _worldHPBarSuppressed = true;
        _hpBarRequesting = false;

        if (_hpBar != null)
        {
            Managers.MonsterHPBar.ReturnHPBar(_hpBar);
            _hpBar = null;
        }
    }

    /// <summary>월드 HP 바를 다시 표시한다. 숨김 해제 후 호출.</summary>
    public void ShowWorldHPBar()
    {
        if (!UseWorldHPBar) return;
        if (!gameObject.activeInHierarchy) return;
        _worldHPBarSuppressed = false;
        RequestHPBarAsync().Forget();
    }

    private async UniTaskVoid RequestHPBarAsync()
    {
        if (!UseWorldHPBar) return;
        if (_worldHPBarSuppressed) return;
        if (_hpBarRequesting) return;
        _hpBarRequesting = true;

        if (_hpBar != null)
        {
            Managers.MonsterHPBar.ReturnHPBar(_hpBar);
            _hpBar = null;
        }
        _hpBar = await Managers.MonsterHPBar.RequestHPBarAsync(this, _runtime.CurrentHp, EffectiveMaxHp, _hpBarAnchor != null ? _hpBarAnchor : _headBone, HPBarHeadOffset);
        if (_worldHPBarSuppressed && _hpBar != null)
        {
            Managers.MonsterHPBar.ReturnHPBar(_hpBar);
            _hpBar = null;
        }
        if (_config != null)
            _hpBar?.SetMonsterInfo(_config.monsterName);
        _hpBarRequesting = false;
    }

    /// <summary>
    /// Agent 를 가장 가까운 NavMesh 위치로 스냅한다.
    /// 직접 Warp(transform.position) 이 NavMesh 와의 미세 오프셋으로 실패하는 경우를
    /// 대비해 NavMesh.SamplePosition 으로 5m 반경 nearest 포인트를 찾아 Warp 한다.
    /// </summary>
    public bool TrySnapAgentToNavMesh()
    {
        if (_agent == null || !_agent.enabled) return false;
        if (_agent.isOnNavMesh) return true;

        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 5f, NavMesh.AllAreas))
        {
            _agent.Warp(hit.position);
            return _agent.isOnNavMesh;
        }
        return false;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 에디터 Gizmo
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void OnDrawGizmosSelected()
    {
        if (_config == null) return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, _config.detection.detectionRange);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, _config.stat.attackRange);

        Gizmos.color = new Color(1f, 0f, 0f, 0.25f);
        Gizmos.DrawSphere(transform.position, _config.stat.attackRadius);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, _config.detection.chaseGiveUpRange);

        if (_runtime != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(_runtime.SpawnPosition, Vector3.one * 0.3f);
        }
    }

}
}
