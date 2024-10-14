using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.VisualScripting;
using UnityEngine;
public class Define
{

    public enum Scene
    {
        
    }

    public enum Sound
    {


    }
    public enum WorldObject
    {

    }

    public enum WorldObjectUI
    {

    }

    public enum State //상태
    {
        //기본적으로 사용하는 상태
        Die,
        Idle,
        Moving,
        Runing,
        Dodge,
        NormalAttack_01,
        NormalAttack_02,
        NormalAttack_03,
        NormalAttack_04,
        NormalAttack_05,

        NormalSkile_01,
        UltimateSkile_01,
        
    }

    public enum MonsterState //몬스터 상태
    {
        //기본적으로 사용하는 상태
        Die,
        Idle,
        Moving,
        Runing,
        Dodge,
        NormalAttack_01,
        NormalAttack_02,
        NormalAttack_03,
        NormalAttack_04,
        NormalAttack_05,

        NormalSkile_01,
        UltimateSkile_01,
        
    }




    public enum UIEvent
    {

    }

    public enum MouseEvent
    {
        Press,
        Click,
    }

    public enum cameraMode
    {
        QuarterView,
    }
}