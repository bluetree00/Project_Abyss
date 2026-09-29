using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 칸(격자) 모양을 <b>바깥 외곽선 하나</b>로 두르는 그래픽 — 룬판처럼 계단진 판의 테두리(09-29 사용자 「얇고 깔끔한 룬판 테두리」).
/// <para>칸 집합에서 경계 변을 모아 닫힌 고리로 잇고, 직각 꼭짓점을 바깥으로 <c>pad</c>만큼 밀어 그린다(볼록 · 오목 모두
/// 꼭짓점 + pad·(들어오는 변 법선 + 나가는 변 법선)). 선 한 겹 + 은은한 빛 한 겹, 드로우 콜 하나.</para>
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class UIRectilinearOutline : MaskableGraphic
{
    // ── Private ──────────────────────────────────────────
    private readonly List<List<Vector2>> _loops = new();
    private float _lineWidth = 2f;
    private float _glowWidth = 10f;
    private float _glowAlpha = 0.14f;

    // ── Public Methods ───────────────────────────────────

    /// <summary>
    /// 칸 집합으로 외곽선을 만든다. <paramref name="cells"/>는 (열, 행) — 행은 아래로 늘어난다.
    /// <paramref name="toLocal"/>은 격자 점(열 경계, 행 경계)을 이 그래픽의 로컬 좌표로 바꾼다.
    /// </summary>
    public void Build(ICollection<Vector2Int> cells, System.Func<float, float, Vector2> toLocal,
                      float pad, float lineWidth, float glowWidth, float glowAlpha)
    {
        _lineWidth = lineWidth; _glowWidth = glowWidth; _glowAlpha = glowAlpha;
        _loops.Clear();
        if (cells == null || cells.Count == 0 || toLocal == null) { SetVerticesDirty(); return; }

        var set = cells as HashSet<Vector2Int> ?? new HashSet<Vector2Int>(cells);

        // 경계 변(시계 방향, 행은 아래로): 시작 점 → (끝 점, 바깥 법선)
        var next = new Dictionary<Vector2Int, (Vector2Int to, Vector2Int normal)>();
        foreach (var c in set)
        {
            if (!set.Contains(c + new Vector2Int(0, -1))) next[new Vector2Int(c.x, c.y)]         = (new Vector2Int(c.x + 1, c.y),     new Vector2Int(0, -1));
            if (!set.Contains(c + new Vector2Int(1, 0)))  next[new Vector2Int(c.x + 1, c.y)]     = (new Vector2Int(c.x + 1, c.y + 1), new Vector2Int(1, 0));
            if (!set.Contains(c + new Vector2Int(0, 1)))  next[new Vector2Int(c.x + 1, c.y + 1)] = (new Vector2Int(c.x, c.y + 1),     new Vector2Int(0, 1));
            if (!set.Contains(c + new Vector2Int(-1, 0))) next[new Vector2Int(c.x, c.y + 1)]     = (new Vector2Int(c.x, c.y),         new Vector2Int(-1, 0));
        }

        var used = new HashSet<Vector2Int>();
        foreach (var start in next.Keys)
        {
            if (used.Contains(start)) continue;
            // 한 고리를 따라가며 방향이 바뀌는 점만 꼭짓점으로 남긴다.
            var pts = new List<(Vector2Int p, Vector2Int nIn, Vector2Int nOut)>();
            var cur = start;
            Vector2Int prevNormal = default; bool first = true;
            int guard = next.Count + 4;
            while (guard-- > 0 && next.TryGetValue(cur, out var e) && used.Add(cur))
            {
                if (first || e.normal != prevNormal) pts.Add((cur, prevNormal, e.normal));
                prevNormal = e.normal; first = false;
                cur = e.to;
                if (cur == start) break;
            }
            if (pts.Count < 3) continue;
            // 첫 꼭짓점의 들어오는 법선 = 고리의 마지막 변 법선
            pts[0] = (pts[0].p, prevNormal, pts[0].nOut);
            if (pts[0].nIn == pts[0].nOut) pts.RemoveAt(0);   // 시작이 직선 위였다면 꼭짓점이 아니다

            var loop = new List<Vector2>(pts.Count);
            foreach (var (p, nIn, nOut) in pts)
            {
                var o = toLocal(p.x, p.y);
                // 격자 법선(행 아래 +) → 로컬(위 +)
                var n = new Vector2(nIn.x + nOut.x, -(nIn.y + nOut.y));
                loop.Add(o + n * pad);
            }
            _loops.Add(loop);
        }
        SetVerticesDirty();
    }

    // ── Private Methods ──────────────────────────────────

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        var c = (Color32)color;
        var glow = color; glow.a *= _glowAlpha;
        foreach (var loop in _loops)
        {
            if (_glowWidth > 0f && _glowAlpha > 0f) AddLoop(vh, loop, _glowWidth, glow);
            AddLoop(vh, loop, _lineWidth, c);
        }
    }

    /// <summary>닫힌 고리를 두께 w의 띠로 — 각 변을 반 두께만큼 양끝으로 늘려 직각 모서리를 메운다.</summary>
    private static void AddLoop(VertexHelper vh, List<Vector2> loop, float w, Color32 col)
    {
        float h = w * 0.5f;
        for (int i = 0; i < loop.Count; i++)
        {
            Vector2 a = loop[i], b = loop[(i + 1) % loop.Count];
            Vector2 d = (b - a);
            float len = d.magnitude;
            if (len < 0.01f) continue;
            d /= len;
            Vector2 nrm = new Vector2(-d.y, d.x) * h;
            Vector2 a2 = a - d * h, b2 = b + d * h;
            int idx = vh.currentVertCount;
            vh.AddVert(a2 - nrm, col, Vector4.zero);
            vh.AddVert(a2 + nrm, col, Vector4.zero);
            vh.AddVert(b2 + nrm, col, Vector4.zero);
            vh.AddVert(b2 - nrm, col, Vector4.zero);
            vh.AddTriangle(idx, idx + 1, idx + 2);
            vh.AddTriangle(idx, idx + 2, idx + 3);
        }
    }
}
