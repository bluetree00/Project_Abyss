using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 방의 「실제로 막힌 경계」를 물리로 잰다 — 진입 지점에서 걸어서 닿는 영역을 칸 단위로 번져 나가며 구하고,
/// 그 바깥 테두리를 선으로 돌려준다(10-01 사용자: 「방이 오브젝트로 막히는데 실제 막힌 경계도 이펙트로 표기」).
///
/// 벽 · 장식(나무·바위) · 낭떠러지 어느 것으로 막혔든 기준은 하나다 — 「밟을 바닥이 있고 플레이어 몸이 들어가는가」.
/// 그래서 격자 방과 손맵 아레나를 가리지 않는다. 방 안쪽 장애물(섬)은 그리지 않는다 — 바깥 테두리 하나만.
/// 잴 범위(<c>limit</c>) 끝에서 끊긴 곳은 막힌 게 아니라 <b>열린 곳</b>(문 · 통로)이라 선을 비운다.
///
/// 방을 지을 때 한 번 부른다(칸당 광선 1 + 캡슐 1 — 30 m 방에서 수천 회, 전환 화면 뒤). 매 프레임 부르지 말 것.
/// </summary>
public static class RoomBoundaryTracer
{
    // ── Constants ──────────────────────────────────────────────
    private const float Cell        = 0.5f;    // 표본 칸 — 격자 방(1 m 칸)을 넷으로 나눈다
    private const float BodyRadius  = 0.27f;   // 플레이어 캡슐 반지름
    private const float BodyLow     = 0.4f;    // 이보다 낮은 턱은 몸 판정에서 뺀다(턱은 높이차로 거른다)
    private const float BodyHigh    = 1.5f;
    private const float StepMax     = 0.4f;    // 옆 칸과 이보다 높이차가 나면 못 넘는다
    private const float RayUp       = 1.2f;
    private const float RayDown     = 1.2f;
    private const int   MaxSide     = 320;     // 한 변 최대 칸 수(160 m)
    private const int   RefineSteps = 3;       // 닿는 칸 ↔ 막힌 칸 사이를 이분해 경계를 6 cm 안으로 좁힌다
    private const int   SmoothPasses = 2;
    private const float Outset      = 0.12f;   // 발이 멈추는 선보다 살짝 바깥(몸 가장자리 쪽)에 그린다
    private const float SimplifyEps = 0.04f;
    private const float MinLength   = 2f;      // 이보다 짧은 토막(문설주 사이 등)은 버린다

    // 방향: 0=남(−z) 1=동(+x) 2=북(+z) 3=서(−x). 테두리를 영역을 왼쪽에 두고 돌면 남→동→북→서 = 왼쪽으로 꺾기.
    private static readonly int[] Dx = { 0, 1, 0, -1 };
    private static readonly int[] Dz = { -1, 0, 1, 0 };

    public struct Line
    {
        public Vector3[] points;   // 월드 좌표
        public bool loop;
        public float length;
    }

    private struct Edge
    {
        public int from, to;       // 칸 모서리 격자 인덱스
        public int dir;
        public bool open;          // 범위 끝 — 막힌 게 아니라 열린 곳
        public Vector3 point;      // 경계 위 한 점(월드)
    }

    // ── Public Methods ─────────────────────────────────────────

    /// <summary>
    /// <paramref name="entry"/>에서 걸어서 닿는 영역의 바깥 테두리 — 막힌 구간마다 선 하나(열린 곳이 없으면 닫힌 고리 하나).
    /// <paramref name="limit"/>는 XZ 범위만 쓴다. 잴 수 없으면(진입 지점에 바닥 없음) 빈 목록.
    /// </summary>
    public static List<Line> Trace(Vector3 entry, Bounds limit)
    {
        var lines = new List<Line>();
        int mask = ~LayerMask.GetMask("Player", "Monster", "MonsterHit", "Ignore Raycast", "UI", "TransparentFX", "Water");
        Physics.SyncTransforms();   // 방금 놓은 블록·장식이 질의에 잡히게

        int nx = Mathf.Clamp(Mathf.CeilToInt(limit.size.x / Cell), 1, MaxSide);
        int nz = Mathf.Clamp(Mathf.CeilToInt(limit.size.z / Cell), 1, MaxSide);
        float x0 = limit.min.x, z0 = limit.min.z;

        // ── ① 번지기 ──
        var reached = new bool[nx * nz];
        var floorY = new float[nx * nz];
        int seed = FindSeed(entry, x0, z0, nx, nz, mask, out float seedY);
        if (seed < 0)
        {
            Debug.LogWarning($"[RoomBoundaryTracer] 진입 지점 {entry}에서 설 자리를 못 찾았다 — 경계 표시 생략");
            return lines;
        }
        var queue = new Queue<int>();
        reached[seed] = true; floorY[seed] = seedY; queue.Enqueue(seed);
        while (queue.Count > 0)
        {
            int c = queue.Dequeue();
            int cx = c % nx, cz = c / nx;
            for (int d = 0; d < 4; d++)
            {
                int ax = cx + Dx[d], az = cz + Dz[d];
                if (ax < 0 || az < 0 || ax >= nx || az >= nz) continue;
                int a = az * nx + ax;
                if (reached[a]) continue;
                if (!Standable(CellCenter(ax, x0), CellCenter(az, z0), floorY[c], StepMax, mask, out float y)) continue;
                reached[a] = true; floorY[a] = y; queue.Enqueue(a);
            }
        }

        // ── ② 테두리 변 — 닿는 칸의 네 변 중 이웃이 안 닿는 변. 영역을 왼쪽에 두는 방향으로 잇는다 ──
        int cw = nx + 1;
        var edges = new List<Edge>();
        var out0 = new int[cw * (nz + 1)];
        var out1 = new int[out0.Length];
        for (int i = 0; i < out0.Length; i++) { out0[i] = -1; out1[i] = -1; }
        for (int cz = 0; cz < nz; cz++)
        {
            for (int cx = 0; cx < nx; cx++)
            {
                int c = cz * nx + cx;
                if (!reached[c]) continue;
                for (int d = 0; d < 4; d++)
                {
                    int ax = cx + Dx[d], az = cz + Dz[d];
                    bool outside = ax < 0 || az < 0 || ax >= nx || az >= nz;
                    if (!outside && reached[az * nx + ax]) continue;

                    var e = new Edge { dir = d, open = outside };
                    switch (d)
                    {
                        case 0:  e.from = cz * cw + cx;           e.to = cz * cw + cx + 1;         break;
                        case 1:  e.from = cz * cw + cx + 1;       e.to = (cz + 1) * cw + cx + 1;   break;
                        case 2:  e.from = (cz + 1) * cw + cx + 1; e.to = (cz + 1) * cw + cx;       break;
                        default: e.from = (cz + 1) * cw + cx;     e.to = cz * cw + cx;             break;
                    }
                    e.point = BoundaryPoint(CellCenter(cx, x0), CellCenter(cz, z0), floorY[c], d, outside, mask);
                    if (out0[e.from] < 0) out0[e.from] = edges.Count; else out1[e.from] = edges.Count;
                    edges.Add(e);
                }
            }
        }

        // ── ③ 고리로 잇고 가장 넓은 것(= 바깥 테두리)을 고른다. 안쪽 장애물 고리는 넓이가 음수다 ──
        var used = new bool[edges.Count];
        List<int> outer = null;
        float outerArea = 0f;
        for (int s = 0; s < edges.Count; s++)
        {
            if (used[s]) continue;
            var ring = new List<int>();
            float area = 0f;
            int cur = s;
            while (cur >= 0 && !used[cur])
            {
                used[cur] = true;
                ring.Add(cur);
                var e = edges[cur];
                area += (e.from % cw) * (float)(e.to / cw) - (e.to % cw) * (float)(e.from / cw);
                int n0 = out0[e.to], n1 = out1[e.to];
                // 모서리에서 나가는 변이 둘이면(대각으로만 맞닿은 칸) 왼쪽으로 꺾어 같은 덩어리를 돈다
                cur = n1 < 0 || edges[n0].dir == (e.dir + 1) % 4 ? n0 : n1;
            }
            if (area > outerArea) { outerArea = area; outer = ring; }
        }
        if (outer == null) return lines;

        // ── ④ 열린 변에서 끊어 막힌 구간마다 선 하나 ──
        int start = -1;
        for (int i = 0; i < outer.Count; i++)
            if (edges[outer[i]].open) { start = i; break; }
        if (start < 0)
        {
            var pts = new List<Vector3>(outer.Count);
            for (int i = 0; i < outer.Count; i++) pts.Add(edges[outer[i]].point);
            AddLine(lines, pts, true);
            return lines;
        }
        var run = new List<Vector3>();
        for (int i = 1; i <= outer.Count; i++)
        {
            var e = edges[outer[(start + i) % outer.Count]];
            if (!e.open) { run.Add(e.point); continue; }
            if (run.Count > 0) { AddLine(lines, run, false); run = new List<Vector3>(); }
        }
        return lines;
    }

    // ── Private Methods ────────────────────────────────────────

    private static float CellCenter(int i, float origin) => origin + (i + 0.5f) * Cell;

    /// <summary>진입 지점 칸 — 막혀 있으면(마커가 장식 옆 등) 가까운 칸부터 두 칸 둘레까지 찾는다.</summary>
    private static int FindSeed(Vector3 entry, float x0, float z0, int nx, int nz, int mask, out float y)
    {
        int sx = Mathf.FloorToInt((entry.x - x0) / Cell), sz = Mathf.FloorToInt((entry.z - z0) / Cell);
        for (int r = 0; r <= 2; r++)
        {
            for (int dz = -r; dz <= r; dz++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r) continue;
                    int cx = sx + dx, cz = sz + dz;
                    if (cx < 0 || cz < 0 || cx >= nx || cz >= nz) continue;
                    if (Standable(CellCenter(cx, x0), CellCenter(cz, z0), entry.y, RayDown, mask, out y)) return cz * nx + cx;
                }
            }
        }
        y = entry.y;
        return -1;
    }

    /// <summary>그 자리에 설 수 있는가 — 기준 높이 근처에 위를 보는 면이 있고, 그 위로 플레이어 몸이 들어간다.</summary>
    private static bool Standable(float x, float z, float refY, float stepMax, int mask, out float y)
    {
        y = refY;
        if (!Physics.Raycast(new Vector3(x, refY + RayUp, z), Vector3.down, out var hit, RayUp + RayDown, mask, QueryTriggerInteraction.Ignore))
            return false;
        if (hit.normal.y < 0.5f || Mathf.Abs(hit.point.y - refY) > stepMax) return false;
        y = hit.point.y;
        return !Physics.CheckCapsule(new Vector3(x, y + BodyLow + BodyRadius, z), new Vector3(x, y + BodyHigh - BodyRadius, z),
                                     BodyRadius, mask, QueryTriggerInteraction.Ignore);
    }

    /// <summary>닿는 칸 중심에서 <paramref name="dir"/> 쪽 이웃 칸 중심 사이, 설 수 있는 마지막 자리(이분).</summary>
    private static Vector3 BoundaryPoint(float x, float z, float y, int dir, bool open, int mask)
    {
        float lo = 0f, hi = 1f;
        if (open) lo = hi = 0.5f;   // 범위 끝 — 칸 변 그대로
        else
        {
            for (int i = 0; i < RefineSteps; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (Standable(x + Dx[dir] * Cell * mid, z + Dz[dir] * Cell * mid, y, StepMax, mask, out _)) lo = mid;
                else hi = mid;
            }
        }
        float t = (lo + hi) * 0.5f * Cell;
        return new Vector3(x + Dx[dir] * t, y, z + Dz[dir] * t);
    }

    /// <summary>경계 점열을 다듬어(고르기 → 바깥으로 살짝 → 줄이기) 선으로 담는다.</summary>
    private static void AddLine(List<Line> lines, List<Vector3> pts, bool loop)
    {
        int n = pts.Count;
        if (n < 3) return;

        var tmp = new Vector3[n];
        for (int pass = 0; pass < SmoothPasses; pass++)
        {
            for (int i = 0; i < n; i++)
            {
                if (!loop && (i == 0 || i == n - 1)) { tmp[i] = pts[i]; continue; }
                tmp[i] = pts[(i + n - 1) % n] * 0.25f + pts[i] * 0.5f + pts[(i + 1) % n] * 0.25f;
            }
            for (int i = 0; i < n; i++) pts[i] = tmp[i];
        }

        // 진행 방향의 오른쪽이 바깥(영역을 왼쪽에 두고 돈다)
        for (int i = 0; i < n; i++)
        {
            Vector3 a = pts[loop ? (i + n - 1) % n : Mathf.Max(i - 1, 0)];
            Vector3 b = pts[loop ? (i + 1) % n : Mathf.Min(i + 1, n - 1)];
            var outward = new Vector3(b.z - a.z, 0f, -(b.x - a.x));
            float m = outward.magnitude;
            tmp[i] = m > 1e-4f ? pts[i] + outward * (Outset / m) : pts[i];
        }

        var keep = new bool[n];
        keep[0] = keep[n - 1] = true;
        Simplify(tmp, keep, 0, n - 1);
        var result = new List<Vector3>(n);
        for (int i = 0; i < n; i++) if (keep[i]) result.Add(tmp[i]);

        float len = 0f;
        for (int i = 1; i < result.Count; i++) len += Vector3.Distance(result[i - 1], result[i]);
        if (loop) len += Vector3.Distance(result[result.Count - 1], result[0]);
        if (len < MinLength) return;
        lines.Add(new Line { points = result.ToArray(), loop = loop, length = len });
    }

    /// <summary>더글러스-포이커 — 곧은 구간의 촘촘한 점을 걷어 낸다.</summary>
    private static void Simplify(Vector3[] p, bool[] keep, int a, int b)
    {
        if (b <= a + 1) return;
        Vector3 ab = p[b] - p[a];
        float abLen = ab.magnitude;
        float worst = 0f;
        int at = -1;
        for (int i = a + 1; i < b; i++)
        {
            float d = abLen > 1e-4f ? Vector3.Cross(ab, p[i] - p[a]).magnitude / abLen : Vector3.Distance(p[i], p[a]);
            if (d > worst) { worst = d; at = i; }
        }
        if (worst <= SimplifyEps) return;
        keep[at] = true;
        Simplify(p, keep, a, at);
        Simplify(p, keep, at, b);
    }
}
