using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 룬 획득 팝업의 「지금 룬판」 — 판 전체(존맵 12행 × 13열)를 칸 단위로 작게 그리고, 속성마다 채운 칸 · 단계를 적는다.
///
/// <para><b>왜 여기 있나</b> — 룬 선택지를 3장으로 고정하면서(10-01 사용자 결정) 네 번째 카드 자리가 비었다.
/// 그 자리에 판을 두면 고르는 순간 「이 룬이 판 어디로 가는가 · 그 속성이 얼마나 찼는가」를 판을 열지 않고 본다.
/// 카드를 고르면 그 룬이 들어갈 수 있는 빈 칸만 밝아진다(<see cref="Highlight"/>) — 규칙은 판과 같은 <see cref="RuneZoneRule"/>.</para>
///
/// <para><b>읽기 전용 스냅숏</b>이다 — 팝업을 여는 순간의 점유(<see cref="MerlinRuneBridge.CaptureRuneCells"/>)와
/// 존맵(<see cref="RuneDataManager.GetZoneMapRows"/>)으로 한 번 그리고, 판 · 시너지를 건드리지 않는다.
/// 칸 좌표는 판(<see cref="MerlinRuneHexGridView.BuildGrid"/>)과 같은 규칙(행마다 가운데 정렬한 절대 열)이다.</para>
/// </summary>
public sealed class RuneBoardMini
{
    // ── Constants ──
    private const float Pad      = 12f;
    private const float TitleY   = 12f;
    private const float TitleH   = 26f;
    private const float BoardY   = 46f;
    private const float CellGap  = 2f;
    private const float RowsGap  = 12f;   // 판 아래 → 속성 줄
    private const float RowH     = 24f;
    private const float TileSize = 14f;
    private const float NameW    = 48f;
    private const float CountW   = 58f;
    private const float PipSize  = 8f;
    private const float PipGap   = 4f;
    private const int   MaxTier  = 4;     // 속성 단계 30 · 50 · 70 · 100%

    private const float EmptyAlpha  = 0.30f;
    private const float CenterAlpha = 0.22f;
    private const float HintAlpha   = 0.60f;   // 고른 룬이 들어갈 수 있는 빈 칸 — 놓인 칸(타일 · 불투명)보다는 옅게
    private const float OffAlpha    = 0.08f;   // 고른 룬이 못 들어가는 빈 칸
    private const float RowOffAlpha = 0.45f;   // 고른 룬과 다른 속성 줄
    private static readonly Color PipOff = new(1f, 1f, 1f, 0.14f);

    private sealed class Cell
    {
        public Image Img;
        public char  Code;
        public Color Zone;
        public bool  Occupied;
    }

    // ── Private ──
    private readonly RectTransform _root;
    private readonly List<Cell> _cells = new();
    private readonly Dictionary<string, CanvasGroup> _rows = new();

    private RuneBoardMini(RectTransform root) { _root = root; }

    // ── Public Methods ──

    /// <summary>
    /// <paramref name="panel"/>(폭 <paramref name="width"/>) 안에 판을 짓는다. 존맵이 없으면 null — 호출측은 판 자리를 비운다.
    /// </summary>
    public static RuneBoardMini Build(RectTransform panel, float width, RunItemInventory inventory)
    {
        var rows = Managers.RuneData?.GetZoneMapRows();
        if (panel == null || rows == null || rows.Count == 0) return null;

        int maxLen = 0, rowCount = 0;
        foreach (var r in rows)
        {
            if (r?.pattern == null) continue;
            maxLen   = Mathf.Max(maxLen, r.pattern.Length);
            rowCount = Mathf.Max(rowCount, r.hex_row + 1);
        }
        if (maxLen == 0) return null;

        var bridge   = MerlinRuneBridge.Instance;
        var occupied = new HashSet<Vector2Int>();
        var snap     = bridge != null ? bridge.CaptureRuneCells() : null;
        if (snap != null) foreach (var c in snap) occupied.Add(c);

        // 칸 → 놓인 룬의 속성. 중앙 칸은 어떤 속성이든 받아 존 코드로는 알 수 없다 — 배치 기록에서 읽는다.
        var cellElement = new Dictionary<Vector2Int, string>();
        var placements  = bridge != null ? bridge.CaptureRunePlacements() : null;
        if (placements != null && inventory != null)
            foreach (var p in placements)
            {
                var item = FindPlaced(inventory, p?.instanceId);
                if (item == null || p.cells == null) continue;
                foreach (var c in p.cells) cellElement[c] = item.element;
            }

        var mini = new RuneBoardMini(panel);
        var totals = new Dictionary<char, int>();
        var filled = new Dictionary<char, int>();

        // ── 판 ──
        float step = (width - Pad * 2f) / maxLen;
        float cell = step - CellGap;
        foreach (var r in rows)
        {
            if (r?.pattern == null) continue;
            int colOffset = (maxLen - r.pattern.Length) / 2;   // 판과 같은 다이아몬드 가운데 정렬
            for (int c = 0; c < r.pattern.Length; c++)
            {
                char code = r.pattern[c];
                var  pos  = new Vector2Int(c + colOffset, r.hex_row);
                bool occ  = occupied.Contains(pos);
                if (!ElementDef.TryGetCodeColor(code, out var zone)) zone = ElementDef.CenterColor;

                totals.TryGetValue(code, out int t); totals[code] = t + 1;
                if (occ) { filled.TryGetValue(code, out int f); filled[code] = f + 1; }

                var img = ShopUIStyle.MakeImage(panel, $"C{pos.x}_{pos.y}", Color.white);
                PlaceTL(img.rectTransform, Pad + pos.x * step + CellGap * 0.5f, BoardY + pos.y * step + CellGap * 0.5f, cell, cell);

                var entry = new Cell { Img = img, Code = code, Zone = zone, Occupied = occ };
                if (occ)
                {
                    // 판 위 블록과 같은 속성 타일 — 「어느 속성이 판 어디를 먹었는가」가 판과 같은 그림으로 읽힌다.
                    string el   = cellElement.TryGetValue(pos, out var e) ? e : ElementDef.CodeToId(code);
                    var    tile = RuneArt.GetBlockTile(el);
                    if (tile != null) img.sprite = tile;
                    else              img.color  = ElementDef.IdColor(el, zone);
                }
                else img.color = EmptyColor(entry);
                mini._cells.Add(entry);
            }
        }

        // ── 제목 · 전체 채움 ──
        int allTotal = 0, allFilled = 0;
        foreach (var kv in totals)  allTotal  += kv.Value;
        foreach (var kv in filled)  allFilled += kv.Value;

        var title = ShopUIStyle.MakeText(panel, "Title", 18f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, ShopUIStyle.TextPrimary);
        PlaceTL(title.rectTransform, Pad, TitleY, width * 0.5f, TitleH);
        title.text = "지금 룬판";

        var total = ShopUIStyle.MakeText(panel, "Total", 16f, FontStyles.Normal, TextAlignmentOptions.MidlineRight, ShopUIStyle.TextDim);
        PlaceTL(total.rectTransform, width * 0.5f, TitleY, width * 0.5f - Pad, TitleH);
        total.text = $"<color=#{ColorUtility.ToHtmlStringRGB(ShopUIStyle.TextPrimary)}>{allFilled}</color> / {allTotal}칸";

        // ── 속성 줄 — 채운 칸 / 존 칸 · 단계 ──
        float y = BoardY + rowCount * step + RowsGap;
        foreach (var id in ElementDef.Order)
        {
            var def = ElementDef.GetById(id);
            if (def == null) continue;
            totals.TryGetValue(def.Code, out int zt);
            filled.TryGetValue(def.Code, out int zf);
            int tier = bridge != null ? Mathf.Clamp(bridge.GetZoneTier(id), 0, MaxTier) : 0;

            var row = ShopUIStyle.MakeRect(panel, $"Row_{id}", typeof(CanvasGroup)).GetComponent<RectTransform>();
            PlaceTL(row, Pad, y, width - Pad * 2f, RowH);
            mini._rows[id] = row.GetComponent<CanvasGroup>();

            var tileArt = RuneArt.GetBlockTile(id);
            var dot = ShopUIStyle.MakeImage(row, "Tile", tileArt != null ? Color.white : def.Color);
            if (tileArt != null) { dot.sprite = tileArt; dot.preserveAspect = true; }
            PlaceTL(dot.rectTransform, 0f, (RowH - TileSize) * 0.5f, TileSize, TileSize);

            var name = ShopUIStyle.MakeText(row, "Name", 16f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft, def.Color);
            PlaceTL(name.rectTransform, TileSize + 6f, 0f, NameW, RowH);
            name.text = def.Name;

            var count = ShopUIStyle.MakeText(row, "Count", 16f, FontStyles.Normal, TextAlignmentOptions.MidlineRight,
                                             zf > 0 ? ShopUIStyle.TextPrimary : ShopUIStyle.TextDim);
            PlaceTL(count.rectTransform, TileSize + 6f + NameW, 0f, CountW, RowH);
            count.text = $"{zf}/{zt}";

            // 단계 — 채운 만큼 속성 색 조각. 시너지가 실제로 선 단계(브릿지)를 그린다.
            float pipX = width - Pad * 2f - (MaxTier * PipSize + (MaxTier - 1) * PipGap);
            for (int i = 0; i < MaxTier; i++)
            {
                var pip = ShopUIStyle.MakeImage(row, $"Pip{i}", i < tier ? def.Color : PipOff);
                PlaceTL(pip.rectTransform, pipX + i * (PipSize + PipGap), (RowH - PipSize) * 0.5f, PipSize, PipSize);
            }
            y += RowH;
        }

        return mini;
    }

    /// <summary>
    /// 고른 룬이 들어갈 수 있는 <b>빈 칸</b>만 밝히고 나머지 빈 칸은 누른다. 그 룬의 속성 줄만 또렷하게.
    /// null이면 원래대로. 놓인 칸은 그대로 둔다(판 위 사실이라 선택과 무관하다).
    /// </summary>
    public void Highlight(RuntimeItemData rune)
    {
        if (_root == null) return;   // 지난 창의 판(파괴됨) — 새 판을 짓기 전에 선택 초기화가 먼저 불린다

        string el       = RuneZoneRule.ElementOf(rune);
        bool   noCenter = RuneZoneRule.NoCenter(rune);
        foreach (var c in _cells)
        {
            if (c.Occupied || c.Img == null) continue;
            c.Img.color = rune == null ? EmptyColor(c)
                        : WithAlpha(c.Zone, RuneZoneRule.Accepts(c.Code, el, noCenter) ? HintAlpha : OffAlpha);
        }
        foreach (var kv in _rows)
            if (kv.Value != null) kv.Value.alpha = rune == null || el == null || kv.Key == el ? 1f : RowOffAlpha;
    }

    // ── Private Methods ──

    private static Color EmptyColor(Cell c)
        => WithAlpha(c.Zone, c.Code == ElementDef.CenterCode ? CenterAlpha : EmptyAlpha);

    private static RuntimeItemData FindPlaced(RunItemInventory inventory, string instanceId)
    {
        if (string.IsNullOrEmpty(instanceId)) return null;
        foreach (var it in inventory.PlacedItems)
            if (it != null && it.instanceId == instanceId) return it;
        return null;
    }

    /// <summary>판(패널) 왼쪽 위 기준 사각형.</summary>
    private static void PlaceTL(RectTransform rt, float x, float y, float w, float h)
    {
        var tl = new Vector2(0f, 1f);
        ShopUIStyle.Anchor(rt, tl, tl, tl, new Vector2(x, -y), new Vector2(w, h));
    }

    private static Color WithAlpha(Color c, float a) { c.a = a; return c; }
}
