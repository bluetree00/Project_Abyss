// ================================================================
// FairyBatSetupEditor.cs
// 메뉴: Tools > Lee Monster > Setup FairyBat All Assets
//
// 한 번 클릭으로 아래 작업을 전부 수행합니다.
//   1) TagManager에 "Player" / "Monster" 레이어 추가
//   2) FairyBat AnimatorController 생성 (기존 Bat FBX 클립 연결)
//   3) FairyBat SO 에셋 6개 생성 및 수치 설정
//   4) Addressables 그룹에 FairyBatConfig / AnimatorController 등록
// ================================================================

// ThirdParty에 전역 네임스페이스 AnimatorController(MonoBehaviour)가 있어
// UnityEditor.Animations.AnimatorController 와 이름이 충돌하므로 별칭으로 구분.
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using AC  = UnityEditor.Animations.AnimatorController;
using ASM = UnityEditor.Animations.AnimatorStateMachine;
using AS  = UnityEditor.Animations.AnimatorState;

public static class FairyBatSetupEditor
{
    // ── 경로 상수 ──────────────────────────────────────────
    private const string BAT_ANIM_PATH =
        "Assets/Abyss/Characters/Monster/FSMMonster/Bat/Animation/Bat";

    private const string OUTPUT_PATH =
        "Assets/Abyss/Characters/Monster/leeMonster/FairyBat";

    private const string SO_PATH =
        "Assets/Abyss/Characters/Monster/leeMonster/FairyBat/SO";

    private const string CONTROLLER_ADDRESS  = "FairyBat/FairyBatAnimatorController";
    private const string CONFIG_ADDRESS      = "FairyBat/FairyBatConfig";
    private const string ADDRESSABLE_GROUP   = "Lee Monster";

    // ── 엔트리 포인트 ──────────────────────────────────────

    [MenuItem("Tools/Lee Monster/Setup FairyBat All Assets")]
    public static void SetupAll()
    {
        // ── Step 1: 레이어 추가 ──────────────────────────
        AddLayerIfMissing("Player");
        AddLayerIfMissing("Monster");
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        int playerLayer  = LayerMask.NameToLayer("Player");
        int monsterLayer = LayerMask.NameToLayer("Monster");
        Debug.Log($"[FairyBatSetup] Player Layer={playerLayer}, Monster Layer={monsterLayer}");

        // ── Step 2: 폴더 보장 ────────────────────────────
        EnsureFolder("Assets/Abyss/Characters/Monster/leeMonster/FairyBat");
        EnsureFolder("Assets/Abyss/Characters/Monster/leeMonster/FairyBat/SO");

        // ── Step 3: AnimatorController 생성 ─────────────
        var controller = CreateAnimatorController();

        // ── Step 4: SO 에셋 생성 및 값 설정 ─────────────
        var statSO      = GetOrCreateSO<MonsterStatSO>(      "FairyBatStat");
        var detectionSO = GetOrCreateSO<MonsterDetectionSO>( "FairyBatDetection");
        var patrolSO    = GetOrCreateSO<MonsterPatrolSO>(    "FairyBatPatrol");
        var combatSO    = GetOrCreateSO<MonsterCombatSO>(    "FairyBatCombat");
        var animSO      = GetOrCreateSO<MonsterAnimationSO>( "FairyBatAnimation");
        var configSO    = GetOrCreateSO<MonsterConfigSO>(    "FairyBatConfig");

        ApplyStatValues(statSO);
        ApplyDetectionValues(detectionSO);
        ApplyPatrolValues(patrolSO);
        ApplyCombatValues(combatSO, playerLayer);
        ApplyAnimationValues(animSO);
        ApplyConfigValues(configSO, statSO, detectionSO, patrolSO, combatSO, animSO, playerLayer);

        EditorUtility.SetDirty(statSO);
        EditorUtility.SetDirty(detectionSO);
        EditorUtility.SetDirty(patrolSO);
        EditorUtility.SetDirty(combatSO);
        EditorUtility.SetDirty(animSO);
        EditorUtility.SetDirty(configSO);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // ── Step 5: Addressables 등록 ────────────────────
        RegisterAddressable(AssetDatabase.GetAssetPath(controller), CONTROLLER_ADDRESS);
        RegisterAddressable(AssetDatabase.GetAssetPath(configSO),   CONFIG_ADDRESS);

        AssetDatabase.SaveAssets();

        // ── 완료 메시지 ──────────────────────────────────
        string msg =
            "페어리 박쥐 세팅 완료!\n\n" +
            "■ 추가로 씬에서 해야 할 작업:\n" +
            "  1) Player 오브젝트 → Layer = 'Player'\n" +
            "  2) FairyBat 오브젝트 → Layer = 'Monster'\n" +
            "  3) FairyBat 오브젝트에 FairyBatMonster 컴포넌트 추가\n" +
            "  4) NavMeshAgent + Rigidbody(Freeze Rotation XYZ) 추가\n" +
            "  5) 자식으로 박쥐 메시(Animator 포함) 배치";

        EditorUtility.DisplayDialog("FairyBat Setup", msg, "OK");
        Debug.Log("[FairyBatSetup] ✅ 전체 세팅 완료.");
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // AnimatorController 생성
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private static AC CreateAnimatorController()
    {
        string controllerPath = $"{OUTPUT_PATH}/FairyBatAnimatorController.controller";

        // 이미 있으면 재사용
        var existing = AssetDatabase.LoadAssetAtPath<AC>(controllerPath);
        if (existing != null)
        {
            Debug.Log("[FairyBatSetup] AnimatorController 이미 존재 — 재사용.");
            return existing;
        }

        var controller = AC.CreateAnimatorControllerAtPath(controllerPath);

        // ── 파라미터 추가 ────────────────────────────────
        controller.AddParameter("Attack01",       AnimatorControllerParameterType.Trigger);
        controller.AddParameter("GetHit",         AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Die",            AnimatorControllerParameterType.Trigger);
        controller.AddParameter("SenseSomething", AnimatorControllerParameterType.Trigger);

        // ── 클립 로드 ────────────────────────────────────
        var clipIdle        = LoadClip("IdleNormal_Bat_Anim.fbx");
        var clipIdleBattle  = LoadClip("IdleBattle_Bat_Anim.fbx");
        var clipFly         = LoadClip("FlyFWD_Bat_Anim.fbx");
        var clipAttack01    = LoadClip("Attack01_Bat_Anim.fbx");
        var clipGetHit      = LoadClip("GetHit_Bat_Anim.fbx");
        var clipDie         = LoadClip("Die_Bat_Anim.fbx");
        var clipSense       = LoadClip("SenseSomethingST_Bat_Anim.fbx");

        // ── 상태 머신 ────────────────────────────────────
        ASM sm = controller.layers[0].stateMachine;

        AS stIdle        = AddState(sm, "Idle_Normal",      clipIdle,       new Vector3(-200, 0));
        AS stMove        = AddState(sm, "MoveBlend",        clipFly,        new Vector3(-200, 80));
        AS stAttackReady = AddState(sm, "AttackReady",      clipIdleBattle, new Vector3(100, -80));
        AS stAttack01    = AddState(sm, "Attack01",         clipAttack01,   new Vector3(100, 0));
        AS stGetHit      = AddState(sm, "GetHit",           clipGetHit,     new Vector3(100, 80));
        AS stDie         = AddState(sm, "Die",              clipDie,        new Vector3(100, 160));
        AS stSense       = AddState(sm, "SenseSomething",   clipSense,      new Vector3(-200, 160));

        sm.defaultState = stIdle;

        // ── Any State 전환 ───────────────────────────────
        // 피격, 사망, 감지, 공격은 AnyState 트리거로 진입
        AddAnyTransition(sm, stGetHit,   "GetHit",         canTransitionToSelf: false);
        AddAnyTransition(sm, stDie,      "Die",            canTransitionToSelf: false);
        AddAnyTransition(sm, stSense,    "SenseSomething", canTransitionToSelf: false);
        AddAnyTransition(sm, stAttack01, "Attack01",       canTransitionToSelf: false);

        // ── 종료 후 복귀 전환 (exit time 기반 fallback) ──
        // 코드가 CrossFade로 강제 전환하기 전에 애니메이션이 끝나면 Idle로 복귀
        AddExitTransition(stAttack01,  stAttackReady, exitTime: 0.9f);
        AddExitTransition(stGetHit,    stIdle,        exitTime: 0.9f);
        AddExitTransition(stSense,     stAttackReady, exitTime: 0.9f);

        // ── 저장 ────────────────────────────────────────
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        Debug.Log($"[FairyBatSetup] AnimatorController 생성: {controllerPath}");
        return controller;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // SO 값 적용 (페어리 박쥐 스펙)
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private static void ApplyStatValues(MonsterStatSO s)
    {
        s.maxHp        = 10;
        s.defense      = 1f;
        s.attackPower  = 10f;
        s.moveSpeed    = 3f;
        s.attackRange  = 2f;
        s.attackRadius = 1f;
        s.attackRate   = 1f;
        s.attackDelay  = 1f;
        s.knockbackForce = 5f;
    }

    private static void ApplyDetectionValues(MonsterDetectionSO s)
    {
        s.detectionRange   = 5f;
        s.chaseGiveUpRange = 8f;
    }

    private static void ApplyPatrolValues(MonsterPatrolSO s)
    {
        s.patrolType      = PatrolType.Horizontal;
        s.patrolRange     = 3f;
        s.patrolSpeed     = 1.5f;
        s.waypointWaitTime = 0.5f;
    }

    private static void ApplyCombatValues(MonsterCombatSO s, int playerLayerIndex)
    {
        s.damageApplyDelay = 0.4f;

        // Player 레이어 마스크 설정 (-1이면 레이어 없음 → 경고 후 Default 사용)
        if (playerLayerIndex >= 0)
            s.targetLayer = 1 << playerLayerIndex;
        else
        {
            s.targetLayer = 1; // Default layer fallback
            Debug.LogWarning("[FairyBatSetup] 'Player' 레이어를 찾을 수 없습니다. targetLayer를 수동으로 설정하세요.");
        }
    }

    private static void ApplyAnimationValues(MonsterAnimationSO s)
    {
        s.animatorControllerAddress = CONTROLLER_ADDRESS;
        s.idleStateName             = "Idle_Normal";
        s.patrolStateName           = "MoveBlend";
        s.chaseStateName            = "MoveBlend";
        s.attackReadyStateName      = "AttackReady";
        s.attackTrigger             = "Attack01";
        s.getHitTrigger             = "GetHit";
        s.dieTrigger                = "Die";
        s.detectTrigger             = "SenseSomething";
        s.speedParam                = "";
        s.crossFadeDuration         = 0.15f;
    }

    private static void ApplyConfigValues(
        MonsterConfigSO s,
        MonsterStatSO stat,
        MonsterDetectionSO detection,
        MonsterPatrolSO patrol,
        MonsterCombatSO combat,
        MonsterAnimationSO anim,
        int playerLayerIndex)
    {
        s.monsterName = "페어리 박쥐";
        s.grade       = MonsterGrade.Normal;
        s.stat        = stat;
        s.detection   = detection;
        s.patrol      = patrol;
        s.combat      = combat;
        s.animation   = anim;

        if (playerLayerIndex >= 0)
            s.playerLayer = 1 << playerLayerIndex;
        else
            s.playerLayer = 1;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // Addressables 등록
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private static void RegisterAddressable(string assetPath, string address)
    {
        if (string.IsNullOrEmpty(assetPath))
        {
            Debug.LogWarning($"[FairyBatSetup] Addressable 등록 실패 (경로 없음): {address}");
            return;
        }

        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogWarning("[FairyBatSetup] Addressable Settings를 찾을 수 없습니다. " +
                             "Window > Asset Management > Addressables > Groups 에서 초기화하세요.");
            return;
        }

        // 그룹 가져오기 또는 생성
        var group = settings.FindGroup(ADDRESSABLE_GROUP);
        if (group == null)
        {
            group = settings.CreateGroup(
                ADDRESSABLE_GROUP, false, false, false,
                new System.Collections.Generic.List<AddressableAssetGroupSchema>
                {
                    ScriptableObject.CreateInstance<BundledAssetGroupSchema>(),
                    ScriptableObject.CreateInstance<ContentUpdateGroupSchema>()
                });
        }

        string guid  = AssetDatabase.AssetPathToGUID(assetPath);
        var    entry = settings.CreateOrMoveEntry(guid, group, false, false);
        if (entry != null)
        {
            entry.address = address;
            Debug.Log($"[FairyBatSetup] Addressable 등록: '{address}' ({assetPath})");
        }

        settings.SetDirty(
            AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // TagManager 레이어 추가
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private static void AddLayerIfMissing(string layerName)
    {
        // 이미 존재하면 스킵
        if (LayerMask.NameToLayer(layerName) != -1)
        {
            Debug.Log($"[FairyBatSetup] 레이어 '{layerName}' 이미 존재.");
            return;
        }

        SerializedObject tagManager = new SerializedObject(
            AssetDatabase.LoadMainAssetAtPath("ProjectSettings/TagManager.asset"));

        SerializedProperty layers = tagManager.FindProperty("layers");

        for (int i = 6; i < layers.arraySize; i++) // 0-5는 Unity 예약 레이어
        {
            var element = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(element.stringValue))
            {
                element.stringValue = layerName;
                tagManager.ApplyModifiedProperties();
                Debug.Log($"[FairyBatSetup] 레이어 추가: '{layerName}' (index {i})");
                return;
            }
        }

        Debug.LogWarning($"[FairyBatSetup] 빈 레이어 슬롯이 없습니다. '{layerName}' 레이어를 수동으로 추가하세요.");
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 공통 유틸
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private static T GetOrCreateSO<T>(string fileName) where T : ScriptableObject
    {
        string path     = $"{SO_PATH}/{fileName}.asset";
        var    existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing != null) return existing;

        var so = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(so, path);
        return so;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;

        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        string folder = Path.GetFileName(path);

        if (!AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);

        AssetDatabase.CreateFolder(parent, folder);
    }

    private static AnimationClip LoadClip(string fbxFileName)
    {
        string fullPath = $"{BAT_ANIM_PATH}/{fbxFileName}";

        // FBX 서브에셋에서 AnimationClip 추출
        Object[] allAssets = AssetDatabase.LoadAllAssetsAtPath(fullPath);
        foreach (var asset in allAssets)
        {
            if (asset is AnimationClip clip &&
                !clip.name.StartsWith("__preview__", System.StringComparison.Ordinal))
            {
                return clip;
            }
        }

        Debug.LogWarning($"[FairyBatSetup] 클립을 찾지 못했습니다: {fullPath}");
        return null;
    }

    private static AS AddState(
        ASM sm,
        string stateName,
        Motion motion,
        Vector3 position)
    {
        AS state   = sm.AddState(stateName, position);
        state.motion = motion;
        return state;
    }

    private static void AddAnyTransition(
        ASM sm,
        AS dest,
        string triggerName,
        bool canTransitionToSelf)
    {
        var t = sm.AddAnyStateTransition(dest);
        t.hasExitTime         = false;
        t.duration            = 0.1f;
        t.canTransitionToSelf = canTransitionToSelf;
        t.AddCondition(UnityEditor.Animations.AnimatorConditionMode.If, 0, triggerName);
    }

    private static void AddExitTransition(AS from, AS to, float exitTime)
    {
        var t = from.AddTransition(to);
        t.hasExitTime = true;
        t.exitTime    = exitTime;
        t.duration    = 0.15f;
    }
}
