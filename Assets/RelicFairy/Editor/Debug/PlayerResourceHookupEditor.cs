using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// [데이터 적용 · 편집 모드] 코드는 부르는데 에셋이 없어 조용히 비어 있던 곳을 프로젝트 안 리소스로 채운다(09-25).
///
/// 1) 랜슬롯 Q 전용 검 <c>Relic/Lancelot/QSword</c> — 캐릭터 팩(플레이어 모델과 같은 팩)의 검을 복사해 붉은 보석 재질(랜슬롯 계열색)로.
///    무기 팩(WeaponObject)의 검 6종은 모두 플레이어 무기가 쓰고 있다.
/// 2) 효과음 — 이벤트 표(<c>SoundEventTable</c>)가 없어 문 열림·골드·아이템 소리가 전부 꺼져 있었다(AppBootstrapper가 주소로 찾는다).
///    표를 만들고, 플레이어 공격·스킬·유물 소리(기존 소리를 잘라 피치를 옮긴 사본)와 팩 UI 소리를 등록한다.
/// 3) 무형일섬 마무리 이펙트 — 빨강(색 규약상 피격·즉시 회피 색) → Temp/iasen_finish_choice.txt에 적힌 프리팹(렌더 비교로 고른다).
///
/// 몇 번 돌려도 같은 결과.
/// </summary>
public static class PlayerResourceHookupEditor
{
    private const string WeaponsGroup = "Weapons";
    private const string SoundGroup   = "Default Local Group";
    private const string EffectsGroup = "Effects";

    // ── 1) 랜슬롯 Q 검 ─────────────────────────────────────────────
    private const string QSwordSource   = "Assets/CombatGirlsCharacterPack/CombatGirl_Shield/Weapons/Weapon_Sword.prefab";
    private const string QSwordMaterial = "Assets/CombatGirlsCharacterPack/CombatGirl_Shield/Materials/Weapon/Weapon_Shield_Sword_03.mat";
    private const string QSwordPrefab   = "Assets/RelicFairy/Systems/Relic/Prefabs/Lancelot_QSword.prefab";
    private const string QSwordAddress  = "Relic/Lancelot/QSword";

    // ── 2) 효과음 ──────────────────────────────────────────────────
    private const string TablePath    = "Assets/RelicFairy/Systems/Sound/SoundEventTable.asset";
    private const string TableAddress = "SoundEventTable";
    private const string AttackSfx    = "Assets/RelicFairy/Characters/Player/Sound/Attack/";
    private const string RelicSfx     = "Assets/RelicFairy/Systems/Relic/Sound/";
    private const string UiSfx        = "Assets/RelicFairy/Prefabs/UI/Bamao/BamaoUIPack/Sound/SoundFx/UI/";

    private static readonly (string address, string clip)[] Clips =
    {
        ("sfx_player_swing",       AttackSfx + "sfx_player_swing.wav"),
        ("sfx_player_swing_heavy", AttackSfx + "sfx_player_swing_heavy.wav"),
        ("sfx_player_shot",        AttackSfx + "sfx_player_shot.wav"),
        ("sfx_player_skill",       AttackSfx + "sfx_player_skill.wav"),
        ("sfx_player_finisher",    AttackSfx + "sfx_player_finisher.wav"),
        ("sfx_relic_sun_fall",     RelicSfx  + "sfx_relic_sun_fall.wav"),
        ("sfx_relic_sun_impact",   RelicSfx  + "sfx_relic_sun_impact.wav"),
        (SoundKey.Sfx.GoldPickup,  UiSfx     + "Coin Pop.wav"),
        (SoundKey.Sfx.ItemPickup,  UiSfx     + "Cartoon Pop.wav"),
    };

    // 이벤트 → (효과음 주소, 볼륨, 피치 흔들림). 볼륨 기준: 몬스터 적중음(볼륨 1, 정점 약 −8dB)보다 휘두름이 작게.
    private static readonly (string evt, string key, float volume, float jitter)[] Events =
    {
        (SoundEvent.PlayerSwing,      "sfx_player_swing",       0.30f, 0.08f),
        (SoundEvent.PlayerSwingHeavy, "sfx_player_swing_heavy", 0.40f, 0.06f),
        (SoundEvent.PlayerShot,       "sfx_player_shot",        0.30f, 0.10f),
        (SoundEvent.PlayerSkill,      "sfx_player_skill",       0.50f, 0.05f),
        (SoundEvent.PlayerFinisher,   "sfx_player_finisher",    0.55f, 0.04f),
        (SoundEvent.RelicSunFall,     "sfx_relic_sun_fall",     0.70f, 0f),
        (SoundEvent.RelicSunImpact,   "sfx_relic_sun_impact",   0.85f, 0f),
        (SoundEvent.DoorOpen,         SoundKey.Sfx.OpenDoor,    0.55f, 0f),   // 이미 등록된 문 열림 소리
        (SoundEvent.GoldPickup,       SoundKey.Sfx.GoldPickup,  0.45f, 0.08f),
        (SoundEvent.ItemPickup,       SoundKey.Sfx.ItemPickup,  0.60f, 0.05f),
    };

    // ── 3) 무형일섬 마무리 ─────────────────────────────────────────
    private const string IasenChoiceFile = "Temp/iasen_finish_choice.txt";
    private const string IasenAsset      = "Assets/RelicFairy/Weapon/Nameless/Data/NamelessBase/NamelessIasen.asset";
    private const string IasenAddress    = "NamelessIasenFinish";

    [MenuItem("RelicFairy/Debug/플레이어 리소스 연결 — 랜슬롯 검·효과음 표 (편집 모드)")]
    private static void ApplySwordAndSound()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) { Debug.LogWarning("[리소스연결] Addressables 설정 없음 — 중단"); return; }

        // 1) 랜슬롯 Q 검
        if (AssetDatabase.LoadAssetAtPath<GameObject>(QSwordPrefab) == null
            && !AssetDatabase.CopyAsset(QSwordSource, QSwordPrefab))
        { Debug.LogWarning("[리소스연결] 검 프리팹 복사 실패: " + QSwordSource); return; }
        var mat  = AssetDatabase.LoadAssetAtPath<Material>(QSwordMaterial);
        var root = PrefabUtility.LoadPrefabContents(QSwordPrefab);
        try
        {
            if (mat != null && root.TryGetComponent<MeshRenderer>(out var mr)) mr.sharedMaterial = mat;
            root.name = Path.GetFileNameWithoutExtension(QSwordPrefab);
            PrefabUtility.SaveAsPrefabAsset(root, QSwordPrefab);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        if (!Register(settings, WeaponsGroup, QSwordPrefab, QSwordAddress)) return;

        // 2) 효과음 — 소리 등록 → 표 생성·채움 → 표 등록
        foreach (var (address, clip) in Clips)
            if (!Register(settings, SoundGroup, clip, address)) return;

        var table = AssetDatabase.LoadAssetAtPath<SoundEventTableSO>(TablePath);
        if (table == null)
        {
            table = ScriptableObject.CreateInstance<SoundEventTableSO>();
            AssetDatabase.CreateAsset(table, TablePath);
        }
        var so      = new SerializedObject(table);
        var entries = so.FindProperty("_entries");
        entries.arraySize = Events.Length;
        for (int i = 0; i < Events.Length; i++)
        {
            var e = entries.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("eventId").stringValue    = Events[i].evt;
            e.FindPropertyRelative("sfxKey").stringValue     = Events[i].key;
            e.FindPropertyRelative("volume").floatValue      = Events[i].volume;
            e.FindPropertyRelative("pitchJitter").floatValue = Events[i].jitter;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(table);
        if (!Register(settings, SoundGroup, TablePath, TableAddress)) return;

        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, null, true, true);
        AssetDatabase.SaveAssets();
        Debug.Log($"[리소스연결] 완료 — 검 1 · 소리 {Clips.Length} · 이벤트 {Events.Length}");
    }

    [MenuItem("RelicFairy/Debug/플레이어 리소스 연결 — 무형일섬 마무리 교체 (편집 모드)")]
    private static void ApplyIasenFinish()
    {
        if (!File.Exists(IasenChoiceFile)) { Debug.LogWarning("[리소스연결] 선택 파일 없음: " + IasenChoiceFile); return; }
        string prefab = File.ReadAllText(IasenChoiceFile).Trim();
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null || !Register(settings, EffectsGroup, prefab, IasenAddress)) return;
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, null, true, true);

        var skill = AssetDatabase.LoadAssetAtPath<ScriptableObject>(IasenAsset);
        var prop  = skill != null ? new SerializedObject(skill).FindProperty("slashEffectKey") : null;
        if (prop == null) { Debug.LogWarning("[리소스연결] 무형일섬 데이터·필드 없음"); return; }
        string before = prop.stringValue;
        prop.stringValue = IasenAddress;
        prop.serializedObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(skill);
        AssetDatabase.SaveAssets();
        Debug.Log($"[리소스연결] 무형일섬 slashEffectKey: {before} → {IasenAddress} ← {Path.GetFileName(prefab)}");
    }

    private static bool Register(AddressableAssetSettings settings, string groupName, string path, string address)
    {
        var group = settings.FindGroup(groupName);
        string guid = AssetDatabase.AssetPathToGUID(path);
        if (group == null || string.IsNullOrEmpty(guid))
        {
            Debug.LogWarning($"[리소스연결] 등록 실패 — 그룹 '{groupName}' 또는 에셋 없음: {path}");
            return false;
        }
        var entry = settings.CreateOrMoveEntry(guid, group, readOnly: false, postEvent: false);
        entry.address = address;
        Debug.Log($"[리소스연결] 등록 {address} ← {Path.GetFileName(path)} ({groupName})");
        return true;
    }
}
