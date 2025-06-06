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

    public float detectionRange = 10f;
    public float attackRange = 2f;
    public NavMeshAgent agent;
    public Animator animator;

    protected override async Task InitAsync()
    {
        await base.InitAsync();
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();
    }

    public abstract void HandleAI();
}
