using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FSMMonsterAIController : IMonsterAIController
{
    private MonsterController monster;
    private MonsterController.MonsterState currentState;

    public void InitAI(MonsterController monster)
    {
        this.monster = monster;
        currentState = MonsterController.MonsterState.Idle;
    }

    public void TickAI()
    {
        switch (currentState)
        {
            case MonsterController.MonsterState.Idle:
                HandleIdle();
                break;
            case MonsterController.MonsterState.Patrol:
                HandlePatrol();
                break;
            // ... 다른 상태
        }
    }

    public void OnEnterCombat() { currentState = MonsterController.MonsterState.Chase; }
    public void OnExitCombat() { currentState = MonsterController.MonsterState.Patrol; }

    private void HandleIdle() { /* 로직 */ }
    private void HandlePatrol() { /* 로직 */ }
}
