using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// 아레나 지형 점검 — 「보이는 바닥 · 밟히는 바닥 · 벽 · NavMesh」를 0.25 m 칸으로 대조해 어긋난 곳을 찾는다(에디터 전용).
///   · 밟힘: 실제 콜라이더를 위에서 내리쏜다. 설 수 있음 = 발밑 0.35 m 안에 Ground 레이어(플레이어 접지와 같은 기준).
///   · 보임: 렌더러 메시를 별도 물리 씬에 MeshCollider로 복제해 내리쏜다(게임 물리는 건드리지 않는다).
///   · NavMesh: 삼각형을 칸에 옮기고, 아레나 중심과 이어지지 않은 조각을 「섬」으로 센다(벽 위·장식 위에 구워진 것).
///   · 도달: 플레이어 자리에서 설 수 있는 칸을 따라 채운다(벽·턱은 한 칸 부풀려 막는다 — 몸 두께).
/// 전투 상태(입구 벽·결계 켜짐)에서 본다. 플레이어는 점검 동안 무적.
/// 메뉴: RelicFairy/Boss/Arena Terrain Inspector (Play) — 보스 아레나 안에서.
///       RelicFairy/Boss/Arena Terrain Inspector - Selected Root (Play) — 방 개념이 없는 곳(베이스캠프 등): 계층에서 고른 오브젝트가 루트,
///       기준 바닥 = 플레이어 발밑 Ground(층이 여럿이면 층마다 그 층에 서서 돌린다), 보스 입구 단계는 건너뛴다.
///       RelicFairy/Boss/Arena Terrain Inspector - Current Room (Play) — 플레이어가 선 방(발밑 Ground의 ProcRoom 조상) — 보스 대기방 등.
/// 결과: Logs/arena_terrain/&lt;방&gt;_&lt;시각&gt;/ top.png(위에서 본 화면) · terrain.png(분류) · overlay.png(겹침) · view_*.png(비스듬히 4방향) · game.png · report.txt
/// </summary>
public static class ArenaTerrainInspectorEditor
{
    // ── Constants ──────────────────────────────────────────────
    private const float Cell       = 0.25f;
    private const float Margin     = 6f;
    private const float Step       = 0.35f;   // 이보다 높으면 턱 · 발밑 비접지 면 허용치
    private const float Band       = 0.5f;    // 바닥 높이로 치는 폭(±) — 보이는 바닥 판정
    private const float WallHeight = 2f;
    private const float DropDepth  = 1f;      // 바닥보다 이만큼 낮으면 떨어지는 곳
    private const int   Px         = 4;       // 칸당 픽셀
    private const int   SeenCells  = 16;      // 도달 영역에서 4 m 안 = 플레이 카메라에 잡히는 범위로 본다
    private const float MainNavRadius = 4f;   // 아레나 중심 이 반경 안의 NavMesh 조각 = 본 영역
    private const int   MaxClusters = 12;
    private const float RayHeight   = 150f;   // 바닥 위 이 높이에서 내리쏜다 — 벽보다 높아야 한다(안에서 시작하면 그 벽을 못 맞힌다)
    private const float BodyRadius  = 0.25f;  // 설 수 있는 면 판정 — 위로 몸(0.35~1.75 m)이 들어갈 트임
    private const int   MaxSurfaces = 6;      // 칸마다 위에서부터 훑는 면 수(지붕·층·바닥)
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private enum K : byte { Hole, Stand, Air, Ledge, Wall }

    // ── Static ─────────────────────────────────────────────────
    private static CancellationTokenSource s_cts;

    // ── Public Methods ─────────────────────────────────────────
    [MenuItem("RelicFairy/Boss/Arena Terrain Inspector (Play)")]
    public static void Run()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[TerrainProbe] 플레이 모드에서만."); return; }
        s_cts?.Cancel(); s_cts?.Dispose();
        s_cts = new CancellationTokenSource();
        RunAsync(s_cts.Token, null).Forget();
    }

    [MenuItem("RelicFairy/Boss/Arena Terrain Inspector - Current Room (Play)")]
    public static void RunCurrentRoom()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[TerrainProbe] 플레이 모드에서만."); return; }
        var room = CurrentRoom(GameRunBootstrapper.Instance?.Run?.Player);
        if (room == null) { Debug.LogWarning("[TerrainProbe] 준비 안 됨 — 플레이어 발밑에 방(ProcRoom) 바닥이 없다"); return; }
        s_cts?.Cancel(); s_cts?.Dispose();
        s_cts = new CancellationTokenSource();
        RunAsync(s_cts.Token, room).Forget();
    }

    /// <summary>플레이어 발밑 Ground 콜라이더의 ProcRoom 조상 — 없으면 null.</summary>
    internal static Transform CurrentRoom(PlayerController player)
    {
        if (player == null) return null;
        if (!Physics.Raycast(player.transform.position + Vector3.up, Vector3.down, out var hit, 6f, LayerMask.GetMask("Ground"), QueryTriggerInteraction.Ignore))
            return null;
        for (Transform t = hit.collider.transform; t != null; t = t.parent)
            if (t.name.StartsWith("ProcRoom")) return t;
        return null;
    }

    [MenuItem("RelicFairy/Boss/Arena Terrain Inspector - Selected Root (Play)")]
    public static void RunSelectedRoot()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[TerrainProbe] 플레이 모드에서만."); return; }
        var root = Selection.activeTransform;
        if (root == null) { Debug.LogWarning("[TerrainProbe] 계층에서 루트 오브젝트를 먼저 고른다."); return; }
        s_cts?.Cancel(); s_cts?.Dispose();
        s_cts = new CancellationTokenSource();
        RunAsync(s_cts.Token, root).Forget();
    }

    // ── Private Methods ────────────────────────────────────────
    /// <summary>점검 한 번. 다른 도구가 층마다 기다리며 부를 수 있게 UniTask로 돌려준다(메뉴는 Forget).</summary>
    internal static async UniTask RunAsync(CancellationToken ct, Transform rootOverride)
    {
        PlayerController player = null;
        FieldInfo invField = null;
        bool savedInv = false;
        Scene vis = default;
        try
        {
            player = GameRunBootstrapper.Instance?.Run?.Player;
            if (player == null) player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
            Transform room = rootOverride;
            if (room == null)
            {
                var spawner = UnityEngine.Object.FindFirstObjectByType<BossSpawner>();
                if (player == null || spawner == null) { Debug.LogWarning($"[TerrainProbe] 준비 안 됨 — player={player != null} spawner={spawner != null}"); return; }
                room = spawner.transform;
                while (room.parent != null && !room.name.StartsWith("ProcRoom")) room = room.parent;
            }
            if (player == null) { Debug.LogWarning("[TerrainProbe] 준비 안 됨 — 플레이어 없음"); return; }

            invField = typeof(PlayerController).GetField("debugInvincible", Inst);
            if (invField != null) { savedInv = (bool)invField.GetValue(player); invField.SetValue(player, true); }

            if (rootOverride == null)   // 보스 아레나 — 입구를 지나 전투 상태(벽·결계 켜짐)에서 본다
            {
                TestHubDebugMenu.StepIntoBossEntrance();
                await UniTask.Delay(600, ignoreTimeScale: true, cancellationToken: ct);
                TestHubDebugMenu.StepPastBossEntrance();
                await UniTask.Delay(1500, ignoreTimeScale: true, cancellationToken: ct);
            }
            Physics.SyncTransforms();

            // ── 격자 ──
            int groundLayer = LayerMask.NameToLayer("Ground");
            var floors = room.GetComponentsInChildren<Collider>(false)
                             .Where(c => c.enabled && !c.isTrigger && c.gameObject.layer == groundLayer).ToList();
            if (floors.Count == 0) { Debug.LogWarning("[TerrainProbe] Ground 바닥이 없다"); return; }
            var mainCol = floors.OrderByDescending(c => c.bounds.size.x * c.bounds.size.z).First();
            // 루트 지정이면 플레이어가 선 층이 기준 — 층이 여럿인 곳(회랑·광장)
            if (rootOverride != null
                && Physics.Raycast(player.transform.position + Vector3.up, Vector3.down, out var under, 4f, 1 << groundLayer, QueryTriggerInteraction.Ignore)
                && floors.Contains(under.collider))
                mainCol = under.collider;
            Bounds main = FloorBounds(floors, mainCol);
            Vector3 center = main.center;
            float floorTop = mainCol.bounds.max.y;
            float hx = main.extents.x + Margin, hz = main.extents.z + Margin;
            int nx = Mathf.RoundToInt(2f * hx / Cell) + 1, nz = Mathf.RoundToInt(2f * hz / Cell) + 1, n = nx * nz;
            float x0 = center.x - hx, z0 = center.z - hz;
            Vector3 CellPos(int i) => new Vector3(x0 + (i % nx) * Cell, floorTop, z0 + (i / nx) * Cell);

            // ── ① 밟힘(실제 콜라이더) ──
            //    칸마다 위에서부터 표면을 차례로 훑어 「설 수 있는 면」(위로 몸이 들어갈 트임)을 찾고, 기준 높이에 가장 가까운 면을 그 칸의 바닥으로 삼는다.
            //    지붕 있는 방(궁륭)·층이 섞인 곳(경사판·계단)도 칸마다 자기 표면 높이로 판정한다(09-27 베이스캠프 — 한 기준 높이로 나누면 지붕=벽·경사판=구멍으로 잘못 셌다).
            int solid = ~LayerMask.GetMask("Player", "Monster", "MonsterHit", "Ignore Raycast", "UI");
            int groundMask = 1 << groundLayer;
            var kind = new K[n];
            var topY = new float[n];
            var topName = new string[n];
            for (int i = 0; i < n; i++)
            {
                topY[i] = float.NaN;
                Vector3 o = CellPos(i) + Vector3.up * RayHeight;
                float remain = RayHeight * 2f;
                bool anyHit = false, found = false, bestGround = false;
                float bestY = 0f, bestDist = float.MaxValue, firstY = float.NaN;
                string bestName = null, firstName = null;
                for (int s = 0; s < MaxSurfaces && remain > 0f; s++)
                {
                    if (!Physics.Raycast(o, Vector3.down, out var h, remain, solid, QueryTriggerInteraction.Ignore)) break;
                    if (!anyHit) { anyHit = true; firstY = h.point.y; firstName = h.collider.name; }
                    if (h.normal.y >= 0.5f)
                    {
                        Vector3 p = h.point;
                        bool clear = !Physics.CheckCapsule(p + Vector3.up * (0.35f + BodyRadius), p + Vector3.up * (1.75f - BodyRadius),
                                                          BodyRadius, solid, QueryTriggerInteraction.Ignore);
                        float d = Mathf.Abs(p.y - floorTop);
                        if (clear && d < bestDist)
                        {
                            found = true; bestDist = d; bestY = p.y; bestName = h.collider.name;
                            bestGround = h.collider.gameObject.layer == groundLayer
                                         || Physics.Raycast(p + Vector3.up * 0.05f, Vector3.down, Step + 0.05f, groundMask, QueryTriggerInteraction.Ignore);
                        }
                    }
                    float next = h.point.y - 0.05f;   // 이 면 바로 아래부터 다시(안에서 시작한 광선은 그 콜라이더를 지나친다)
                    remain -= o.y - next;
                    o.y = next;
                }
                if (found)
                {
                    topY[i] = bestY; topName[i] = bestName;
                    float up = bestY - floorTop;
                    kind[i] = bestGround ? K.Stand : up > WallHeight ? K.Wall : up > Step ? K.Ledge : K.Air;
                }
                else if (anyHit) { kind[i] = K.Wall; topY[i] = firstY; topName[i] = firstName; }   // 면은 있는데 몸이 들어갈 트임이 없다(벽 속·낮은 천장)
                else kind[i] = K.Hole;
            }

            // ── ② 보임(렌더러 메시를 별도 물리 씬에) ──
            vis = SceneManager.CreateScene($"~TerrainVisual_{DateTime.Now:HHmmss}", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var vps = vis.GetPhysicsScene();
            int skipLayers = LayerMask.GetMask("Player", "Monster", "MonsterHit", "UI");
            int copied = 0, failed = 0;
            var combinedDone = new HashSet<Mesh>();
            foreach (var mr in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!mr.enabled || ((1 << mr.gameObject.layer) & skipLayers) != 0) continue;
                var b = mr.bounds;
                if (b.max.x < x0 || b.min.x > x0 + 2f * hx || b.max.z < z0 || b.min.z > z0 + 2f * hz) continue;
                if (!mr.TryGetComponent<MeshFilter>(out var mf) || mf.sharedMesh == null) continue;
                // 정적 배칭된 씬(베이스캠프)은 sharedMesh가 월드 좌표로 합친 메시다 — 한 번만, 원점에 둔다
                // (렌더러 자리에 두면 엉뚱한 곳에 놓여 바닥이 「안 보이는」 것으로 나왔다 — 09-27)
                bool batched = mr.isPartOfStaticBatch;
                if (batched && !combinedDone.Add(mf.sharedMesh)) continue;
                try
                {
                    var go = new GameObject(batched ? $"(정적 배칭) {mf.sharedMesh.name}" : mr.name);
                    SceneManager.MoveGameObjectToScene(go, vis);
                    if (!batched)
                    {
                        go.transform.SetPositionAndRotation(mr.transform.position, mr.transform.rotation);
                        go.transform.localScale = mr.transform.lossyScale;
                    }
                    go.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                    copied++;
                }
                catch (Exception) { failed++; }
            }
            await UniTask.Yield(PlayerLoopTiming.FixedUpdate, ct);

            var visGround = new bool[n];
            var visAny = new bool[n];
            var visName = new string[n];
            var buf = new RaycastHit[64];
            for (int i = 0; i < n; i++)
            {
                Vector3 o = CellPos(i) + Vector3.up * RayHeight;
                int hn = vps.Raycast(o, Vector3.down, buf, RayHeight * 2f, ~0, QueryTriggerInteraction.Ignore);
                visAny[i] = hn > 0;
                for (int k = 0; k < hn; k++)
                {
                    float refY = float.IsNaN(topY[i]) ? floorTop : topY[i];   // 칸 자기 표면 높이 기준(경사판·층)
                    if (buf[k].normal.y < 0.3f || Mathf.Abs(buf[k].point.y - refY) > Band) continue;
                    visGround[i] = true;
                    visName[i] = buf[k].collider.name;
                    break;
                }
                if (!visGround[i] && hn > 0) visName[i] = buf[0].collider.name;
            }

            // ── ③ NavMesh ──
            var navY = new float[n];
            var navComp = new int[n];
            for (int i = 0; i < n; i++) { navY[i] = float.NaN; navComp[i] = -1; }
            var tri = NavMesh.CalculateTriangulation();
            var uf = WeldComponents(tri);
            for (int t = 0; t + 2 < tri.indices.Length; t += 3)
            {
                Vector3 a = tri.vertices[tri.indices[t]], b = tri.vertices[tri.indices[t + 1]], c = tri.vertices[tri.indices[t + 2]];
                int comp = uf[tri.indices[t]];
                int ix0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, b.x, c.x) - x0) / Cell));
                int ix1 = Mathf.Min(nx - 1, Mathf.CeilToInt((Mathf.Max(a.x, b.x, c.x) - x0) / Cell));
                int iz0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.z, b.z, c.z) - z0) / Cell));
                int iz1 = Mathf.Min(nz - 1, Mathf.CeilToInt((Mathf.Max(a.z, b.z, c.z) - z0) / Cell));
                for (int iz = iz0; iz <= iz1; iz++)
                    for (int ix = ix0; ix <= ix1; ix++)
                    {
                        if (!InTriangleXZ(x0 + ix * Cell, z0 + iz * Cell, a, b, c, out float y)) continue;
                        int i = iz * nx + ix;
                        if (float.IsNaN(navY[i]) || y > navY[i]) { navY[i] = y; navComp[i] = comp; }
                    }
            }
            var mainComps = new HashSet<int>();
            int rc = Mathf.CeilToInt(MainNavRadius / Cell);
            int cix = Mathf.RoundToInt((center.x - x0) / Cell), ciz = Mathf.RoundToInt((center.z - z0) / Cell);
            for (int dz = -rc; dz <= rc; dz++)
                for (int dx = -rc; dx <= rc; dx++)
                {
                    int ix = cix + dx, iz = ciz + dz;
                    if (ix < 0 || iz < 0 || ix >= nx || iz >= nz || dx * dx + dz * dz > rc * rc) continue;
                    int i = iz * nx + ix;
                    if (navComp[i] >= 0 && Mathf.Abs(navY[i] - floorTop) <= 1f) mainComps.Add(navComp[i]);
                }

            // ── ④ 도달(플레이어 자리에서) ──
            var blocked = new bool[n];
            for (int i = 0; i < n; i++)
            {
                if (kind[i] != K.Wall && kind[i] != K.Ledge) continue;
                int ix = i % nx, iz = i / nx;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int ax = ix + dx, az = iz + dz;
                        if (ax >= 0 && az >= 0 && ax < nx && az < nz) blocked[az * nx + ax] = true;
                    }
            }
            bool Passable(int i) => (kind[i] == K.Stand || kind[i] == K.Air) && !blocked[i];
            int start = NearestPassable(player.transform.position, x0, z0, nx, nz, Passable);
            var reach = new bool[n];
            if (start >= 0)
            {
                var q = new Queue<int>();
                reach[start] = true; q.Enqueue(start);
                while (q.Count > 0)
                {
                    int i = q.Dequeue(), ix = i % nx, iz = i / nx;
                    foreach (int j in Neighbors4(ix, iz, nx, nz))
                    {
                        if (reach[j] || !Passable(j) || topY[j] - topY[i] > Step) continue;   // 내려가기는 제한 없음(걸어서 떨어진다) · 올라가기만 턱 높이까지
                        reach[j] = true; q.Enqueue(j);
                    }
                }
            }
            // 도달 영역에서의 칸 거리(8방향, SeenCells까지)
            var dist = new int[n];
            for (int i = 0; i < n; i++) dist[i] = reach[i] ? 0 : int.MaxValue;
            {
                var q = new Queue<int>();
                for (int i = 0; i < n; i++) if (reach[i]) q.Enqueue(i);
                while (q.Count > 0)
                {
                    int i = q.Dequeue();
                    if (dist[i] >= SeenCells) continue;
                    foreach (int j in Neighbors8(i % nx, i / nx, nx, nz))
                        if (dist[j] > dist[i] + 1) { dist[j] = dist[i] + 1; q.Enqueue(j); }
                }
            }

            // ── ⑤ 결함 ──
            var fall = new bool[n];        // 도달 칸 바로 옆 구멍 — 걸어가면 떨어진다
            var airReach = new bool[n];    // 설 수 있지만 발밑에 Ground가 없다 — 올라서면 공중 상태
            var hollow = new bool[n];      // 바닥이 보이는데 콜라이더가 없다(도달 영역 근처)
            var invisible = new bool[n];   // 밟히는데 보이는 바닥이 없다(도달 영역)
            var voidSeen = new bool[n];    // 아무것도 안 보인다(배경이 보인다) — 도달 영역 4 m 안
            var navIsland = new bool[n];   // 본 영역과 안 이어진 NavMesh 조각
            for (int i = 0; i < n; i++)
            {
                if (kind[i] == K.Hole && dist[i] == 1) fall[i] = true;
                if (reach[i])   // 도달 칸 옆이 1 m 넘게 낮다 = 걸어가면 떨어진다(층이 섞인 곳의 낭떠러지)
                    foreach (int j in Neighbors8(i % nx, i / nx, nx, nz))
                        if (!reach[j] && (kind[j] == K.Stand || kind[j] == K.Air) && topY[j] < topY[i] - DropDepth) fall[j] = true;
                if (reach[i] && kind[i] == K.Air) airReach[i] = true;
                if (kind[i] == K.Hole && visGround[i] && dist[i] <= 2) hollow[i] = true;
                if (reach[i] && !visGround[i]) invisible[i] = true;
                if (!visAny[i] && dist[i] <= SeenCells) voidSeen[i] = true;
                if (navComp[i] >= 0 && !mainComps.Contains(navComp[i])) navIsland[i] = true;
            }

            // ── 보고 ──
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "arena_terrain", $"{Clean(room.name)}_{DateTime.Now:MMdd_HHmmss}");
            Directory.CreateDirectory(dir);
            var log = new StringBuilder();
            int reachCount = reach.Count(r => r);
            log.AppendLine($"방 {room.name} · 기준 바닥 '{mainCol.name}' 외 같은 높이 Ground {floors.Count(c => Mathf.Abs(c.bounds.max.y - floorTop) <= 1f)}개 합 {main.size.x:0.0}x{main.size.z:0.0} m · 윗면 y {floorTop:0.00} · 격자 {Cell} m {nx}x{nz}");
            log.AppendLine($"보이는 메시 복제 {copied}개(실패 {failed}) · NavMesh 삼각형 {tri.indices.Length / 3} · 본 영역 조각 {mainComps.Count}개");
            log.AppendLine($"도달 영역 {reachCount * Cell * Cell:0} m² · 범위 {ReachBox(reach, nx, x0, z0, center)} (시작 {(start >= 0 ? Local(CellPos(start), center) : "없음")})");
            log.AppendLine($"칸 분류: 설 수 있음 {Count(kind, K.Stand)} · 공중면 {Count(kind, K.Air)} · 턱 {Count(kind, K.Ledge)} · 벽 {Count(kind, K.Wall)} · 구멍 {Count(kind, K.Hole)}");
            log.AppendLine("좌표 = 아레나 중심 기준 (x 동+, z 북+) m");
            log.AppendLine();
            int total = 0;
            total += Report(log, "① 떨어지는 가장자리 — 도달 영역 바로 옆이 구멍", fall, nx, x0, z0, center, i => visName[i] ?? "(보이는 것 없음)");
            total += Report(log, "② 공중면 — 올라설 수 있는데 발밑에 Ground가 없다(공중 상태로 조작 불가)", airReach, nx, x0, z0, center, i => topName[i]);
            total += Report(log, "③ 빈 바닥 — 바닥이 보이는데 콜라이더가 없다(도달 영역 0.5 m 안)", hollow, nx, x0, z0, center, i => visName[i]);
            total += Report(log, "④ 안 보이는 바닥 — 도달 영역인데 보이는 바닥이 없다", invisible, nx, x0, z0, center, i => topName[i]);
            total += Report(log, "⑤ 배경이 보이는 빈 곳 — 도달 영역 4 m 안에 아무 메시도 없다", voidSeen, nx, x0, z0, center, i => null);
            total += Report(log, "⑥ NavMesh 섬 — 본 영역과 안 이어진 조각(벽·장식 위에 구워짐)", navIsland, nx, x0, z0, center,
                            i => $"{topName[i]} (높이 {navY[i] - floorTop:+0.0;-0.0})");
            log.AppendLine($"결함 덩어리 합계 {total}");

            File.WriteAllText(Path.Combine(dir, "report.txt"), log.ToString(), new UTF8Encoding(true));

            // ── 그림 ──
            var terrainPx = TerrainColors(kind, visGround, visAny, reach, navComp, mainComps, fall, nx, nz);
            SavePng(Path.Combine(dir, "terrain.png"), terrainPx, nx * Px, nz * Px);
            var topPx = RenderTop(center, floorTop, nx, nz);
            if (topPx != null)
            {
                SavePng(Path.Combine(dir, "top.png"), topPx, nx * Px, nz * Px);
                var over = new Color32[topPx.Length];
                for (int p = 0; p < over.Length; p++) over[p] = Color32.Lerp(topPx[p], terrainPx[p], 0.45f);
                SavePng(Path.Combine(dir, "overlay.png"), over, nx * Px, nz * Px);
            }
            float rx = main.extents.x, rz = main.extents.z;
            var corners = new (string name, Vector3 off)[]
            {
                ("SW", new Vector3(-rx - 6f, 0f, -rz - 6f)), ("SE", new Vector3(rx + 6f, 0f, -rz - 6f)),
                ("NE", new Vector3(rx + 6f, 0f, rz + 6f)),   ("NW", new Vector3(-rx - 6f, 0f, rz + 6f)),
            };
            foreach (var cn in corners)
                RenderView(Path.Combine(dir, $"view_{cn.name}.png"), center + cn.off + Vector3.up * 22f, center);
            await CaptureGameAsync(Path.Combine(dir, "game.png"), ct);

            Debug.Log($"[TerrainProbe] 완료 — 결함 덩어리 {total} — {dir}");
        }
        catch (OperationCanceledException) { Debug.Log("[TerrainProbe] 취소"); }
        finally
        {
            if (player != null) invField?.SetValue(player, savedInv);
            if (vis.IsValid()) SceneManager.UnloadSceneAsync(vis);
        }
    }

    /// <summary>바닥 사각형 = 가장 큰 Ground 콜라이더와 같은 높이(±1 m)인 Ground 콜라이더들의 합 — 5 m 타일로 깐 바닥(리치 아레나)도 한 판으로 본다.</summary>
    internal static Bounds FloorBounds(List<Collider> floors, Collider mainCol)
    {
        float top = mainCol.bounds.max.y;
        Bounds b = mainCol.bounds;
        foreach (var c in floors)
            if (Mathf.Abs(c.bounds.max.y - top) <= 1f) b.Encapsulate(c.bounds);
        return b;
    }

    // NavMesh 삼각형 꼭짓점을 5 cm로 합쳐 이어진 조각 번호를 매긴다(타일 경계 꼭짓점이 겹친다)
    private static int[] WeldComponents(NavMeshTriangulation tri)
    {
        var key = new Dictionary<(int, int, int), int>();
        var id = new int[tri.vertices.Length];
        for (int v = 0; v < tri.vertices.Length; v++)
        {
            var p = tri.vertices[v];
            var k = (Mathf.RoundToInt(p.x * 20f), Mathf.RoundToInt(p.y * 20f), Mathf.RoundToInt(p.z * 20f));
            if (!key.TryGetValue(k, out int w)) { w = key.Count; key[k] = w; }
            id[v] = w;
        }
        var parent = Enumerable.Range(0, key.Count).ToArray();
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        for (int t = 0; t + 2 < tri.indices.Length; t += 3)
        {
            int a = Find(id[tri.indices[t]]), b = Find(id[tri.indices[t + 1]]), c = Find(id[tri.indices[t + 2]]);
            parent[b] = a; parent[Find(c)] = a;
        }
        var comp = new int[tri.vertices.Length];
        for (int v = 0; v < comp.Length; v++) comp[v] = Find(id[v]);
        return comp;
    }

    private static bool InTriangleXZ(float px, float pz, Vector3 a, Vector3 b, Vector3 c, out float y)
    {
        y = 0f;
        float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
        if (Mathf.Abs(d) < 1e-6f) return false;
        float l1 = ((b.z - c.z) * (px - c.x) + (c.x - b.x) * (pz - c.z)) / d;
        float l2 = ((c.z - a.z) * (px - c.x) + (a.x - c.x) * (pz - c.z)) / d;
        float l3 = 1f - l1 - l2;
        if (l1 < -1e-4f || l2 < -1e-4f || l3 < -1e-4f) return false;
        y = l1 * a.y + l2 * b.y + l3 * c.y;
        return true;
    }

    private static int NearestPassable(Vector3 p, float x0, float z0, int nx, int nz, Func<int, bool> passable)
    {
        int cx = Mathf.RoundToInt((p.x - x0) / Cell), cz = Mathf.RoundToInt((p.z - z0) / Cell);
        for (int r = 0; r <= 12; r++)
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int ix = cx + dx, iz = cz + dz;
                    if (ix < 0 || iz < 0 || ix >= nx || iz >= nz) continue;
                    if (passable(iz * nx + ix)) return iz * nx + ix;
                }
        return -1;
    }

    private static IEnumerable<int> Neighbors4(int ix, int iz, int nx, int nz)
    {
        if (ix > 0) yield return iz * nx + ix - 1;
        if (ix < nx - 1) yield return iz * nx + ix + 1;
        if (iz > 0) yield return (iz - 1) * nx + ix;
        if (iz < nz - 1) yield return (iz + 1) * nx + ix;
    }

    private static IEnumerable<int> Neighbors8(int ix, int iz, int nx, int nz)
    {
        for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dz == 0) continue;
                int ax = ix + dx, az = iz + dz;
                if (ax >= 0 && az >= 0 && ax < nx && az < nz) yield return az * nx + ax;
            }
    }

    // 결함 칸을 8방향 덩어리로 묶어 큰 것부터 적는다. 돌려주는 값 = 덩어리 수.
    private static int Report(StringBuilder log, string title, bool[] mask, int nx, float x0, float z0, Vector3 center, Func<int, string> nameOf)
    {
        int n = mask.Length, nz = n / nx;
        var seen = new bool[n];
        var clusters = new List<List<int>>();
        for (int i = 0; i < n; i++)
        {
            if (!mask[i] || seen[i]) continue;
            var cl = new List<int>();
            var q = new Queue<int>();
            seen[i] = true; q.Enqueue(i);
            while (q.Count > 0)
            {
                int c = q.Dequeue(); cl.Add(c);
                foreach (int j in Neighbors8(c % nx, c / nx, nx, nz))
                    if (mask[j] && !seen[j]) { seen[j] = true; q.Enqueue(j); }
            }
            clusters.Add(cl);
        }
        float cellArea = Cell * Cell;
        log.AppendLine($"── {title}: 덩어리 {clusters.Count}개 · 합 {clusters.Sum(c => c.Count) * cellArea:0.0} m²");
        foreach (var cl in clusters.OrderByDescending(c => c.Count).Take(MaxClusters))
        {
            float minX = cl.Min(i => i % nx), maxX = cl.Max(i => i % nx), minZ = cl.Min(i => i / nx), maxZ = cl.Max(i => i / nx);
            string names = string.Join(", ", cl.Select(nameOf).Where(s => !string.IsNullOrEmpty(s))
                                                .GroupBy(s => s).OrderByDescending(g => g.Count()).Take(3)
                                                .Select(g => $"{g.Key}×{g.Count()}"));
            log.AppendLine($"   {cl.Count * cellArea,6:0.00} m²  x {x0 + minX * Cell - center.x:0.0}~{x0 + maxX * Cell - center.x:0.0} · " +
                           $"z {z0 + minZ * Cell - center.z:0.0}~{z0 + maxZ * Cell - center.z:0.0}" + (names.Length > 0 ? $"  [{names}]" : ""));
        }
        if (clusters.Count > MaxClusters) log.AppendLine($"   … 외 {clusters.Count - MaxClusters}개");
        log.AppendLine();
        return clusters.Count;
    }

    private static Color32[] TerrainColors(K[] kind, bool[] visGround, bool[] visAny, bool[] reach, int[] navComp, HashSet<int> mainComps,
                                           bool[] fall, int nx, int nz)
    {
        int w = nx * Px, h = nz * Px;
        var px = new Color32[w * h];
        for (int i = 0; i < kind.Length; i++)
        {
            Color32 c = kind[i] switch
            {
                K.Stand => visGround[i] ? new Color32(70, 120, 70, 255) : new Color32(230, 0, 230, 255),   // 녹 = 정상 · 자홍 = 안 보이는 바닥
                K.Air   => new Color32(255, 140, 0, 255),                                                  // 주황 = 공중면
                K.Ledge => new Color32(165, 165, 165, 255),                                                // 연회 = 턱
                K.Wall  => new Color32(95, 95, 95, 255),                                                   // 회 = 벽
                _       => visGround[i] ? new Color32(255, 30, 30, 255)                                    // 빨강 = 보이는데 빈 바닥
                         : visAny[i]    ? new Color32(40, 40, 70, 255) : new Color32(0, 0, 0, 255),        // 남 = 아래 없음(위에 뭔가) · 검정 = 배경
            };
            if (navComp[i] >= 0)
                c = Color32.Lerp(c, mainComps.Contains(navComp[i]) ? new Color32(0, 220, 255, 255) : new Color32(255, 230, 0, 255),
                                 mainComps.Contains(navComp[i]) ? 0.35f : 0.75f);                          // 하늘 = NavMesh · 노랑 = NavMesh 섬
            if (reach[i]) c = Color32.Lerp(c, new Color32(255, 255, 255, 255), 0.18f);
            if (fall[i]) c = new Color32(255, 0, 0, 255);
            int ix = i % nx, iz = i / nx;
            for (int y = 0; y < Px; y++)
                for (int x = 0; x < Px; x++)
                    px[(iz * Px + y) * w + ix * Px + x] = c;
        }
        // 도달 경계 흰 선
        for (int i = 0; i < kind.Length; i++)
        {
            if (!reach[i]) continue;
            int ix = i % nx, iz = i / nx;
            bool edge = Neighbors4(ix, iz, nx, nz).Any(j => !reach[j]);
            if (!edge) continue;
            for (int y = 0; y < Px; y++)
                for (int x = 0; x < Px; x++)
                    if (x == 0 || y == 0 || x == Px - 1 || y == Px - 1) px[(iz * Px + y) * w + ix * Px + x] = new Color32(255, 255, 255, 255);
        }
        return px;
    }

    // 위에서 직교로 — 격자와 같은 범위·픽셀(위 = 북)
    private static Color32[] RenderTop(Vector3 center, float floorTop, int nx, int nz)
    {
        int w = nx * Px, h = nz * Px;
        var go = new GameObject("~TerrainTopCam") { hideFlags = HideFlags.HideAndDontSave };
        var cam = go.AddComponent<Camera>();
        var rt = new RenderTexture(w, h, 24);
        bool fog = RenderSettings.fog;
        try
        {
            go.transform.SetPositionAndRotation(new Vector3(center.x, floorTop + 80f, center.z), Quaternion.Euler(90f, 0f, 0f));
            cam.orthographic = true;
            cam.orthographicSize = nz * Cell * 0.5f;
            cam.aspect = (float)nx / nz;
            cam.nearClipPlane = 1f;
            cam.farClipPlane = 200f;
            cam.cullingMask = ~LayerMask.GetMask("UI");
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.02f, 0.03f, 1f);
            cam.targetTexture = rt;
            RenderSettings.fog = false;
            cam.Render();
            return ReadRt(rt, w, h);
        }
        finally
        {
            RenderSettings.fog = fog;
            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    private static void RenderView(string path, Vector3 from, Vector3 at)
    {
        const int w = 1280, h = 720;
        var go = new GameObject("~TerrainViewCam") { hideFlags = HideFlags.HideAndDontSave };
        var cam = go.AddComponent<Camera>();
        var rt = new RenderTexture(w, h, 24);
        bool fog = RenderSettings.fog;
        try
        {
            go.transform.position = from;
            go.transform.LookAt(at);
            cam.fieldOfView = 55f;
            cam.farClipPlane = 300f;
            cam.cullingMask = ~LayerMask.GetMask("UI");
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.02f, 0.03f, 1f);
            cam.targetTexture = rt;
            RenderSettings.fog = false;
            cam.Render();
            SavePng(path, ReadRt(rt, w, h), w, h);
        }
        finally
        {
            RenderSettings.fog = fog;
            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    private static Color32[] ReadRt(RenderTexture rt, int w, int h)
    {
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        var px = tex.GetPixels32();
        UnityEngine.Object.DestroyImmediate(tex);
        return px;
    }

    private static void SavePng(string path, Color32[] px, int w, int h)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.SetPixels32(px);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
    }

    private static async UniTask CaptureGameAsync(string path, CancellationToken ct)
    {
        var runner = UnityEngine.Object.FindFirstObjectByType<Managers>();
        if (runner == null) return;
        await UniTask.WaitForEndOfFrame(runner, ct);
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.Destroy(tex);
    }

    private static int Count(K[] kind, K k) => kind.Count(x => x == k);

    private static string Local(Vector3 p, Vector3 center) => $"({p.x - center.x:0.0}, {p.z - center.z:0.0})";

    private static string ReachBox(bool[] reach, int nx, float x0, float z0, Vector3 center)
    {
        var idx = Enumerable.Range(0, reach.Length).Where(i => reach[i]).ToList();
        if (idx.Count == 0) return "없음";
        return $"x {x0 + idx.Min(i => i % nx) * Cell - center.x:0.0}~{x0 + idx.Max(i => i % nx) * Cell - center.x:0.0} · " +
               $"z {z0 + idx.Min(i => i / nx) * Cell - center.z:0.0}~{z0 + idx.Max(i => i / nx) * Cell - center.z:0.0}";
    }

    private static string Clean(string s)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Replace(' ', '_');
    }
}
