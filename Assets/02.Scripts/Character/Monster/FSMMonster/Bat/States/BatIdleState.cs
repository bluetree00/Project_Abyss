using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BatIdleState : IMonsterState
{
    public void Enter()
    {
        throw new System.NotImplementedException();
    }

    public void Exit()
    {
        throw new System.NotImplementedException();
    }

    public void Init(MonsterController controller)
    {
        throw new System.NotImplementedException();
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    MonsterController.MonsterState IMonsterState.Update()
    {
        throw new System.NotImplementedException();
    }
}
