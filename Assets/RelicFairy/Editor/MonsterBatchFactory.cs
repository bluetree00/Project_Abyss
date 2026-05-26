#if UNITY_EDITOR
using System;
using System.IO;
using System.Collections.Generic;
using RelicFairy.Monster;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 신규 몬스터 25마리 프리팹 + Config.asset 일괄 생성 팩토리.
/// RelicFairy > Tools > Create All New Monster Prefabs & Configs 실행.
/// </summary>
public static class MonsterBatchFactory
{
    // ── 데이터 구조 ──────────────────────────────────────────────

    private struct MonsterData
    {
        public string name;           // 폴더명 / 프리팹명 / Config명
        public string thirdPartyPath; // ThirdParty MaskTint 프리팹 경로
        public string typeName;       // MonsterBase 서브클래스 full type name
        public int    grade;          // 0=Normal 1=Elite 2=Hard
        // Stats
        public float maxHp, defense, atk, speed;
        public float atkRange, atkRadius, atkRate, atkDelay, knockback;
        // Detection
        public float detRange, chaseGiveUp;
        // Patrol
        public float patrolRange, patrolSpeed, waitTime;
        // Animation
        public string idle, patrol, chase, atkReady, atkAnim, getHit, die, detect;
    }

    // ── 몬스터 데이터 테이블 ─────────────────────────────────────

    private static readonly MonsterData[] Monsters = new MonsterData[]
    {
        // ── Wave01 ──────────────────────────────────────────────
        new MonsterData {
            name="Spider",
            thirdPartyPath="Assets/_ThirdParty/RPGMonsterBundlePolyart/CommonStuffs/Prefab/Wave01/CharacterMaskTint/SpiderPAMaskTint.prefab",
            typeName="RelicFairy.Monster.SpiderMonster", grade=0,
            maxHp=30,defense=1,atk=10,speed=4.5f,atkRange=1.5f,atkRadius=1.5f,atkRate=1.2f,atkDelay=0.3f,knockback=3f,
            detRange=6,chaseGiveUp=10,patrolRange=4,patrolSpeed=2f,waitTime=1f,
            idle="IdleNormal",patrol="WalkFWD",chase="RunFWD",atkReady="IdleBattle",atkAnim="Attack01",getHit="GetHit",die="Die",detect="SenseSomethingST"
        },
        new MonsterData {
            name="MonsterPlant",
            thirdPartyPath="Assets/_ThirdParty/RPGMonsterBundlePolyart/CommonStuffs/Prefab/Wave01/CharacterMaskTint/MonsterPAMaskTint.prefab",
            typeName="RelicFairy.Monster.MonsterPlantMonster", grade=0,
            maxHp=70,defense=3,atk=12,speed=2.0f,atkRange=1.8f,atkRadius=1.8f,atkRate=0.8f,atkDelay=0.4f,knockback=4f,
            detRange=6,chaseGiveUp=10,patrolRange=4,patrolSpeed=1.2f,waitTime=1.5f,
            idle="IdleNormal",patrol="WalkFWD",chase="RunFWD",atkReady="IdleBattle",atkAnim="Attack01",getHit="GetHit",die="Die",detect="SenseSomethingST"
        },
        new MonsterData {
            name="EvilMage",
            thirdPartyPath="Assets/_ThirdParty/RPGMonsterBundlePolyart/CommonStuffs/Prefab/Wave01/CharacterMaskTint/EvilMagePAMaskTint.prefab",
            typeName="RelicFairy.Monster.EvilMageMonster", grade=1,
            maxHp=50,defense=1,atk=20,speed=2.5f,atkRange=8.0f,atkRadius=0.5f,atkRate=0.5f,atkDelay=0.6f,knockback=5f,
            detRange=10,chaseGiveUp=14,patrolRange=4,patrolSpeed=1.5f,waitTime=1f,
            idle="IdleNormal",patrol="WalkFWD",chase="RunFWD",atkReady="IdleBattle",atkAnim="Attack01",getHit="GetHit",die="Die",detect="SenseSomethingST"
        },
        new MonsterData {
            name="Orc",
            thirdPartyPath="Assets/_ThirdParty/RPGMonsterBundlePolyart/CommonStuffs/Prefab/Wave01/CharacterMaskTint/OrcPAMaskTint.prefab",
            typeName="RelicFairy.Monster.OrcMonster", grade=0,
            maxHp=80,defense=3,atk=18,speed=2.5f,atkRange=2.0f,atkRadius=2.0f,atkRate=0.8f,atkDelay=0.5f,knockback=7f,
            detRange=7,chaseGiveUp=11,patrolRange=5,patrolSpeed=1.5f,waitTime=1f,
            idle="IdleNormal",patrol="WalkFWD",chase="RunFWD",atkReady="IdleBattle",atkAnim="Attack01",getHit="GetHit",die="Die",detect="SenseSomethingST"
        },
        new MonsterData {
            name="Dragon",
            thirdPartyPath="Assets/_ThirdParty/RPGMonsterBundlePolyart/CommonStuffs/Prefab/Wave01/CharacterMaskTint/DragonPAMaskTint.prefab",
            typeName="RelicFairy.Monster.DragonMonster", grade=2,
            maxHp=400,defense=5,atk=35,speed=4.0f,atkRange=3.0f,atkRadius=3.0f,atkRate=0.5f,atkDelay=0.6f,knockback=12f,
            detRange=12,chaseGiveUp=18,patrolRange=8,patrolSpeed=2.5f,waitTime=0.5f,
            idle="IdleNormal",patrol="FlyFWD",chase="FlyFWD",atkReady="IdleBattle",atkAnim="Attack01",getHit="GetHit",die="Die",detect="SenseSomethingST"
        },

        // ── Wave02 ──────────────────────────────────────────────
        new MonsterData {
            name="Beholder",
            thirdPartyPath="Assets/_ThirdParty/RPGMonsterBundlePolyart/CommonStuffs/Prefab/Wave02/CharacterMaskTint/BeholderPAMaskTint.prefab",
            typeName="RelicFairy.Monster.BeholderMonster", grade=1,
            maxHp=80,defense=2,atk=15,speed=2.5f,atkRange=2.0f,atkRadius=2.0f,atkRate=0.7f,atkDelay=0.4f,knockback=5f,
            detRange=8,chaseGiveUp=12,patrolRange=4,patrolSpeed=1.5f,waitTime=1f,
            idle="IdleNormal",patrol="WalkFWD",chase="Run",atkReady="IdleBattle",atkAnim="Attack01",getHit="GetHit",die="Die",detect="SenseSomethingST"
        },
        new MonsterData {
            name="ChestMonster",
            thirdPartyPath="Assets/_ThirdParty/RPGMonsterBundlePolyart/CommonStuffs/Prefab/Wave02/CharacterMaskTint/ChestMonsterPAMaskTint.prefab",
            typeName="RelicFairy.Monster.ChestMonster", grade=1,
            maxHp=100,defense=3,atk=30,speed=2.5f,atkRange=2.0f,atkRadius=2.0f,atkRate=0.6f,atkDelay=0.5f,knockback=8f,
            detRange=8,chaseGiveUp=12,patrolRange=4,patrolSpeed=1.5f,waitTime=1f,
            idle="IdleChest",patrol="WalkFWD",chase="Run",atkReady="IdleBattle",atkAnim="Attack01",getHit="GetHit",die="Die",detect="SenseSomethingST"
        },
        new MonsterData {
            name="CrabMonster",
            thirdPartyPath="Assets/_ThirdParty/RPGMonsterBundlePolyart/CommonStuffs/Prefab/Wave02/CharacterMaskTint/CrabMonsterPAMaskTint.prefab",
            typeName="RelicFairy.Monster.CrabMonster", grade=1,
            maxHp=150,defense=6,atk=15,speed=1.5f,atkRange=1.8f,atkRadius=1.8f,atkRate=0.6f,atkDelay=0.5f,knockback=6f,
            detRange=6,chaseGiveUp=10,patrolRange=4,patrolSpeed=1.0f,waitTime=1.5f,
            idle="IdleNormal",patrol="WalkFWD",chase="Run",atkReady="IdleBattle",atkAnim="Attack01",getHit="GetHit",die="Die",detect="SenseSomethingST"
        },
        new MonsterData {
            name="FlyingDemon",
            thirdPartyPath="Assets/_ThirdParty/RPGMonsterBundlePolyart/CommonStuffs/Prefab/Wave02/CharacterMaskTint/FylingDemonPAMaskTint.prefab",
            typeName="RelicFairy.Monster.FlyingDemonMonster", grade=1,
            maxHp=90,defense=2,atk=20,speed=4.5f,atkRange=1.8f,atkRadius=1.8f,atkRate=1.0f,atkDelay=0.3f,knockback=6f,
            detRange=9,chaseGiveUp=14,patrolRange=6,patrolSpeed=2.5f,waitTime=0.5f,
            idle="IdleNormal",patrol="WalkFWD",chase="Run",atkReady="IdleBattle",atkAnim="Attack01",getHit="GetHit",die="Die",detect="SenseSomethingST"
        },
        new MonsterData {
            name="LizardWarrior",
            thirdPartyPath="Assets/_ThirdParty/RPGMonsterBundlePolyart/CommonStuffs/Prefab/Wave02/CharacterMaskTint/LizardWarriorPAMaskTint.prefab",
            typeName="RelicFairy.Monster.LizardWarriorMonster", grade=1,
            maxHp=100,defense=4,atk=20,speed=3.0f,atkRange=2.0f,atkRadius=2.0f,atkRate=0.7f,atkDelay=0.4f,knockback=6f,
            detRange=8,chaseGiveUp=12,patrolRange=5,patrolSpeed=1.8f,waitTime=1f,
            idle="IdleNormal",patrol="WalkFWD",chase="Run",atkReady="IdleBattle",atkAnim="Attack01",getHit="GetHit",die="Die",detect="SenseSomethingST"
        },
        new MonsterData {
            name="RatAssassin",
            thirdPartyPath="Assets/_ThirdParty/RPGMonsterBundlePolyart/CommonStuffs/Prefab/Wave02/CharacterMaskTint/RatAssassinPAMaskTint.prefab",
            typeName="RelicFairy.Monster.RatAssassinMonster", grade=0,
            maxHp=25,defense=1,atk=15,speed=5.0f,atkRange=1.5f,atkRadius=1.5f,atkRate=1.5f,atkDelay=0.2f,knockback=3f,
            detRange=8,chaseGiveUp=12,patrolRange=5,patrolSpeed=3f,waitTime=0.5f,
            idle="IdleNormal",patrol="Walk",chase="Run",atkReady="IdleBattle",atkAnim="Attack01",getHit="GetHit",die="Die",detect="SenseSomethingST"
        },
        new MonsterData {
            name="Specter",
            thirdPartyPath="Assets/_ThirdParty/RPGMonsterBundlePolyart/CommonStuffs/Prefab/Wave02/CharacterMaskTint/SpecterPAMaskTint.prefab",
            typeName="RelicFairy.Monster.SpecterMonster", grade=1,
            maxHp=70,defense=1,atk=18,speed=4.0f,atkRange=1.8f,atkRadius=1.8f,atkRate=0.8f,atkDelay=0.3f,knockback=5f,
            detRange=9,chaseGiveUp=13,patrolRange=5,patrolSpeed=2f,waitTime=0.5f,
            idle="IdleNormal",patrol="WalkFWD",chase="Run",atkReady="IdleBattle",atkAnim="Attack01",getHit="GetHit",die="Die",detect="SenseSomethingST"
        },
        new MonsterData {
            name="Werewolf",
            thirdPartyPath="Assets/_ThirdParty/RPGMonsterBundlePolyart/CommonStuffs/Prefab/Wave02/CharacterMaskTint/WerewolfPAMaskTint.prefab",
            typeName="RelicFairy.Monster.WerewolfMonster", grade=1,
            maxHp=110,defense=2,atk=25,speed=4.0f,atkRange=1.8f,atkRadius=1.8f,atkRate=0.9f,atkDelay=0.4f,knockback=7f,
            detRange=9,chaseGiveUp=13,patrolRange=5,patrolSpeed=2.2f,waitTime=0.8f,
            idle="IdleNormal",patrol="WalkFWD",chase="Run",atkReady="IdleBattle",atkAnim="Attack01",getHit="GetHit",die="Die",detect="SenseSomethingST"
        },
        new MonsterData {
            name="WormMonster",
            thirdPartyPath="Assets/_ThirdParty/RPGMonsterBundlePolyart/CommonStuffs/Prefab/Wave02/CharacterMaskTint/WormMonsterPAMaskTint.prefab",
            typeName="RelicFairy.Monster.WormMonster", grade=0,
            maxHp=60,defense=2,atk=20,speed=0f,atkRange=2.0f,atkRadius=2.0f,atkRate=1.0f,atkDelay=0.4f,knockback=5f,
            detRange=5,chaseGiveUp=8,patrolRange=2,patrolSpeed=0f,waitTime=2f,
            idle="IdleNormal",patrol="IdleNormal",chase="IdleNormal",atkReady="IdleBattle",atkAnim="Attack01",getHit="GetHit",die="Die",detect="SenseSomethingST"
        },

        // ── Wave03 ──────────────────────────────────────────────
        new MonsterData {
            name="BattleBee",
            thirdPartyPath="Assets/_ThirdParty/RPGMonsterBundlePolyart/CommonStuffs/Prefab/Wave03/CharacterMaskTint/BattleBeePAMaskTint.prefab",
            typeName="RelicFairy.Monster.BattleBeeMonster", grade=0,
            maxHp=20,defense=1,atk=10,speed=5.5f,atkRange=1.5f,atkRadius=1.5f,atkRate=1.2f,atkDelay=0.2f,knockback=3f,
            detRange=7,chaseGiveUp=11,patrolRange=5,patrolSpeed=3f,waitTime=0.5f,
            idle="BattleBee_IdleNormal",patrol="BattleBee_FlyFWD",chase="BattleBee_FlyFWDFast",atkReady="BattleBee_IdleBattle",atkAnim="BattleBee_Attack01",getHit="BattleBee_GetHit",die="BattleBee_Die",detect="BattleBee_SenseSomethingStart"
        },
        new MonsterData {
            name="BishopKnight",
            thirdPartyPath="Assets/_ThirdParty/RPGMonsterBundlePolyart/CommonStuffs/Prefab/Wave03/CharacterMaskTint/BishopKnightPAMaskTint.prefab",
            typeName="RelicFairy.Monster.BishopKnightMonster", grade=1,
            maxHp=130,defense=5,atk=22,speed=2.5f,atkRange=2.0f,atkRadius=2.0f,atkRate=0.7f,atkDelay=0.5f,knockback=6f,
            detRange=7,chaseGiveUp=11,patrolRange=4,patrolSpeed=1.5f,waitTime=1f,
            idle="BishopKnight_IdleNormal",patrol="BishopKnight_WalkFWD",chase="BishopKnight_RunFWD",atkReady="BishopKnight_IdleBattle",atkAnim="BishopKnight_Attack01",getHit="BishopKnight_GetHit",die="BishopKnight_Die",detect="BishopKnight_SenseSomethingStart"
        },
        new MonsterData {
            name="Cactus",
            thirdPartyPath="Assets/_ThirdParty/RPGMonsterBundlePolyart/CommonStuffs/Prefab/Wave03/CharacterMaskTint/CactusPAMaskTint.prefab",
            typeName="RelicFairy.Monster.CactusMonster", grade=0,
            maxHp=90,defense=5,atk=14,speed=1.5f,atkRange=1.5f,atkRadius=1.5f,atkRate=0.8f,atkDelay=0.4f,knockback=4f,
            detRange=5,chaseGiveUp=9,patrolRange=3,patrolSpeed=1f,waitTime=2f,
            idle="Cactus_IdlePlant",patrol="Cactus_WalkFWD",chase="Cactus_RunFWD",atkReady="Cactus_IdleBattle",atkAnim="Cactus_Attack01",getHit="Cactus_GetHit",die="Cactus_Die",detect="Cactus_IdlePlantToBattle"
        },
        new MonsterData {
            name="Cyclops",
            thirdPartyPath="Assets/_ThirdParty/RPGMonsterBundlePolyart/CommonStuffs/Prefab/Wave03/CharacterMaskTint/CyclopsPAMaskTint.prefab",
            typeName="RelicFairy.Monster.CyclopsMonster", grade=1,
            maxHp=120,defense=4,atk=25,speed=2.0f,atkRange=10.0f,atkRadius=0.5f,atkRate=0.4f,atkDelay=0.7f,knockback=8f,
            detRange=12,chaseGiveUp=16,patrolRange=5,patrolSpeed=1.5f,waitTime=1f,
            idle="Cyclops_IdleNormal",patrol="Cyclops_WalkFWD",chase="Cyclops_RunFWD",atkReady="Cyclops_IdleBattle",atkAnim="Cyclops_Attack01",getHit="Cyclops_GetHit",die="Cyclops_Die",detect="Cyclops_SenseSomethingStart"
        },
        new MonsterData {
            name="DemonKing",
            thirdPartyPath="Assets/_ThirdParty/RPGMonsterBundlePolyart/CommonStuffs/Prefab/Wave03/CharacterMaskTint/DemonKingPAMaskTint.prefab",
            typeName="RelicFairy.Monster.DemonKingMonster", grade=2,
            maxHp=350,defense=6,atk=40,speed=3.5f,atkRange=2.5f,atkRadius=2.5f,atkRate=0.5f,atkDelay=0.6f,knockback=12f,
            detRange=12,chaseGiveUp=18,patrolRange=7,patrolSpeed=2f,waitTime=0.5f,
            idle="DemonKing_IdleNormal",patrol="DemonKing_WalkFWD",chase="DemonKing_RunFWD",atkReady="DemonKing_IdleBattle",atkAnim="DemonKing_Attack01",getHit="DemonKing_GetHit",die="DemonKing_Die",detect="DemonKing_SenseSomethingStart"
        },
        new MonsterData {
            name="Fishman",
            thirdPartyPath="Assets/_ThirdParty/RPGMonsterBundlePolyart/CommonStuffs/Prefab/Wave03/CharacterMaskTint/FishmanPAMaskTint.prefab",
            typeName="RelicFairy.Monster.FishmanMonster", grade=0,
            maxHp=40,defense=2,atk=15,speed=3.0f,atkRange=6.0f,atkRadius=0.5f,atkRate=0.8f,atkDelay=0.5f,knockback=5f,
            detRange=9,chaseGiveUp=13,patrolRange=4,patrolSpeed=1.8f,waitTime=1f,
            idle="Fishman_IdleNormal",patrol="Fishman_WalkFWD",chase="Fishman_RunFWD",atkReady="Fishman_IdleBattle",atkAnim="Fishman_Attack01",getHit="Fishman_GetHit",die="Fishman_Die",detect="Fishman_SenseSomethingStart"
        },
        new MonsterData {
            name="MushroomAngry",
            thirdPartyPath="Assets/_ThirdParty/RPGMonsterBundlePolyart/CommonStuffs/Prefab/Wave03/CharacterMaskTint/MushroomAngryPAMaskTint.prefab",
            typeName="RelicFairy.Monster.MushroomAngryMonster", grade=0,
            maxHp=50,defense=2,atk=18,speed=2.5f,atkRange=2.0f,atkRadius=2.0f,atkRate=0.8f,atkDelay=0.4f,knockback=4f,
            detRange=6,chaseGiveUp=10,patrolRange=3,patrolSpeed=1.5f,waitTime=1.5f,
            idle="Mushroom_IdlePlant",patrol="Mushroom_walkFWDAngry",chase="Mushroom_runFWDAngry",atkReady="Mushroom_IdleBattleAngry",atkAnim="Mushroom_Attack01Angry",getHit="Mushroom_GetHitAngry",die="Mushroom_DieAngry",detect="Mushroom_IdlePlantToBattleAngry"
        },
        new MonsterData {
            name="MushroomSmile",
            thirdPartyPath="Assets/_ThirdParty/RPGMonsterBundlePolyart/CommonStuffs/Prefab/Wave03/CharacterMaskTint/MushroomSmilePAMaskTint.prefab",
            typeName="RelicFairy.Monster.MushroomSmileMonster", grade=0,
            maxHp=40,defense=1,atk=20,speed=1.5f,atkRange=2.0f,atkRadius=2.0f,atkRate=0.8f,atkDelay=0.4f,knockback=4f,
            detRange=6,chaseGiveUp=10,patrolRange=3,patrolSpeed=1f,waitTime=2f,
            idle="Mushroom_IdlePlant",patrol="Mushroom_walkFWDSmile",chase="Mushroom_runFWDSmile",atkReady="Mushroom_IdleBattleSmile",atkAnim="Mushroom_Attack01Smile",getHit="Mushroom_GetHitSmile",die="Mushroom_DieSmile",detect="Mushroom_IdlePlantToBattleSmile"
        },
        new MonsterData {
            name="NagaWizard",
            thirdPartyPath="Assets/_ThirdParty/RPGMonsterBundlePolyart/CommonStuffs/Prefab/Wave03/CharacterMaskTint/NagaWizardPAMaskTint.prefab",
            typeName="RelicFairy.Monster.NagaWizardMonster", grade=1,
            maxHp=60,defense=2,atk=18,speed=2.5f,atkRange=8.0f,atkRadius=0.5f,atkRate=0.5f,atkDelay=0.6f,knockback=5f,
            detRange=10,chaseGiveUp=14,patrolRange=4,patrolSpeed=1.5f,waitTime=1f,
            idle="NagaWizard_IdleNormal",patrol="NagaWizard_WalkFWD",chase="NagaWizard_RunFWD",atkReady="NagaWizard_IdleBattle",atkAnim="NagaWizard_Attack01",getHit="NagaWizard_GetHit",die="NagaWizard_Die",detect="NagaWizard_SenseSomethingStart"
        },
        new MonsterData {
            name="Salamander",
            thirdPartyPath="Assets/_ThirdParty/RPGMonsterBundlePolyart/CommonStuffs/Prefab/Wave03/CharacterMaskTint/SalamanderPAMaskTint.prefab",
            typeName="RelicFairy.Monster.SalamanderMonster", grade=1,
            maxHp=120,defense=3,atk=22,speed=3.0f,atkRange=2.0f,atkRadius=2.0f,atkRate=0.7f,atkDelay=0.4f,knockback=6f,
            detRange=8,chaseGiveUp=12,patrolRange=5,patrolSpeed=1.8f,waitTime=1f,
            idle="Salamander_IdleNormal",patrol="Salamander_WalkFWD",chase="Salamander_RunFWD",atkReady="Salamander_IdleBattle",atkAnim="Salamander_Attack01",getHit="Salamander_GetHit",die="Salamander_Die",detect="Salamander_SenseSomethingStart"
        },
        new MonsterData {
            name="StingRay",
            thirdPartyPath="Assets/_ThirdParty/RPGMonsterBundlePolyart/CommonStuffs/Prefab/Wave03/CharacterMaskTint/StingRayPAMaskTint.prefab",
            typeName="RelicFairy.Monster.StingRayMonster", grade=0,
            maxHp=45,defense=1,atk=12,speed=4.5f,atkRange=1.5f,atkRadius=1.5f,atkRate=1.0f,atkDelay=0.3f,knockback=4f,
            detRange=7,chaseGiveUp=11,patrolRange=5,patrolSpeed=2.5f,waitTime=0.5f,
            idle="StingRay_IdleNormal",patrol="StingRay_FlyFWD",chase="StingRay_FlyFWDFast",atkReady="StingRay_IdleBattle",atkAnim="StingRay_Attack01",getHit="StingRay_GetHit",die="StingRay_Die",detect="StingRay_SenseSomethingStart"
        },
    };

    // ── 실행 진입점 ──────────────────────────────────────────────

    [MenuItem("RelicFairy/Tools/Create All New Monster Prefabs and Configs")]
    public static void CreateAll()
    {
        int created = 0;
        int skipped = 0;

        var addressSettings = AddressableAssetSettingsDefaultObject.Settings;
        var addrGroup       = addressSettings?.DefaultGroup;

        // SpawnTable 로드
        var spawnTable = AssetDatabase.LoadAssetAtPath<MonsterSpawnTableSO>(
            "Assets/RelicFairy/Characters/Monster/Monster/Core/MonsterSpawnTable.asset");
        var spawnTableSO = spawnTable != null ? new SerializedObject(spawnTable) : null;
        var entriesProp  = spawnTableSO?.FindProperty("entries");

        foreach (var m in Monsters)
        {
            try
            {
                bool prefabOk  = CreatePrefab(m, addrGroup);
                bool configOk  = CreateConfig(m, addrGroup);

                if (prefabOk && configOk)
                {
                    AddToSpawnTable(entriesProp, m);
                    created++;
                }
                else
                    skipped++;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MonsterBatchFactory] {m.name} 생성 중 오류: {ex.Message}\n{ex.StackTrace}");
                skipped++;
            }
        }

        // SpawnTable 저장
        if (spawnTableSO != null)
        {
            spawnTableSO.ApplyModifiedProperties();
            EditorUtility.SetDirty(spawnTable);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[MonsterBatchFactory] 완료: {created}개 생성, {skipped}개 스킵");
        EditorUtility.DisplayDialog("MonsterBatchFactory",
            $"완료: {created}개 생성 / {skipped}개 스킵 (콘솔 확인)", "OK");
    }

    // ── 프리팹 생성 ──────────────────────────────────────────────

    private static bool CreatePrefab(MonsterData m, AddressableAssetGroup addrGroup)
    {
        string outFolder  = $"Assets/RelicFairy/Characters/Monster/Monster/{m.name}/Prefab";
        string prefabPath = $"{outFolder}/{m.name}.prefab";

        if (File.Exists(Path.Combine(Application.dataPath,
            prefabPath.Substring("Assets/".Length))))
        {
            Debug.Log($"[MonsterBatchFactory] 프리팹 이미 존재, 스킵: {prefabPath}");
            RegisterAddressable(addrGroup, prefabPath, $"{m.name}/{m.name}");
            return true;
        }

        // ThirdParty 프리팹 로드
        var tpPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(m.thirdPartyPath);
        if (tpPrefab == null)
        {
            Debug.LogError($"[MonsterBatchFactory] ThirdParty 프리팹 없음: {m.thirdPartyPath}");
            return false;
        }

        // 출력 폴더 생성
        EnsureFolder(outFolder);

        // 루트 GameObject 생성
        var root = new GameObject(m.name);

        // 루트 컴포넌트 추가
        var rb = root.AddComponent<Rigidbody>();
        rb.isKinematic    = true;
        rb.useGravity     = false;
        rb.constraints    = RigidbodyConstraints.FreezeRotation;

        var agent = root.AddComponent<NavMeshAgent>();
        agent.radius      = 0.5f;
        agent.height      = 2.0f;
        agent.speed       = m.speed;
        agent.stoppingDistance = 0.3f;

        var col = root.AddComponent<CapsuleCollider>();
        col.radius = 0.5f;
        col.height = 2.0f;

        // MonsterBase 서브클래스 추가
        var type = Type.GetType($"{m.typeName}, Assembly-CSharp");
        if (type == null)
            type = Type.GetType(m.typeName);
        if (type == null)
        {
            Debug.LogWarning($"[MonsterBatchFactory] 타입 찾기 실패: {m.typeName} — 나중에 수동 추가 필요");
        }
        else
        {
            root.AddComponent(type);
        }

        // ThirdParty 프리팹을 자식으로 인스턴스화
        var child = PrefabUtility.InstantiatePrefab(tpPrefab) as GameObject;
        if (child != null)
        {
            child.transform.SetParent(root.transform, false);
            child.transform.localPosition = Vector3.zero;
            child.transform.localRotation = Quaternion.identity;

            // MonsterAnimEventReceiver 추가 (자식에)
            if (child.GetComponent<MonsterAnimEventReceiver>() == null)
                child.AddComponent<MonsterAnimEventReceiver>();
        }

        // 프리팹 저장
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        UnityEngine.Object.DestroyImmediate(root);

        if (prefab == null)
        {
            Debug.LogError($"[MonsterBatchFactory] 프리팹 저장 실패: {prefabPath}");
            return false;
        }

        // Addressables 등록
        RegisterAddressable(addrGroup, prefabPath, $"{m.name}/{m.name}");

        Debug.Log($"[MonsterBatchFactory] 프리팹 생성: {prefabPath}");
        return true;
    }

    // ── Config 생성 ──────────────────────────────────────────────

    private static bool CreateConfig(MonsterData m, AddressableAssetGroup addrGroup)
    {
        string outFolder   = $"Assets/RelicFairy/Characters/Monster/Monster/{m.name}/SO";
        string configPath  = $"{outFolder}/{m.name}Config.asset";

        if (File.Exists(Path.Combine(Application.dataPath,
            configPath.Substring("Assets/".Length))))
        {
            Debug.Log($"[MonsterBatchFactory] Config 이미 존재, 스킵: {configPath}");
            RegisterAddressable(addrGroup, configPath, $"{m.name}/{m.name}Config");
            return true;
        }

        EnsureFolder(outFolder);

        var config = ScriptableObject.CreateInstance<MonsterConfigSO>();
        var so     = new SerializedObject(config);

        so.FindProperty("monsterName").stringValue  = m.name;
        so.FindProperty("grade").intValue           = m.grade;
        so.FindProperty("playerLayer").FindPropertyRelative("m_Bits").intValue = 64;

        // Stats
        var stat = so.FindProperty("stat");
        stat.FindPropertyRelative("maxHp").floatValue       = m.maxHp;
        stat.FindPropertyRelative("defense").floatValue     = m.defense;
        stat.FindPropertyRelative("attackPower").floatValue = m.atk;
        stat.FindPropertyRelative("moveSpeed").floatValue   = m.speed;
        stat.FindPropertyRelative("attackRange").floatValue = m.atkRange;
        stat.FindPropertyRelative("attackRadius").floatValue= m.atkRadius;
        stat.FindPropertyRelative("attackRate").floatValue  = m.atkRate;
        stat.FindPropertyRelative("attackDelay").floatValue = m.atkDelay;
        stat.FindPropertyRelative("knockbackForce").floatValue = m.knockback;

        // Detection
        var det = so.FindProperty("detection");
        det.FindPropertyRelative("detectionRange").floatValue  = m.detRange;
        det.FindPropertyRelative("chaseGiveUpRange").floatValue= m.chaseGiveUp;

        // Patrol
        var pat = so.FindProperty("patrol");
        pat.FindPropertyRelative("patrolType").intValue       = 2; // Random
        pat.FindPropertyRelative("patrolRange").floatValue    = m.patrolRange;
        pat.FindPropertyRelative("patrolSpeed").floatValue    = m.patrolSpeed;
        pat.FindPropertyRelative("waypointWaitTime").floatValue= m.waitTime;

        // Combat
        var cbt = so.FindProperty("combat");
        cbt.FindPropertyRelative("damageApplyDelay").floatValue = m.atkDelay;
        cbt.FindPropertyRelative("targetLayer").FindPropertyRelative("m_Bits").intValue = 64;

        // Animation
        var anim = so.FindProperty("animation");
        anim.FindPropertyRelative("animatorControllerAddress").stringValue = "";
        anim.FindPropertyRelative("idleStateName").stringValue        = m.idle;
        anim.FindPropertyRelative("patrolStateName").stringValue      = m.patrol;
        anim.FindPropertyRelative("chaseStateName").stringValue       = m.chase;
        anim.FindPropertyRelative("attackReadyStateName").stringValue = m.atkReady;
        anim.FindPropertyRelative("attackTrigger").stringValue        = m.atkAnim;
        anim.FindPropertyRelative("getHitTrigger").stringValue        = m.getHit;
        anim.FindPropertyRelative("dieTrigger").stringValue           = m.die;
        anim.FindPropertyRelative("detectTrigger").stringValue        = m.detect;
        anim.FindPropertyRelative("speedParam").stringValue           = "";
        anim.FindPropertyRelative("speedDampTime").floatValue         = 0.1f;
        anim.FindPropertyRelative("crossFadeDuration").floatValue     = 0.15f;

        // Elemental (기본 배율 1.0 = 서버 데이터 없을 때 기준)
        so.FindProperty("elemental").FindPropertyRelative("maxAccumulationScale").floatValue = 1f;

        so.ApplyModifiedProperties();
        AssetDatabase.CreateAsset(config, configPath);

        RegisterAddressable(addrGroup, configPath, $"{m.name}/{m.name}Config");

        Debug.Log($"[MonsterBatchFactory] Config 생성: {configPath}");
        return true;
    }

    // ── SpawnTable 항목 추가 ─────────────────────────────────────

    private static void AddToSpawnTable(SerializedProperty entriesProp, MonsterData m)
    {
        if (entriesProp == null) return;

        string key = $"{m.name}/{m.name}";

        // 중복 체크
        for (int i = 0; i < entriesProp.arraySize; i++)
        {
            var e = entriesProp.GetArrayElementAtIndex(i);
            if (e.FindPropertyRelative("addressableKey").stringValue == key)
                return;
        }

        entriesProp.arraySize++;
        var entry = entriesProp.GetArrayElementAtIndex(entriesProp.arraySize - 1);
        entry.FindPropertyRelative("displayName").stringValue    = $"{m.name}Monster";
        entry.FindPropertyRelative("addressableKey").stringValue = key;
        entry.FindPropertyRelative("weight").floatValue          = 1f;
        entry.FindPropertyRelative("enabled").boolValue          = true;
    }

    // ── Addressables 등록 ────────────────────────────────────────

    private static void RegisterAddressable(AddressableAssetGroup group, string assetPath, string address)
    {
        if (group == null) return;
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) return;

        string guid = AssetDatabase.AssetPathToGUID(assetPath);
        if (string.IsNullOrEmpty(guid)) return;

        var entry = settings.CreateOrMoveEntry(guid, group, false, false);
        if (entry != null)
        {
            entry.address = address;
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryAdded, entry, true);
        }
    }

    // ── 유틸리티 ─────────────────────────────────────────────────

    private static void EnsureFolder(string folderPath)
    {
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            string full = Path.Combine(Application.dataPath,
                folderPath.Substring("Assets/".Length));
            Directory.CreateDirectory(full);
            AssetDatabase.Refresh();
        }
    }

    // ── NavMesh 베이크 ───────────────────────────────────────────

    [MenuItem("RelicFairy/Tools/Bake NavMesh")]
    public static void BakeNavMesh()
    {
        UnityEditor.AI.NavMeshBuilder.BuildNavMesh();
        Debug.Log("[MonsterBatchFactory] NavMesh 베이크 완료.");
    }

    // ── 테스트 씬 오픈 ────────────────────────────────────────────

    [MenuItem("RelicFairy/Tools/Open leeTestGameSSecene")]
    public static void OpenTestScene()
    {
        const string path = "Assets/RelicFairy/Scenes/leeTestGameSSecene.unity";
        if (UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path);
    }

    // ── SpawnTable maxCount 업데이트 ─────────────────────────────

    [MenuItem("RelicFairy/Tools/Set Spawner MaxCount to 10")]
    public static void SetSpawnerMaxCount()
    {
        // 씬에서 MonsterSpawner 찾기
        var spawners = UnityEngine.Object.FindObjectsByType<MonsterSpawner>(FindObjectsSortMode.None);
        if (spawners.Length == 0)
        {
            Debug.LogWarning("[MonsterBatchFactory] MonsterSpawner를 씬에서 찾을 수 없습니다.");
            return;
        }
        foreach (var s in spawners)
        {
            var so = new SerializedObject(s);
            so.FindProperty("maxMonsterCount").intValue = 10;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(s);
            Debug.Log($"[MonsterBatchFactory] maxMonsterCount = 10 설정: {s.gameObject.name}");
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
    }
}
#endif
