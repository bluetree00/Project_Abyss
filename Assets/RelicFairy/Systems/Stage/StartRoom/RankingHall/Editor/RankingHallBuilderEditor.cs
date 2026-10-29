#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// [도구 · 편집 모드] 랭킹방 = <b>높은 탑 꼭대기</b>(10-29 사용자 「높은 탑에서 아래의 던전 풍경을 웅장하게 보여주는 느낌」 · 「난간은 있어야 — 타일이 잘려 보인다」).
/// · 꼭대기 — 고딕 바닥 칸(반지름 12 m 안쪽만) · 둘레 돌 띠 · 난간(FantasyCastle 난간 · 난간 기둥, 돌아가는 문 자리만 비움) · 가고일 넷 · 보스 석상 넷 · 가운데 기록 비석 · 돌아가는 문.
/// · 탑 몸통 — 꼭대기 아래로 뻗는 둥근 탑(여러 토막) · 버팀 탑 넷.
/// · 아래 풍경 — 석상이 바라보는 네 방향 50 m 아래에 던전 구역(바위 섬 · 큰 탑 · 작은 탑 둘 · 성벽 · 챕터 색 빛), 그 아래 안개 판, 멀리 탑 실루엣.
/// · 도착 둘러보기 카메라 자리(VistaFrom · VistaTo · VistaLook) — RankingHall.VisitAsync가 아래 풍경을 한 번 훑고 플레이어에게 돌아온다.
/// 에셋 크기는 렌더러 경계로 재서 목표 크기에 맞춘다. RankingHall 필드를 잇고 Addressables 「RankingHall/RankingHall」로 등록한다.
/// 실측 도구가 기대는 이름: Stele · ReturnDoor/Trigger(RankingHallProbeEditor · RecordDoorProbeEditor).
/// </summary>
internal static class RankingHallBuilderEditor
{
    private const string Dir    = "Assets/RelicFairy/Systems/Stage/StartRoom/RankingHall";
    private const string Path   = Dir + "/RankingHall.prefab";
    private const string Gothic = "Assets/RelicFairy/_Imported/Gothic_Interior/Environment/Asset/Prefabs/";
    private const string Castle = "Assets/RelicFairy/_Imported/LeartesStudios/FantasyCastle/~HDRP/Art/Prefabs/";
    private const string CastleBP = "Assets/RelicFairy/_Imported/LeartesStudios/FantasyCastle/~HDRP/Art/Blueprints/";
    private const float  Radius = 12f;
    private const float  RailR  = Radius + 0.3f;   // 난간 줄
    private const float  DoorGapDeg = 14f;         // 돌아가는 문 자리(뒤쪽 180°) 난간 비움 반각
    private const float  TileSize   = 2f;          // 꼭대기 바닥 칸
    private const float  CurbWidth  = 2.6f;        // 둘레 돌 띠 폭

    // 석상 방향 = 그 보스 구역이 내려다보이는 방향
    private static readonly (string name, float deg, string statue, Color tint, string tower)[] Bosses =
    {
        ("숲의 수호자",       45f, "SM_Statue_01a", new Color(0.45f, 1.00f, 0.55f), "BP_Tall_Tower02"),
        ("화룡",             135f, "SM_Statue_01b", new Color(1.00f, 0.55f, 0.25f), "BP_Tall_Tower05"),
        ("타락한 모르드레드", 225f, "SM_Statue_01c", new Color(0.55f, 0.72f, 1.00f), "BP_Tall_Tower07"),
        ("리치",             315f, "SM_Statue_01a", new Color(0.78f, 0.45f, 1.00f), "BP_Tall_Tower09"),
    };

    private enum Axis { Height, Width }

    [MenuItem("RelicFairy/Stage/10-29 §5 랭킹방 홀 프리팹 짓기 (Edit)")]
    private static void Build()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[RankingHallBuilder] 편집 모드에서"); return; }
        var root = new GameObject("RankingHall");
        try
        {
            int ground = LayerMask.NameToLayer("Ground"), wall = LayerMask.NameToLayer("Wall");
            var hall = root.AddComponent<RankingHall>();
            var tint = new List<Renderer>();
            var lights = new List<Light>();

            BuildTerrace(root.transform, ground, wall);
            BuildRailing(root.transform);
            BuildTowerBody(root.transform);
            BuildVista(root.transform);

            // ── 보스 석상 넷 — 3.6 m · 가운데를 본다 · 이름 판(그 뒤 아래로 그 보스 구역이 보인다)
            var statues = Child(root.transform, "Statues", Vector3.zero);
            foreach (var (name, deg, src, _, _) in Bosses)
            {
                var at = Dir2(deg) * (Radius - 3.5f);
                var holder = Child(statues, $"Statue_{name}", at);
                holder.rotation = Quaternion.LookRotation(-at.normalized);
                var t = Fit(LoadG(src), holder, Vector3.zero, 0f, 3.6f, Axis.Height, local: true);
                if (t != null) tint.AddRange(t.GetComponentsInChildren<Renderer>(true));
                var plate = Label(holder, "Name", name, new Vector3(0f, 0.35f, 1.4f), 2.4f);
                plate.transform.localRotation = Quaternion.identity;
                lights.Add(PointLight(holder, new Vector3(0f, 4.5f, 1.8f), 9f, 3.2f));
            }

            // ── 가운데 기록 비석 — 2.2 m · 위에 제목
            var stele = Child(root.transform, "Stele", new Vector3(0f, 0f, 2f));
            var st = Fit(LoadG("SM_Altar_01a"), stele, Vector3.zero, 0f, 2.2f, Axis.Height, local: true);
            if (st != null) tint.AddRange(st.GetComponentsInChildren<Renderer>(true));
            stele.rotation = Quaternion.LookRotation(Vector3.back);   // 들어오는 쪽(뒤 문)을 본다
            var title = Label(stele, "Title", "해방기 기록", new Vector3(0f, 2.9f, 0f), 4f);
            lights.Add(PointLight(stele, new Vector3(0f, 3.5f, -1.5f), 8f, 2.2f));

            // ── 뒤(난간 빈 자리)에 돌아가는 문 · 시작 자리
            var door = Child(root.transform, "ReturnDoor", new Vector3(0f, 0f, -Radius));
            Fit(LoadG("SM_ArchWallDoor_01a"), door, Vector3.zero, 0f, 4.5f, Axis.Width, local: true);
            var trig = Child(door, "Trigger", new Vector3(0f, 1.5f, 0.8f));
            var box = trig.gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true; box.size = new Vector3(3.2f, 3f, 1.6f);
            trig.gameObject.AddComponent<RankingHallDoor>();
            Label(door, "DoorName", "돌아가는 문", new Vector3(0f, 4.6f, 0.6f), 2.6f);
            var spawn = Child(root.transform, "SpawnPoint", new Vector3(0f, 0.05f, -Radius + 4f));
            spawn.rotation = Quaternion.LookRotation(Vector3.forward);

            // ── 도착 둘러보기 카메라 — 숲 구역(45°)을 내려다보며 시작 → 뒤 위에서 꼭대기 전체 + 너머 풍경
            Vector3 forest = Dir2(45f) * DistrictDist + Vector3.up * (DistrictY + 30f);   // 숲 구역 첨탑 허리 — 위에서 내려다본다
            var vFrom = Child(root.transform, "VistaFrom", Dir2(20f) * (RailR + 3f) + Vector3.up * 7f);
            vFrom.rotation = Quaternion.LookRotation(forest - vFrom.localPosition);
            var vTo   = Child(root.transform, "VistaTo", new Vector3(0f, 15f, -Radius - 13f));
            var vLook = Child(root.transform, "VistaLook", new Vector3(0f, -4f, 10f));
            vTo.rotation = Quaternion.LookRotation(vLook.localPosition - vTo.localPosition);

            // ── RankingHall 필드 잇기
            var so = new SerializedObject(hall);
            so.FindProperty("_spawnPoint").objectReferenceValue = spawn;
            so.FindProperty("_stele").objectReferenceValue = stele;
            so.FindProperty("_title").objectReferenceValue = title;
            so.FindProperty("_vistaFrom").objectReferenceValue = vFrom;
            so.FindProperty("_vistaTo").objectReferenceValue = vTo;
            so.FindProperty("_vistaLook").objectReferenceValue = vLook;
            var pl = so.FindProperty("_themeLights"); pl.arraySize = lights.Count;
            for (int i = 0; i < lights.Count; i++) pl.GetArrayElementAtIndex(i).objectReferenceValue = lights[i];
            var pr = so.FindProperty("_tintRenderers"); pr.arraySize = tint.Count;
            for (int i = 0; i < tint.Count; i++) pr.GetArrayElementAtIndex(i).objectReferenceValue = tint[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, Path);
            Register(Path, RankingHall.PrefabKey);
            Debug.Log($"[RankingHallBuilder] 저장 {Path} · 석상 {Bosses.Length} · 빛 {lights.Count} · 물들 렌더러 {tint.Count} · 렌더러 {root.GetComponentsInChildren<Renderer>(true).Length}");
        }
        finally { Object.DestroyImmediate(root); }
    }

    // ── 꼭대기 ───────────────────────────────────────────────

    private static void BuildTerrace(Transform root, int ground, int wall)
    {
        // 바닥 칸 — 칸이 난간 밖으로 삐져나가지 않게 칸 전체가 반지름 안에 드는 것만(10-29 「타일이 잘려 보인다」)
        var floorSrc = LoadG("SM_ArchFloor_01a");
        var floorRoot = Child(root, "Floor", Vector3.zero);
        if (floorSrc != null)
        {
            var b = MeasureBounds(floorSrc);
            // 칸을 2 m로 줄여 원을 촘촘히 따른다 — 4 m 칸은 대각선 칸이 통째로 빠져 탑 지붕이 보였다(10-29 사진)
            float k = TileSize / Mathf.Max(0.5f, Mathf.Max(b.size.x, b.size.z));
            float sx = b.size.x * k, sz = b.size.z * k;
            int nx = Mathf.CeilToInt(RailR / sx) + 1, nz = Mathf.CeilToInt(RailR / sz) + 1;
            for (int ix = -nx; ix <= nx; ix++)
            for (int iz = -nz; iz <= nz; iz++)
            {
                var c = new Vector3(ix * sx, 0f, iz * sz);
                if (c.magnitude > RailR - 0.9f) continue;   // 끝 톱니는 둘레 돌 띠(2.6 m)가 덮는다
                var t = Place(floorSrc, floorRoot, c - new Vector3(b.center.x, b.max.y, b.center.z) * k, Quaternion.identity, k);
                SetLayerRecursive(t.gameObject, ground);
            }
        }
        // 둘레 돌 띠 — 바닥 칸 끝(톱니)과 난간 사이를 덮는다. 고딕 바닥 칸을 얇게 눌러 고리로 두른다.
        var curbSrc = LoadG("SM_ArchFloor_01a");
        var curb = Child(root, "Curb", Vector3.zero);
        if (curbSrc != null)
        {
            var b = MeasureBounds(curbSrc);
            int n = 48;
            float chord = 2f * (RailR + 0.2f) * Mathf.Sin(Mathf.PI / n) * 1.08f;
            for (int i = 0; i < n; i++)
            {
                float deg = i * 360f / n;
                var t = Place(curbSrc, curb, Dir2(deg) * (RailR - 0.6f), Quaternion.Euler(0f, deg, 0f), 1f);
                t.localScale = new Vector3(chord / Mathf.Max(0.1f, b.size.x), 1f, CurbWidth / Mathf.Max(0.1f, b.size.z));
                t.localPosition += Vector3.up * (0.03f - b.max.y);
                SetLayerRecursive(t.gameObject, ground);
            }
        }
        var walk = Child(root, "WalkPlane", new Vector3(0f, -0.5f, 0f));
        walk.gameObject.layer = ground;
        walk.gameObject.AddComponent<BoxCollider>().size = new Vector3(Radius * 2f + 6f, 1f, Radius * 2f + 6f);
        // 둘레 막이 — 떨어지지 않게(보이지 않음 · 난간 바로 안쪽)
        var rim = Child(root, "Rim", Vector3.zero);
        for (int i = 0; i < 24; i++)
        {
            float deg = i * 15f;
            var seg = Child(rim, $"Rim_{i}", Dir2(deg) * (RailR + 0.2f) + Vector3.up * 2f);
            seg.rotation = Quaternion.LookRotation(-Dir2(deg));
            seg.gameObject.layer = wall < 0 ? 0 : wall;
            seg.gameObject.AddComponent<BoxCollider>().size = new Vector3(2 * Mathf.PI * (RailR + 0.2f) / 24f + 0.3f, 4f, 0.5f);
        }
    }

    private static void BuildRailing(Transform root)
    {
        var railSrc   = LoadC("SM_Railing_01");
        var pillarSrc = LoadC("SM_Railing_Pillar_01");
        var gargSrc   = LoadC("SM_Gargoyle_1");
        var rail = Child(root, "Railing", Vector3.zero);
        if (railSrc == null) return;

        var b = MeasureBounds(railSrc);
        bool longX = b.size.x >= b.size.z;
        float len = Mathf.Max(0.5f, longX ? b.size.x : b.size.z);
        float hScale = 1.15f / Mathf.Max(0.1f, b.size.y);                 // 난간 높이 1.15 m
        float natural = len * hScale;
        int n = Mathf.Max(12, Mathf.RoundToInt(2f * Mathf.PI * RailR / natural));
        float chord = 2f * RailR * Mathf.Sin(Mathf.PI / n);
        float step = 360f / n;
        for (int i = 0; i < n; i++)
        {
            float mid = (i + 0.5f) * step;
            if (Mathf.Abs(Mathf.DeltaAngle(mid, 180f)) < DoorGapDeg) continue;   // 돌아가는 문 자리
            float yaw = mid + (longX ? 0f : 90f);
            var t = Place(railSrc, rail, Vector3.zero, Quaternion.Euler(0f, yaw, 0f), 1f);
            float along = chord / len;
            t.localScale = longX ? new Vector3(along, hScale, hScale) : new Vector3(hScale, hScale, along);
            var c = Quaternion.Euler(0f, yaw, 0f) * Vector3.Scale(new Vector3(b.center.x, b.min.y, b.center.z), t.localScale);
            t.localPosition = Dir2(mid) * RailR * Mathf.Cos(Mathf.PI / n) - c;
        }
        if (pillarSrc != null)
            for (int i = 0; i < n; i++)
            {
                float deg = i * step;
                if (Mathf.Abs(Mathf.DeltaAngle(deg, 180f)) < DoorGapDeg - step * 0.5f) continue;
                Fit(pillarSrc, rail, Dir2(deg) * RailR, deg, 1.45f, Axis.Height);
            }
        // 가고일 넷 — 난간 위에서 바깥 아래를 내려다본다(석상 사이 방향)
        if (gargSrc != null)
            foreach (float deg in new[] { 0f, 90f, 270f, 180f + DoorGapDeg + 30f })
                Fit(gargSrc, rail, Dir2(deg) * (RailR + 0.1f) + Vector3.up * 1.45f, deg, 1.3f, Axis.Height);
    }

    // ── 탑 몸통 ─────────────────────────────────────────────

    private static void BuildTowerBody(Transform root)
    {
        var body = Child(root, "TowerBody", Vector3.zero);
        var towerSrc = LoadC("SM_Round_Tower");
        if (towerSrc != null)
        {
            var b = MeasureBounds(towerSrc);
            float s = (RailR * 2f + 3f) / Mathf.Max(0.1f, Mathf.Max(b.size.x, b.size.z));
            float h = b.size.y * s, top = -0.3f;
            for (int k = 0; k < 6 && top > -120f; k++)   // 아래로 120 m까지 토막을 잇는다(안개에 묻힌다)
            {
                var t = Place(towerSrc, body, Vector3.zero, Quaternion.Euler(0f, k * 37f, 0f), s);
                t.localPosition = new Vector3(0f, top, 0f) - Quaternion.Euler(0f, k * 37f, 0f) * new Vector3(b.center.x, b.max.y, b.center.z) * s;
                top -= h * 0.98f;
            }
        }
        var support = LoadC("SM_Support_Tower");
        if (support != null)
            for (int i = 0; i < 4; i++)
            {
                float deg = 45f + i * 90f;
                Fit(support, body, Dir2(deg) * (RailR + 2.5f) + Vector3.up * -48f, deg + 180f, 44f, Axis.Height);
            }
    }

    // ── 아래 풍경 ───────────────────────────────────────────

    // 10-29 1차 사진: 구역이 작고 멀어 하늘만 보였다 → 큰 첨탑(82 m)이 아래에서 꼭대기 높이까지 솟게 · 사이 구역 넷 · 깊은 곳 빛
    private const float DistrictDist = 105f;
    private const float DistrictY    = -84f;   // 첨탑 꼭대기가 난간보다 아래 — 「내려다보는」 풍경

    private static void BuildVista(Transform root)
    {
        var vista = Child(root, "Vista", Vector3.zero);
        string[] rocks  = { "SM_GroupedRock_1", "SM_Grouped_Rock_2", "SM_Grouped_Rock_3", "SM_GroupedRock_4" };
        string[] smalls = { "BP_Small_Tower01", "BP_Small_Tower02", "BP_Small_Tower03", "BP_Small_Tower04" };
        for (int d = 0; d < Bosses.Length; d++)
        {
            var (name, deg, _, color, tower) = Bosses[d];
            var dist = Child(vista, $"District_{name}", Dir2(deg) * DistrictDist + Vector3.up * DistrictY);
            Vector3 side = Dir2(deg + 90f);
            // 바위 섬 — 위가 구역 바닥
            var rock = LoadC(rocks[d % rocks.Length]);
            if (rock != null)
            {
                var t = Fit(rock, dist, Vector3.zero, deg * 1.7f, 64f, Axis.Width, local: true, topAnchor: true);
                if (t != null) t.localPosition += Vector3.up * 2f;
            }
            Fit(LoadBP(tower), dist, Vector3.zero, deg + 180f, 82f, Axis.Height, local: true);
            Fit(LoadBP(smalls[d]), dist, side * 21f + Dir2(deg) * 5f, deg + 160f, 40f, Axis.Height, local: true);
            Fit(LoadBP(smalls[(d + 2) % 4]), dist, -side * 19f - Dir2(deg) * 8f, deg + 200f, 34f, Axis.Height, local: true);
            Fit(LoadBP("BP_Main_Wall02"), dist, -Dir2(deg) * 18f, deg + 90f, 36f, Axis.Width, local: true);
            // 구역 빛 — 그 챕터 색. 멀리서도 「저기가 숲 · 화룡 · 기사 · 리치 구역」으로 읽히게
            var l = PointLight(dist, new Vector3(0f, 40f, 0f), 95f, 10f);
            l.color = color;
            var glow = PointLight(dist, new Vector3(0f, 6f, 0f) - Dir2(deg) * 14f, 55f, 7f);
            glow.color = Color.Lerp(color, new Color(1f, 0.8f, 0.55f), 0.4f);

            // 사이 구역 — 석상 사이 방향 더 멀리 · 더 깊게(겹겹이 이어진 던전)
            float mid = deg + 45f;
            var sub = Child(vista, $"Outskirt_{d}", Dir2(mid) * 158f + Vector3.up * -112f);
            Fit(LoadC(rocks[(d + 1) % rocks.Length]), sub, Vector3.zero, mid * 2.1f, 44f, Axis.Width, local: true, topAnchor: true);
            Fit(LoadBP(d % 2 == 0 ? "BP_Tall_Tower03" : "BP_Round_Tower02"), sub, Vector3.zero, mid + 150f, 70f, Axis.Height, local: true);
            Fit(LoadBP(smalls[(d + 1) % 4]), sub, Dir2(mid + 90f) * 14f, mid + 120f, 32f, Axis.Height, local: true);
            var sl = PointLight(sub, new Vector3(0f, 30f, 0f), 80f, 6f);
            sl.color = new Color(1f, 0.72f, 0.45f);
        }

        // 깊은 곳 빛 — 탑 아래 심연에서 올라오는 따뜻한 빛(아래가 「깊다」로 읽히게)
        var abyss = PointLight(vista, new Vector3(0f, -175f, 0f), 300f, 7f);
        abyss.color = new Color(1f, 0.55f, 0.3f);

        // 안개 판 — 구역 아래로 깔린다(깊이)
        var fog = Child(vista, "FogBelow", Vector3.zero);
        var fogSrc = LoadC("SM_Fog_Plane");
        if (fogSrc != null)
            for (int i = 0; i < 18; i++)
            {
                float deg = i * 360f / 18f + (i % 2) * 11f;
                float r = 50f + (i % 3) * 55f;
                Fit(fogSrc, fog, Dir2(deg) * r + Vector3.up * (DistrictY - 8f - (i % 3) * 10f), deg * 2.3f, 130f, Axis.Width);
            }

        // 먼 탑 실루엣 — 안개에 묻혀 「아래로 끝없이 이어진 던전」
        var far = Child(vista, "FarSilhouettes", Vector3.zero);
        string[] farTowers = { "BP_Round_Tower01", "BP_Tall_Tower03", "BP_Round_Tower03", "BP_Tall_Tower06", "BP_Small_Tower02", "BP_Tall_Tower01" };
        for (int i = 0; i < 12; i++)
        {
            float deg = i * 30f + 15f;
            float r = 170f + (i % 3) * 25f;
            Fit(LoadBP(farTowers[i % farTowers.Length]), far, Dir2(deg) * r + Vector3.up * (-125f - (i % 2) * 15f), deg * 3f, 80f + (i % 3) * 15f, Axis.Height);
        }
    }

    // ── 도구 ─────────────────────────────────────────────────

    private static Vector3 Dir2(float deg)
    {
        float a = deg * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
    }

    private static GameObject LoadG(string name)  => Load(Gothic + name + ".prefab");
    private static GameObject LoadC(string name)  => Load(Castle + name + ".prefab");
    private static GameObject LoadBP(string name) => Load(CastleBP + name + ".prefab");

    private static GameObject Load(string path)
    {
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (go == null) Debug.LogWarning("[RankingHallBuilder] 에셋 없음 " + path);
        return go;
    }

    /// <summary>
    /// 프리팹을 세워 <paramref name="size"/>(높이 또는 가로 큰 쪽)에 맞춘다 — <paramref name="pos"/>에 바닥 가운데(<paramref name="topAnchor"/>면 위 가운데).
    /// <paramref name="local"/>이면 pos는 부모 기준 · 아니면 뿌리 기준(부모가 뿌리 바로 아래라 같다).
    /// </summary>
    private static Transform Fit(GameObject prefab, Transform parent, Vector3 pos, float yaw, float size, Axis axis, bool local = true, bool topAnchor = false)
    {
        if (prefab == null) return null;
        var b = MeasureBounds(prefab);
        float basis = axis == Axis.Height ? b.size.y : Mathf.Max(b.size.x, b.size.z);
        float s = size / Mathf.Max(0.05f, basis);
        var rot = Quaternion.Euler(0f, yaw, 0f);
        var t = ((GameObject)PrefabUtility.InstantiatePrefab(prefab, parent)).transform;
        t.localRotation = rot;
        t.localScale = prefab.transform.localScale * s;
        var anchor = new Vector3(b.center.x, topAnchor ? b.max.y : b.min.y, b.center.z) * s;
        t.localPosition = pos - rot * anchor;
        return t;
    }

    /// <summary>프리팹 원본 좌표계의 렌더러 경계(배율 1).</summary>
    private static Bounds MeasureBounds(GameObject prefab)
    {
        var tmp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        try
        {
            tmp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            tmp.transform.localScale = Vector3.one;
            var rs = tmp.GetComponentsInChildren<Renderer>(true).Where(r => !(r is ParticleSystemRenderer)).ToList();
            if (rs.Count == 0) return new Bounds(Vector3.zero, Vector3.one);
            var b = rs[0].bounds;
            foreach (var r in rs.Skip(1)) b.Encapsulate(r.bounds);
            return b;
        }
        finally { Object.DestroyImmediate(tmp); }
    }

    private static Transform Place(GameObject prefab, Transform parent, Vector3 localPos, Quaternion rot, float scale)
    {
        var t = ((GameObject)PrefabUtility.InstantiatePrefab(prefab, parent)).transform;
        t.localPosition = localPos; t.localRotation = rot; t.localScale = Vector3.one * scale;
        return t;
    }

    private static Transform Child(Transform parent, string name, Vector3 localPos)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = localPos;
        return t;
    }

    private static TextMeshPro Label(Transform parent, string name, string text, Vector3 localPos, float size)
    {
        var t = Child(parent, name, localPos);
        var tmp = t.gameObject.AddComponent<TextMeshPro>();
        tmp.text = text; tmp.fontSize = size; tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap; tmp.color = new Color(0.95f, 0.88f, 0.7f, 1f);
        tmp.rectTransform.sizeDelta = new Vector2(12f, 2f);
        tmp.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);   // 글 면이 홀 가운데(들어오는 쪽)를 본다 — TMP를 붙이면 Transform이 RectTransform으로 바뀌어 t는 죽는다
        return tmp;
    }

    private static Light PointLight(Transform parent, Vector3 localPos, float range, float intensity)
    {
        var t = Child(parent, "Light", localPos);
        var l = t.gameObject.AddComponent<Light>();
        l.type = LightType.Point; l.range = range; l.intensity = intensity; l.shadows = LightShadows.None;
        return l;
    }

    private static void SetLayerRecursive(GameObject go, int layer)
    {
        if (layer < 0) return;
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
    }

    private static void Register(string path, string address)
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) { Debug.LogWarning("[RankingHallBuilder] Addressables 설정 없음"); return; }
        var group = settings.DefaultGroup;
        var e = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(path), group, false, false);
        e.address = address;
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, e, true);
        AssetDatabase.SaveAssets();
    }
}
#endif
