using UnityEngine;
using Game.CharacterStates;
using Game.CharacterStates.MonsterControllerStates;
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

    protected IMonsterAIController aiController;

    protected virtual void Update()
    {
        aiController?.TickAI();
    }

    public void InitializeAI(IMonsterAIController controller)
    {
        aiController = controller;
        aiController.InitAI(this);
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

    public abstract void HandleAI(); // FSM/BT에서 override
}
