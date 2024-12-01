using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.VisualScripting;
using UnityEngine;
public class Define
{

    public enum Layer
    {
        Monster = 8,
        Ground = 9,
        Block = 10,
    }

    public enum Scene
    {
        Unknown,
        Login,
        Lobby,
        Game,
    }

    public enum Sound
    {


    }
    
    public enum WorldObject
    {
        Unknown,
        Player,
        Monster,
    }

    public enum WorldObjectUI
    {
        HPBar,
        PostureBar,
    }

    public enum PopupType
    {
        None,            // 기본값
        Settings,        // 설정 팝업
        Inventory,       // 인벤토리 팝업
        Warning,         // 경고 팝업
        Confirmation,    // 확인 팝업
        Tutorial,        // 튜토리얼 팝업
    }

    public enum UIEvent
    {
        Click,
        Drag,
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
        Hit,
        NormalAttack_01,
        NormalAttack_02,
        NormalAttack_03,
        NormalAttack_04,
        NormalAttack_05,

        NormalSkile_01,
        UltimateSkile_01,
        
    }

    public enum MouseEvent
    {
        Press,
        PointerDown,
        PointerUp,
        Click,
    }

}