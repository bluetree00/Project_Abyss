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
        "equipment_data.json",
        "element_effect_data.json",
        "chapter_data.json",
        "map_data.json",
        "monster_data.json",
        "item_data.json",
        "buff_data.json",
        "passive_data.json",
        "player_data.json",
        "block_grid_data.json",
        "block_shape_data.json",
        "GraphData.json",
        "StageData.json",
    };

    [MenuItem("Tools/Abyss/Cache/Clear Equipment Cache")]
    public static void ClearEquipment()
    {
        DeleteOne("equipment_data.json");
    }

    [MenuItem("Tools/Abyss/Cache/Clear All Server Caches")]
    public static void ClearAll()
    {
        int deleted = 0;
        foreach (var name in CacheFiles)
            if (DeleteOne(name)) deleted++;
        Debug.Log($"[CacheCleaner] 총 {deleted}개 캐시 삭제 완료");
    }

    [MenuItem("Tools/Abyss/Cache/Open persistentDataPath")]
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
}
