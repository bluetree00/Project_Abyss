#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RelicFairy.Monster;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Animations;
using EdAnimatorController = UnityEditor.Animations.AnimatorController;   // 프로젝트에 같은 이름의 형이 있다
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// [제작 도구 · 편집 모드] 10-12 중간 엘리트 보스 「황소 탱크」(기획/RelicFairy_중간엘리트보스_황소탱크_설계_20261012.md).
/// ① 팩 정리: <c>Assets/Zectorlab</c> → <c>Assets/_ThirdParty/Zectorlab</c>(받은 원본 · git 추적 밖), 쓰는 것(URP 프리팹 · 메시 · 애니 · URP 재질 · 텍스처)과
///    연쇄 의존만 <c>Assets/RelicFairy/_Imported/Zectorlab/</c>로 Unity 안 이동(GUID 보존 — project_asset_pipeline_lfs 규칙). HDRP · Built-In · Maps는 _ThirdParty에 남는다.
/// ② 만들기: 전용 AnimatorController(상태 15 · writeDefaultValues=false) · 설정 SO(늑대인간 정예 설정을 바탕으로) · 챕터별 프리팹 4(겉모습 · 몸 색) · 어드레서블 등록.
///    원본 프리팹의 래그돌(뼈마다 콜라이더 · 리지드바디 · 조인트)은 몬스터 판정과 부딪쳐 떼어 낸다.
/// </summary>
internal static class BullTankBuilderEditor
{
    private const string Root       = "RelicFairy/Boss/BullTank/";
    private const string SrcPack    = "Assets/Zectorlab";
    private const string ThirdParty = "Assets/_ThirdParty/Zectorlab";
    private const string Imported   = "Assets/RelicFairy/_Imported/Zectorlab";
    private const string Pack       = "/BullTank";
    private const string OutDir     = "Assets/RelicFairy/Characters/Monster/Monster/BullTank";
    private const string Group      = "Characters";

    // 챕터별 겉모습(83 배치 설계 — 경호원 차림은 아서왕 톤 밖이라 옷은 끄고 곤봉만 빌린다):
    // Ch1 맨몸 · 갈색 · 곤봉 / Ch2 바이킹 옷 · 회색 · 곤봉 / Ch3 바이킹 · 갈색 · 도끼 / Ch4 바이킹 · 파랑 · 도끼 / 악몽 = 검은 몸(런타임)
    private static readonly (string basePrefab, bool vikingSuit, string colour)[] Chapters =
    {
        ("Bodyguard", false, "Brown"), ("Bodyguard", true, "Grey"), ("Viking", true, "Brown"), ("Viking", true, "Blue"),
    };
    private static readonly string[] BodyColours = { "Black", "Blue", "Brown", "Grey" };

    // 상태 이름(BullTankMonster 상수) → 클립 이름(FBX 안)
    private static readonly (string state, string fbx, string clip)[] States =
    {
        ("Idle",         "AS_BullTank_Idle",                 "Idle"),
        ("Walk",         "AS_BullTank_SpeedWalk_F",          "SpeedWalk_F"),
        ("Run",          "AS_BullTank_Run",                  "Run"),
        ("Attack01",     "AS_BullTank_Attack01",             "Attack_01"),
        ("Charge_Start", "AS_BullTank_Attack02_Charge_Start","Attack02_Charge_Start"),
        ("Charge_Loop",  "AS_BullTank_Attack02_Charge_Loop", "Attack02_Charge_Loop"),
        ("Run_Start",    "AS_BullTank_Attack02_Run_Start",   "Attack02_Run_Start"),
        ("Run_Loop",     "AS_BullTank_Attack02_Run_Loop",    "Attack02_Run_Loop"),
        ("Charge_End",   "AS_BullTank_Attack02_End",         "Attack02_End"),
        ("Jump_Start",   "AS_BullTank_Jump_Start",           "Jump_Start"),
        ("Jump_Loop",    "AS_BullTank_Jump_Loop",            "Jump_Loop"),
        ("Jump_End",     "AS_BullTank_Jump_End",             "Jump_End"),
        ("Angry",        "AS_BullTank_Angry",                "Angry"),
        ("Hit",          "AS_BullTank_Hit",                  "Hit"),
        ("Death",        "AS_BullTank_Death",                "Death"),
    };

    // 소리(설계서 §6 — 추적 중인 것만 · _ThirdParty 소리는 협업자에게 없다)
    private const string SfxRoar  = "Assets/RelicFairy/Characters/Monster/Monster/ForestGuardian/Art/Sound/Voice3.mp3";
    private const string SfxSwing = "Assets/RelicFairy/Characters/Player/Sound/Attack/sfx_player_swing_heavy.wav";
    private const string SfxLand  = "Assets/RelicFairy/Characters/Monster/Monster/DragonBoss/Art/Sound/Slam.mp3";
    private const string SfxStomp = "Assets/RelicFairy/Characters/Monster/Monster/DragonBoss/Art/Sound/Walk1.mp3";
    private const string SfxWall  = "Assets/RelicFairy/Characters/Monster/Monster/ForestGuardian/Art/Sound/Ground.mp3";

    private const string BaseConfig = "Assets/RelicFairy/Characters/Monster/Monster/Werewolf/SO/WerewolfConfig.asset";
    private const int    BigAgentType = -1923039037;   // NavMesh 에이전트 종류(반경 0.5 — 숲 · 화룡과 같은 큰 몸)

    // ── ① 팩 정리 ──────────────────────────────────────────
    [MenuItem(Root + "1 Move Pack (Edit)")]
    private static void MovePack()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[BullTankBuilder] 편집 모드에서만"); return; }
        if (AssetDatabase.IsValidFolder(SrcPack))
        {
            EnsureFolder("Assets/_ThirdParty");
            if (AssetDatabase.IsValidFolder(ThirdParty)) { Debug.LogError($"[BullTankBuilder] {ThirdParty}가 이미 있다 — 손으로 합칠 것"); return; }
            string err = AssetDatabase.MoveAsset(SrcPack, ThirdParty);
            if (!string.IsNullOrEmpty(err)) { Debug.LogError("[BullTankBuilder] 팩 이동 실패: " + err); return; }
        }
        string tp = ThirdParty + Pack;
        var roots = new List<string>
        {
            tp + "/Prefabs/Prefab_BullTank Bodyguard_URP.prefab",
            tp + "/Prefabs/Prefab_BullTank Viking_URP.prefab",
        };
        foreach (var s in States) roots.Add($"{tp}/Art/Animations/{s.fbx}.FBX");
        foreach (var c in BodyColours) roots.Add($"{tp}/Art/Pipelines/URP/URP_Materials/M_BullTank_URP_{c}.mat");
        roots.RemoveAll(p => AssetDatabase.LoadMainAssetAtPath(p) == null && !File.Exists(p));

        var deps = AssetDatabase.GetDependencies(roots.ToArray(), true)
            .Where(p => p.StartsWith(ThirdParty + "/") && !p.EndsWith(".cs")).Distinct().ToList();
        int moved = 0;
        foreach (var src in deps)
        {
            string dst = Imported + src.Substring(ThirdParty.Length);
            EnsureFolder(Path.GetDirectoryName(dst).Replace('\\', '/'));
            string err = AssetDatabase.MoveAsset(src, dst);
            if (string.IsNullOrEmpty(err)) moved++;
            else Debug.LogWarning($"[BullTankBuilder] 이동 실패 {src}: {err}");
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[BullTankBuilder] 팩 정리 끝 — 쓰는 것 {moved}/{deps.Count}개 → {Imported} · 나머지는 {ThirdParty}");
    }

    // ── ② 만들기 ───────────────────────────────────────────
    [MenuItem(Root + "2 Build Animator Config Prefabs (Edit)")]
    private static void Build()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[BullTankBuilder] 편집 모드에서만"); return; }
        string im = Imported + Pack;
        if (!AssetDatabase.IsValidFolder(im)) { Debug.LogError("[BullTankBuilder] 먼저 ① 팩 정리"); return; }
        EnsureFolder(OutDir + "/Art");
        EnsureFolder(OutDir + "/SO");
        EnsureFolder(OutDir + "/Prefab");

        var ctrl   = BuildAnimator(im);
        var config = BuildConfig();
        if (ctrl == null || config == null) return;

        var settings = AddressableAssetSettingsDefaultObject.Settings;
        var group = settings != null ? settings.FindGroup(Group) : null;
        if (group == null) { Debug.LogError($"[BullTankBuilder] 어드레서블 그룹 '{Group}' 없음"); return; }
        Register(settings, group, AssetDatabase.GetAssetPath(config), "BullTank/BullTankConfig");

        for (int i = 0; i < Chapters.Length; i++)
        {
            string path = BuildPrefab(im, i + 1, Chapters[i].basePrefab, Chapters[i].vikingSuit, Chapters[i].colour, ctrl);
            if (path != null) Register(settings, group, path, $"BullTank/BullTank_Ch{i + 1}");
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[BullTankBuilder] 만들기 끝 — 컨트롤러 · 설정 · 프리팹 4 · 어드레서블 등록");
    }

    private static EdAnimatorController BuildAnimator(string im)
    {
        string path = OutDir + "/Art/BullTankAnimator.controller";
        if (AssetDatabase.LoadAssetAtPath<EdAnimatorController>(path) != null) AssetDatabase.DeleteAsset(path);
        var ctrl = EdAnimatorController.CreateAnimatorControllerAtPath(path);
        var sm = ctrl.layers[0].stateMachine;
        AnimatorState idle = null;
        foreach (var (state, fbx, clipName) in States)
        {
            string fbxPath = $"{im}/Art/Animations/{fbx}.FBX";
            var clip = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>()
                .FirstOrDefault(c => c.name == clipName && !c.name.StartsWith("__preview__"));
            if (clip == null) { Debug.LogWarning($"[BullTankBuilder] 클립 없음 {fbxPath} · {clipName}"); continue; }
            var st = sm.AddState(state);
            st.motion = clip;
            st.writeDefaultValues = false;   // 새 상태 규칙
            if (state == "Idle") idle = st;
        }
        if (idle != null) sm.defaultState = idle;
        EditorUtility.SetDirty(ctrl);
        return ctrl;
    }

    private static MonsterConfigSO BuildConfig()
    {
        string path = OutDir + "/SO/BullTankConfig.asset";
        var existing = AssetDatabase.LoadAssetAtPath<MonsterConfigSO>(path);
        var baseCfg  = AssetDatabase.LoadAssetAtPath<MonsterConfigSO>(BaseConfig);
        if (baseCfg == null) { Debug.LogError("[BullTankBuilder] 바탕 설정 없음 " + BaseConfig); return null; }
        var cfg = existing != null ? existing : Object.Instantiate(baseCfg);
        if (existing != null) EditorUtility.CopySerialized(baseCfg, cfg);

        cfg.name               = "BullTankConfig";
        cfg.monsterName        = "Yoked Bull";   // 머리 위 이름 · 보스 체력바 이름은 BullTankMonster가 시기로 고른다
        cfg.grade              = MonsterGrade.Elite;   // 정예 — 포이즈가 붙는다(접두어는 SpawnMidBossAsync 길이라 안 붙음)
        cfg.visualHeightMeters = 2.32f;                // × 정예 1.25 = 2.9 m(83 배치 설계 · 미니보스 띠 2.7~3.2)
        cfg.stat.maxHp         = 2000;                 // Ch1 · 현실 DPS 45 × 약 44초(설계서 §4 50초 → 10-26 사용자 「체력을 조금 줄여야」) — 챕터 배율은 정예 층
        cfg.stat.defense       = 4f;
        cfg.stat.attackPower   = 95f;
        cfg.stat.moveSpeed     = 3.8f;                 // 10-23 B6 「약간 더 재빠르게」 3.2 →
        cfg.stat.attackRange   = 4f;
        cfg.stat.knockbackForce = 4f;
        cfg.detection.detectionRange   = 12f;
        cfg.detection.chaseGiveUpRange = 60f;
        cfg.animation.animatorControllerAddress = string.Empty;
        cfg.animation.idleStateName        = "Idle";
        cfg.animation.patrolStateName      = "Walk";
        cfg.animation.chaseStateName       = "Run";
        cfg.animation.attackReadyStateName = "Idle";
        cfg.animation.attackStateName      = "Attack01";
        cfg.animation.getHitStateName      = "Hit";
        cfg.animation.dieStateName         = "Death";
        cfg.animation.attackTrigger = cfg.animation.getHitTrigger = cfg.animation.dieTrigger = cfg.animation.detectTrigger = string.Empty;
        cfg.death.dissolveDuration = 0.8f;             // 큰 몸 — 디졸브 0.8초(10-12 디졸브 원칙)
        cfg.drop.coinMin   = 13;                       // 처치 골드 약 240 = 평균 15개 × 정예 1.6 × 10(83 배치 설계)
        cfg.drop.coinMax   = 17;
        cfg.drop.coinValue = 10;
        cfg.specialStates  = new List<SpecialStateEntry>();
        cfg.stateOverrides = new List<MonsterStateOverrideSO>();   // 일반 공격 안 씀 — 패턴 셋은 BullTankMonster가

        if (existing == null) AssetDatabase.CreateAsset(cfg, path);
        EditorUtility.SetDirty(cfg);
        return cfg;
    }

    private static string BuildPrefab(string im, int chapter, string variant, bool vikingSuit, string colour, EdAnimatorController ctrl)
    {
        var src = AssetDatabase.LoadAssetAtPath<GameObject>($"{im}/Prefabs/Prefab_BullTank {variant}_URP.prefab");
        var mat = AssetDatabase.LoadAssetAtPath<Material>($"{im}/Art/Pipelines/URP/URP_Materials/M_BullTank_URP_{colour}.mat");
        var black = AssetDatabase.LoadAssetAtPath<Material>($"{im}/Art/Pipelines/URP/URP_Materials/M_BullTank_URP_Black.mat");
        if (src == null) { Debug.LogError($"[BullTankBuilder] 원본 프리팹 없음 {variant}"); return null; }

        var root = new GameObject($"BullTank_Ch{chapter}");
        try
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(src, root.transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;

            // 래그돌 떼기 — 조인트 → 리지드바디 → 콜라이더 순(조인트가 리지드바디를 붙잡고 있다)
            foreach (var j in model.GetComponentsInChildren<CharacterJoint>(true)) Object.DestroyImmediate(j);
            foreach (var r in model.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(r);
            foreach (var c in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);

            // 옷 — 경호원 옷은 늘 끄고(아서왕 톤 밖) 바이킹 옷은 챕터대로. 부품 이름이 FBX마다 달라 재질로 찾는다(곤봉 · 도끼는 남긴다)
            var parts = new List<string>();
            foreach (var rend in model.GetComponentsInChildren<Renderer>(true))
            {
                string mn = rend.sharedMaterial != null ? rend.sharedMaterial.name : "";
                parts.Add($"{rend.name}[{mn}]");
                if (mn.Contains("Bodyguards")) rend.gameObject.SetActive(false);
                else if (mn.EndsWith("_Viking")) rend.gameObject.SetActive(vikingSuit);
            }
            Debug.Log($"[BullTankBuilder] Ch{chapter} 부품: {string.Join(" · ", parts)}");

            // 몸 색 — 네 색 재질 중 하나를 쓰는 렌더러만 바꾼다(옷 · 무기는 그대로)
            if (mat != null)
                foreach (var rend in model.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = rend.sharedMaterials;
                    bool changed = false;
                    for (int k = 0; k < mats.Length; k++)
                        if (mats[k] != null && mats[k].name.StartsWith("M_BullTank_URP_")
                            && BodyColours.Any(cn => mats[k].name.EndsWith(cn)))
                        { mats[k] = mat; changed = true; }
                    if (changed) rend.sharedMaterials = mats;
                }

            var anim = model.GetComponentInChildren<Animator>(true) ?? model.AddComponent<Animator>();
            anim.runtimeAnimatorController = ctrl;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            if (anim.GetComponent<MonsterAnimEventReceiver>() == null) anim.gameObject.AddComponent<MonsterAnimEventReceiver>();

            // 몸 크기 → 캡슐 · 에이전트(런타임 신장 맞춤이 루트 배율을 바꾸면 같이 커진다)
            var b = new Bounds(root.transform.position, Vector3.zero);
            foreach (var rend in model.GetComponentsInChildren<Renderer>(true)) b.Encapsulate(rend.bounds);
            float h = Mathf.Max(1f, b.size.y);
            float rad = Mathf.Clamp(Mathf.Min(b.size.x, b.size.z) * 0.35f, 0.4f, 1.2f);

            var rb = root.AddComponent<Rigidbody>();
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            var cap = root.AddComponent<CapsuleCollider>();
            cap.height = h; cap.radius = rad; cap.center = new Vector3(0f, h * 0.5f, 0f);
            var agent = root.AddComponent<NavMeshAgent>();
            agent.agentTypeID  = BigAgentType;
            agent.radius       = 0.7f;   // 83 배치 설계(반경 약 0.7 · 큰 몸 에이전트)
            agent.height       = h;
            agent.speed        = 3.2f;
            agent.angularSpeed = 360f;
            agent.acceleration = 12f;
            agent.stoppingDistance = 0f;

            var mon = root.AddComponent<BullTankMonster>();
            var so  = new SerializedObject(mon);
            so.FindProperty("_configAddress").stringValue = "BullTank/BullTankConfig";
            so.FindProperty("_roarSfx").objectReferenceValue  = AssetDatabase.LoadAssetAtPath<AudioClip>(SfxRoar);
            so.FindProperty("_swingSfx").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(SfxSwing);
            so.FindProperty("_landSfx").objectReferenceValue  = AssetDatabase.LoadAssetAtPath<AudioClip>(SfxLand);
            so.FindProperty("_stompSfx").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(SfxStomp);
            so.FindProperty("_wallSfx").objectReferenceValue  = AssetDatabase.LoadAssetAtPath<AudioClip>(SfxWall);
            so.FindProperty("_nightmareBodyMaterial").objectReferenceValue = black;
            so.ApplyModifiedPropertiesWithoutUndo();

            string path = $"{OutDir}/Prefab/BullTank_Ch{chapter}.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log($"[BullTankBuilder] 프리팹 {path} — {variant}{(vikingSuit ? " + 바이킹 옷" : "")} · {colour} · 높이 {h:0.0} m · 캡슐 반경 {rad:0.00}");
            return path;
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static void Register(AddressableAssetSettings settings, AddressableAssetGroup group, string path, string address)
    {
        string guid = AssetDatabase.AssetPathToGUID(path);
        if (string.IsNullOrEmpty(guid)) return;
        var e = settings.CreateOrMoveEntry(guid, group, false, false);
        e.address = address;
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, e, true);
    }

    private static void EnsureFolder(string path)
    {
        path = path.TrimEnd('/');
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
#endif
