#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using RelicFairy.Monster;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// 리치 이펙트 목록(<see cref="LichVfxSetSO"/>)을 채우고 Addressables에 등록한다.
/// 메뉴: RelicFairy/Boss/Lich/Apply VFX Mapping + Register Addressables
///
/// · 매핑 = 같은 폴더 Editor/LichVfxMapping.json (칸 이름 → 프리팹 경로·배율·수명·메모). 손으로 인스펙터를 채우다
///   칸을 빠뜨리는 일을 막으려고 파일 하나를 정본으로 둔다.
/// · 등록 = 목록 에셋은 <see cref="LichVfx.SetAddress"/>, 각 프리팹은 "Lich/VFX/칸이름"으로 Monsters 그룹에 넣는다
///   (리치 설정·해골 설정과 같은 그룹 — 기존 보스들처럼 설정·프리팹이 Addressables로 로드된다).
/// · _ThirdParty(깃 추적 안 함) 경로의 프리팹은 등록을 거부한다 — 먼저 _Imported로 옮겨야 한다.
/// · 소리 목록(<see cref="LichSfxSetSO"/>)도 같은 메뉴에서 — 매핑 = Editor/LichSfxMapping.json, 주소 <see cref="LichSfx.SetAddress"/>.
///   클립은 목록 에셋이 직접 참조하므로 따로 주소를 붙이지 않는다.
/// </summary>
public static class LichVfxAddressableRegistrar
{
    private const string SetPath     = "Assets/RelicFairy/Characters/Monster/Boss_Monster/Lich/VFX/LichVfxSet.asset";
    private const string MappingPath = "Assets/RelicFairy/Characters/Monster/Boss_Monster/Lich/VFX/Editor/LichVfxMapping.json";
    private const string GroupName   = "Monsters";
    private const string PrefabAddressPrefix = "Lich/VFX/";
    private const string SfxSetPath     = "Assets/RelicFairy/Characters/Monster/Boss_Monster/Lich/Audio/LichSfxSet.asset";
    private const string SfxMappingPath = "Assets/RelicFairy/Characters/Monster/Boss_Monster/Lich/VFX/Editor/LichSfxMapping.json";

    [Serializable]
    private class MappingFile
    {
        public MappingRow[] rows;
        public string       chainMaterial;
        public string       crackMaterial;
    }

    [Serializable]
    private class MappingRow
    {
        public string slot;
        public string path;
        public float  scale;
        public float  lifetime;
        public float  offsetY;
        public string note;
        public float  beamLength;
        public string tint;   // "#RRGGBB" 또는 "#RRGGBBAA" — 비우면 원래 색
    }

    [Serializable]
    private class SfxMappingFile { public SfxRow[] rows; }

    [Serializable]
    private class SfxRow
    {
        public string slot;
        public string path;
        public float  volume;
        public float  startTime;
        public float  pitch;
        public float  minDistance;
        public string note;
    }

    [MenuItem("RelicFairy/Boss/Lich/Apply VFX Mapping + Register Addressables")]
    public static void ApplyAndRegister()
    {
        var set = LoadOrCreateSet();
        if (set == null) return;

        int applied = ApplyMapping(set);
        Register(set);
        Debug.Log($"[LichVfx 등록] 매핑 {applied}칸 반영 · 목록 {set.Entries.Count}칸");

        int sounds = ApplySfxMapping();
        Debug.Log($"[LichSfx 등록] 소리 {sounds}칸 반영");
    }

    private static LichVfxSetSO LoadOrCreateSet()
    {
        var set = AssetDatabase.LoadAssetAtPath<LichVfxSetSO>(SetPath);
        if (set != null) return set;

        Directory.CreateDirectory(Path.GetDirectoryName(SetPath));
        set = ScriptableObject.CreateInstance<LichVfxSetSO>();
        AssetDatabase.CreateAsset(set, SetPath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[LichVfx 등록] 목록 에셋 생성 — {SetPath}");
        return set;
    }

    private static int ApplyMapping(LichVfxSetSO set)
    {
        if (!File.Exists(MappingPath))
        {
            Debug.LogWarning($"[LichVfx 등록] 매핑 파일 없음 — {MappingPath} (목록은 그대로 둔다)");
            return 0;
        }

        var file = JsonUtility.FromJson<MappingFile>(File.ReadAllText(MappingPath));
        if (file?.rows == null) { Debug.LogError("[LichVfx 등록] 매핑 파일을 읽지 못했다"); return 0; }

        var so      = new SerializedObject(set);
        var entries = so.FindProperty("entries");
        entries.ClearArray();

        int applied = 0;
        foreach (var row in file.rows)
        {
            if (!Enum.TryParse(row.slot, out LichVfxSlot slot) || slot == LichVfxSlot.None)
            {
                Debug.LogWarning($"[LichVfx 등록] 모르는 칸 이름: {row.slot}");
                continue;
            }
            if (row.path.Contains("/_ThirdParty/"))
            {
                Debug.LogError($"[LichVfx 등록] {slot}: _ThirdParty 경로는 쓸 수 없다(깃 추적 밖) — 먼저 _Imported로 옮길 것: {row.path}");
                continue;
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(row.path);
            if (prefab == null)
            {
                Debug.LogError($"[LichVfx 등록] {slot}: 프리팹 없음 — {row.path}");
                continue;
            }
            string grab = FindGrabPassShader(prefab);
            if (grab != null)
            {
                Debug.LogError($"[LichVfx 등록] {slot}: GrabPass 셰이더({grab})는 URP에서 화면을 덮는다 — 다른 프리팹을 고를 것: {row.path}");
                continue;
            }

            int i = entries.arraySize;
            entries.InsertArrayElementAtIndex(i);
            var e = entries.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("slot").intValue       = (int)slot;
            e.FindPropertyRelative("prefab").objectReferenceValue = prefab;
            e.FindPropertyRelative("scale").floatValue    = row.scale;
            e.FindPropertyRelative("lifetime").floatValue = row.lifetime;
            e.FindPropertyRelative("offset").vector3Value = new Vector3(0f, row.offsetY, 0f);
            e.FindPropertyRelative("note").stringValue    = row.note ?? string.Empty;
            e.FindPropertyRelative("beamLength").floatValue = row.beamLength;
            e.FindPropertyRelative("tint").colorValue =
                !string.IsNullOrEmpty(row.tint) && ColorUtility.TryParseHtmlString(row.tint, out var tint) ? tint : Color.clear;
            applied++;
        }

        so.FindProperty("chainMaterial").objectReferenceValue = LoadMaterial(file.chainMaterial, "사슬");
        so.FindProperty("crackMaterial").objectReferenceValue = LoadMaterial(file.crackMaterial, "균열");

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();
        return applied;
    }

    private static Material LoadMaterial(string path, string label)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (path.Contains("/_ThirdParty/"))
        {
            Debug.LogError($"[LichVfx 등록] {label} 재질: _ThirdParty 경로는 쓸 수 없다 — {path}");
            return null;
        }
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) Debug.LogError($"[LichVfx 등록] {label} 재질 없음 — {path}");
        return mat;
    }

    private static int ApplySfxMapping()
    {
        if (!File.Exists(SfxMappingPath))
        {
            Debug.LogWarning($"[LichSfx 등록] 매핑 파일 없음 — {SfxMappingPath}");
            return 0;
        }
        var file = JsonUtility.FromJson<SfxMappingFile>(File.ReadAllText(SfxMappingPath));
        if (file?.rows == null) { Debug.LogError("[LichSfx 등록] 매핑 파일을 읽지 못했다"); return 0; }

        var set = AssetDatabase.LoadAssetAtPath<LichSfxSetSO>(SfxSetPath);
        if (set == null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SfxSetPath));
            set = ScriptableObject.CreateInstance<LichSfxSetSO>();
            AssetDatabase.CreateAsset(set, SfxSetPath);
            Debug.Log($"[LichSfx 등록] 목록 에셋 생성 — {SfxSetPath}");
        }

        var so      = new SerializedObject(set);
        var entries = so.FindProperty("entries");
        entries.ClearArray();

        int applied = 0;
        foreach (var row in file.rows)
        {
            if (!Enum.TryParse(row.slot, out LichSfxSlot slot) || slot == LichSfxSlot.None)
            {
                Debug.LogWarning($"[LichSfx 등록] 모르는 칸 이름: {row.slot}");
                continue;
            }
            if (row.path.Contains("/_ThirdParty/"))
            {
                Debug.LogError($"[LichSfx 등록] {slot}: _ThirdParty 경로는 쓸 수 없다 — {row.path}");
                continue;
            }
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(row.path);
            if (clip == null)
            {
                Debug.LogError($"[LichSfx 등록] {slot}: 클립 없음 — {row.path}");
                continue;
            }

            int i = entries.arraySize;
            entries.InsertArrayElementAtIndex(i);
            var e = entries.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("slot").intValue              = (int)slot;
            e.FindPropertyRelative("clip").objectReferenceValue  = clip;
            e.FindPropertyRelative("volume").floatValue          = row.volume;
            e.FindPropertyRelative("startTime").floatValue       = row.startTime;
            e.FindPropertyRelative("pitch").floatValue           = row.pitch;
            e.FindPropertyRelative("minDistance").floatValue     = row.minDistance;
            e.FindPropertyRelative("note").stringValue           = row.note ?? string.Empty;
            applied++;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();

        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings != null)
        {
            var group = settings.FindGroup(GroupName) ?? settings.DefaultGroup;
            var entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(SfxSetPath), group, false, false);
            entry.address = LichSfx.SetAddress;
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, null, true);
            AssetDatabase.SaveAssets();
        }
        return applied;
    }

    /// <summary>프리팹 렌더러가 쓰는 셰이더 중 GrabPass를 쓰는 것의 경로(없으면 null).</summary>
    private static string FindGrabPassShader(GameObject prefab)
    {
        var checkedPaths = new HashSet<string>();
        foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
        {
            foreach (var m in r.sharedMaterials)
            {
                if (m == null || m.shader == null) continue;
                string path = AssetDatabase.GetAssetPath(m.shader);
                if (string.IsNullOrEmpty(path) || !path.EndsWith(".shader") || !checkedPaths.Add(path)) continue;
                if (File.ReadAllText(path).Contains("GrabPass")) return path;
            }
        }
        return null;
    }

    private static void Register(LichVfxSetSO set)
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) { Debug.LogError("[LichVfx 등록] AddressableAssetSettings 없음"); return; }

        var group = settings.FindGroup(GroupName) ?? settings.DefaultGroup;

        var setEntry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(SetPath), group, false, false);
        setEntry.address = LichVfx.SetAddress;

        var seen = new HashSet<string>();
        int count = 0;
        foreach (var e in set.Entries)
        {
            if (e.prefab == null) continue;
            string path = AssetDatabase.GetAssetPath(e.prefab);
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (!seen.Add(guid)) continue;   // 같은 프리팹을 여러 칸이 쓰면 주소는 첫 칸 이름으로

            // 다른 그룹에 이미 주소가 있으면(플레이어 이펙트 공유 등) 옮기지 않는다.
            var existing = settings.FindAssetEntry(guid);
            if (existing != null && existing.parentGroup != group)
            {
                Debug.Log($"[LichVfx 등록] {e.slot}: 이미 '{existing.parentGroup.Name}' 그룹에 '{existing.address}'로 등록됨 — 그대로 둔다");
                continue;
            }

            var entry = settings.CreateOrMoveEntry(guid, group, false, false);
            entry.address = PrefabAddressPrefix + e.slot;
            count++;
        }

        // 이 도구가 붙였던 주소 중 목록에서 빠진 프리팹(칸 교체)은 등록을 거둔다 — 같은 주소가 둘 남지 않게.
        var stale = new List<string>();
        foreach (var entry in group.entries)
            if (entry.address.StartsWith(PrefabAddressPrefix) && !seen.Contains(entry.guid)
                && entry.address != LichVfx.SetAddress)
                stale.Add(entry.guid);
        foreach (var guid in stale)
            settings.RemoveAssetEntry(guid, false);
        if (stale.Count > 0)
            Debug.Log($"[LichVfx 등록] 목록에서 빠진 프리팹 {stale.Count}개의 주소를 거뒀다");

        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, null, true);
        AssetDatabase.SaveAssets();
        Debug.Log($"[LichVfx 등록] Addressables — 목록 1 + 프리팹 {count}개 (그룹 '{group.Name}')");
    }
}
#endif
