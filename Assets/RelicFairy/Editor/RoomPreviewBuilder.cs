#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 룸 풀 CSV의 방 하나를 <b>에디터에서</b> 그대로 세워 조명·그림자·벽 높이를 눈으로 확인하는 프리뷰 도구.
///
/// 기존 <c>MapDataTestRunner</c>와 다른 점:
///   · 플레이 모드가 필요 없다 (런 부트스트랩 없이 방만 본다)
///   · 실제 챕터 팔레트를 쓴다 (TestPalette 아님)
///   · <c>BuildRoomLights</c>까지 호출한다 — 조명 검증이 목적이므로
///
/// 런타임 경로(GameRunBootstrapper.BuildProcRoomAsync)와 같은 인자 규약을 따르되
/// 등장 연출·스포너·트리거는 뺀다. 프리뷰 루트 이름은 <c>__RoomPreview</c>이며 Clear로 지운다.
/// </summary>
public static class RoomPreviewBuilder
{
    private const string PreviewRootName = "__RoomPreview";
    private const string PoolCsvFmt   = "Assets/RelicFairy/Docs/CHAPTER_{0}_ROOM_POOL.csv";
    private const string PaletteFmt   = "Assets/RelicFairy/Systems/Stage/MapGen/Data/Palettes/BlockPalette_{0}.asset";

    // 챕터 → 팔레트 에셋명. ChapterN.asset의 theme(Forest/Ruins/Sacred/Fortress)이 매칭하는 팔레트다
    // (themeMatch가 에셋명과 달라서 — Cave→Ruins, Castle→Sacred, Throne→Fortress).
    private static readonly string[] PaletteByChapter = { null, "Forest", "Cave", "Castle", "Throne" };

    // 런타임 앵커와 동일 좌표에 세운다(GameRunBootstrapper: StartRunAsync(new Vector3(0,0,2000))).
    // 같은 좌표계에서 실측하려는 것이다.
    private static readonly Vector3 RunAnchor = new(0f, 0f, 2000f);

    [MenuItem("RelicFairy/Map/Preview/Build Ch1 Room")] public static void BuildCh1() => BuildRoom(1);
    [MenuItem("RelicFairy/Map/Preview/Build Ch2 Room")] public static void BuildCh2() => BuildRoom(2);
    [MenuItem("RelicFairy/Map/Preview/Build Ch3 Room")] public static void BuildCh3() => BuildRoom(3);
    [MenuItem("RelicFairy/Map/Preview/Build Ch4 Room")] public static void BuildCh4() => BuildRoom(4);

    private static void BuildRoom(int chapter)
    {
        Clear();

        string poolCsvPath = string.Format(PoolCsvFmt, chapter);
        string palettePath = string.Format(PaletteFmt, PaletteByChapter[chapter]);

        var csv = AssetDatabase.LoadAssetAtPath<TextAsset>(poolCsvPath);
        if (csv == null) { Debug.LogError($"[RoomPreview] 룸 풀 CSV 없음: {poolCsvPath}"); return; }

        var palette = AssetDatabase.LoadAssetAtPath<BlockPalette>(palettePath);
        if (palette == null) { Debug.LogError($"[RoomPreview] 팔레트 없음: {palettePath}"); return; }

        if (!TryReadFirstGridCsv(csv.text, out string poolKey, out string gridCsv))
        {
            Debug.LogError("[RoomPreview] CSV에서 grid_csv를 못 읽었다.");
            return;
        }

        var grid = MapDataLoader.Parse(gridCsv);
        if (grid == null) { Debug.LogError("[RoomPreview] grid 파싱 실패."); return; }

        int w = grid.GetLength(0), h = grid.GetLength(1);
        int wallLayers = palette.WallHeight > 0 ? palette.WallHeight : 8;

        var root = new GameObject(PreviewRootName).transform;
        root.position = RunAnchor;

        MapBuilder.Build(grid, palette, root, 1f, 0f, null, wallLayers);
        if (palette.HasCeiling)
            MapBuilder.BuildCeiling(grid, palette, root, 1f, 0f, wallLayers * 1f);
        MapBuilder.BuildRoomLights(grid, root, 1f, 0f, wallLayers, palette.Lighting);

        Debug.Log($"[RoomPreview] Ch{chapter} '{poolKey}' {w}x{h} · 팔레트 {palette.name}(theme {palette.ThemeMatch}) " +
                  $"· 벽 {wallLayers}층 · 천장 {palette.HasCeiling} · 반경 약 {Mathf.Max(w, h) * 0.5f:0.#}m");
        Selection.activeTransform = root;
    }

    [MenuItem("RelicFairy/Map/Preview/Clear Preview")]
    public static void Clear()
    {
        var existing = GameObject.Find(PreviewRootName);
        if (existing != null) Object.DestroyImmediate(existing);
    }

    /// <summary>CSV 첫 데이터 행의 pool_key / grid_csv를 뽑는다. grid_csv는 줄바꿈을 품은 따옴표 필드다.</summary>
    private static bool TryReadFirstGridCsv(string text, out string poolKey, out string gridCsv)
    {
        poolKey = null; gridCsv = null;

        var header = SplitCsvLine(text, 0, out int cursor);
        int keyIdx  = header.IndexOf("pool_key");
        int gridIdx = header.IndexOf("grid_csv");
        if (keyIdx < 0 || gridIdx < 0) return false;

        var row = SplitCsvLine(text, cursor, out _);
        if (row.Count <= gridIdx) return false;

        poolKey = row[keyIdx];
        gridCsv = row[gridIdx];
        return !string.IsNullOrEmpty(gridCsv);
    }

    /// <summary>따옴표 안의 콤마·줄바꿈을 보존하며 한 행을 파싱한다.</summary>
    private static List<string> SplitCsvLine(string text, int start, out int next)
    {
        var fields = new List<string>();
        var sb = new System.Text.StringBuilder();
        bool quoted = false;
        int i = start;

        for (; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; }
                    else quoted = false;
                }
                else sb.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { fields.Add(sb.ToString().Trim('﻿')); sb.Clear(); }
            else if (c == '\n') { i++; break; }
            else if (c != '\r') sb.Append(c);
        }
        fields.Add(sb.ToString().Trim('﻿'));
        next = i;
        return fields;
    }
}
#endif
