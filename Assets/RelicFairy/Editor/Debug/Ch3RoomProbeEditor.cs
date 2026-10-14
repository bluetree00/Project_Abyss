#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 10-12 Ch3 방 크기 · 장식 바인드 확인 촬영(플레이 중 · 테스트 허브).
/// 룸 풀 CSV(<c>Assets/RelicFairy/Docs/CHAPTER_N_ROOM_POOL.csv</c> — 디스크 판, CDN 아님)에서 Ch3 방 4개를
/// 멀리 떨어진 자리에 실제 방 빌더(<see cref="GameRunBootstrapper.BuildProcRoomAsync"/>)로 지어, 플레이어를 입구 안 6 m에 세우고
/// 게임 카메라 그대로 한 장씩 찍는다. 이어서 장식 「권장 · 후보」를 줄 세운 사진, 바닥 재질 견본 사진을 찍는다(6장).
/// 결과: <c>Temp/ui_shots/CH3ROOM_*.png</c> · <c>Temp/ui_shots/CH3ROOM_report.txt</c>(덧붙임 — 방 크기 · 벽 높이 · 장식 코드별 개수 ·
/// LOD0 렌더러 크기 · 밑면 높이 · 켜진 콜라이더 수 · 공중에 뜬 수 · 벽에서 튀어나온 길이 · 몸통 방향). <c>Assets/</c>에는 아무것도 쓰지 않는다.
/// 지은 방 · 줄 · 견본(런타임 재질 사본 포함)은 끝나면 지운다.
/// 쓰는 법: 고친 판 반입 전 한 번(지금 판) · 반입 후 한 번(고친 판) 돌려 두 보고를 비교한다.
/// </summary>
public static class Ch3RoomProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string MenuPath   = "RelicFairy/Debug/10-12 Ch3 방 크기 · 장식 촬영 (테스트 허브, 플레이 중)";
    private const string ShotDir    = "Temp/ui_shots";
    private const string ReportPath = "Temp/ui_shots/CH3ROOM_report.txt";
    private const string GothicDir  = "Assets/RelicFairy/_Imported/Gothic_Interior/Environment/Asset/Prefabs/";
    private const string CastleDir  = "Assets/RelicFairy/_Imported/LeartesStudios/FantasyCastle/~HDRP/Art/Prefabs/";
    private const string GothicMat  = "Assets/RelicFairy/_Imported/Gothic_Interior/Environment/Asset/Materials/";
    private const string WorldTile  = "Vector1_1";   // S_OpaqueORMWorldAlign 「UVScale2」 — 월드 정렬 무늬 한 장의 크기(M_Tiles_01a = 4)
    private const float  WalkIn     = 6f;            // 입구 안쪽으로 걸어 들어간 자리
    private const float  SettleMs   = 3000f;         // 디졸브 · 카메라 안정

    private static readonly (int chapter, string key, string why)[] Rooms =
    {
        (3, "ch3_normal_sacred_01", "작은 일반방(32→42) — 벽 둘레 보강"),
        (3, "ch3_normal_sacred_11", "가로 벽 두 줄 구조 방 — 벽 둘레 보강"),
        (3, "ch3_normal_sacred_15", "높은 난이도 일반방 — 벽 둘레 보강"),
        (3, "ch3_elite_sacred_04",  "정예 방 — 벽 촛대 4개(LOD0 고정 확인)"),
        (4, "ch4_normal_fortress_05", "Ch4 넓힌 방(10-13) — 벽 둘레 성채 소품 · 벽 · 바닥 재질"),
    };

    // 「권장 · 후보」 줄 — (이름, 경로, 배율, Y 회전: 앞면이 카메라를 보게)
    private static readonly (string label, string path, float scale, float yaw)[][] Lineup =
    {
        new[] {   // 앞줄 — 벽 둘레 묶음의 바닥 소품
            ("bok SM_Merged_Books_01c",   GothicDir + "SM_Merged_Books_01c.prefab", 1f, 0f),
            ("can2 SM_Candleabra_04b",    GothicDir + "SM_Candleabra_04b.prefab",   1f, 0f),
            ("can SM_Candleabra_01a",     GothicDir + "SM_Candleabra_01a.prefab",   1f, 0f),
            ("ped SM_Pedestal_01a",       GothicDir + "SM_Pedestal_01a.prefab",     1f, 0f),
            ("amb SM_Bookpedestal_01a",   GothicDir + "SM_Bookpedestal_01a.prefab", 1f, 0f),
        },
        new[] {   // 가운데 줄 — 기사 석상(kst) 권장 · 후보
            ("kst 권장 SM_Statue_2(미늘창 기사)", CastleDir + "SM_Statue_2.prefab", 1f, 180f),
            ("kst 후보 SM_Statue_3",              CastleDir + "SM_Statue_3.prefab", 1f, 180f),
            ("kst 후보 SM_Statue_4",              CastleDir + "SM_Statue_4.prefab", 1f, 180f),
            ("cha SM_LargeCandleHolder_01b",      GothicDir + "SM_LargeCandleHolder_01b.prefab", 1f, 0f),
        },
        new[] {   // 뒷줄 — 벽걸이(앞면을 카메라 쪽으로)
            ("ban 권장 SM_banner×0.6",     CastleDir + "SM_banner.prefab",                    0.6f,  90f),
            ("win 유리판",                 GothicDir + "SM_GlassWindowCathedral_01a.prefab",  1f,    90f),
            ("wcan 벽 촛대(LOD0 고정)",    GothicDir + "SM_WallCandleabra_01a.prefab",        1f,    -90f),
            ("ros 장미창×0.45",            GothicDir + "SM_Rosette_Window_01a.prefab",        0.45f, 90f),
        },
    };
    private static readonly float[] LineupRowZ = { 4f, 9f, 16f };

    private static CancellationTokenSource s_cts;

    [MenuItem(MenuPath)]
    private static void Begin()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[Ch3RoomProbe] 플레이 모드(테스트 허브 또는 런 중)에서"); return; }
        s_cts?.Cancel();
        s_cts?.Dispose();
        s_cts = new CancellationTokenSource();
        RunAsync(s_cts.Token).Forget();
    }

    private static async UniTaskVoid RunAsync(CancellationToken ct)
    {
        var sb    = new StringBuilder($"\n== 10-12 Ch3 방 촬영 · {DateTime.Now:MM-dd HH:mm:ss}\n");
        var built = new List<GameObject>();
        var temps = new List<Object>();
        try
        {
            Directory.CreateDirectory(ShotDir);
            if (!await EnsureRunAsync(sb, ct)) return;

            var boot   = GameRunBootstrapper.Instance;
            var player = boot.Run.Player;
            Vector3 home = player.transform.position;
            var pools  = new Dictionary<int, List<ZonePoolEntry>>();
            int shot   = 0;
            GameObject stage = null;
            Vector3 stageStand = default;

            for (int i = 0; i < Rooms.Length; i++)
            {
                var (ch, key, why) = Rooms[i];
                if (!pools.TryGetValue(ch, out var pool)) pools[ch] = pool = ReadPool(ch);
                var entry = pool.Find(e => e.pool_key == key);
                if (entry == null) { sb.AppendLine($"{key}: 디스크 룸 풀에 없다"); continue; }

                var anchor = new Vector3(4000f + i * 160f, 0f, 4000f);
                var res = await boot.BuildProcRoomAsync(entry, anchor, false, 0, ct, new System.Random(12), new System.Random(12));
                if (res == null || res.roomGO == null) { sb.AppendLine($"{key}: 빌드 실패"); continue; }
                built.Add(res.roomGO);
                ShowBlocks(res);

                Vector3 stand = res.entryPos + Vector3.forward * WalkIn;
                Teleport(player, stand);
                await UniTask.Delay((int)SettleMs, DelayType.Realtime, cancellationToken: ct);
                await UniTask.WaitForEndOfFrame(ct);

                shot++;
                ScreenCapture.CaptureScreenshot(Path.Combine(ShotDir, $"CH3ROOM_{shot}_{key}.png"));
                await UniTask.Delay(400, DelayType.Realtime, cancellationToken: ct);
                DescribeRoom(sb, shot, why, entry, res, anchor, player.transform.position);

                if (stage == null && ch == 3) { stage = res.roomGO; stageStand = stand; }
            }

            if (stage != null)
            {
                // 장식 줄
                Teleport(player, stageStand);
                var row = BuildLineup(stageStand, sb);
                built.Add(row);
                await UniTask.Delay((int)SettleMs, DelayType.Realtime, cancellationToken: ct);
                await UniTask.WaitForEndOfFrame(ct);
                shot++;
                ScreenCapture.CaptureScreenshot(Path.Combine(ShotDir, $"CH3ROOM_{shot}_후보줄.png"));
                await UniTask.Delay(400, DelayType.Realtime, cancellationToken: ct);
                Object.Destroy(row);

                // 바닥 재질 견본(D6) — 같은 방 바닥 위에 6×6 m 판 4장
                var swatch = BuildFloorSwatches(stageStand, sb, temps);
                built.Add(swatch);
                await UniTask.Delay((int)SettleMs, DelayType.Realtime, cancellationToken: ct);
                await UniTask.WaitForEndOfFrame(ct);
                shot++;
                ScreenCapture.CaptureScreenshot(Path.Combine(ShotDir, $"CH3ROOM_{shot}_바닥견본.png"));
                await UniTask.Delay(400, DelayType.Realtime, cancellationToken: ct);
                await UniTask.WaitForEndOfFrame(ct);
                MeasureSwatches(sb);
            }

            Teleport(player, home);
            sb.AppendLine($"사진 {shot}장 → {ShotDir}/CH3ROOM_*.png");
        }
        catch (OperationCanceledException) { sb.AppendLine("취소됨"); }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            foreach (var go in built) if (go != null) Object.Destroy(go);
            foreach (var o in temps) if (o != null) Object.Destroy(o);
            File.AppendAllText(ReportPath, sb.ToString());
            Debug.Log($"[Ch3RoomProbe] 끝 — {ReportPath}");
        }
    }

    /// <summary>런이 없으면 테스트 허브에서 Ch3 대기방부터 시작한다.</summary>
    private static async UniTask<bool> EnsureRunAsync(StringBuilder sb, CancellationToken ct)
    {
        if (GameRunBootstrapper.Instance?.Run?.Player != null) return true;

        var launcher = Object.FindFirstObjectByType<TestHubLauncher>();
        if (launcher == null) { sb.AppendLine("테스트 허브도 런도 아니다"); return false; }
        launcher.SelectChapter(3);
        typeof(TestHubLauncher).GetField("_bossApproach", Inst)?.SetValue(launcher, false);
        if (!launcher.TryLaunch()) { sb.AppendLine("런 시작 실패(허브 준비 전)"); return false; }

        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 60f)
        {
            await UniTask.Delay(500, DelayType.Realtime, cancellationToken: ct);
            if (GameRunBootstrapper.Instance?.Run?.Player != null && Camera.main != null)
            {
                await UniTask.Delay(4000, DelayType.Realtime, cancellationToken: ct);
                for (int i = 0; i < 6; i++) TestHubDebugMenu.AdvanceDialogue();
                await UniTask.Delay(1500, DelayType.Realtime, cancellationToken: ct);
                return true;
            }
        }
        sb.AppendLine("런 준비 대기 초과(60초)");
        return false;
    }

    // ── 룸 풀 — 디스크 CSV를 직접 읽는다(CDN · 캐시와 무관하게 「지금 디스크 판」을 찍기 위해) ──
    private static List<ZonePoolEntry> ReadPool(int chapter)
    {
        var list = new List<ZonePoolEntry>();
        string text = File.ReadAllText($"Assets/RelicFairy/Docs/CHAPTER_{chapter}_ROOM_POOL.csv").TrimStart((char)0xFEFF);
        var rows = ParseCsv(text);
        if (rows.Count < 2) return list;
        var head = rows[0];
        string Get(List<string> r, string col) { int k = head.IndexOf(col); return k >= 0 && k < r.Count ? r[k] : string.Empty; }
        for (int i = 1; i < rows.Count; i++)
        {
            var r = rows[i];
            if (r.Count < 5) continue;
            int.TryParse(Get(r, "grid_width"), out int w);
            int.TryParse(Get(r, "grid_height"), out int h);
            int.TryParse(Get(r, "max_active_spawners"), out int maxSp);
            float.TryParse(Get(r, "difficulty_scale"), System.Globalization.NumberStyles.Float,
                           System.Globalization.CultureInfo.InvariantCulture, out float ds);
            list.Add(new ZonePoolEntry
            {
                pool_key            = Get(r, "pool_key"),
                category            = Get(r, "category"),
                grid_width          = w,
                grid_height         = h,
                grid_csv            = Get(r, "grid_csv"),
                theme               = Get(r, "theme"),
                palette             = Get(r, "palette"),
                arena_template_key  = Get(r, "arena_template_key"),
                difficulty_scale    = ds,
                max_active_spawners = maxSp,
            });
        }
        return list;
    }

    private static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var row  = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else cell.Append(c);
                continue;
            }
            if (c == '"') quoted = true;
            else if (c == ',') { row.Add(cell.ToString()); cell.Clear(); }
            else if (c == '\n') { row.Add(cell.ToString().TrimEnd('\r')); cell.Clear(); rows.Add(row); row = new List<string>(); }
            else cell.Append(c);
        }
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }
        return rows;
    }

    // ── 방 · 플레이어 ──
    private static void ShowBlocks(ProcRoomResult res)
    {
        if (res.blocks == null) return;
        foreach (var b in res.blocks)
        {
            if (b.instance == null) continue;
            foreach (var r in b.instance.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
        }
    }

    private static void Teleport(PlayerController player, Vector3 pos)
    {
        if (player == null) return;
        player.transform.position = pos;
        if (player.TryGetComponent<Rigidbody>(out var rb)) { rb.position = pos; rb.linearVelocity = Vector3.zero; }
        GameCameraController.Instance?.SetHeadingImmediate(0f);   // 방은 북쪽(+Z)이 직진 — 입구에서 방 안을 본다
    }

    // ── 크기 재기 — LOD가 있으면 LOD0 렌더러만(하위 LOD의 깨진 정점 · 다른 단계 메시가 경계를 부풀리지 않게) ──
    private static bool MeasureBounds(GameObject go, out Bounds b)
    {
        b = default;
        var rends = new List<Renderer>();
        var lod = go.GetComponentInChildren<LODGroup>();
        if (lod != null && lod.lodCount > 0)
            foreach (var r in lod.GetLODs()[0].renderers) if (r != null && r is MeshRenderer) rends.Add(r);
        if (rends.Count == 0)
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>()) rends.Add(r);
        if (rends.Count == 0) return false;
        b = rends[0].bounds;
        for (int i = 1; i < rends.Count; i++) b.Encapsulate(rends[i].bounds);
        return true;
    }

    // ── 기록 ──
    private static readonly HashSet<string> WallCodes = new() { "win", "ros", "wcan", "ban", "kst", "arc", "arc2", "bar", "cab", "chn", "web", "lL", "lM", "lH" };

    /// <summary>장식 코드 하나의 합계.</summary>
    private sealed class CodeStat
    {
        public int     N;          // 개수
        public Vector3 Size;       // LOD0 렌더러 경계 합
        public float   Bottom;     // 밑면 − 바닥 합
        public int     Floating;   // 벽걸이: 1.2 m 안에 벽 없음 · 바닥 소품: 밑면이 바닥보다 0.15 m 위
        public int     Buried;     // 밑면이 바닥보다 0.3 m 아래
        public int     Fin;        // 벽걸이가 벽에 직각으로 튀어나온 판
        public float   Stick;      // 벽걸이가 벽에서 방 안쪽으로 차지하는 깊이 합
        public int     Solid;      // 켜진 콜라이더(트리거 아님)가 있는 개수
        public int     ToRoom;     // 벽걸이 몸통(경계 중심)이 기준점보다 방 쪽
        public int     ToWall;     // 벽걸이 몸통이 기준점보다 벽 쪽(벽 속으로 박힘 의심)
    }

    private static void DescribeRoom(StringBuilder sb, int shot, string why, ZonePoolEntry e, ProcRoomResult res, Vector3 anchor, Vector3 playerPos)
    {
        Physics.SyncTransforms();
        var rows = e.grid_csv.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        int h = rows.Length, w = rows.Length > 0 ? rows[0].Split(',').Length : 0;
        int floorCells = 0;
        foreach (var row in rows) foreach (var t in row.Split(',')) { var s = t.Trim(); if (s != "W" && !s.StartsWith("DR")) floorCells++; }

        int wallMask = LayerMask.GetMask("Wall");
        var box = new Vector3(w * 0.5f + 1f, 30f, h * 0.5f + 1f);
        float wallTop = float.NaN;
        foreach (var c in Physics.OverlapBox(anchor + Vector3.up * 15f, box, Quaternion.identity, wallMask, QueryTriggerInteraction.Ignore))
            wallTop = float.IsNaN(wallTop) ? c.bounds.max.y : Mathf.Max(wallTop, c.bounds.max.y);

        var cam = DisplayCamera();
        string camLine = cam == null ? "카메라 없음"
            : $"카메라 높이 {cam.transform.position.y - playerPos.y:0.0} m · 수평 {new Vector2(cam.transform.position.x - playerPos.x, cam.transform.position.z - playerPos.z).magnitude:0.0} m · 각 {cam.transform.eulerAngles.x:0}° · FOV {cam.fieldOfView:0}";

        sb.AppendLine($"[{shot}] {e.pool_key} ({e.category} · {e.theme}) — {why}");
        sb.AppendLine($"    격자 {w}×{h}칸(1칸 1 m) · 선언 {e.grid_width}×{e.grid_height} · 벽 아닌 칸 {floorCells} · 벽 윗면 {(float.IsNaN(wallTop) ? "?" : (wallTop - anchor.y).ToString("0.0"))} m · {camLine}");

        var catalog = LoadCatalog(e.theme);
        var stats = new SortedDictionary<string, CodeStat>();
        int decoTotal = 0;
        float floorY = anchor.y;
        foreach (Transform t in res.roomGO.transform)
        {
            if (!t.name.StartsWith("Deco_")) continue;
            var parts = t.name.Split('_');
            string code = parts.Length >= 4 ? parts[3] : "?";
            if (!stats.TryGetValue(code, out var s)) stats[code] = s = new CodeStat();
            s.N++; decoTotal++;
            foreach (var col in t.GetComponentsInChildren<Collider>())
                if (col.enabled && !col.isTrigger) { s.Solid++; break; }

            if (!MeasureBounds(t.gameObject, out var b)) continue;
            s.Size   += b.size;
            s.Bottom += b.min.y - floorY;
            if (b.min.y < floorY - 0.3f) s.Buried++;

            if (WallCodes.Contains(code))
            {
                // 벽까지 수평 거리(네 방향) — 1.2 m 안에 벽이 없으면 공중에 뜬 벽걸이
                float best = float.MaxValue; Vector3 dir = Vector3.zero;
                foreach (var d in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
                    if (Physics.Raycast(new Vector3(t.position.x, b.center.y, t.position.z), d, out var hit, 3f, wallMask, QueryTriggerInteraction.Ignore) && hit.distance < best)
                        { best = hit.distance; dir = d; }
                if (best > 1.2f) { s.Floating++; continue; }
                float stick = Mathf.Abs(dir.x) > 0.5f ? b.size.x : b.size.z;   // 벽에서 방 안쪽으로 차지하는 깊이
                float along = Mathf.Abs(dir.x) > 0.5f ? b.size.z : b.size.x;   // 벽을 따라 차지하는 폭
                s.Stick += stick;
                if (stick > 1.5f && stick > along) s.Fin++;                    // 판이 벽에 직각으로 튀어나왔다
                // 몸통 방향 — 경계 중심이 기준점에서 방 쪽(-dir)인가 벽 쪽(+dir)인가
                float lean = Vector3.Dot(new Vector3(b.center.x - t.position.x, 0f, b.center.z - t.position.z), -dir);
                if (lean > 0.05f) s.ToRoom++;
                else if (lean < -0.05f) s.ToWall++;
            }
            else if (b.min.y > floorY + 0.15f) s.Floating++;
        }

        sb.AppendLine($"    장식 {decoTotal}개 · 밀도 {(floorCells > 0 ? decoTotal * 100f / floorCells : 0f):0.0}%");
        foreach (var kv in stats)
        {
            var s = kv.Value;
            string prefab = catalog != null && catalog.Get(kv.Key)?.prefab != null ? catalog.Get(kv.Key).prefab.name : "?";
            var avg = s.Size / Mathf.Max(1, s.N);
            sb.AppendLine($"    d{kv.Key,-5} ×{s.N,3}  {prefab,-30} 크기 {avg.x:0.00}×{avg.y:0.00}×{avg.z:0.00} m · 밑면 {s.Bottom / Mathf.Max(1, s.N):+0.00;-0.00} m · 콜라이더 켜짐 {s.Solid}" +
                          (s.Floating > 0 ? $" · 공중 {s.Floating}" : "") + (s.Buried > 0 ? $" · 바닥 밑 {s.Buried}" : "") +
                          (WallCodes.Contains(kv.Key) ? $" · 벽 직각 판 {s.Fin} · 벽에서 튀어나옴 평균 {s.Stick / Mathf.Max(1, s.N - s.Floating):0.00} m · 몸통 방 쪽 {s.ToRoom} / 벽 쪽 {s.ToWall}" : ""));
        }
    }

    private static DecorationCatalogSO LoadCatalog(string theme)
        => string.IsNullOrEmpty(theme) ? null
           : AssetDatabase.LoadAssetAtPath<DecorationCatalogSO>($"Assets/RelicFairy/Settings/DecorationCatalog_{theme}.asset");

    private static Camera DisplayCamera()
    {
        Camera best = null;
        foreach (var c in Camera.allCameras)
            if (c.targetTexture == null && (best == null || c.depth > best.depth)) best = c;
        return best;
    }

    // ── 후보 줄 ──
    private static GameObject BuildLineup(Vector3 stand, StringBuilder sb)
    {
        var root = new GameObject("@Ch3RoomProbe_Lineup");
        sb.AppendLine("[후보 줄] 왼쪽 → 오른쪽 · LOD0 렌더러 경계(가로×높이×깊이 m)");
        for (int r = 0; r < Lineup.Length; r++)
        {
            var placed = new List<(GameObject go, Bounds b, string label)>();
            foreach (var (label, path, scale, yaw) in Lineup[r])
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { sb.AppendLine($"    {label}: 프리팹 없음 {path}"); continue; }
                var go = Object.Instantiate(prefab, root.transform);
                go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                go.transform.localScale *= scale;
                go.transform.position = Vector3.zero;
                foreach (var lod in go.GetComponentsInChildren<LODGroup>()) lod.ForceLOD(0);   // 깨진 하위 LOD를 보이지 않게
                if (!MeasureBounds(go, out var b)) b = new Bounds(go.transform.position, Vector3.one);
                placed.Add((go, b, label));
            }
            float total = -1f;
            foreach (var p in placed) total += p.b.size.x + 1f;
            float x = stand.x - total * 0.5f;
            var line = new StringBuilder($"    {r + 1}줄(앞 {LineupRowZ[r]:0} m): ");
            foreach (var (go, b, label) in placed)
            {
                // 경계 왼쪽 끝을 x에, 바닥을 플레이어 발 높이에
                var offset = new Vector3(x - b.min.x, stand.y - b.min.y, stand.z + LineupRowZ[r] - b.center.z);
                go.transform.position += offset;
                x += b.size.x + 1f;
                line.Append($"{label} {b.size.x:0.0}×{b.size.y:0.0}×{b.size.z:0.0} | ");
            }
            sb.AppendLine(line.ToString());
        }
        return root;
    }

    // ── 바닥 재질 견본(D6) — 에셋은 만들지 않는다(런타임 재질 사본) ──
    // 견본 가운데(월드) · 이름 — 촬영 뒤 화면 밝기를 잰다
    private static readonly List<(Vector3 center, string label)> s_swatches = new();

    // S_OpaqueORMWorldAlign 속성(그래프 이름: ColorMultiply · Color · Metalness Intensity · Smoothness Intensity · AO Intensity)
    private const string PropColorMul = "Vector1_1ecf8adf107543b697e04962611a6572";
    private const string PropColor    = "Color_AF14EE56";
    private const string PropMetal    = "Vector1_119DBA34";
    private const string PropSmooth   = "Vector1_ED619312";
    private const string PropAo       = "Vector1_4B28F8D1";

    private static GameObject BuildFloorSwatches(Vector3 stand, StringBuilder sb, List<Object> temps)
    {
        var root = new GameObject("@Ch3RoomProbe_FloorSwatches");
        s_swatches.Clear();
        var ch4  = AssetDatabase.LoadAssetAtPath<Material>("Assets/RelicFairy/Systems/Stage/MapGen/Materials/M_RF_Ch4_Floor_Tiles.mat");
        var list = new List<(string label, Material mat)>();
        if (ch4 != null)
        {
            list.Add(("① Ch4 바닥 지금(색 0.65 · 매끈함 0.7 · 대비 0.8)", ch4));
            Material Variant(string label, System.Action<Material> edit)
            {
                var m = new Material(ch4) { name = "(견본) " + label };
                edit(m);
                temps.Add(m);
                list.Add((label, m));
                return m;
            }
            Variant("② 색 배수(ColorMultiply) 0.6", m => Set(m, PropColorMul, 0.6f));
            Variant("③ 금속 0.67 → 0.2 · 매끈함 → 0.15", m => { Set(m, PropMetal, 0.2f); Set(m, PropSmooth, 0.15f); });
            Variant("④ ③ + 색 배수 0.6", m => { Set(m, PropMetal, 0.2f); Set(m, PropSmooth, 0.15f); Set(m, PropColorMul, 0.6f); });
            Variant("⑤ 색 알파 1(색 곱이 알파로 섞이는지)", m => { if (m.HasProperty(PropColor)) { var c = m.GetColor(PropColor); c.a = 1f; m.SetColor(PropColor, c); } });
        }

        // 견본 한 장 = 무늬 한 주기(월드 정렬 4 m)에 딱 맞춘다 — 장마다 같은 무늬가 들어가 밝기를 견줄 수 있다
        const float Size = 4f, Gap = 0f;
        float x0 = Mathf.Floor((stand.x - list.Count * Size * 0.5f) / Size) * Size;
        float z0 = Mathf.Ceil((stand.z + 3f) / Size) * Size;
        sb.AppendLine($"[바닥 견본 — Ch4 바닥 손잡이] 왼쪽 → 오른쪽 · 각 {Size}×{Size} m(1 m 큐브 · 무늬 한 주기) · 플레이어 앞 · 화면 밝기는 아래 「견본 밝기」");
        for (int i = 0; i < list.Count; i++)
        {
            var (label, mat) = list[i];
            var patch = new GameObject($"Swatch_{i}");
            patch.transform.SetParent(root.transform, false);
            float cx = x0 + i * (Size + Gap) + Size * 0.5f, cz = z0 + Size * 0.5f;
            for (int gx = 0; gx < (int)Size; gx++)
            for (int gz = 0; gz < (int)Size; gz++)
                AddCube(patch.transform, new Vector3(cx - Size * 0.5f + gx + 0.5f, stand.y + 0.01f, cz - Size * 0.5f + gz + 0.5f), new Vector3(1f, 0.02f, 1f), mat);
            s_swatches.Add((new Vector3(cx, stand.y + 0.02f, cz), label));   // 가운데 — 잴 때는 네 귀퉁이를 화면에 비춘 사각형 안쪽 60%
            sb.AppendLine($"    {i + 1}. {label}");
        }
        return root;
    }

    private static void Set(Material m, string prop, float v)
    {
        if (m.HasProperty(prop)) m.SetFloat(prop, v);
    }

    /// <summary>견본 가운데 둘레(화면 40×40 px)의 평균 밝기(0~255 회색조)와 편차 — 같은 조명 · 같은 카메라에서 손잡이만 다르다.</summary>
    private static void MeasureSwatches(StringBuilder sb)
    {
        var cam = Camera.main;
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        if (cam == null || tex == null) { sb.AppendLine("  견본 밝기 — 카메라 · 화면 없음"); return; }
        try
        {
            foreach (var (center, label) in s_swatches)
            {
                // 견본(4×4 m) 네 귀퉁이를 화면에 비춘 사각형 — 그 안쪽 60%만(가장자리 · 이웃 견본 빼고)
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                bool behind = false;
                foreach (var d in new[] { new Vector3(-2f, 0f, -2f), new Vector3(2f, 0f, -2f), new Vector3(2f, 0f, 2f), new Vector3(-2f, 0f, 2f) })
                {
                    Vector3 sp = cam.WorldToScreenPoint(center + d);
                    if (sp.z <= 0f) behind = true;
                    minX = Mathf.Min(minX, sp.x); maxX = Mathf.Max(maxX, sp.x); minY = Mathf.Min(minY, sp.y); maxY = Mathf.Max(maxY, sp.y);
                }
                if (behind) { sb.AppendLine($"  견본 밝기 {label}: 화면 밖"); continue; }
                float sx = (float)tex.width / Screen.width, sy = (float)tex.height / Screen.height;
                int x0p = Mathf.RoundToInt(Mathf.Lerp(minX, maxX, 0.2f) * sx), x1p = Mathf.RoundToInt(Mathf.Lerp(minX, maxX, 0.8f) * sx);
                int y0p = Mathf.RoundToInt(Mathf.Lerp(minY, maxY, 0.2f) * sy), y1p = Mathf.RoundToInt(Mathf.Lerp(minY, maxY, 0.8f) * sy);
                int cx = (x0p + x1p) / 2, cy = (y0p + y1p) / 2;
                double sum = 0, sq = 0; int n = 0;
                for (int y = y0p; y < y1p; y += 2)
                for (int x = x0p; x < x1p; x += 2)
                {
                    if (x < 0 || y < 0 || x >= tex.width || y >= tex.height) continue;
                    var c = tex.GetPixel(x, y);
                    double l = 255.0 * (0.299 * c.r + 0.587 * c.g + 0.114 * c.b);
                    sum += l; sq += l * l; n++;
                }
                if (n == 0) { sb.AppendLine($"  견본 밝기 {label}: 화면 밖"); continue; }
                double mean = sum / n, sd = System.Math.Sqrt(System.Math.Max(0, sq / n - mean * mean));
                sb.AppendLine($"  견본 밝기 {label}: 평균 {mean:0.0} · 편차 {sd:0.0} (화면 가운데 {cx},{cy} · 표본 {n})");
            }
        }
        finally { Object.Destroy(tex); }
    }

    private static void AddCube(Transform parent, Vector3 pos, Vector3 scale, Material mat)
    {
        var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
        if (c.TryGetComponent<Collider>(out var col)) Object.Destroy(col);
        c.transform.SetParent(parent, true);
        c.transform.position = pos;
        c.transform.localScale = scale;
        c.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }
}
#endif
