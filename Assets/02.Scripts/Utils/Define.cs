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

    public enum AugmentGrade
    {
        Common,
        Rare,
        Unique
    }

    public enum CharacterClass  // {Test} 각 캐릭터의 클래스를 알아보기 위한 클래스 Enum
    {     
        Knight,
        Mage,
        Hunter,
        Rogue,
        Guardian,
    }

    public static string GetCharacterClassString(string characterName)
    {
        switch (characterName)
        {
            case "Character_01":
                return "Knight_container";
            case "Mage":
                return "Mage_container";
            case "Hunter":
                return "Hunter_container";
            case "Rogue":
                return "Rogue_container";
            case "Guardian":
                return "Guardian_container";
            default:
                return "Unknown";
        }
    }

   
    public enum State //상태
    {
        //기본적으로 사용하는 상태
        Die,
        Idle,
        Moving,
        Runing,
        Dodge,
        ChangeWeapon,
        NormalAttack_01,
        NormalAttack_02,
        NormalAttack_03,
        NormalAttack_04,
        NormalAttack_05,
        JumpAttack,

        NormalSkill_01,
        UltimateSkill_01,

        // 무기와 일반 Idle 상태 분리

        currentWeaponIdle,
        
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

    public enum ShapeType
    {
        Unknown,
        Apple,
        Banana,
        Orange,
        Cherry,
        Grape,
        Pear
    }


    // 그리드 형태를 정의하는 배열들
    public static readonly int[,] AppleShape = new int[8, 8]
    {
        {0, 0, 0, 1, 1, 0, 0, 0},
        {0, 0, 1, 1, 1, 1, 0, 0},
        {0, 1, 1, 1, 1, 1, 1, 0},
        {0, 1, 1, 1, 1, 1, 1, 0},
        {0, 0, 1, 1, 1, 1, 0, 0},
        {0, 0, 0, 1, 1, 0, 0, 0},
        {0, 0, 0, 0, 0, 0, 0, 0},
        {0, 0, 0, 0, 0, 0, 0, 0}
    };

    public static readonly int[,] BananaShape = new int[8, 8] // 예시: 다른 모양도 추가
    {
        {0, 0, 1, 1, 1, 0, 0, 0},
        {0, 1, 1, 1, 1, 1, 0, 0},
        {1, 1, 1, 1, 1, 1, 1, 0},
        {0, 1, 1, 1, 1, 1, 1, 0},
        {0, 0, 1, 1, 1, 1, 0, 0},
        {0, 0, 1, 1, 1, 0, 0, 0},
        {0, 0, 0, 0, 0, 0, 0, 0},
        {0, 0, 0, 0, 1, 1, 1, 0}
    };

    // 모양을 불러오는 함수 (필요한 경우)
    public static int[,] GetShapeGrid(ShapeType shapeType)
    {
        switch (shapeType)
        {
            case ShapeType.Apple:
                return AppleShape;
            case ShapeType.Banana:
                return BananaShape;
            default:
                return new int[8, 8]; // 기본적으로 빈 배열을 반환
        }
    }

}