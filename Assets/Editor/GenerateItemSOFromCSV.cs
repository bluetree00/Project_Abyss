using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// CSV(ITEM_DATA)에서 ItemSO 껍데기를 자동 생성하고 ItemSODatabase에 등록.
/// 메뉴: Tools/Item/Generate SO from CSV
///
/// - 이미 존재하는 SO는 건너뜀 (덮어쓰기 안 함)
/// - displayName은 CSV description 또는 itemId에서 파싱
/// - icon은 null — 나중에 에디터에서 할당
/// </summary>
public static class GenerateItemSOFromCSV
{
    private const string SOFolder = "Assets/RelicFairy/Shared/Item/SOdata";
    private const string DatabasePath = "Assets/RelicFairy/Shared/Item/SOdata/ItemSODatabase.asset";

    [MenuItem("RelicFairy/Gameplay/Item/Generate SO from CSV")]
    public static void Generate()
    {
        // CSV 로드
        var json = Resources.Load<TextAsset>("ITEM_DATA");
        if (json == null)
        {
            Debug.LogError("[GenerateItemSO] Resources/ITEM_DATA.json not found.");
            return;
        }

        var col = JsonUtility.FromJson<ItemEntryCollection>(json.text);
        if (col?.items == null)
        {
            Debug.LogError("[GenerateItemSO] CSV 파싱 실패.");
            return;
        }

        // itemId별 첫 엔트리 수집 (메타 정보용)
        var metaMap = new Dictionary<string, ItemEntry>();
        foreach (var entry in col.items)
        {
            string id = !string.IsNullOrEmpty(entry.item_id) ? entry.item_id : entry.passive_id;
            if (string.IsNullOrEmpty(id)) continue;
            entry.item_id = id;

            if (!metaMap.ContainsKey(id))
                metaMap[id] = entry;
        }

        if (!Directory.Exists(SOFolder))
            Directory.CreateDirectory(SOFolder);

        // 기존 SO 스캔
        var existingIds = new HashSet<string>();
        var existingSOs = AssetDatabase.FindAssets("t:ItemSO", new[] { SOFolder });
        foreach (var guid in existingSOs)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var so = AssetDatabase.LoadAssetAtPath<ItemSO>(path);
            if (so != null && !string.IsNullOrEmpty(so.itemId))
                existingIds.Add(so.itemId);
        }

        // SO 생성
        int created = 0;
        var allSOs = new List<ItemSO>();

        // 기존 SO 먼저 수집 (삭제된 에셋 null 필터)
        foreach (var guid in existingSOs)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var so = AssetDatabase.LoadAssetAtPath<ItemSO>(path);
            if (so != null && !string.IsNullOrEmpty(so.itemId)) allSOs.Add(so);
        }

        foreach (var kvp in metaMap)
        {
            string itemId = kvp.Key;
            var meta = kvp.Value;

            if (existingIds.Contains(itemId)) continue;

            var newSO = ScriptableObject.CreateInstance<ItemSO>();
            newSO.itemId = itemId;

            // displayName: description에서 가져오거나 itemId 변환
            if (!string.IsNullOrEmpty(meta.description))
                newSO.displayName = meta.description;
            else
                newSO.displayName = itemId.Replace("item_", "").Replace("_", " ");

            // rarity
            newSO.rarity = meta.rarity switch
            {
                "Rare" => ItemRarity.Rare,
                "Epic" => ItemRarity.Epic,
                _ => ItemRarity.Common,
            };

            // category
            newSO.category = meta.category switch
            {
                "Necklace" => ItemCategory.Necklace,
                "Boots" => ItemCategory.Boots,
                "Gloves" => ItemCategory.Gloves,
                "Belt" => ItemCategory.Belt,
                "Charm" => ItemCategory.Charm,
                "Active" => ItemCategory.Active,
                _ => ItemCategory.Ring,
            };

            newSO.shapeId = meta.shape_id;

            string fileName = itemId.Replace(".", "_");
            string assetPath = $"{SOFolder}/{fileName}.asset";
            AssetDatabase.CreateAsset(newSO, assetPath);
            allSOs.Add(newSO);
            created++;
        }

        // ItemSODatabase 생성 or 갱신
        var db = AssetDatabase.LoadAssetAtPath<ItemSODatabase>(DatabasePath);
        if (db == null)
        {
            db = ScriptableObject.CreateInstance<ItemSODatabase>();
            AssetDatabase.CreateAsset(db, DatabasePath);
        }

        // 리플렉션으로 private items 리스트에 접근
        var field = typeof(ItemSODatabase).GetField("items",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (field != null)
        {
            field.SetValue(db, allSOs);
            EditorUtility.SetDirty(db);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[GenerateItemSO] {created}개 SO 생성, 총 {allSOs.Count}개 → Database 등록 완료");
    }
}
