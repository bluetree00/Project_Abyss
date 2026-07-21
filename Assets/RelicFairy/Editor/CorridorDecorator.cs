#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Game_Intro 씬 통로 장식 배치 도구.
/// Tools/Decorate Corridor 메뉴로 실행. 기존 큐브 MeshRenderer를 숨기고 Gothic 에셋을 배치한다.
/// </summary>
public static class CorridorDecorator
{
    private const string P = "Assets/RelicFairy/_Imported/Gothic_Interior/Environment/Asset/Prefabs/";

    // 통로 파라미터 (씬 좌표 기준)
    private const float Z_START = -80f;   // 후방
    private const float Z_END   = -30f;   // 보스방 입구
    private const float TILE    =  8f;    // 타일 간격
    private const float Y_FLOOR =  0f;
    private const float Y_CEIL  =  9f;
    private const float X_WALL  =  4f;    // 절반 너비

    [MenuItem("Tools/Rollback Corridor Deco")]
    public static void Rollback()
    {
        var deco = GameObject.Find("Corridor_Deco");
        if (deco != null) Object.DestroyImmediate(deco);

        ShowRenderer("Corridor_Floor");
        ShowRenderer("Wall_South");
        ShowRenderer("Wall_North");
        ShowRenderer("Ceiling");

        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log("[CorridorDecorator] 롤백 완료.");
    }

    private static void ShowRenderer(string goName)
    {
        var go = GameObject.Find(goName);
        if (go == null) return;
        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null) mr.enabled = true;
    }

    [MenuItem("Tools/Decorate Corridor")]
    public static void Decorate()
    {
        // ── 기존 큐브 렌더러 숨기기 ──────────────────────────────────────────
        HideRenderer("Corridor_Floor");
        HideRenderer("Wall_South");
        HideRenderer("Wall_North");
        HideRenderer("Ceiling");

        // ── 기존 장식 GO 제거 (재실행 안전) ─────────────────────────────────
        var old = GameObject.Find("Corridor_Deco");
        if (old != null) Object.DestroyImmediate(old);

        var root = new GameObject("Corridor_Deco");

        // ── 바닥 타일 (SM_ArchFloor_01a) ────────────────────────────────────
        // 통로 방향: Z축. 타일 1개가 8 units 커버한다고 가정.
        // Z=-76, -68, -60, -52, -44, -36 (6장)
        for (int i = 0; i < 6; i++)
        {
            float z = -76f + i * TILE;
            Place(P + "SM_ArchFloor_01a.prefab",
                  new Vector3(0, Y_FLOOR, z),
                  Quaternion.Euler(0, 90, 0), root);
        }

        // ── 천장 타일 (SM_ArchCeiling_01a) ──────────────────────────────────
        // 메쉬가 기본적으로 아래를 향하지 않으면 X=180 필요. Y=0 → X방향 타일링
        for (int i = 0; i < 6; i++)
        {
            float z = -76f + i * TILE;
            Place(P + "SM_ArchCeiling_01a.prefab",
                  new Vector3(0, Y_CEIL, z),
                  Quaternion.Euler(180, 0, 0), root);
        }

        // ── 벽 패널 (South X=+4, North X=-4) ────────────────────────────────
        // 패턴: 창문벽 / 일반벽 / 창문벽 / ... 번갈아가며
        string[] wallTypes = { "SM_ArchWallWin_01a", "SM_ArchWall_01a",
                                "SM_ArchWallWin_01a", "SM_ArchWall_01a",
                                "SM_ArchWallWin_01a", "SM_ArchWall_01a" };
        for (int i = 0; i < 6; i++)
        {
            float z   = -76f + i * TILE;
            string pf = P + wallTypes[i] + ".prefab";

            // South (X=+4): 안쪽(-X)을 향함 → Y=-90
            Place(pf, new Vector3( X_WALL, Y_FLOOR, z), Quaternion.Euler(0, -90, 0), root);
            // North (X=-4): 안쪽(+X)을 향함 → Y=+90
            Place(pf, new Vector3(-X_WALL, Y_FLOOR, z), Quaternion.Euler(0,  90, 0), root);

            // 창문 안에 스테인드글라스 삽입
            if (wallTypes[i].Contains("Win"))
            {
                Place(P + "SM_GlassWindowCathedral_01a.prefab",
                      new Vector3( X_WALL - 0.15f, 3.5f, z),
                      Quaternion.Euler(0, -90, 0), root);
                Place(P + "SM_GlassWindowCathedral_01a.prefab",
                      new Vector3(-X_WALL + 0.15f, 3.5f, z),
                      Quaternion.Euler(0,  90, 0), root);
            }
        }

        // ── 기둥 (SM_StonePillar_01a) ── 16유닛 간격 ────────────────────────
        float[] pillarZs = { -76f, -60f, -44f };
        foreach (float z in pillarZs)
        {
            Place(P + "SM_StonePillar_01a.prefab",
                  new Vector3( X_WALL - 0.3f, Y_FLOOR, z),
                  Quaternion.identity, root);
            Place(P + "SM_StonePillar_01a.prefab",
                  new Vector3(-X_WALL + 0.3f, Y_FLOOR, z),
                  Quaternion.identity, root);
        }

        // ── 아치 조인트 (SM_ArcheJoint_01a) ── 기둥 천장 연결 ──────────────
        // 기둥 상단~천장 사이 코너 조인트. 자연 방향(Y=0)은 X축 방향 아치.
        foreach (float z in pillarZs)
        {
            Place(P + "SM_ArcheJoint_01a.prefab",
                  new Vector3( X_WALL - 0.3f, Y_FLOOR, z),
                  Quaternion.Euler(0, -90, 0), root);
            Place(P + "SM_ArcheJoint_01a.prefab",
                  new Vector3(-X_WALL + 0.3f, Y_FLOOR, z),
                  Quaternion.Euler(0,  90, 0), root);
        }

        // ── 대형 촛대 (SM_Candleabra_04a) ── 기둥 사이 ──────────────────────
        float[] candleZs = { -68f, -52f, -36f };
        foreach (float z in candleZs)
        {
            Place(P + "SM_Candleabra_04a.prefab",
                  new Vector3( 2.8f, Y_FLOOR, z),
                  Quaternion.identity, root);
            Place(P + "SM_Candleabra_04a.prefab",
                  new Vector3(-2.8f, Y_FLOOR, z),
                  Quaternion.identity, root);
        }

        // ── 벽 촛대 (SM_WallCandleabra_01a) ── 창문 없는 벽에 ──────────────
        float[] wallCandleZs = { -68f, -52f, -36f };
        foreach (float z in wallCandleZs)
        {
            Place(P + "SM_WallCandleabra_01a.prefab",
                  new Vector3( X_WALL - 0.1f, 5.5f, z),
                  Quaternion.Euler(0, -90, 0), root);
            Place(P + "SM_WallCandleabra_01a.prefab",
                  new Vector3(-X_WALL + 0.1f, 5.5f, z),
                  Quaternion.Euler(0,  90, 0), root);
        }

        // ── 조각상 (SM_Statue_01a) ── 입구(스폰) 쪽 ─────────────────────────
        Place(P + "SM_Statue_01a.prefab",
              new Vector3( 2.5f, Y_FLOOR, -75f),
              Quaternion.Euler(0, 180, 0), root);
        Place(P + "SM_Statue_01a.prefab",
              new Vector3(-2.5f, Y_FLOOR, -75f),
              Quaternion.Euler(0, 180, 0), root);

        // ── 해골 장식 (SM_SkullTop_01a) ── 기둥 상단 분위기 ─────────────────
        Place(P + "SM_SkullTop_01a.prefab",
              new Vector3( X_WALL - 0.4f, 7f, -68f),
              Quaternion.Euler(0, -90, 0), root);
        Place(P + "SM_SkullTop_01a.prefab",
              new Vector3(-X_WALL + 0.4f, 7f, -52f),
              Quaternion.Euler(0,  90, 0), root);

        // ── 아치 트림 (SM_ArchArcheTrim_01a) ── 기둥 사이, 통로 폭 방향으로 걸침
        // 기본 방향(Y=0)이 X축 방향으로 스팬 → 통로 가로(폭 8유닛)를 가로지름
        float[] trimZs = { -72f, -64f, -56f, -48f, -40f };
        foreach (float z in trimZs)
        {
            Place(P + "SM_ArchArcheTrim_01a.prefab",
                  new Vector3(0, Y_CEIL - 0.1f, z),
                  Quaternion.Euler(0, 0, 0), root);
        }

        // ── 씬 저장 마크 ─────────────────────────────────────────────────────
        EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log($"[CorridorDecorator] 완료. 배치 오브젝트 수: {root.transform.childCount}");
    }

    private static void Place(string path, Vector3 pos, Quaternion rot, GameObject parent)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
        {
            Debug.LogWarning($"[CorridorDecorator] 프리팹 없음: {path}");
            return;
        }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.transform.position = pos;
        go.transform.rotation = rot;
        go.transform.SetParent(parent.transform, true);
        // Navigation Static 제거 — NavMesh는 Corridor_Floor 큐브가 담당
        var flags = GameObjectUtility.GetStaticEditorFlags(go);
        GameObjectUtility.SetStaticEditorFlags(go, flags & ~StaticEditorFlags.NavigationStatic);
    }

    private static void HideRenderer(string goName)
    {
        var go = GameObject.Find(goName);
        if (go == null) return;
        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null) mr.enabled = false;
    }
}
#endif
