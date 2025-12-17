using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BatDieState : IMonsterState
{
    public void Enter()
    {
   
    }

    public void Exit()
    {
       
    }

    public void Init(MonsterController controller)
    {
       
    }

    public void Init(MonsterController controller, IMonsterStateChanger stateChanger)
    {
     
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    MonsterController.MonsterState IMonsterState.StateUpdate()
    {
        Debug.Log("BatDieState Update");
        throw new System.NotImplementedException();
    }
}
