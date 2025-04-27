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
        // 사운드 관련 식별자들을 추가
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

    public enum CharacterClass  // 각 캐릭터의 클래스를 알아보기 위한 Enum
    {     
        Knight,
        Mage,
        Hunter,
        Rogue,
        Guardian,
    }

    public enum EquipmentSlotType
    {
        Weapon,
        SubWeapon,
        Armor,
        Helmet,
        Gloves,
        Boots,
        Ring,
        Necklace
    }

    // 무기 등급
    public enum WeaponRarity
    {
        Common,
        Rare,
        Epic,
        Legendary
    }

    public enum WeaponPrefabKey
    {
        basic_Knight_02,
        Sword_01,
        Bow_Elite,
        Staff_Legend,
        
        
    }

     // 무기 타입
    public enum WeaponType
    {
        Sword,
        Bow,
        Staff,
        Dagger,
        Axe
    }

    public static class WeaponTypeStrings
    {
        public static readonly Dictionary<WeaponType, string> WeaponTypeMap = new Dictionary<WeaponType, string>
        {
            { WeaponType.Sword, "Sword" },
            { WeaponType.Bow, "Bow" },
            { WeaponType.Staff, "Staff" },
            { WeaponType.Dagger, "Dagger" },
            { WeaponType.Axe, "Axe" }
        };

        public static string GetWeaponTypeString(WeaponType weaponType)
        {
            return WeaponTypeMap[weaponType];
        }
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
   
    public enum State // 상태
    {
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
        currentWeaponIdle,
    }

    public enum MonsterState // 몬스터 상태
    {
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

    // MonsterType 식별자를
    public enum MonsterType
    {
        EvilMage,
        Orc,
        Slime,
        Specter
    }

    // 그리드 형태를 정의하는 배열들
    public static readonly int[,] AppleShape = new int[10, 10]
    {
        {0, 0, 0, 0, 1, 1, 0, 0, 0, 0},
        {0, 0, 0, 1, 1, 1, 1, 0, 0, 0},
        {0, 0, 1, 1, 1, 1, 1, 1, 0, 0},
        {0, 1, 1, 1, 1, 1, 1, 1, 1, 0},
        {0, 1, 1, 1, 1, 1, 1, 1, 1, 0},
        {0, 0, 1, 1, 1, 1, 1, 1, 0, 0},
        {0, 0, 0, 1, 1, 1, 1, 0, 0, 0},
        {0, 0, 0, 0, 1, 1, 0, 0, 0, 0},
        {0, 0, 0, 0, 0, 0, 0, 0, 0, 0},
        {0, 0, 0, 0, 0, 0, 0, 0, 0, 0}
    };

    public static readonly int[,] BananaShape = new int[10, 10]
    {
        {1, 1, 1, 0, 0, 0, 0, 1, 1, 1},
        {0, 1, 0, 0, 0, 0, 0, 0, 1, 0},
        {0, 0, 1, 0, 1, 0, 1, 0, 0, 0},
        {0, 0, 0, 1, 1, 1, 1, 1, 0, 0},
        {0, 0, 1, 1, 1, 1, 1, 0, 0, 0},
        {0, 0, 0, 1, 1, 1, 1, 1, 0, 0},
        {0, 0, 1, 1, 1, 1, 1, 0, 0, 0},
        {0, 0, 0, 1, 0, 1, 0, 1, 0, 0},
        {0, 1, 0, 0, 0, 0, 0, 0, 1, 0},
        {1, 1, 1, 0, 0, 0, 0, 1, 1, 1}
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
                return new int[10, 10]; // 기본적으로 빈 배열을 반환
        }
    }
}
