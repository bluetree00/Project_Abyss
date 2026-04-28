using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;


namespace Abyss.Monster
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
public abstract class MonsterBase : MonoBehaviour, IDamageable, IElementTarget
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

    // ── 원소 시스템 ───────────────────────────────────────
    private ElementBuildup         _elementBuildup;
    private ElementVisualFeedback  _elementVisual;
    private ElementNativePalette   _elementPalette;
    private float                  _baseAgentSpeed;
    private float                  _baseDefense;
    private float                  _incomingDamageMulti = 1f;
    // 원소 효과로 인한 일시 배율 (Water slow/Earth petrify/Lightning paralyze)
    private float                  _defenseMulti        = 1f;
    private float                  _attackSpeedMulti    = 1f;
    // 네이티브 원소 패시브 보너스 (인스턴스별 — Config 공유 캐시 오염 방지)
    private float                  _nativeAttackMulti   = 1f;
    private float                  _nativeHpMulti       = 1f;
    private float                  _nativeDefenseMulti  = 1f;
    private float                  _nativeRateMulti     = 1f;
    private float                  _grassRegenTimer;

    [Header("Runtime Element (현재 속성)")]
    [Tooltip("스폰 시 SetRandomNativeElement로 자동 주입됨. 플레이 중 Inspector에서 값을 바꾸면 " +
             "OnValidate가 SetNativeElement를 호출해 tinted/Rim 머티리얼 + 보너스 스탯이 즉시 재적용된다.")]
    [SerializeField] private ElementType _effectiveElement = ElementType.None;
    // 풀 재사용 race 방어용 lifecycle 카운터 — OnEnable마다 증가하여 외부 콜백(dissolve onComplete 등)이
    // 자기 세대 값과 비교해 이전 인스턴스에 대한 호출을 무시할 수 있도록 한다.
    // 기존 _worldHPBarSuppressed/_hpBarRequesting과 계층이 달라(외부 vs 내부 UI) 겹치지 않음.
    private int                    _generationId;

    /// <summary>외부 비동기 콜백이 풀 재사용 이전 세대에 대한 것인지 검증할 때 비교하는 값. OnEnable마다 +1.</summary>
    public int GenerationId => _generationId;

    /// <summary>공격 상태/어빌리티가 쿨다운 계산 시 곱할 배율. 0 = 공격 불가.</summary>
    public float AttackSpeedMultiplier => _attackSpeedMulti;

    /// <summary>이 몬스터 인스턴스에 실제 적용된 원소. 스폰 시 SetRandomNativeElement로 결정.</summary>
    public ElementType EffectiveElement => _effectiveElement;

    /// <summary>네이티브 보너스와 원소 효과 배율이 모두 반영된 유효 공격력.</summary>
    public float EffectiveAttackPower => _config != null ? _config.stat.attackPower * _nativeAttackMulti : 0f;

    /// <summary>네이티브 보너스가 반영된 유효 최대 HP (반올림).</summary>
    public int EffectiveMaxHp => _config != null ? Mathf.RoundToInt(_config.stat.maxHp * _nativeHpMulti) : 0;

    /// <summary>네이티브 보너스가 반영된 유효 공격 속도.</summary>
    public float EffectiveAttackRate => _config != null ? _config.stat.attackRate * _nativeRateMulti : 0f;

    // ── HP 변경 이벤트 (보스 UI 등 외부에서 구독) ─────────
    /// <summary>HP가 변경될 때마다 발행. (currentHp, maxHp)</summary>
    public event System.Action<int, int> OnHPChanged;

    /// <summary>몬스터 사망 시 1회 발행. RoomClearController 등 외부 수명주기가 구독.</summary>
    public event System.Action<MonsterBase> OnDied;

    /// <summary>DieState.Enter에서 호출. 외부 구독자가 사망을 감지할 수 있도록 이벤트 래핑.</summary>
    public void RaiseDied() => OnDied?.Invoke(this);

    /// <summary>보스 HP 바 초기화용. Config 로드 후 유효.</summary>
    public int CurrentHp => _runtime != null ? _runtime.CurrentHp : 0;
    public int    BossMaxHp => EffectiveMaxHp;
    public string BossName  => _config != null ? _config.monsterName : string.Empty;

    // ── 특수 상태 인스턴스 (SO 데이터로 자동 생성) ────────
    private readonly List<SpecialStateBase> _specialStates = new();

    // ── 원소 상태 인스턴스 (원소별 고정 슬롯) ─────────────
    private readonly SpecialStateBase[] _elementalStates = new SpecialStateBase[ElementTypeUtil.Count];

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

    private async void Awake()
    {
        await InitAsync();
    }

    protected virtual async UniTask InitAsync()
    {
        // 1. MonsterConfigSO 로드 (주소별 캐시 — 동종 몬스터는 Instantiate·JSON 적용을 1회만 수행)
        if (!_configCache.TryGetValue(ConfigAddress, out _config))
        {
            var loaded = await Managers.AddressableManager.LoadAssetAsync<MonsterConfigSO>(ConfigAddress);
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

                // 2-0. 구버전 .asset 값(절대 100~1000) → 배율(1.0~10.0) 자동 마이그레이션.
                // 새 필드 maxAccumulationScale은 "배율"이라 10을 초과하면 구버전 저장값으로 간주.
                if (_config.elemental.maxAccumulationScale > 10f)
                    _config.elemental.maxAccumulationScale /= 100f;

                // 2-1. 서버 CDN(MONSTER_ELEMENT_STAT_DATA) 으로 수치 오버라이드 (Addressable JSON 위에 덮어쓰기)
                ApplyServerStatOverride();
            }
        }

        // 2-2. base 스탯 캐싱 (ApplyNativeElementBonus 로그가 올바른 DEF를 찍도록 선행)
        _baseDefense = _config.stat.defense;

        // 2-3. 인스턴스 고유 원소를 Config 기본값으로 초기화 + 보너스 배율 세팅.
        // (MonsterSpawner가 스폰 직후 SetRandomNativeElement로 6원소 중 랜덤 덮어씀)
        _effectiveElement = _config.stat.nativeElement;
        ApplyNativeElementBonus();

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

        // Agent 를 가장 가까운 NavMesh 로 스냅. baseOffset=0 + voxel 오차로
        // isOnNavMesh=false 로 시작하는 경우를 방지한다. SamplePosition 기반 재시도.
        TrySnapAgentToNavMesh();

        // NavMeshAgent가 위치를 제어하므로 Rigidbody는 kinematic 유지
        _rb = GetComponent<Rigidbody>();
        if (_rb != null) _rb.isKinematic = true;

        // 원소 시스템 자동 부착 (없으면 생성) + 배율 주입
        _elementBuildup = GetComponent<ElementBuildup>();
        if (_elementBuildup == null) _elementBuildup = gameObject.AddComponent<ElementBuildup>();
        _elementVisual  = GetComponent<ElementVisualFeedback>();
        if (_elementVisual == null)  _elementVisual  = gameObject.AddComponent<ElementVisualFeedback>();
        _elementBuildup.SetMonsterMaxAccumulationScale(_config.elemental.maxAccumulationScale);
        _elementBuildup.OnTriggered += HandleElementTriggered;
        _elementPalette = GetComponent<ElementNativePalette>();
        if (_elementPalette == null) _elementPalette = gameObject.AddComponent<ElementNativePalette>();

        // 4. Animator 설정 (Addressables에서 AnimatorController 로드)
        _animator = GetComponentInChildren<Animator>();
        if (_animator == null)
            Debug.LogWarning($"[MonsterBase] {name}: Animator 컴포넌트를 찾을 수 없습니다. 프리팹 구조를 확인하세요.", this);
        await LoadAnimatorControllerAsync();

        // Collider 배열 캐싱 (OnEnable에서 GetComponentsInChildren 반복 방지)
        _cachedColliders = GetComponentsInChildren<Collider>(true);

        // 4-1. Head 본 탐색
        if (!string.IsNullOrEmpty(HPBarAnchorName))
            _hpBarAnchor = FindBoneRecursive(transform, HPBarAnchorName);

        if (!string.IsNullOrEmpty(HeadBoneName))
            _headBone = FindBoneRecursive(transform, HeadBoneName);

        // 5. 런타임 데이터 초기화
        _runtime = new MonsterRuntimeData
        {
            CurrentHp        = EffectiveMaxHp,
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
            _hpBar?.SetMonsterInfo(_config.monsterName, _effectiveElement);
        }

        OnInitialized();
    }

    /// <summary>초기화 완료 후 파생 클래스에서 추가 처리가 필요한 경우 오버라이드.</summary>
    protected virtual void OnInitialized() { }

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

        // ── 원소 상태 등록 (null = DefaultElementalState, SO 있으면 커스텀) ──
        for (int i = 0; i < ElementTypeUtil.Count; i++)
        {
            var elementType = (ElementType)i;
            var entry = _config.elemental.Get(elementType);
            _elementalStates[i] = entry?.overrideState?.Create(this)
                                   ?? new DefaultElementalState(elementType);
        }
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

        // 원소 누적치 게이지 UI 갱신 (독 게이지일 때 스택 수 포함)
        if (_hpBar != null && _elementBuildup != null)
            _hpBar.UpdateElement(_elementBuildup.Ratio, _elementBuildup.Accum, _elementBuildup.Threshold,
                                 _elementBuildup.LastElement, _elementBuildup.PoisonStacks);

        // 네이티브 Grass — 2초마다 최대HP의 5% 재생
        if (_effectiveElement == ElementType.Grass && _runtime.CurrentHp < EffectiveMaxHp)
        {
            _grassRegenTimer += Time.deltaTime;
            if (_grassRegenTimer >= 2f)
            {
                _grassRegenTimer -= 2f;
                int effMax = EffectiveMaxHp;
                int heal = Mathf.Max(1, Mathf.RoundToInt(effMax * 0.05f));
                int newHp = Mathf.Min(effMax, _runtime.CurrentHp + heal);
                if (newHp != _runtime.CurrentHp)
                {
                    _runtime.CurrentHp = newHp;
                    _hpBar?.UpdateHP(newHp, effMax);
                    OnHPChanged?.Invoke(newHp, effMax);
                }
            }
        }

        _fsm?.Update();

#if UNITY_EDITOR
        _debugState = _fsm?.CurrentType?.Name;
#endif
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

        // 원소 누적치 처리는 ElementBuildup 으로 이전 (HandleElementTriggered 이벤트 참조)
    }

    /// <summary>
    /// 공격 히트 판정: 반경 내 플레이어에게 데미지 + 넉백 적용.
    /// AttackState 또는 MonsterAnimEventReceiver에서 호출.
    /// </summary>
    public void DealDamageToPlayer()
    {
        if (_runtime?.PlayerTarget == null || _config == null) return;

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

        player.TakeDamage(damage);

        Vector3 dir = (_runtime.PlayerTarget.position - transform.position).normalized;
        dir.y = 0.3f;
        player.ApplyKnockback(dir.normalized * knockbackForce);
    }

    /// <summary>애니메이션 이벤트에서 호출 (MonsterAnimEventReceiver 경유).</summary>
    public void OnAnimAttackHit() => DealDamageToPlayer();

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

    public virtual void TakeDamage(float amount, GameObject instigator, float knockbackMultiplier = 1f,
                                   ElementType element = ElementType.None, float elementAmount = 0f)
    {
        if (_runtime == null || _runtime.IsDead) return;

        var constraints = _fsm?.CurrentConstraints ?? SpecialStateConstraint.None;

        // 무적 상태 — 데미지 자체 무시
        if ((constraints & SpecialStateConstraint.Invincible) != 0) return;

        // 방어력(네이티브 보너스 + 원소 효과 배율) + 데미지 배율 + 받는 데미지 배율 (최소 1 데미지)
        float defense = _baseDefense * _nativeDefenseMulti * _defenseMulti;
        float actual = Mathf.Max(1f, (amount - defense) * _runtime.DamageMultiplier * _incomingDamageMulti);
        _runtime.CurrentHp -= (int)actual;

        // 원소 누적치는 ElementBuildup 으로 위임 (resistance 반영)
        if (element.IsValid() && elementAmount > 0f && _elementBuildup != null)
        {
            var entry = _config.elemental.Get(element);
            float scaled = elementAmount * (entry?.resistance ?? 1f);
            _elementBuildup.AddBuildup(element, scaled, actual);
        }
        int effMax = EffectiveMaxHp;
        _hpBar?.UpdateHP(_runtime.CurrentHp, effMax);
        OnHPChanged?.Invoke(_runtime.CurrentHp, effMax);

        if (_runtime.CurrentHp <= 0)
        {
            _runtime.CurrentHp = 0;
            _runtime.IsDead    = true;
            ChangeState<DieState>();
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

            // NavMeshAgent가 활성화된 몬스터는 Agent가 위치를 제어하므로 Rigidbody 넉백 생략
            // (isKinematic ↔ Agent 충돌로 발생하는 "Setting linear velocity of kinematic body" 경고 방지)
            if (instigator != null && _rb != null && (_agent == null || !_agent.isActiveAndEnabled))
            {
                _rb.isKinematic = false;
                Vector3 dir = (transform.position - instigator.transform.position).normalized;
                _rb.AddForce(dir * 3f * knockbackMultiplier, ForceMode.Impulse);
            }

            ChangeState<GetHitState>();
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

    /// <summary>
    /// MONSTER_ELEMENT_STAT_DATA 서버 값으로 _config 수치 덮어쓰기.
    /// HP / 공격력 / 방어력 / 누적치 임계값 / 네이티브 원소.
    /// 서버 데이터가 없거나 매니저 미초기화 시 SO 기본값 유지.
    /// </summary>
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

        if (entry.max_hp > 0)           _config.stat.maxHp         = entry.max_hp;
        if (entry.base_attack > 0f)     _config.stat.attackPower   = entry.base_attack;
        if (entry.base_defense >= 0f)   _config.stat.defense       = entry.base_defense;
        if (entry.max_accumulation > 0f) _config.elemental.maxAccumulationScale = entry.max_accumulation;

        _config.stat.nativeElement = ParseElementType(entry.element);

        Debug.Log($"[MonsterBase] 서버 스탯 적용: {id} ({entry.monster_name}) | HP={entry.max_hp} ATK={entry.base_attack} DEF={entry.base_defense} | Elem={_config.stat.nativeElement} AccumScale={entry.max_accumulation}");
    }

    /// <summary>
    /// 인스턴스 원소에 따른 패시브 보너스를 배율 필드에 적용.
    /// Config를 직접 수정하지 않으므로 동종 몬스터 간 공유 캐시가 오염되지 않는다.
    ///   Fire      → 공격력 +10%
    ///   Water     → 최대HP +10%
    ///   Lightning → 공격속도 +10%
    ///   Earth     → 방어력 +10%
    ///   Grass     → 2초마다 최대HP의 5% 체력 재생 (Update 루프에서 처리)
    ///   None      → 보너스 없음
    /// </summary>
    private void ApplyNativeElementBonus()
    {
        const float Bonus = 0.1f;
        // 기본값 리셋 — 재사용(풀) 시 이전 속성 배율 잔존 방지
        _nativeAttackMulti  = 1f;
        _nativeHpMulti      = 1f;
        _nativeDefenseMulti = 1f;
        _nativeRateMulti    = 1f;

        switch (_effectiveElement)
        {
            case ElementType.Fire:
                _nativeAttackMulti = 1f + Bonus;
                break;
            case ElementType.Water:
                _nativeHpMulti = 1f + Bonus;
                break;
            case ElementType.Lightning:
                _nativeRateMulti = 1f + Bonus;
                break;
            case ElementType.Earth:
                _nativeDefenseMulti = 1f + Bonus;
                break;
            case ElementType.Grass:
                // 재생은 Update 루프에서 nativeElement 체크로 처리
                break;
            default:
                return; // None — 보너스 없음
        }

        Debug.Log($"[MonsterBase] 네이티브 원소 보너스: {name} Element={_effectiveElement} → HP={EffectiveMaxHp} ATK={EffectiveAttackPower:F1} DEF={_baseDefense * _nativeDefenseMulti:F1} Rate={EffectiveAttackRate:F2}");
    }

    /// <summary>스폰 시 6원소(None 포함) 중 균등 랜덤으로 인스턴스 원소 결정 + 보너스/틴트 재적용.
    /// 같은 Config 공유 캐시를 쓰므로 여러 인스턴스가 서로 다른 원소 variant가 될 수 있다.
    /// InitAsync 진행 중이면 완료될 때까지 대기 후 적용.</summary>
    public void SetRandomNativeElement()
    {
        // None(-1) 포함 6개. (int)값이 -1~4이므로 Random.Range(-1, 5)
        int r = UnityEngine.Random.Range(-1, ElementTypeUtil.Count);
        int gen = _generationId;
        ApplyElementWhenReady((ElementType)r, gen).Forget();
    }

    private async UniTaskVoid ApplyElementWhenReady(ElementType element, int expectedGen)
    {
        // Config/runtime이 아직 null이면 InitAsync가 진행 중. 완료 대기.
        while ((_config == null || _runtime == null) && this != null && gameObject != null)
        {
            if (_generationId != expectedGen) return; // 대기 중 풀 재사용 → 무시
            await UniTask.Yield(destroyCancellationToken);
        }
        if (this == null || gameObject == null) return;
        if (_generationId != expectedGen) return;        // 완료 시점에 재사용됐어도 무시
        if (!gameObject.activeInHierarchy) return;

        // 여러 몬스터가 같은 프레임에 디졸브 종료 → 동시 Apply 시 renderer.materials 복제 스파이크 발생.
        // 0~2 프레임 랜덤 지연으로 Apply 호출을 분산해 프레임당 부담을 1/3로 경감.
        int jitterFrames = UnityEngine.Random.Range(0, 3);
        for (int i = 0; i < jitterFrames; i++)
        {
            if (this == null || gameObject == null) return;
            if (_generationId != expectedGen) return;
            await UniTask.Yield(destroyCancellationToken);
        }
        if (!gameObject.activeInHierarchy) return;

        SetNativeElement(element);
    }

    /// <summary>특정 원소로 강제 설정. 방별 테마 스폰 등 특수 케이스용.</summary>
    public void SetNativeElement(ElementType element)
    {
        _effectiveElement = element;
#if UNITY_EDITOR
        _lastInspectorElement = element; // OnValidate 중복 트리거 방지
#endif
        ApplyNativeElementBonus();
        _grassRegenTimer = 0f; // 원소 바뀌면 재생 타이머 초기화
        _elementPalette?.Apply(element);

        // HP 배율이 바뀌면 CurrentHp도 비례 조정 (풀피 기준 유지)
        if (_runtime != null && !_runtime.IsDead)
            _runtime.CurrentHp = EffectiveMaxHp;

        _hpBar?.UpdateHP(_runtime != null ? _runtime.CurrentHp : 0, EffectiveMaxHp);
        if (_config != null)
            _hpBar?.SetMonsterInfo(_config.monsterName, _effectiveElement);
    }

    private static ElementType ParseElementType(string s) => s switch
    {
        "Lightning" => ElementType.Lightning,
        "Water"     => ElementType.Water,
        "Fire"      => ElementType.Fire,
        "Grass"     => ElementType.Grass,
        "Earth"     => ElementType.Earth,
        _           => ElementType.None,
    };

    // Animator Controller는 프리팹에 직접 할당 — 런타임 로드 불필요
    private UniTask LoadAnimatorControllerAsync() => UniTask.CompletedTask;

    private void SetPlayerTarget(Transform player)
    {
        if (_runtime == null) return;
        _runtime.PlayerTarget = player;
        _runtime.CachedPlayer = player != null ? player.GetComponent<PlayerController>() : null;
    }

    private void OnPlayerSpawned(Transform player)
    {
        SetPlayerTarget(player);
        Managers.Player.OnPlayerSpawned -= OnPlayerSpawned;
    }

    protected void BindBossHud()
    {
        if (_config == null || _runtime == null) return;

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

#if UNITY_EDITOR
    // Inspector에서 _effectiveElement를 직접 바꿔 쉐이더/머티리얼 연출을 즉시 확인할 수 있게 한다.
    // 코드 경로로 값이 바뀌는 경우(Awake, SetNativeElement 등)는 _lastInspectorElement 동기화로 중복 호출 방지.
    private ElementType _lastInspectorElement = ElementType.None;

    private void OnValidate()
    {
        if (!Application.isPlaying) return;
        if (_effectiveElement == _lastInspectorElement) return;

        var newElement = _effectiveElement;
        _lastInspectorElement = newElement;

        // Awake 이전/Config 미로드 상태에서는 Apply 경로 스킵 — Awake가 이후 정상 초기화.
        if (_elementPalette == null || _config == null || _runtime == null) return;
        SetNativeElement(newElement);
    }
#endif

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 풀 재사용 — OnEnable/OnDisable 콜백
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected virtual void OnEnable()
    {
        // 세대 카운터는 _config 유무와 무관하게 먼저 증가 — 아직 InitAsync 미완 상태에서도
        // 외부 콜백이 풀 재사용을 감지할 수 있도록.
        _generationId++;

        if (_config == null || _runtime == null) return;

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
        _runtime.AttackMultiplier    = 1f;
        _runtime.DamageMultiplier    = 1f;

        if (_agent != null)
        {
            _agent.enabled = true;
            TrySnapAgentToNavMesh();
        }

        if (_rb != null)
        {
            _rb.isKinematic     = true;
            _rb.linearVelocity  = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }

        if (_cachedColliders != null)
            foreach (var col in _cachedColliders)
                if (col != null) col.enabled = true;

        _firedHpTriggers.Clear();
        System.Array.Clear(_runtime.ElementAccumulation, 0, _runtime.ElementAccumulation.Length);

        // 원소 상태/버프 리셋 (일시 배율만 — 네이티브 보너스 배율은 SetRandomNativeElement에서 재설정)
        _incomingDamageMulti = 1f;
        _defenseMulti        = 1f;
        _attackSpeedMulti    = 1f;
        _grassRegenTimer     = 0f;
        _elementBuildup?.ResetAll();
        if (_agent != null) _agent.speed = _baseAgentSpeed;

        // 풀 재사용 시 이전 Die에서 설정된 HP바 숨김 플래그/요청중 플래그를 리셋 —
        // 리셋 없이는 RequestHPBarAsync가 조기 탈출해 HP바/이름/속성 라벨이 영구 미표시됨.
        _worldHPBarSuppressed = false;
        _hpBarRequesting      = false;

        foreach (var cb in _onEnabledCallbacks) cb?.Invoke();
        _fsm?.ChangeState<PatrolState>();

        RequestHPBarAsync().Forget();
    }

    protected virtual void OnDisable()
    {
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
            _hpBar?.SetMonsterInfo(_config.monsterName, _effectiveElement);
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

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // IElementTarget 구현
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    Transform IElementTarget.Transform => transform;
    GameObject IElementTarget.GameObject => gameObject;
    float IElementTarget.MaxHp => EffectiveMaxHp;

    public void TakeElementalDoT(float damage, ElementType source)
    {
        if (_runtime == null || _runtime.IsDead || damage <= 0f) return;
        _runtime.CurrentHp -= Mathf.Max(1, (int)damage);
        if (_runtime.CurrentHp <= 0)
        {
            _runtime.CurrentHp = 0;
            _runtime.IsDead    = true;
            ChangeState<DieState>();
        }
        int effMax = EffectiveMaxHp;
        _hpBar?.UpdateHP(_runtime.CurrentHp, effMax);
        OnHPChanged?.Invoke(_runtime.CurrentHp, effMax);

        DamagePopupSpawner.Spawn(transform.position + Vector3.up * 1.5f, damage, false, source);
    }

    public void SetIncomingDamageMultiplier(float multi) => _incomingDamageMulti = Mathf.Max(0f, multi);

    public void SetMovementMultiplier(float multi)
    {
        if (_agent == null) return;
        _agent.speed = _baseAgentSpeed * Mathf.Max(0f, multi);
    }

    public void SetAttackSpeedMultiplier(float multi)
    {
        _attackSpeedMulti = Mathf.Max(0f, multi);
    }

    public void SetDefenseMultiplier(float multi)
    {
        _defenseMulti = Mathf.Max(0f, multi);
    }

    private void HandleElementTriggered(ElementType element, ElementEffectEntry entry)
    {
        Debug.Log($"[Monster:{name}] 원소 발동 → {element} ({entry.effect_id}) dur={entry.duration}s");
    }
}
}
