using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BTMonsterAIController : IMonsterAIController
{
    private MonsterController monster;
    private BTNode rootNode;

    public void InitAI(MonsterController monster)
    {
        this.monster = monster;
        rootNode = ConstructBehaviorTree();
    }

    public void TickAI()
    {
        rootNode?.Evaluate();
    }

    public void OnEnterCombat() { /* 트리 조건 변경 등 */ }
    public void OnExitCombat() { /* 상태 리셋 등 */ }

    private BTNode ConstructBehaviorTree()
    {
        return new SelectorNode(
            new ConditionNode(IsPlayerInRange),
            new SequenceNode(
                new ActionNode(MoveToPlayer),
                new ActionNode(AttackPlayer)
            )
        );
    }

    private bool IsPlayerInRange() { return false; }
    private BTNode.Result MoveToPlayer() { return BTNode.Result.Success; }
    private BTNode.Result AttackPlayer() { return BTNode.Result.Success; }
}
