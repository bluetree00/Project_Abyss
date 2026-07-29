using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// BossArena 하위 기존 오브젝트(Floor, Wall, Pillar)를
/// FantasyCastle 에셋으로 1:1 교체한다.
/// 기존 BoxCollider 데이터로 크기를 추론해 타일을 깔고,
/// 기둥은 동일 위치에 SM_Big_support_pillar로 교체한다.
/// Menu: RelicFairy → Replace BossArena with Castle Assets
/// </summary>
public static class BossArenaSetupTool
{
    // ── FantasyCastle 에셋 GUID ────────────────────────────────────────────
    private const string G_Floor1    = "abc00000000017806285891072112065"; // SM_InteriorFloor_1
    private const string G_Floor2    = "abc00000000013326756541527823674"; // SM_InteriorFloor_2
    private const string G_BigPillar = "abc00000000001787116979384913304"; // SM_Big_support_pillar
    private const string G_Pillar    = "abc00000000001192071775915216481"; // SM_Pillar
    private const string G_Wall02    = "abc00000000001419077956115737462"; // SM_InteriorWall_A_02
    private const string G_Wall03    = "abc00000000006330598960719143503"; // SM_InteriorWall_A_03
    private const string G_CeilA     = "abc00000000003551554089420198536"; // SM_ceiling_A_01
    private const string G_CeilB     = "abc00000000004697412823939738453"; // SM_ceiling_B_01
    private const string G_Chandelier= "abc00000000016555180349305040200"; // SM_chandelier
    private const string G_GroundTorch= "abc00000000014629984655225578207"; // SM_ground_torch
    private const string G_Torch     = "abc00000000007134588715427654109"; // SM_torch
    private const string G_Gargoyle1 = "abc00000000007343807900967935473"; // SM_Gargoyle_1
    private const string G_Gargoyle2 = "abc00000000009763068170551952810"; // SM_Gargoyle_2
    private const string G_Statue1   = "abc00000000012710110913266971717"; // SM_Statue_1
    private const string G_Statue2   = "abc00000000014482205491929310782"; // SM_Statue_2
    private const string G_Banner    = "abc00000000002504902498064927479"; // SM_banner
    private const string G_WallGarg  = "abc00000000006866068108951975429"; // SM_WallGargoyle_1

    // 기존 SM_chandelier0 실측 y≈30.3
    private const float CeilingY = 30f;
    private const float TorchY   = 8f;

    [MenuItem("RelicFairy/Replace BossArena with Castle Assets")]
    public static void Replace() => ReplaceInternal();

    // 다이얼로그 없이 바로 실행 (MCP 호환)
    [MenuItem("RelicFairy/Replace BossArena (No Dialog)")]
    public static void ReplaceNoDialog() => ReplaceInternal();

    private static void ReplaceInternal()
    {
        var bossArena = GameObject.Find("BossArena");
        if (bossArena == null)
        {
            // 경로로 찾기
            var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
            foreach (var r in roots)
            {
                var t = r.transform.Find("LichTestMap/BossArena");
                if (t != null) { bossArena = t.gameObject; break; }
                t = r.transform.Find("BossArena");
                if (t != null) { bossArena = t.gameObject; break; }
            }
        }
        if (bossArena == null) { Debug.LogError("[BossArenaSetupTool] BossArena GO를 찾을 수 없음"); return; }

        var oldContainer = bossArena.transform.Find("CastleAssets");
        if (oldContainer != null) Undo.DestroyObjectImmediate(oldContainer.gameObject);

        var container = new GameObject("CastleAssets");
        Undo.RegisterCreatedObjectUndo(container, "BossArena Replace");
        container.transform.SetParent(bossArena.transform, worldPositionStays: false);

        // 기존 오브젝트를 분류해서 처리
        var info = AnalyzeBossArena(bossArena);

        // Floor 교체
        if (info.floor != null)
            ReplaceFloor(info.floor, container.transform);

        // Wall 교체
        foreach (var w in info.walls)
            ReplaceWall(w, container.transform);

        // Pillar 교체
        foreach (var pil in info.pillars)
            ReplacePillar(pil, container.transform);

        // 조명 + 장식 추가 (기존 없는 경우)
        AddLighting(info, container.transform);
        AddDecorations(info, container.transform);

        // 기존 MeshRenderer 오브젝트 비활성화 (BoxCollider는 유지)
        foreach (var go in info.allMeshObjects)
        {
            Undo.RecordObject(go, "Disable Old Mesh");
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null) mr.enabled = false;
        }

        EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log("[BossArenaSetupTool] 교체 완료!");
    }

    // ── 분석 ──────────────────────────────────────────────────────────────

    private class ArenaInfo
    {
        public GameObject        floor;
        public List<GameObject>  walls   = new();
        public List<GameObject>  pillars = new();
        public List<GameObject>  allMeshObjects = new();
        public Bounds            arenaBounds;
    }

    private static ArenaInfo AnalyzeBossArena(GameObject bossArena)
    {
        var info = new ArenaInfo();
        var bounds = new Bounds();
        bool first = true;

        foreach (Transform child in bossArena.transform)
        {
            var go  = child.gameObject;
            var mr  = go.GetComponent<MeshRenderer>();
            var col = go.GetComponent<BoxCollider>();

            if (mr == null) continue; // BossSpawn 등 메시 없는 것 스킵
            info.allMeshObjects.Add(go);

            // 콜라이더 bounds 누적 → 전체 아레나 범위 계산
            if (col != null)
            {
                var b = col.bounds;
                if (first) { bounds = b; first = false; }
                else        bounds.Encapsulate(b);
            }

            string n = go.name.ToLower();
            if (n.Contains("floor"))        info.floor = go;
            else if (n.Contains("wall"))    info.walls.Add(go);
            else if (n.Contains("pillar"))  info.pillars.Add(go);
        }

        info.arenaBounds = bounds;
        Debug.Log($"[BossArenaSetupTool] Arena bounds: center={bounds.center} size={bounds.size}");
        return info;
    }

    // ── Floor 교체 ────────────────────────────────────────────────────────

    private static void ReplaceFloor(GameObject floorGO, Transform parent)
    {
        var col = floorGO.GetComponent<BoxCollider>();
        if (col == null) return;

        var   bounds = col.bounds;
        var   f1     = LoadPrefab(G_Floor1);
        var   f2     = LoadPrefab(G_Floor2);
        if (f1 == null && f2 == null) return;

        // 메시 실제 크기 읽기
        var   meshSz = GetPrefabMeshSize(G_Floor1, Vector3.one * 4f);
        float tileX  = meshSz.x > 0.1f ? meshSz.x : 4f;
        float tileZ  = meshSz.z > 0.1f ? meshSz.z : 4f;

        // 타일 수: 아레나 크기를 타일 크기로 딱 나누기
        int xi = Mathf.Max(1, Mathf.RoundToInt(bounds.size.x / tileX));
        int zi = Mathf.Max(1, Mathf.RoundToInt(bounds.size.z / tileZ));

        // 실제 배치 간격 (bounds에 꽉 맞춤)
        float stepX = bounds.size.x / xi;
        float stepZ = bounds.size.z / zi;
        float y     = bounds.min.y;

        var p = MakeGroup("Floor", parent);
        for (int ix = 0; ix < xi; ix++)
        for (int iz = 0; iz < zi; iz++)
        {
            // 메시 피봇이 코너인지 중앙인지에 따라 오프셋 결정
            // bounds.min에서 시작해 step씩 이동 (피봇=코너 가정)
            float x = bounds.min.x + stepX * ix;
            float z = bounds.min.z + stepZ * iz;
            var pref = (ix + iz) % 3 == 0 && f2 != null ? f2 : f1 ?? f2;
            PlacePrefab(pref, new Vector3(x, y, z), Quaternion.identity, p).name = $"Floor_{ix}_{iz}";
        }

        Debug.Log($"[BossArenaSetupTool] Floor {xi}×{zi} 배치 stepX={stepX:F2} stepZ={stepZ:F2}");
    }

    // ── Wall 교체 ─────────────────────────────────────────────────────────

    private static void ReplaceWall(GameObject wallGO, Transform parent)
    {
        var col = wallGO.GetComponent<BoxCollider>();
        if (col == null) return;

        var   bounds  = col.bounds;
        float unitW   = EstimateTileSize(G_Wall02, 4f);
        float unitH   = 4f;
        var   wA      = LoadPrefab(G_Wall02);
        var   wB      = LoadPrefab(G_Wall03);
        if (wA == null && wB == null) return;

        // 벽 이름에서 방향 추출 → 아레나 내부를 향하는 회전 결정
        // SM_InteriorWall_A_02: face 기본 방향 = +X 기준
        //   face를 -Z(남)로 돌리려면 Y=90°, +Z(북)=Y=270°, -X(서)=Y=180°, +X(동)=Y=0°
        string nm    = wallGO.name.ToUpper();
        bool   isN   = nm.Contains("_N") && !nm.Contains("NW") && !nm.Contains("NE");
        bool   isS   = nm.Contains("_S") && !nm.Contains("SW") && !nm.Contains("SE");
        bool   isE   = nm.Contains("_E") && !nm.Contains("NE") && !nm.Contains("SE");
        bool   isW   = nm.Contains("_W") && !nm.Contains("NW") && !nm.Contains("SW");

        // face=+X 기준: N벽→남향(Y=90°), S벽→북향(Y=270°), E벽→서향(Y=180°), W벽→동향(Y=0°)
        Quaternion rot;
        bool isXWall;
        if      (isN) { rot = Quaternion.Euler(0,  90, 0); isXWall = true;  }
        else if (isS) { rot = Quaternion.Euler(0, 270, 0); isXWall = true;  }
        else if (isE) { rot = Quaternion.Euler(0, 180, 0); isXWall = false; }
        else if (isW) { rot = Quaternion.Euler(0,   0, 0); isXWall = false; }
        else
        {
            isXWall = bounds.size.x > bounds.size.z;
            rot     = isXWall ? Quaternion.Euler(0, 90, 0) : Quaternion.Euler(0, 0, 0);
        }

        // 실제 벽 메시 크기 읽기
        var   wallSize = GetPrefabMeshSize(G_Wall02, new Vector3(unitW, unitH, unitW));
        float meshH    = wallSize.y  > 0.1f ? wallSize.y  : unitH;
        // 메시의 길이 방향: isXWall=true면 X방향, 아니면 Z방향 기준
        float meshL    = (isXWall ? Mathf.Max(wallSize.x, wallSize.z) : Mathf.Max(wallSize.x, wallSize.z));
        if (meshL < 0.1f) meshL = unitW;

        float arenaLen = isXWall ? bounds.size.x : bounds.size.z;

        // 섹션 수: 메시 너비로 나눔 (실제 메시 크기 = 간격 → 틈 없음)
        int secL = Mathf.Max(1, Mathf.RoundToInt(arenaLen / meshL));
        // 높이: 최소 2레이어 쌓아 웅장한 높이 확보
        int secH = Mathf.Max(2, Mathf.CeilToInt(bounds.size.y / meshH) + 1);

        // 배치 간격: 메시 크기 정확히 사용 (틈 없음)
        float stepL = meshL;
        float stepH = meshH;

        var p = MakeGroup($"Wall_{wallGO.name}", parent);

        for (int l = 0; l < secH; l++)
        for (int s = 0; s < secL; s++)
        {
            float y = bounds.min.y + stepH * l;  // 바닥에서 위로 쌓기 (피봇=바닥)
            float x, z;
            if (isXWall)
            {
                x = bounds.min.x + stepL * s;
                z = bounds.center.z;
            }
            else
            {
                x = bounds.center.x;
                z = bounds.min.z + stepL * s;
            }
            var pref = (l + s) % 2 == 0 && wB != null ? wB : wA ?? wB;
            PlacePrefab(pref, new Vector3(x, y, z), rot, p).name = $"W_{l}_{s}";
        }

        Debug.Log($"[BossArenaSetupTool] Wall '{wallGO.name}' → {secH}×{secL} 섹션 meshH={meshH:F1} meshL={meshL:F1} rot={rot.eulerAngles}");
    }

    private static void ReplaceCeilingOver(Bounds wallBounds, Transform parent)
    {
        // 벽 위쪽 천장은 별도 배치 (벽 교체 시 호출되므로 Floor에만 해당)
        // 아레나 전체 천장은 AddLighting에서 처리
    }

    // ── Pillar 교체 ───────────────────────────────────────────────────────

    private static void ReplacePillar(GameObject pillarGO, Transform parent)
    {
        var col    = pillarGO.GetComponent<BoxCollider>();
        var pillar = LoadPrefab(G_BigPillar) ?? LoadPrefab(G_Pillar);
        if (pillar == null) return;

        Vector3 pos = pillarGO.transform.position;
        if (col != null)
        {
            pos   = col.bounds.center;
            pos.y = col.bounds.min.y; // 바닥 면 기준
        }

        PlacePrefab(pillar, pos, Quaternion.identity, parent).name = $"Pillar_{pillarGO.name}";
        Debug.Log($"[BossArenaSetupTool] Pillar '{pillarGO.name}' → {pos}");
    }

    // ── 조명 추가 ─────────────────────────────────────────────────────────

    private static void AddLighting(ArenaInfo info, Transform parent)
    {
        var p    = MakeGroup("Lighting", parent);
        var chan = LoadPrefab(G_Chandelier);
        var gtor = LoadPrefab(G_GroundTorch);
        var tor  = LoadPrefab(G_Torch);

        var b    = info.arenaBounds;
        float mid = (b.min.z + b.max.z) * 0.5f;
        float cy  = b.max.y;  // 실제 아레나 천장 높이 사용

        // 샹들리에 — 실제 arenaBounds 천장에 매달기
        if (chan != null)
            foreach (float z in new[] { b.min.z + 8f, mid - 8f, mid + 8f, b.max.z - 8f })
                PlacePrefab(chan, new Vector3(b.center.x, cy, z), Quaternion.identity, p);

        // 지면 횃불
        if (gtor != null)
            for (float z = b.min.z + 6f; z <= b.max.z - 4f; z += 14f)
            {
                PlacePrefab(gtor, new Vector3(b.min.x + 2f, 0, z), Quaternion.Euler(0,  90, 0), p);
                PlacePrefab(gtor, new Vector3(b.max.x - 2f, 0, z), Quaternion.Euler(0, -90, 0), p);
            }

        // 벽 횃불
        if (tor != null)
            for (float z = b.min.z + 5f; z <= b.max.z - 4f; z += 10f)
            {
                PlacePrefab(tor, new Vector3(b.min.x + 0.5f, TorchY, z), Quaternion.Euler(0,  90, 0), p);
                PlacePrefab(tor, new Vector3(b.max.x - 0.5f, TorchY, z), Quaternion.Euler(0, -90, 0), p);
            }
    }

    // ── 장식 추가 ─────────────────────────────────────────────────────────

    private static void AddDecorations(ArenaInfo info, Transform parent)
    {
        var p   = MakeGroup("Decorations", parent);
        var b   = info.arenaBounds;
        float cx = b.center.x;
        float mid = (b.min.z + b.max.z) * 0.5f;

        var g1  = LoadPrefab(G_Gargoyle1);
        var g2  = LoadPrefab(G_Gargoyle2);
        var s1  = LoadPrefab(G_Statue1);
        var s2  = LoadPrefab(G_Statue2);
        var ban = LoadPrefab(G_Banner);
        var wg  = LoadPrefab(G_WallGarg);

        if (g1 != null) PlacePrefab(g1, new Vector3(b.min.x + 2f, 0, b.min.z + 2f), Quaternion.Euler(0,  45, 0), p).name = "Garg_L";
        if (g2 != null) PlacePrefab(g2, new Vector3(b.max.x - 2f, 0, b.min.z + 2f), Quaternion.Euler(0, -45, 0), p).name = "Garg_R";
        if (s1 != null) PlacePrefab(s1, new Vector3(cx - 6f, 0, b.max.z - 8f), Quaternion.identity, p).name = "Statue_L";
        if (s2 != null) PlacePrefab(s2, new Vector3(cx + 6f, 0, b.max.z - 8f), Quaternion.Euler(0, 180, 0), p).name = "Statue_R";

        if (ban != null)
            foreach (float z in new[] { mid - 10f, mid, mid + 10f })
            {
                PlacePrefab(ban, new Vector3(b.min.x + 0.3f, 10f, z), Quaternion.Euler(0,  90, 0), p);
                PlacePrefab(ban, new Vector3(b.max.x - 0.3f, 10f, z), Quaternion.Euler(0, -90, 0), p);
            }

        if (wg != null)
            foreach (float z in new[] { mid - 6f, mid + 6f })
            {
                PlacePrefab(wg, new Vector3(b.min.x + 0.5f, 18f, z), Quaternion.Euler(0,  90, 0), p);
                PlacePrefab(wg, new Vector3(b.max.x - 0.5f, 18f, z), Quaternion.Euler(0, -90, 0), p);
            }
    }

    // ── 유틸 ──────────────────────────────────────────────────────────────

    /// <summary>프리팹 메시의 실제 크기를 읽는다.</summary>
    private static Vector3 GetPrefabMeshSize(string guid, Vector3 fallback)
    {
        var path = AssetDatabase.GUIDToAssetPath(guid);
        if (string.IsNullOrEmpty(path)) return fallback;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) return fallback;
        var mf = prefab.GetComponentInChildren<MeshFilter>();
        if (mf?.sharedMesh == null) return fallback;
        return mf.sharedMesh.bounds.size;
    }

    private static float EstimateTileSize(string guid, float fallback)
    {
        var s = GetPrefabMeshSize(guid, Vector3.one * fallback);
        return Mathf.Max(s.x, s.z);
    }

    private static Transform MakeGroup(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    private static GameObject PlacePrefab(GameObject prefab, Vector3 pos, Quaternion rot, Transform parent)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        Undo.RegisterCreatedObjectUndo(go, "Place");
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(pos, rot);
        return go;
    }

    private static GameObject LoadPrefab(string guid)
    {
        var path = AssetDatabase.GUIDToAssetPath(guid);
        return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }
}
