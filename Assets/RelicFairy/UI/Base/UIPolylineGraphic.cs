using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 두께 있는 꺾은선 — 점 목록을 잇고 <see cref="Progress"/>만큼만 그린다(0~1, 앞에서부터).
/// 기억의 제단의 「빛실」(부모 → 자식이 자라는 선)과 깊이 고리(닫힌 타원)가 쓴다(09-29).
/// <para>레이캐스트는 받지 않는다. 좌표는 이 RectTransform의 로컬(피벗 기준).</para>
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class UIPolylineGraphic : MaskableGraphic
{
    // ── Constants ────────────────────────────────────────
    private const int EllipseSegments = 96;

    // ── Private ──────────────────────────────────────────
    private readonly List<Vector2> _points = new();
    private readonly List<float>   _cum    = new();   // 누적 길이
    private float _width    = 3f;
    private float _progress = 1f;
    private bool  _closed;

    // ── Properties ───────────────────────────────────────
    public float Progress
    {
        get => _progress;
        set { float v = Mathf.Clamp01(value); if (Mathf.Approximately(v, _progress)) return; _progress = v; SetVerticesDirty(); }
    }

    public float Width
    {
        get => _width;
        set { if (Mathf.Approximately(value, _width)) return; _width = Mathf.Max(0.5f, value); SetVerticesDirty(); }
    }

    /// <summary>그려진 끝점(Progress 위치) — 선을 타고 가는 빛 알갱이 자리.</summary>
    public Vector2 HeadPoint => PointAt(_progress);

    // ── Lifecycle ────────────────────────────────────────
    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
    }

    // ── Public Methods ───────────────────────────────────

    public void SetSegment(Vector2 a, Vector2 b)
    {
        _points.Clear(); _points.Add(a); _points.Add(b);
        _closed = false;
        Rebuild();
    }

    /// <summary>중심 원점 기준 타원(반지름 rx, ry). 닫힌 선.</summary>
    public void SetEllipse(float rx, float ry)
    {
        _points.Clear();
        for (int i = 0; i <= EllipseSegments; i++)
        {
            float a = i / (float)EllipseSegments * Mathf.PI * 2f + Mathf.PI * 0.5f;   // 위에서 시작
            _points.Add(new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry));
        }
        _closed = true;
        Rebuild();
    }

    /// <summary>선 위 비율 t(0~1) 지점.</summary>
    public Vector2 PointAt(float t)
    {
        if (_points.Count == 0) return Vector2.zero;
        if (_points.Count == 1) return _points[0];
        float total = _cum[_cum.Count - 1];
        float d = Mathf.Clamp01(t) * total;
        for (int i = 1; i < _points.Count; i++)
        {
            if (_cum[i] < d) continue;
            float seg = _cum[i] - _cum[i - 1];
            float k = seg > 0f ? (d - _cum[i - 1]) / seg : 0f;
            return Vector2.Lerp(_points[i - 1], _points[i], k);
        }
        return _points[_points.Count - 1];
    }

    // ── Private Methods ──────────────────────────────────

    private void Rebuild()
    {
        _cum.Clear();
        float acc = 0f;
        for (int i = 0; i < _points.Count; i++)
        {
            if (i > 0) acc += Vector2.Distance(_points[i - 1], _points[i]);
            _cum.Add(acc);
        }
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (_points.Count < 2 || _progress <= 0f) return;

        float total = _cum[_cum.Count - 1];
        float limit = _progress * total;
        float half  = _width * 0.5f;
        var   col   = (Color32)color;

        for (int i = 1; i < _points.Count; i++)
        {
            if (_cum[i - 1] >= limit) break;
            Vector2 a = _points[i - 1];
            Vector2 b = _points[i];
            if (_cum[i] > limit)
            {
                float seg = _cum[i] - _cum[i - 1];
                b = Vector2.Lerp(a, b, seg > 0f ? (limit - _cum[i - 1]) / seg : 0f);
            }
            Vector2 dir = b - a;
            if (dir.sqrMagnitude < 1e-6f) continue;
            Vector2 n = new Vector2(-dir.y, dir.x).normalized * half;
            // 이음매가 벌어지지 않게 선분을 반 두께만큼 늘린다(곡선 고리에서 틈이 보였다).
            Vector2 ext = dir.normalized * (_closed ? half * 0.5f : 0f);
            int v = vh.currentVertCount;
            vh.AddVert(a - ext - n, col, Vector2.zero);
            vh.AddVert(a - ext + n, col, Vector2.zero);
            vh.AddVert(b + ext + n, col, Vector2.zero);
            vh.AddVert(b + ext - n, col, Vector2.zero);
            vh.AddTriangle(v, v + 1, v + 2);
            vh.AddTriangle(v, v + 2, v + 3);
        }
    }
}
