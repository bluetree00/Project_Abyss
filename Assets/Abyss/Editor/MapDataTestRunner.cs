using UnityEditor;
using UnityEngine;
using Cysharp.Threading.Tasks;

public static class MapDataTestRunner
{
    [MenuItem("Tools/Map/Test Load MapData from Backend")]
    public static void TestLoadFromBackend()
    {
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        Debug.Log("[MapDataTest] 뒤끝 CDN에서 MapData 로드 시작...");

        var mgr = new MapDataManager();
        await mgr.InitializeAsync();

        if (!mgr.IsInitialized || mgr.GetAll().Count == 0)
        {
            Debug.LogError("[MapDataTest] 로드 실패 또는 데이터 0건.");
            return;
        }

        Debug.Log($"[MapDataTest] 총 {mgr.GetAll().Count}개 방 로드 완료!");

        foreach (var kv in mgr.GetAll())
        {
            var r = kv.Value;
            Debug.Log($"  [{r.room_id}] {r.category} | {r.theme} | grid={(!string.IsNullOrEmpty(r.grid_csv) ? "CSV" : "RULE")}");

            if (!string.IsNullOrEmpty(r.grid_csv))
            {
                var grid = MapDataLoader.Parse(r.grid_csv);
                if (grid != null)
                {
                    int w = grid.GetLength(0);
                    int h = grid.GetLength(1);
                    var playerPos = MapDataLoader.FindFirst(grid, TileType.PlayerSpawn);
                    var monsters = MapDataLoader.FindAll(grid, TileType.MonsterSpawn);
                    Debug.Log($"    → {w}x{h} | Player={playerPos} | Monsters={monsters.Count}");
                }
            }
        }

        Debug.Log("[MapDataTest] 완료.");
    }

    [MenuItem("Tools/Map/Test Load MapData (Offline JSON)")]
    public static void TestLoadOffline()
    {
        var textAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(
            "Assets/Abyss/Systems/Stage/MapGen/STAGEDATA_MAP.json");

        if (textAsset == null)
        {
            Debug.LogError("[MapDataTest] STAGEDATA_MAP.json not found.");
            return;
        }

        var mgr = new MapDataManager();
        mgr.InitializeFromJson(textAsset.text);

        Debug.Log($"[MapDataTest] 오프라인 로드: {mgr.GetAll().Count}개 방");

        foreach (var kv in mgr.GetAll())
        {
            var r = kv.Value;
            Debug.Log($"  [{r.room_id}] {r.category} | {r.theme} | grid={(!string.IsNullOrEmpty(r.grid_csv) ? "CSV" : "RULE")}");

            if (!string.IsNullOrEmpty(r.grid_csv))
            {
                var grid = MapDataLoader.Parse(r.grid_csv);
                if (grid != null)
                {
                    int w = grid.GetLength(0);
                    int h = grid.GetLength(1);
                    var playerPos = MapDataLoader.FindFirst(grid, TileType.PlayerSpawn);
                    var monsters = MapDataLoader.FindAll(grid, TileType.MonsterSpawn);
                    Debug.Log($"    → {w}x{h} | Player={playerPos} | Monsters={monsters.Count}");
                }
            }
        }

        Debug.Log("[MapDataTest] 완료.");
    }

    [MenuItem("Tools/Map/Test Build Map (battle_001)")]
    public static void TestBuildMap()
    {
        if (!Application.isPlaying)
        {
            Debug.LogError("[MapDataTest] 플레이 모드에서만 실행 가능합니다.");
            return;
        }

        TestBuildMapAsync().Forget();
    }

    private static async UniTaskVoid TestBuildMapAsync()
    {
        // 1. 데이터 로드
        var textAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(
            "Assets/Abyss/Systems/Stage/MapGen/STAGEDATA_MAP.json");
        if (textAsset == null) { Debug.LogError("STAGEDATA_MAP.json not found"); return; }

        var mgr = new MapDataManager();
        mgr.InitializeFromJson(textAsset.text);

        var room = mgr.GetById("battle_001");
        if (room == null) { Debug.LogError("battle_001 not found"); return; }

        // 2. 그리드 파싱
        var grid = MapDataLoader.Parse(room.grid_csv);
        if (grid == null) { Debug.LogError("grid parse failed"); return; }

        // 3. 팔레트 로드
        var palette = AssetDatabase.LoadAssetAtPath<BlockPalette>(
            "Assets/Abyss/Systems/Stage/MapGen/Data/TestPalette.asset");
        if (palette == null) { Debug.LogError("TestPalette not found"); return; }

        // 4. 맵 루트 생성
        var mapRoot = new GameObject("GeneratedMap").transform;

        // 5. 투명 바닥
        int w = grid.GetLength(0);
        int h = grid.GetLength(1);
        var safeFloor = MapBuilder.CreateSafeFloor(w, h, 1f, 0f, mapRoot);

        // 6. 블록 생성
        var blocks = MapBuilder.Build(grid, palette, mapRoot, cellSize: 1f, baseY: 0f);
        Debug.Log($"[MapDataTest] {blocks.Count}개 블록 생성 완료. 연출 시작...");

        // 7. 등장 연출 (entrance 필드로 스타일 분기)
        var entrance = MapEntranceRegistry.Resolve(room.entrance);
        await entrance.PlayAsync(blocks, new MapEntranceContext(room), default);

        // 8. 투명 바닥 제거
        Object.Destroy(safeFloor);

        Debug.Log("[MapDataTest] 맵 생성 + 연출 완료!");
    }
}
