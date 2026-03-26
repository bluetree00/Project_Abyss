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
    /// HP 바 위치의 기준이 될 Head 본 이름.
    /// 파생 클래스에서 실제 본 이름으로 오버라이드.
    /// null이면 콜라이더 상단 기준 폴백.
    /// </summary>
    protected virtual string HeadBoneName => "Head";

    /// <summary>Head 본 위에서 추가로 올릴 오프셋 (m).</summary>
    protected virtual float HPBarHeadOffset => 0.1f;

    /// <summary>월드 스페이스 HP 바 사용 여부. 보스처럼 HUD에서 체력을 표시하는 경우 false로 오버라이드.</summary>
    protected virtual bool UseWorldHPBar => true;

    // ── 내부 필드 ─────────────────────────────────────────
    protected MonsterConfigSO    _config;
    protected MonsterFSM         _fsm;
    protected MonsterContext     _ctx;
    protected MonsterRuntimeData _runtime;
    protected NavMeshAgent       _agent;
    protected Animator           _animator;
    private   MonsterHPBar       _hpBar;
    private   bool               _hpBarRequesting;

    // ── HP 변경 이벤트 (보스 UI 등 외부에서 구독) ─────────
    /// <summary>HP가 변경될 때마다 발행. (currentHp, maxHp)</summary>
    public event System.Action<int, int> OnHPChanged;

    /// <summary>보스 HP 바 초기화용. Config 로드 후 유효.</summary>
    public int    BossMaxHp => _config != null ? _config.stat.maxHp : 0;
    public string BossName  => _config != null ? _config.monsterName : string.Empty;

    // ── 특수 상태 인스턴스 (SO 데이터로 자동 생성) ────────
    private readonly List<SpecialStateBase> _specialStates = new();

    /// <summary>인덱스로 특수 상태 인스턴스를 가져온다. 파생 클래스의 TryGetSpecialState에서 활용.</summary>
    protected SpecialStateBase GetSpecialState(int index)
        => index < _specialStates.Count ? _specialStates[index] : null;

    // Inspector 디버그용
    [SerializeField] private string _debugState;
    private float     _diagTimer;
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
        // 1. MonsterConfigSO 로드 (Addressables)
        _config = await Managers.AddressableManager.LoadAssetAsync<MonsterConfigSO>(ConfigAddress);

        if (_config == null)
        {
            Debug.LogError($"[MonsterBase] ConfigSO 로드 실패: {ConfigAddress}", this);
            return;
        }

        if (_config.stat == null || _config.detection == null ||
            _config.patrol == null || _config.combat == null || _config.animation == null)
        {
            Debug.LogError($"[MonsterBase] ConfigSO '{ConfigAddress}' 의 서브 SO 중 null 항목이 있습니다.", this);
            return;
        }

        // 2. JSON 데이터 로드 후 SO에 덮어쓰기
        await LoadAndApplyJsonDataAsync();

        // 3. NavMeshAgent 설정
        _agent = GetComponent<NavMeshAgent>();
        if (_agent == null)
        {
            Debug.LogWarning("[MonsterBase] NavMeshAgent가 없어 자동 추가합니다.", this);
            _agent = gameObject.AddComponent<NavMeshAgent>();
        }
        _agent.speed            = _config.stat.moveSpeed;
        _agent.stoppingDistance = _config.stat.attackRange * 0.9f;

        // NavMeshAgent가 위치를 제어하므로 Rigidbody는 kinematic 유지
        var _rb = GetComponent<Rigidbody>();
        if (_rb != null) _rb.isKinematic = true;

        // 4. Animator 설정 (Addressables에서 AnimatorController 로드)
        _animator = GetComponentInChildren<Animator>();
        await LoadAnimatorControllerAsync();

        // 4-1. Head 본 탐색
        if (!string.IsNullOrEmpty(HeadBoneName))
            _headBone = FindBoneRecursive(transform, HeadBoneName);

        // 5. 런타임 데이터 초기화
        _runtime = new MonsterRuntimeData
        {
            CurrentHp        = _config.stat.maxHp,
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
            _runtime.PlayerTarget = Managers.Player.PlayerTransform;
        else
            Managers.Player.OnPlayerSpawned += OnPlayerSpawned;

        // 8. FSM 초기화
        _fsm = new MonsterFSM(_ctx);
        RegisterStates();
        _fsm.ChangeState<PatrolState>();

        // 9. HP 바 요청 (보스 등 UseWorldHPBar == false면 건너뜀)
        if (UseWorldHPBar && gameObject.activeInHierarchy)
            _hpBar = await Managers.MonsterHPBar.RequestHPBarAsync(this, _runtime.CurrentHp, _config.stat.maxHp, _headBone, HPBarHeadOffset);

        OnInitialized();
    }

    /// <summary>초기화 완료 후 파생 클래스에서 추가 처리가 필요한 경우 오버라이드.</summary>
    protected virtual void OnInitialized() { }

    /// <summary>
    /// 공용 상태를 FSM에 등록한다.
    /// </summary>
    protected virtual void RegisterStates()
    {
        _fsm.Register(new PatrolState());
        _fsm.Register(new ChaseState());
        _fsm.Register(new AttackReadyState());
        _fsm.Register(new AttackState());
        _fsm.Register(new GetHitState());
        _fsm.Register(new DieState());

        // 특수 상태: 슬롯에 데이터가 있으면 자동으로 인스턴스 생성
        _specialStates.Clear();
        foreach (var data in new[] {
            _config.specialState0,
            _config.specialState1,
            _config.specialState2,
            _config.specialState3 })
        {
            if (data != null)
                _specialStates.Add(data.CreateState());
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 매 프레임
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected virtual void Update()
    {
        if (_config == null || _runtime == null || _runtime.IsDead) return;

        // 매 프레임 가장 가까운 PlayerController를 타깃으로 갱신
        _runtime.PlayerTarget = FindClosestPlayerTarget();

        _fsm?.Update();

#if UNITY_EDITOR
        _debugState = _fsm?.CurrentType?.Name;
#endif
    }

    /// <summary>씬에서 이 몬스터와 가장 가까운 살아있는 PlayerController Transform을 반환한다.</summary>
    private Transform FindClosestPlayerTarget()
    {
        var all = Object.FindObjectsOfType<PlayerController>();
        Transform closestAlive = null;
        Transform closestAny   = null;
        float     minAliveDist = float.MaxValue;
        float     minAnyDist   = float.MaxValue;

        foreach (var pc in all)
        {
            float d     = Vector3.Distance(transform.position, pc.transform.position);
            bool  alive = pc.RuntimeStats != null && pc.RuntimeStats.Hp > 0;

            if (d < minAnyDist)   { minAnyDist   = d; closestAny   = pc.transform; }
            if (alive && d < minAliveDist) { minAliveDist = d; closestAlive = pc.transform; }
        }

        return closestAlive ?? closestAny ?? Managers.Player.PlayerTransform;
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
        float dist = Vector3.Distance(ctx.Transform.position, ctx.Runtime.PlayerTarget.position);
        return dist <= ctx.Detection.detectionRange;
    }

    /// <summary>Chase → Patrol 전환 조건. 오버라이드로 포기 거리·조건 교체.</summary>
    public virtual bool ShouldGiveUpChase(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null || IsPlayerDead()) return true;
        float dist = Vector3.Distance(ctx.Transform.position, ctx.Runtime.PlayerTarget.position);
        return dist > ctx.Detection.chaseGiveUpRange;
    }

    /// <summary>Chase → AttackReady 전환 조건. 오버라이드로 공격 진입 거리 교체.</summary>
    public virtual bool ShouldEnterAttackReady(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return false;
        float dist = Vector3.Distance(ctx.Transform.position, ctx.Runtime.PlayerTarget.position);
        return dist <= ctx.Stat.attackRange;
    }

    /// <summary>
    /// 데미지를 받아 HP가 감소한 직후, GetHitState 전환 전에 호출된다.
    /// 파생 클래스에서 오버라이드해 HP 임계값 기반 특수 상태 진입을 구현한다.
    /// 이 메서드 안에서 ChangeState(specialState) 를 호출하면
    /// 이후 GetHitState 전환이 자동으로 스킵된다.
    /// </summary>
    protected virtual void OnDamageTaken() { }

    /// <summary>
    /// 공격 히트 판정: 반경 내 플레이어에게 데미지 + 넉백 적용.
    /// AttackState 또는 MonsterAnimEventReceiver에서 호출.
    /// </summary>
    public void DealDamageToPlayer()
    {
        if (_runtime?.PlayerTarget == null || _config == null) return;

        float dist = Vector3.Distance(transform.position, _runtime.PlayerTarget.position);
        if (dist > _config.stat.attackRadius) return;

        var player = _runtime.PlayerTarget.GetComponent<PlayerController>();
        if (player == null) return;

        player.TakeDamage((int)(_config.stat.attackPower * _runtime.AttackMultiplier));

        var rb = _runtime.PlayerTarget.GetComponent<Rigidbody>();
        if (rb != null)
        {
            Vector3 dir = (_runtime.PlayerTarget.position - transform.position).normalized;
            dir.y = 0.3f;
            rb.AddForce(dir.normalized * _config.stat.knockbackForce, ForceMode.Impulse);
        }
    }

    /// <summary>애니메이션 이벤트에서 호출 (MonsterAnimEventReceiver 경유).</summary>
    public void OnAnimAttackHit() => DealDamageToPlayer();

    /// <summary>플레이어가 사망했는지 확인.</summary>
    public bool IsPlayerDead()
    {
        if (_runtime?.PlayerTarget == null) return true;
        var pc = _runtime.PlayerTarget.GetComponent<PlayerController>();
        if (pc == null) return false;
        if (pc.RuntimeStats == null) return false;
        return pc.RuntimeStats.Hp <= 0;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // IDamageable — 플레이어 공격에 맞을 때 호출됨
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    public void TakeDamage(float amount, GameObject instigator, float knockbackMultiplier = 1f)
    {
        if (_runtime == null || _runtime.IsDead) return;

        var constraints = _fsm?.CurrentConstraints ?? SpecialStateConstraint.None;

        // 무적 상태 — 데미지 자체 무시
        if ((constraints & SpecialStateConstraint.Invincible) != 0) return;

        // 방어력 + 데미지 배율 적용 (최소 1 데미지)
        float actual = Mathf.Max(1f, (amount - _config.stat.defense) * _runtime.DamageMultiplier);
        _runtime.CurrentHp -= (int)actual;
        _hpBar?.UpdateHP(_runtime.CurrentHp, _config.stat.maxHp);
        OnHPChanged?.Invoke(_runtime.CurrentHp, _config.stat.maxHp);

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

            if (instigator != null)
            {
                var rb = GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.isKinematic = false;
                    Vector3 dir = (transform.position - instigator.transform.position).normalized;
                    rb.AddForce(dir * 3f * knockbackMultiplier, ForceMode.Impulse);
                }
            }

            // HP 임계값 특수 상태 진입 훅 — 파생 클래스에서 ChangeState(special) 호출 가능
            OnDamageTaken();

            // 특수 상태로 전환됐으면 GetHitState 스킵
            if (!IsInSpecialState)
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

    private async UniTask LoadAnimatorControllerAsync()
    {
        if (_config.animation == null) return;
        string addr = _config.animation.animatorControllerAddress;
        if (string.IsNullOrEmpty(addr)) return;

        var overrideCtrl = await Managers.AddressableManager
            .LoadAssetAsync<AnimatorOverrideController>(addr);

        if (overrideCtrl == null || _animator == null) return;

        _animator.runtimeAnimatorController = overrideCtrl;
    }

    private void OnPlayerSpawned(Transform player)
    {
        if (_runtime != null)
            _runtime.PlayerTarget = player;
        Managers.Player.OnPlayerSpawned -= OnPlayerSpawned;
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
        if (_config == null || _runtime == null) return;

        _runtime.CurrentHp           = _config.stat.maxHp;
        _runtime.IsDead              = false;
        _runtime.SpawnPosition       = transform.position;
        _runtime.PatrolDirection     = 1;
        _runtime.IsWaitingAtWaypoint = false;
        _runtime.PatrolWaitTimer     = 0f;
        _runtime.StateTimer          = 0f;
        _runtime.AttackHitDealt      = false;
        _runtime.IsFirstAttack       = true;
        _runtime.SpeedMultiplier     = 1f;
        _runtime.AttackMultiplier    = 1f;
        _runtime.DamageMultiplier    = 1f;

        if (_agent != null) _agent.enabled = true;

        var rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic     = true;
            rb.linearVelocity  = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        foreach (var col in GetComponentsInChildren<Collider>())
            col.enabled = true;

        _fsm?.ChangeState<PatrolState>();

        RequestHPBarAsync().Forget();
    }

    protected virtual void OnDisable()
    {
        _hpBarRequesting = false;
        if (_hpBar != null)
        {
            Managers.MonsterHPBar.ReturnHPBar(_hpBar);
            _hpBar = null;
        }
    }

    /// <summary>런타임 HP 변경(회복 등) 후 HP 바를 동기화한다. 특수 상태에서 호출.</summary>
    public void NotifyHPChanged()
    {
        _hpBar?.UpdateHP(_runtime.CurrentHp, _config.stat.maxHp);
        OnHPChanged?.Invoke(_runtime.CurrentHp, _config.stat.maxHp);
    }

    private async UniTaskVoid RequestHPBarAsync()
    {
        if (!UseWorldHPBar) return;
        if (_hpBarRequesting) return;
        _hpBarRequesting = true;

        if (_hpBar != null)
        {
            Managers.MonsterHPBar.ReturnHPBar(_hpBar);
            _hpBar = null;
        }
        _hpBar = await Managers.MonsterHPBar.RequestHPBarAsync(this, _runtime.CurrentHp, _config.stat.maxHp, _headBone, HPBarHeadOffset);
        _hpBarRequesting = false;
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
