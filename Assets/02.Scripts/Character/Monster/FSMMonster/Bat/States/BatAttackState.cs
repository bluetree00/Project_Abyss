using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BatAttackState : IMonsterState
{
     private MonsterController controller;
    private IMonsterStateChanger stateChanger;


    public void Init(MonsterController controller, IMonsterStateChanger stateChanger)
    {
        this.controller = controller;
        this.stateChanger = stateChanger;
       
    }

    public void Enter()
    {
        Debug.Log("Bat Attack State Entered");
        controller.Anim.CrossFade("NormalAttack_1", 1f);
    }

    public void Exit()
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

    MonsterController.MonsterState IMonsterState.Update()
    {
        throw new System.NotImplementedException();
    }
}
