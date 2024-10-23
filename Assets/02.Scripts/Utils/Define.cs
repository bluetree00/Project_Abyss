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

   // 이펙트 이름을 키로, 타격 간격을 값으로 갖는 Dictionary 이펙트 마다 타격 간격을 정함.
   // 사용 방법 예시) float 사용할변수 = Define.HitIntervals["ShinySlash"];
    public static Dictionary<string, float> HitIntervals = new Dictionary<string, float>
    {
        { "ShinySlash", 1f },    // ShinySlash의 타격 간격 0.5초
        { "Fireball", 1.0f },      // Fireball의 타격 간격 1.0초
        { "LightningStrike", 0.7f } // LightningStrike의 타격 간격 0.7초
    };

    public static Dictionary<string, bool> HitCooldowns = new Dictionary<string, bool>
    {
        { "ShinySlash", true },    // ShinySlash의 공격 가능 체크


    };

    

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
        Hit,
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