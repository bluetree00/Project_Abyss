using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DetectAbility : IMonsterAbility
{
    private float range;
    private MonsterController owner;

    public Define.MonsterAbilityType Type => Define.MonsterAbilityType.Detect;

    public DetectAbility(float range)
    {
        this.range = range;
    }

    public void Init(MonsterController owner)
    {
        this.owner = owner;
    }

    public void Execute()
    {
        Transform player = owner.playerTarget;

        if (player == null)
        {
            owner.SetDetected(false);
            return;
        }

        float distance = Vector3.Distance(owner.transform.position, player.position);
        owner.SetDetected(distance < range);
    }
}
