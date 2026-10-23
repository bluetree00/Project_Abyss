using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 룬 조각(<see cref="Shape"/>) 하나의 <b>바깥 윤곽</b> — 10-18 사용자 「여전히 룬들이 붙어 있으면 식별하기가 어려워 —
/// 어떤 모양들이 붙은 건지 정확한 라인 표기가 필요해 보여」.
/// <para>예전 조각 외곽선(09-14 · 10-11)은 판(MerlinRuneHexGridView)이 칸 <b>안쪽</b> 가장자리에 그었는데, 그 위에 Shape 블록 그림이
/// 얹혀 거의 다 가렸다 — 그림 테 틈으로 1px만 비쳐, 같은 속성 룬이 붙으면 칸이 모두 같은 그림이라 어디까지가 한 룬인지 안 보였다(10-18 사진 02).</para>
/// <para>그래서 조각이 스스로 그린다. Shape <b>루트</b>에 붙는 그래픽 하나다(자식이 아니다 — GridManager는 Shape의 자식을 전부 블록으로 센다).
/// 부모 그래픽은 자식(블록)보다 먼저 그려지므로 ① 받침은 블록 밑에 깔려 같은 조각 칸 사이 틈만 드러나고(안쪽 칸 경계가 흐려진다)
/// ② 금선은 블록 바깥 칸 틈에 그어져 블록에 가리지 않는다. 다른 조각과 맞닿은 틈(5px)엔 두 조각의 선 2px씩 사이에 1px 어둠이 남아
/// 「여기서 갈린다」가 보인다. 칸 그림 자체는 건드리지 않는다.</para>
/// <para>끄는 동안에도 조각과 함께 움직인다(배치 미리보기). 칸이 바뀔 때(블록 재생성)와 고름 상태가 바뀔 때만 메시를 다시 만든다 — 매 프레임 일은 없다.</para>
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class RunePieceOutline : MaskableGraphic
{
    // ── Constants ──
    private const float LineWidth = 2f;   // 1080 기준 px — 칸 틈(5px)에 맞닿은 두 조각의 선이 1px 사이를 두고 들어간다

    // 받침 — 블록 그림의 청동 테(sRGB ≈ 100,66,26)보다 한 단계 어둡게. 같은 조각 칸 사이 틈이 판 바탕(거의 검정) 대신 이 색으로 메워져 한 덩어리로 읽힌다.
    private static readonly Color PlateColor = new(0.30f, 0.21f, 0.10f, 0.90f);
    // 금선 — 평소 = 금과 잉크 사이의 옅은 금(속성 그림의 노랑보다 밝게), 고른 조각(호버 · 정보판 · 끄는 중) = 거의 흰 금.
    private static readonly Color LineIdle = UITheme.Thin.WithA(Color.Lerp(UITheme.Thin.Gold, UITheme.Thin.Ink, 0.35f), 0.80f);
    private static readonly Color LineHot  = UITheme.Thin.WithA(Color.Lerp(UITheme.Thin.Gold, Color.white, 0.65f), 1f);

    private static readonly Vector2Int Right = new(1, 0), Up = new(0, 1);

    // ── Private ──
    private readonly HashSet<Vector2Int> _cells = new();   // Shape.cellOffsets — 위가 +y
    private float _step, _size;                             // 칸 간격 · 블록 크기(판 위 68 · 63)
    private bool  _hovered, _selected, _dragging;

    // ── Public Methods ──

    /// <summary>조각 칸과 칸 간격 · 블록 크기로 다시 그린다. Shape가 블록을 새로 만들 때마다 부른다.</summary>
    public void SetCells(IReadOnlyList<Vector2Int> cells, float step, float size)
    {
        _cells.Clear();
        if (cells != null)
            for (int i = 0; i < cells.Count; i++) _cells.Add(cells[i]);
        _step = step;
        _size = size;
        SetVerticesDirty();
    }

    /// <summary>판에 놓인 조각 위에 커서가 있다 — 선을 밝게.</summary>
    public void SetHovered(bool on)  { if (_hovered  == on) return; _hovered  = on; SetVerticesDirty(); }

    /// <summary>오른쪽 정보판에 뜬 놓인 룬 — 선을 밝게(UI_GridPanel이 정보판이 바뀔 때마다 부른다).</summary>
    public void SetSelected(bool on) { if (_selected == on) return; _selected = on; SetVerticesDirty(); }

    /// <summary>끄는 중 — 선을 밝게.</summary>
    public void SetDragging(bool on) { if (_dragging == on) return; _dragging = on; SetVerticesDirty(); }

    // ── Private Methods ──

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (_cells.Count == 0 || _step <= 0f) return;

        float h = _size * 0.5f;
        float g = Mathf.Max(0f, _step - _size);   // 칸 틈
        float w = LineWidth;
        Color32 plate = PlateColor;
        Color32 line  = _hovered || _selected || _dragging ? LineHot : LineIdle;

        // ① 받침 — 블록 · 같은 조각 칸 사이 틈 · 2×2 가운데 틈. 서로 겹치지 않는 사각형이라 알파가 고르다.
        foreach (var p in _cells)
        {
            Vector2 c = (Vector2)p * _step;
            Quad(vh, c.x - h, c.y - h, c.x + h, c.y + h, plate);
            if (g <= 0f) continue;
            bool r = _cells.Contains(p + Right), u = _cells.Contains(p + Up);
            if (r) Quad(vh, c.x + h, c.y - h, c.x + h + g, c.y + h, plate);
            if (u) Quad(vh, c.x - h, c.y + h, c.x + h, c.y + h + g, plate);
            if (r && u && _cells.Contains(p + Right + Up)) Quad(vh, c.x + h, c.y + h, c.x + h + g, c.y + h + g, plate);
        }

        // ② 금선 — 받침 바깥 둘레 w. 칸마다 바깥으로 향한 변만 긋고 끝은 이웃에 따라 늘린다(서로 겹치지 않게, HorizExt · VertExt).
        foreach (var p in _cells)
        {
            Vector2 c = (Vector2)p * _step;
            for (int s = -1; s <= 1; s += 2)   // -1 = 아래 · 왼쪽, +1 = 위 · 오른쪽
            {
                var n = new Vector2Int(0, s);   // 가로 변(아래 · 위)
                if (!_cells.Contains(p + n))
                {
                    float y0 = s > 0 ? c.y + h : c.y - h - w;
                    Quad(vh, c.x - h - HorizExt(p, n, -1, g, w), y0, c.x + h + HorizExt(p, n, 1, g, w), y0 + w, line);
                }

                n = new Vector2Int(s, 0);       // 세로 변(왼쪽 · 오른쪽)
                if (!_cells.Contains(p + n))
                {
                    float x0 = s > 0 ? c.x + h : c.x - h - w;
                    Quad(vh, x0, c.y - h - VertExt(p, n, -1, g, w), x0 + w, c.y + h + VertExt(p, n, 1, g, w), line);
                }
            }
        }
    }

    /// <summary>
    /// 가로 변 한쪽 끝(dir −1 왼쪽 · +1 오른쪽)을 얼마나 늘리나(n = 변의 바깥 법선).
    /// 볼록 모서리 = +w(모서리 칸은 가로 변이 맡는다) · 오목 = +g(안쪽 틈 위까지) · 곧게 이어짐 = 틈을 왼쪽 칸만 한 번 덮는다.
    /// </summary>
    private float HorizExt(Vector2Int p, Vector2Int n, int dir, float g, float w)
    {
        var a = p + new Vector2Int(dir, 0);
        if (!_cells.Contains(a))    return w;
        if (_cells.Contains(a + n)) return g;
        return dir > 0 ? g : 0f;
    }

    /// <summary>
    /// 세로 변 한쪽 끝(dir −1 아래 · +1 위). 볼록 = 0(가로 변이 모서리를 덮었다) · 오목 = g − w(가로 변이 덮은 w는 빼고) ·
    /// 곧게 이어짐 = 틈을 아래 칸만 한 번 덮는다.
    /// </summary>
    private float VertExt(Vector2Int p, Vector2Int n, int dir, float g, float w)
    {
        var a = p + new Vector2Int(0, dir);
        if (!_cells.Contains(a))    return 0f;
        if (_cells.Contains(a + n)) return g - w;
        return dir > 0 ? g : 0f;
    }

    private static void Quad(VertexHelper vh, float x0, float y0, float x1, float y1, Color32 col)
    {
        int i = vh.currentVertCount;
        vh.AddVert(new Vector3(x0, y0), col, Vector4.zero);
        vh.AddVert(new Vector3(x0, y1), col, Vector4.zero);
        vh.AddVert(new Vector3(x1, y1), col, Vector4.zero);
        vh.AddVert(new Vector3(x1, y0), col, Vector4.zero);
        vh.AddTriangle(i, i + 1, i + 2);
        vh.AddTriangle(i, i + 2, i + 3);
    }
}
