using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Lee 몬스터 시스템의 추상 기반 클래스.
///
/// ━━━ 설계 원칙 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  • Inspector에 SO를 직접 할당하지 않는다.
///  • ConfigAddress 주소 하나로 Addressables에서 MonsterConfigSO를 로드.
///  • FSM 상태는 CreateStates()를 오버라이드해 교체 가능.
/// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///
/// 새 몬스터 추가 방법:
///   1) LeeMonsterBase를 상속한 MonoBehaviour 클래스 작성
///   2) ConfigAddress 프로퍼티에 해당 SO의 Addressable 주소 반환
///   3) 빈 오브젝트에 해당 클래스 + Rigidbody + NavMeshAgent 추가
///   4) SO .asset 파일들 생성 후 Addressables 등록
/// </summary>
public abstract class LeeMonsterBase : MonoBehaviour, IDamageable
{
    // ── 추상 멤버 (파생 클래스가 구현) ────────────────────
    /// <summary>Addressables에 등록된 MonsterConfigSO 주소.</summary>
    protected abstract string ConfigAddress { get; }

    // ── 내부 필드 ─────────────────────────────────────────
    protected MonsterConfigSO       _config;
    protected LeeMonsterFSM         _fsm;
    protected LeeMonsterContext     _ctx;
    protected LeeMonsterRuntimeData _runtime;
    protected NavMeshAgent          _agent;
    protected Animator              _animator;

    // Inspector 디버그용 (ReadOnly 어트리뷰트가 있으면 [ReadOnly] 사용)
    [SerializeField] private string _debugState;
    private float _diagTimer;

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
            Debug.LogError($"[LeeMonsterBase] ConfigSO 로드 실패: {ConfigAddress}", this);
            return;
        }

        if (_config.stat == null || _config.detection == null ||
            _config.patrol == null || _config.combat == null || _config.animation == null)
        {
            Debug.LogError($"[LeeMonsterBase] ConfigSO '{ConfigAddress}' 의 서브 SO 중 null 항목이 있습니다.", this);
            return;
        }

        // 2. NavMeshAgent 설정
        _agent = GetComponent<NavMeshAgent>();
        if (_agent == null)
        {
            Debug.LogWarning("[LeeMonsterBase] NavMeshAgent가 없어 자동 추가합니다.", this);
            _agent = gameObject.AddComponent<NavMeshAgent>();
        }
        _agent.speed            = _config.stat.moveSpeed;
        _agent.stoppingDistance = _config.stat.attackRange * 0.9f;

        // 3. Animator 설정 (Addressables에서 AnimatorController 로드)
        _animator = GetComponentInChildren<Animator>();
        await LoadAnimatorControllerAsync();

        // 4. 런타임 데이터 초기화
        _runtime = new LeeMonsterRuntimeData
        {
            CurrentHp        = _config.stat.maxHp,
            SpawnPosition    = transform.position,
            PatrolDirection  = 1,
        };

        // 5. 컨텍스트 조립
        _ctx = new LeeMonsterContext
        {
            Monster   = this,
            Agent     = _agent,
            Animator  = _animator,
            Config    = _config,
            Runtime   = _runtime,
        };

        // 6. 플레이어 타깃 등록
        if (Managers.Player.PlayerTransform != null)
            _runtime.PlayerTarget = Managers.Player.PlayerTransform;
        else
            Managers.Player.OnPlayerSpawned += OnPlayerSpawned;

        // 7. FSM 초기화
        var states = CreateStates();
        _fsm = new LeeMonsterFSM(_ctx, states);
        _fsm.ChangeState(LeeMonsterStateType.Patrol);

        OnInitialized();
    }

    /// <summary>초기화 완료 후 파생 클래스에서 추가 처리가 필요한 경우 오버라이드.</summary>
    protected virtual void OnInitialized() { }

    /// <summary>
    /// FSM 상태 딕셔너리를 반환한다.
    /// 특정 상태만 교체하고 싶은 파생 클래스에서 오버라이드할 수 있다.
    /// </summary>
    protected virtual Dictionary<LeeMonsterStateType, ILeeMonsterState> CreateStates()
    {
        return new Dictionary<LeeMonsterStateType, ILeeMonsterState>
        {
            { LeeMonsterStateType.Patrol,      new LeePatrolState()      },
            { LeeMonsterStateType.Chase,       new LeeChaseState()       },
            { LeeMonsterStateType.AttackReady, new LeeAttackReadyState() },
            { LeeMonsterStateType.Attack,      new LeeAttackState()      },
            { LeeMonsterStateType.GetHit,      new LeeGetHitState()      },
            { LeeMonsterStateType.Die,         new LeeDieState()         },
        };
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
        _debugState = _fsm?.CurrentType.ToString();

        _diagTimer -= Time.deltaTime;
        if (_diagTimer <= 0f)
        {
            _diagTimer = 1f;
            Transform tgt = _runtime.PlayerTarget;
            float dist = tgt != null
                ? Vector3.Distance(transform.position, tgt.position)
                : -1f;
            Debug.Log(
                $"[FairyBat] State={_fsm?.CurrentType} | Target={tgt?.name ?? "NULL"}" +
                $" | Dist={dist:F1}" +
                $" | DetectRange={_config?.detection?.detectionRange}" +
                $" | IsPlayerDead={IsPlayerDead()}",
                this);
        }
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

        // 살아있는 플레이어 우선, 없으면 임의 PlayerController, 없으면 Managers 등록 값
        return closestAlive ?? closestAny ?? Managers.Player.PlayerTransform;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 공개 API (상태 클래스 → 몬스터 베이스 콜백)
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>FSM 상태 전환 요청.</summary>
    public void ChangeState(LeeMonsterStateType type) => _fsm?.ChangeState(type);

    /// <summary>
    /// 공격 히트 판정: 반경 내 플레이어에게 데미지 + 넉백 적용.
    /// LeeAttackState 또는 LeeMonsterAnimEventReceiver에서 호출.
    /// </summary>
    public void DealDamageToPlayer()
    {
        if (_runtime?.PlayerTarget == null || _config == null) return;

        // 레이어 마스크 대신 이미 캐싱된 PlayerTarget으로 거리 판정
        // → Player 레이어 설정 불필요
        float dist = Vector3.Distance(transform.position, _runtime.PlayerTarget.position);
        if (dist > _config.stat.attackRadius) return;

        var player = _runtime.PlayerTarget.GetComponent<PlayerController>();
        if (player == null) return;

        player.TakeDamage((int)_config.stat.attackPower);

        var rb = _runtime.PlayerTarget.GetComponent<Rigidbody>();
        if (rb != null)
        {
            Vector3 dir = (_runtime.PlayerTarget.position - transform.position).normalized;
            dir.y = 0.3f;
            rb.AddForce(dir.normalized * _config.stat.knockbackForce, ForceMode.Impulse);
        }
    }

    /// <summary>애니메이션 이벤트에서 호출 (LeeMonsterAnimEventReceiver 경유).</summary>
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

        // 방어력 적용 (최소 1 데미지)
        float actual = Mathf.Max(1f, amount - _config.stat.defense);
        _runtime.CurrentHp -= (int)actual;

        if (_runtime.CurrentHp <= 0)
        {
            _runtime.CurrentHp = 0;
            _runtime.IsDead    = true;
            ChangeState(LeeMonsterStateType.Die);  // Die 상태에서 Rigidbody 처리
        }
        else
        {
            // 생존 시에만 넉백 적용 (사망 타격에서 밀려나지 않도록)
            if (instigator != null)
            {
                var rb = GetComponent<Rigidbody>();
                if (rb != null)
                {
                    Vector3 dir = (transform.position - instigator.transform.position).normalized;
                    rb.AddForce(dir * 3f * knockbackMultiplier, ForceMode.Impulse);
                }
            }
            ChangeState(LeeMonsterStateType.GetHit);
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 내부 헬퍼
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private async UniTask LoadAnimatorControllerAsync()
    {
        if (_config.animation == null) return;
        string addr = _config.animation.animatorControllerAddress;
        if (string.IsNullOrEmpty(addr)) return;

        var overrideCtrl = await Managers.AddressableManager
            .LoadAssetAsync<AnimatorOverrideController>(addr);

        if (overrideCtrl == null || _animator == null) return;

        // Override Controller는 자체적으로 Base Controller를 참조하고 있으므로
        // 그대로 할당하면 Base의 모든 파라미터·전환을 유지하면서 클립만 교체됨
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
    // 에디터 Gizmo
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void OnDrawGizmosSelected()
    {
        if (_config == null) return;

        // 감지 거리 (노란색)
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, _config.detection.detectionRange);

        // 공격 사정거리 (빨간 선)
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, _config.stat.attackRange);

        // 공격 히트 범위 (반투명 빨간 구)
        Gizmos.color = new Color(1f, 0f, 0f, 0.25f);
        Gizmos.DrawSphere(transform.position, _config.stat.attackRadius);

        // 추격 포기 거리 (파란 선)
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, _config.detection.chaseGiveUpRange);

        // 스폰 위치 표시
        if (_runtime != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(_runtime.SpawnPosition, Vector3.one * 0.3f);
        }
    }
}
