using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AttackAbility : IMonsterAbility
{
    private MonsterController owner;
    
    public void Execute()
    {
        throw new System.NotImplementedException();
    }

      public void Init(MonsterController owner)
    {
        this.owner = owner;
    }

   
}
