using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// ItemSO를 등급/트리거 기반으로 폴더 분류.
/// 메뉴: Tools/Item/Organize SO by Rarity+Trigger
///
/// 등급 추정: CSV value 합계 기반
///   Epic: value합 ≥ 20 또는 효과 2개 이상
///   Rare: value합 ≥ 5
///   Common: 나머지
/// </summary>
public static class OrganizeItemSO
{
    private const string RootFolder = "Assets/Abyss/Shared/Item/SOdata";

    // 트리거 → 폴더명 매핑
    private static string GetTriggerFolder(string trigger) => trigger switch
    {
        "OnHit" or "OnKill" => "Combat",
        "OnRoomClear" or "OnBossClear" or "OnRecipeComplete" => "Progress",
        "OnRollEnd" or "OnRollLand" or "OnJumpLand" => "Movement",
        "OnNearDeath" => "Survival",
        "OnItemPickup" or "OnPickup" => "Pickup",
        _ => "Passive", // Always, HPBelow, WithXWeapon, WithShield 등
    };

    // 등급 추정
    private static ItemRarity EstimateRarity(List<ItemEntry> entries)
    {
        if (entries == null || entries.Count == 0) return ItemRarity.Common;

        float totalValue = 0f;
        int slotCount = 0;

        foreach (var e in entries)
        {
            if (string.IsNullOrEmpty(e.effect_type)) continue;
            totalValue += Mathf.Abs(e.value);
            slotCount++;
        }

        if (slotCount >= 2 || totalValue >= 20f) return ItemRarity.Epic;
        if (totalValue >= 5f) return ItemRarity.Rare;
        return ItemRarity.Common;
    }

    [MenuItem("Tools/Item/Organize SO by Rarity+Trigger")]
    public static void Organize()
    {
        // CSV 로드
        var json = Resources.Load<TextAsset>("ITEM_DATA");
        if (json == null) { Debug.LogError("[OrganizeItemSO] ITEM_DATA not found"); return; }

        var col = JsonUtility.FromJson<ItemEntryCollection>(json.text);
        if (col?.items == null) { Debug.LogError("[OrganizeItemSO] 파싱 실패"); return; }

        // itemId별 엔트리 그룹핑
        var entryMap = new Dictionary<string, List<ItemEntry>>();
        var triggerMap = new Dictionary<string, string>(); // itemId → 대표 trigger

        foreach (var entry in col.items)
        {
            string id = !string.IsNullOrEmpty(entry.item_id) ? entry.item_id : entry.passive_id;
            if (string.IsNullOrEmpty(id)) continue;

            if (!entryMap.TryGetValue(id, out var list))
            {
                list = new List<ItemEntry>();
                entryMap[id] = list;
            }
            list.Add(entry);

            // slot 1의 trigger를 대표로 사용
            if (entry.slot == 1 && !string.IsNullOrEmpty(entry.trigger))
                triggerMap[id] = entry.trigger;
        }

        // SO 파일 이동
        var soGuids = AssetDatabase.FindAssets("t:ItemSO", new[] { RootFolder });
        int moved = 0;

        foreach (var guid in soGuids)
        {
            var oldPath = AssetDatabase.GUIDToAssetPath(guid);
            var so = AssetDatabase.LoadAssetAtPath<ItemSO>(oldPath);
            if (so == null || string.IsNullOrEmpty(so.itemId)) continue;

            // Database는 이동하지 않음
            if (oldPath.Contains("ItemSODatabase")) continue;

            // 등급 추정 + SO에 반영
            entryMap.TryGetValue(so.itemId, out var entries);
            var rarity = EstimateRarity(entries);
            so.rarity = rarity;

            // 트리거 폴더
            triggerMap.TryGetValue(so.itemId, out var trigger);
            string triggerFolder = GetTriggerFolder(trigger ?? "Always");

            // 등급 폴더
            string rarityFolder = rarity.ToString();

            // 목표 폴더
            string targetDir = $"{RootFolder}/{rarityFolder}/{triggerFolder}";
            if (!AssetDatabase.IsValidFolder(targetDir))
            {
                string rarityDir = $"{RootFolder}/{rarityFolder}";
                if (!AssetDatabase.IsValidFolder(rarityDir))
                    AssetDatabase.CreateFolder(RootFolder, rarityFolder);
                AssetDatabase.CreateFolder(rarityDir, triggerFolder);
            }

            string fileName = Path.GetFileName(oldPath);
            string newPath = $"{targetDir}/{fileName}";

            if (oldPath == newPath) continue;

            var result = AssetDatabase.MoveAsset(oldPath, newPath);
            if (string.IsNullOrEmpty(result))
            {
                EditorUtility.SetDirty(so);
                moved++;
            }
            else
            {
                Debug.LogWarning($"[OrganizeItemSO] 이동 실패: {oldPath} → {newPath}: {result}");
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[OrganizeItemSO] {moved}개 SO 이동 완료");

        // Database 재생성
        EditorApplication.delayCall += () =>
        {
            var menuType = typeof(GenerateItemSOFromCSV);
            var method = menuType.GetMethod("Generate", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            method?.Invoke(null, null);
        };
    }
}
