using UnityEngine;
using UnityEngine.AI;
using System.Threading.Tasks;

public abstract class MonsterController : CharacterBase
{
    public enum MonsterState
    {
        Idle,
        Patrol,
        Chase,
        Attack,
        Die
    }

    [SerializeField] private MonsterAbilitySetSO abilitySetSO;

    public MonsterAbilitySetSO AbilitySet { get; private set; }

    public bool HasDetectedTarget { get; private set; }

    public void SetDetected(bool detected)
    {
        HasDetectedTarget = detected;
    }

    public float detectionRange = 10f;
    public float attackRange = 2f;
    public NavMeshAgent agent;
    public Animator animator;

    protected override async Task InitAsync()
    {
        await base.InitAsync();
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();

        
        AbilitySet = Instantiate(abilitySetSO); // 또는 abilitySetSO 사용
        AbilitySet.InitAbilities(this);
    }

    public void MoveTo(Vector3 destination)
    {
        if (agent != null && agent.isActiveAndEnabled)
        {
            agent.SetDestination(destination);
            animator?.SetBool("isMoving", true); // 필요시 애니메이션 제어
        }
    }

    public void StopMoving()
    {
        if (agent != null && agent.isActiveAndEnabled)
        {
            agent.ResetPath();
            animator?.SetBool("isMoving", false); // 정지 애니메이션
        }
    }



    public abstract void HandleAI();
}
