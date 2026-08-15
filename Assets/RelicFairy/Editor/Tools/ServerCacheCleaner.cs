using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 테스트 환경에서 서버 데이터 캐시를 강제 삭제하는 에디터 도구.
/// 출시에는 stat_version 체크로 자동 갱신되므로 평소엔 쓸 일 없음.
/// </summary>
public static class ServerCacheCleaner
{
    private static readonly string[] CacheFiles =
    {
        "equipment_data.json",          // ServerEquipmentDataManager
        "chapter_data.json",            // ChapterDataManager
        "run_structure.json",           // RunStructureDataManager (런 구조 — boss_threshold 등)
        "map_data.json",                // MapDataManager
        "monster_data.json",            // MonsterDataManager
        "monster_element_stat_data.json", // ServerMonsterStatDataManager
        "item_data.json",               // ItemDataManager
        "buff_data.json",               // BuffDataManager
        "passive_data.json",            // PlayerDataManager (passive)
        "character_data.json",          // PlayerDataManager (character stat)
        "merlin_rune_synergy_data.json", // BlockDataManager (synergy)
        "merlin_rune_piece_data.json",  // BlockDataManager (piece)
        "merlin_rune_zone_map.json",    // BlockDataManager (zone map)
        "relic_awakening_data.json",    // RelicAwakeningDataManager
    };

    private const string ZoneLayoutPrefix = "zone_layout_"; // ZoneLayoutManager

    [MenuItem("RelicFairy/Dev/Cache/Clear Equipment Cache")]
    public static void ClearEquipment()
    {
        DeleteOne("equipment_data.json");
    }

    [MenuItem("RelicFairy/Dev/Cache/Clear Zone Layout Cache")]
    public static void ClearZoneLayouts()
    {
        int deleted = DeleteByPrefix(ZoneLayoutPrefix);
        Debug.Log($"[CacheCleaner] zone_layout_* {deleted}개 삭제 완료");
    }

    [MenuItem("RelicFairy/Dev/Cache/Clear All")]
    public static void ClearAll()
    {
        int deleted = 0;
        foreach (var name in CacheFiles)
            if (DeleteOne(name)) deleted++;

        deleted += DeleteByPrefix(ZoneLayoutPrefix);

        Debug.Log($"[CacheCleaner] 총 {deleted}개 캐시 삭제 완료");
    }

    [MenuItem("RelicFairy/Dev/Cache/Open persistentDataPath")]
    public static void OpenFolder()
    {
        EditorUtility.RevealInFinder(Application.persistentDataPath);
    }

    private static bool DeleteOne(string fileName)
    {
        string path = Path.Combine(Application.persistentDataPath, fileName);
        if (!File.Exists(path))
        {
            Debug.Log($"[CacheCleaner] 없음: {fileName}");
            return false;
        }
        File.Delete(path);
        Debug.Log($"[CacheCleaner] 삭제: {fileName}");
        return true;
    }

    private static int DeleteByPrefix(string prefix)
    {
        int deleted = 0;
        var dir = Application.persistentDataPath;
        foreach (var file in Directory.GetFiles(dir, $"{prefix}*.json"))
        {
            File.Delete(file);
            Debug.Log($"[CacheCleaner] 삭제: {Path.GetFileName(file)}");
            deleted++;
        }
        return deleted;
    }
}
