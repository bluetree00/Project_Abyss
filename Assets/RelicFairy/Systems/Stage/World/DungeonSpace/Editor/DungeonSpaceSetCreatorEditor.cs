using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// S4 던전 공간 세트(<see cref="DungeonSpaceSetSO"/>) 만들기 · 갱신 + Addressable 등록.
/// 값은 아래 표가 정본 — 메뉴를 다시 돌리면 에셋을 이 표로 덮어쓴다(손으로 고친 값은 사라진다).
/// </summary>
public static class DungeonSpaceSetCreatorEditor
{
    private const string AssetPath = "Assets/RelicFairy/Systems/Stage/World/DungeonSpace/DungeonSpaceSet.asset";
    private const string GroupName = "Effects";
    private const string ShaftMaterialPath = "Assets/RelicFairy/Prefabs/Stage/Mat_VoidDust.mat";   // 가산 · 양면 파티클 재질(베이스캠프 허공 먼지)
    // 대기방 도착 마법진(스폰 칸) — Hovl Magic circles 반복판. 챕터 빛깔: 숲 = 숲 궁수 · 화룡 = 불 · 기사 = 해 · 꼭대기 = 해 여럿
    private const string CircleDir = "Assets/RelicFairy/_Imported/EffectSource/Hovl Studio/Magic circles/Prefabs/Loop version/";
    private static readonly (ChapterId ch, string file, float scale)[] SpawnMarks =
    {
        (ChapterId.Chapter1, "Magic circle forest archer loop.prefab", 0.55f),
        (ChapterId.Chapter2, "Magic circle fire loop.prefab",          0.55f),
        (ChapterId.Chapter3, "Magic circle sun loop.prefab",           0.55f),
        (ChapterId.Chapter4, "Magic circle suns loop.prefab",          0.55f),
    };

    // 챕터 어둠 — 1층 숲(짙은 청록) · 2층 화룡(그을린 적갈) · 3층 기사(남회) · 꼭대기(짙은 보라)
    // 빛줄기(S4-3) — 숲 연초록 · 화룡 붉은 · 기사 스테인드글라스 금청 · 꼭대기 금백
    private static readonly (ChapterId ch, Color voidC, Color shell, Color pit, Color shaft)[] Table =
    {
        (ChapterId.Chapter1, new Color(0.020f, 0.045f, 0.045f), new Color(0.22f, 0.30f, 0.28f), new Color(0.06f, 0.09f, 0.08f), new Color(0.62f, 0.92f, 0.62f)),
        (ChapterId.Chapter2, new Color(0.050f, 0.025f, 0.020f), new Color(0.30f, 0.20f, 0.17f), new Color(0.10f, 0.05f, 0.04f), new Color(1.00f, 0.50f, 0.28f)),
        (ChapterId.Chapter3, new Color(0.025f, 0.030f, 0.050f), new Color(0.22f, 0.24f, 0.30f), new Color(0.06f, 0.07f, 0.10f), new Color(0.80f, 0.86f, 1.00f)),
        (ChapterId.Chapter4, new Color(0.035f, 0.020f, 0.055f), new Color(0.24f, 0.20f, 0.30f), new Color(0.08f, 0.05f, 0.11f), new Color(1.00f, 0.90f, 0.66f)),
    };

    [MenuItem("RelicFairy/Stage/던전 공간 세트 만들기·갱신")]
    public static void Build()
    {
        var set = AssetDatabase.LoadAssetAtPath<DungeonSpaceSetSO>(AssetPath);
        if (set == null)
        {
            set = ScriptableObject.CreateInstance<DungeonSpaceSetSO>();
            AssetDatabase.CreateAsset(set, AssetPath);
        }

        var so = new SerializedObject(set);
        var entries = so.FindProperty("entries");
        entries.arraySize = Table.Length;
        for (int i = 0; i < Table.Length; i++)
        {
            var e = entries.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("chapter").enumValueIndex = (int)Table[i].ch - 1;
            e.FindPropertyRelative("voidColor").colorValue   = Table[i].voidC;
            e.FindPropertyRelative("shellTint").colorValue   = Table[i].shell;
            e.FindPropertyRelative("pitTint").colorValue     = Table[i].pit;
            e.FindPropertyRelative("shaftColor").colorValue  = Table[i].shaft;
        }
        for (int i = 0; i < entries.arraySize; i++)
        {
            var e = entries.GetArrayElementAtIndex(i);
            var ch = (ChapterId)(e.FindPropertyRelative("chapter").enumValueIndex + 1);
            foreach (var m in SpawnMarks)
            {
                if (m.ch != ch) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CircleDir + m.file);
                if (prefab == null) Debug.LogWarning($"[DungeonSpaceSet] 도착 마법진 없음: {m.file}");
                e.FindPropertyRelative("spawnMark").objectReferenceValue = prefab;
                e.FindPropertyRelative("spawnMarkScale").floatValue     = m.scale;
            }
        }
        var shaftMat = AssetDatabase.LoadAssetAtPath<Material>(ShaftMaterialPath);
        if (shaftMat == null) Debug.LogWarning($"[DungeonSpaceSet] 빛줄기 재질 없음: {ShaftMaterialPath}");
        so.FindProperty("shaftMaterial").objectReferenceValue = shaftMat;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();

        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) { Debug.LogWarning("[DungeonSpaceSet] Addressable Settings 없음 — 수동 등록 필요"); return; }
        var group = settings.FindGroup(GroupName) ?? settings.DefaultGroup;
        var entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(AssetPath), group, false, false);
        entry.address = DungeonSpaceSetSO.Address;
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true);
        AssetDatabase.SaveAssets();
        Debug.Log($"[DungeonSpaceSet] 갱신 · Addressable 등록: {entry.address} ({group.Name}) · 항목 {Table.Length}");
    }
}
