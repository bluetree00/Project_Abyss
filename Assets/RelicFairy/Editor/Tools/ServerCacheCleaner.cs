using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 테스트 환경에서 서버 데이터 캐시를 강제 삭제하는 에디터 도구.
///
/// ■ 왜 필요한가
///   차트 매니저는 ① persistentDataPath 캐시 → ② CDN 순으로 읽고,
///   AddEntry가 `신규.stat_version &lt;= 기존.stat_version`이면 CDN 행을 버린다.
///   stat_version을 고정 운용(=버전을 올리지 않음)하므로, 갱신을 받으려면
///   <b>캐시를 지워 비교 대상 자체를 없애는</b> 것이 정규 절차다.
///   → 그래서 이 목록에서 빠진 차트는 "영원히 갱신되지 않는다". 누락에 주의할 것.
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
        "relic_stat_data.json",         // RelicStatDataManager      — 2026-09-14 누락 보강
        "relic_parts_data.json",        // RelicPartsDataManager     — 2026-09-14 누락 보강
        "weapon_parts_data.json",       // WeaponPartsDataManager    — 2026-09-14 누락 보강
        "covenant_stat_data.json",      // (로더 제거됨) 고아 캐시 정리
    };

    private const string ZoneLayoutPrefix = "zone_layout_"; // ZoneLayoutManager

    /// <summary>뒤끝 CDN 원본 캐시. 지워야 다음 로드에서 서버 재다운로드가 확실해진다.</summary>
    private const string BackendCdnFile = "backend_cdn.dat";

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

        // 뒤끝 CDN 원본까지 비운다 — 차트 캐시만 지우면 같은 내용을 로컬 .dat에서 다시 읽을 수 있다.
        if (DeleteOne(BackendCdnFile)) deleted++;

        // ChartLoader의 정적 딕셔너리도 비운다. 에디터는 도메인 리로드마다 자동으로 비지만,
        // 리로드 없이 연속 실행할 때는 남아 있어 옛 내용을 그대로 쓴다.
        ChartLoader.ClearCache();

        Debug.Log($"[CacheCleaner] 총 {deleted}개 캐시 삭제 + ChartLoader 정적 캐시 초기화 완료");
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
