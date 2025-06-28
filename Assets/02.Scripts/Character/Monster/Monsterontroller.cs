using UnityEngine;
using UnityEngine.AI;
using Cysharp.Threading.Tasks;

public abstract class MonsterController : CharacterBase
{
    public abstract Define.MonsterType Type { get; }

    public enum MonsterState
    {
        None,
        Idle,
        Patrol,
        Chase,
        Attack,
        AttackReady,
        Die
    }

    [SerializeField] private MonsterAbilitySetSO abilitySetSO;

    [SerializeField] private MonsterEffectProfileSO effectProfile;
    public MonsterEffectProfileSO EffectProfile => effectProfile;


    public MonsterAbilitySetSO AbilitySet { get; private set; }

    public bool HasDetectedTarget { get; private set; }

    public bool IsInAttackRange { get; private set; }

    public bool IsAttacking { get; private set; }

    public Transform playerTarget;

    public void SetDetected(bool detected)
    {
        HasDetectedTarget = detected;
    }

    public void SetInAttackRange(bool inRange)
    {
       IsInAttackRange = inRange;
    }

    public void SetAttack(bool isAttacking)
    {
       IsAttacking = isAttacking;
    }
    
    public NavMeshAgent agent;
    public Animator animator;

    protected override async UniTask InitAsync()
    {
        await base.InitAsync();
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();

        //몬스터 타입에 맞는 자동 풀 키 생성
        string effectPoolKey = $"{Type}EffectPool";
        Debug.Log($"[Monster Init] 자동 풀 키: {effectPoolKey}");
        await Managers.Instance.InitializeObjectPoolAsync(effectPoolKey);


        AbilitySet = Instantiate(abilitySetSO); // 또는 abilitySetSO 사용
        AbilitySet.InitAbilities(this);
        

    }

    private void OnEnable()
    {
        // 매니저에 등록된 플레이어를 찾아옴
        if (Managers.Player.PlayerTransform != null)
        {
            SetTarget(Managers.Player.PlayerTransform);
        }
        else
            Managers.Player.OnPlayerSpawned += SetTarget; // 플레이어가 등록될때 이벤트로 플레이어를 찾아옴
    }

    private void SetTarget(Transform player)
    {
        playerTarget = player;

        if (Managers.Player != null)
            Managers.Player.OnPlayerSpawned -= SetTarget;
    }

    public void MoveTo(Vector3 destination)
    {
        if (agent != null && agent.isActiveAndEnabled)
        {
            agent.SetDestination(destination);
        }
    }

    public void StopMoving()
    {
        if (agent != null && agent.isActiveAndEnabled)
        {
            agent.ResetPath();
        }
    }

    protected override void Update()
    {
        base.Update();
        HandleAI();
    }



    public abstract void HandleAI();
}
