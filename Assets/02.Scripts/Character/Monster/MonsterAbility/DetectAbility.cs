using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DetectAbility : IMonsterAbility
{
    public Define.AbilityType Type => Define.AbilityType.Detect;  // 이 어빌리티의 타입을 명시

    private float range;
    private MonsterController owner;

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
        Transform player = Managers.Player.PlayerTransform;

        if (player == null)
        {
            owner.SetDetected(false);
            return;
        }

        float distance = Vector3.Distance(owner.transform.position, player.position);
        if (distance < range)
        {
            Debug.Log("Player detected!");
            owner.SetDetected(true);
        }
        else
        {
            owner.SetDetected(false);
        }
    }
}
