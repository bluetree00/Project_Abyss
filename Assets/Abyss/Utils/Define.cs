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
        Ground = 3,
        Block = 10,
    }

    public enum Scene
    {
        Unknown,
        Logo,
        Tutorial,
        BaseCamp,
        Lobby,
        StageMap,
        GameScene,
        GameScene_Ch1,
        GameScene_Ch2,
        GameScene_Ch3,
        GameScene_Ch4,
    }

    public enum Sound
    {
        Bgm,
        Effect,
        MaxCount,
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

    public enum CharacterClass
    {
        Knight,      // 레거시 — Default와 동일
        Mage,
        Hunter,
        Rogue,
        Guardian,
        Default,     // 기본 캐릭터
        Berserker,   // 광전사
    }

    public enum EquipmentSlotType
    {
        Weapon0,
        Weapon1,
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
        Basic_Knight_02,
        Basic_Knight_03,
        Basic_Bow_01,
        Sword_01,
        Bow_Elite,
        Staff_Legend,
        
        
    }

    // 무기 타입 — 실제 사용하는 enum은 WeaponType.cs (전역 네임스페이스).
    // 이 enum은 레거시이며 참조 코드가 없으므로 제거 예정.
    [System.Obsolete("Use global WeaponType enum instead")]
    public enum WeaponType
    {
        Sword,
        Bow,
        Staff,
        Dagger,
        Axe,
        BaseTest,
    }

    public enum PlayerAbilityType
    {
        Dodge,
        Attack,
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
        Bat,
        Orc,
        Slime,
        Snail,
        EarthGolem,
        SkeletonKnight,
        Witch,

    }

    public static readonly Dictionary<MonsterType, int> MonsterIdMap = new()
    {
        { MonsterType.Bat, 1001 },
        { MonsterType.Orc, 1002 },
        { MonsterType.Slime, 1003 },
        { MonsterType.Snail, 1005 },
        { MonsterType.EarthGolem, 1006 },
        { MonsterType.SkeletonKnight, 1007 },
        { MonsterType.Witch, 1004 }
    };

    public static int GetMonsterId(MonsterType type)
    {
        return MonsterIdMap.TryGetValue(type, out var id) ? id : -1;
    }

    public enum AttackStyle
    {
        Melee,
        Ranged,
        Magic,
    }

    public enum AttackPurpose
    {
        Normal01,
        Normal02,
        Normal03,
        Normal04,
        Normal05,
        Special,
        Ultimate,
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
