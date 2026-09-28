using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 룬 카드 부품 — 룬이 나오는 화면(룬판 상세·보관함·상점)이 <b>룬 선택 팝업과 같은 얼굴</b>을 하도록
/// 등급 보석 테두리 · 등급/속성 칩 · 작은 모양 · 놓을 자리 판정을 한곳에서 만든다(09-25 UX 시안 「룬 선택 규격」).
///
/// <para>규격의 원본은 <see cref="UI_RuneSelectPopup"/>이다 — 수치(칩 높이 24·글자 16·보석 조각 비율)를 그쪽과 맞춘다.
/// 스킨(<see cref="UISkin.RuneSelect"/>)이 없으면 등급색 막대 테두리로 떨어진다.</para>
/// </summary>
public static class RuneCardKit
{
    // ── Constants ──
    public const float ChipH = 24f;
    private const float ChipPad  = 9f;
    private const float ChipGap  = 6f;
    private const float ChipTile = 14f;

    private static readonly Color ChipFill = new(0.05f, 0.06f, 0.10f, 0.72f);
    public static readonly Color OkColor   = new(0.47f, 0.84f, 0.60f, 1f);
    public static readonly Color NoColor   = new(0.92f, 0.40f, 0.33f, 1f);

    // ── 등급 보석 테두리 ──

    /// <summary>
    /// <paramref name="cardRT"/> 위에 등급 보석 테두리를 새로 세운다(이전 것은 지운다). 모서리 4 + 위·아래 장식바.
    /// 좌상단 모서리 한 장을 축 반전해 네 귀퉁이로 쓴다 — 룬 선택 카드와 같은 방식.
    /// </summary>
    public static void BuildGemFrame(RectTransform cardRT, ItemRarity rarity, float cornerW, float barW, float outset = 3f,
                                     bool bottomBar = true)
    {
        if (cardRT == null) return;
        var root = Rebuild(cardRT, "GemFrame");

        var skin   = UISkin.RuneSelect;
        var corner = skin?.RarityCorner(rarity);
        var bar    = skin?.RarityBar(rarity);
        if (corner == null || bar == null) { BuildColorFrame(root, rarity); return; }

        float ch = cornerW * corner.rect.height / Mathf.Max(1f, corner.rect.width);
        float bh = barW * bar.rect.height / Mathf.Max(1f, bar.rect.width);
        float o  = outset;

        var outer = new Vector2(0f, 1f);
        Piece(root, "Gem_TL", corner, new Vector2(0f, 1f), outer, new Vector2(-o,  o), new Vector2(cornerW, ch), new Vector2( 1f,  1f));
        Piece(root, "Gem_TR", corner, new Vector2(1f, 1f), outer, new Vector2( o,  o), new Vector2(cornerW, ch), new Vector2(-1f,  1f));
        Piece(root, "Gem_BL", corner, new Vector2(0f, 0f), outer, new Vector2(-o, -o), new Vector2(cornerW, ch), new Vector2( 1f, -1f));
        Piece(root, "Gem_BR", corner, new Vector2(1f, 0f), outer, new Vector2( o, -o), new Vector2(cornerW, ch), new Vector2(-1f, -1f));

        var mid = new Vector2(0.5f, 0.5f);
        Piece(root, "Gem_BarT", bar, new Vector2(0.5f, 1f), mid, new Vector2(0f, -bh * 0.12f), new Vector2(barW, bh), new Vector2(1f,  1f));
        // 낮은 카드(보관함 146px)에선 아래 장식바의 보석이 효과 글자 가운데를 덮는다 — 끌 수 있게 한다.
        if (bottomBar)
            Piece(root, "Gem_BarB", bar, new Vector2(0.5f, 0f), mid, new Vector2(0f,  bh * 0.12f), new Vector2(barW, bh), new Vector2(1f, -1f));
    }

    /// <summary>테두리만 지운다(빈 칸으로 돌아갈 때).</summary>
    public static void ClearGemFrame(RectTransform cardRT)
    {
        if (cardRT == null) return;
        Retire(cardRT.Find("GemFrame"));
    }

    // ── 칩 ──

    /// <summary>등급·칸 수 칩 문구 — 「◇ Rare · 3칸」. 칸 수를 모르면 등급만.</summary>
    public static string RarityChipText(RuntimeItemData data)
    {
        int cells = CellCount(data);
        return RewardPresentation.RarityLabel(data.rarity) + (cells > 0 ? $" · {cells}칸" : string.Empty);
    }

    /// <summary>
    /// 등급 칩 + 속성 칩(블록 타일 + 이름)을 <paramref name="row"/> 가운데에 한 줄로 세운다. 이전 칩은 지운다.
    /// 속성 없는 룬은 등급 칩만. 반환 = 줄 전체 폭.
    /// </summary>
    public static float BuildChipRow(RectTransform row, RuntimeItemData data, float fontSize = 16f)
    {
        if (row == null || data == null) return 0f;
        var root = Rebuild(row, "Chips");

        var rarityCol = ShopUIStyle.Rarity(data.rarity);
        var r = MakeChip(root, "RarityChip", RarityChipText(data), rarityCol, WithAlpha(rarityCol, 0.45f), fontSize, out float rw);
        float total = rw;

        RectTransform e = null; float ew = 0f;
        var elem = ElementDef.GetById(data.element);
        if (elem != null)
        {
            var elemCol = ElementDef.IdColor(data.element, ShopUIStyle.TextDim);
            e = MakeChip(root, "ElementChip", elem.Name, elemCol, WithAlpha(elemCol, 0.35f), fontSize, out ew, ChipTile + 4f);
            var tileArt = RuneArt.GetBlockTile(data.element);
            var dot = ShopUIStyle.MakeImage(e, "Tile", tileArt != null ? Color.white : elemCol);
            if (tileArt != null) { dot.sprite = tileArt; dot.preserveAspect = true; }
            ShopUIStyle.Anchor(dot.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                               new Vector2(ChipPad, 0f), Vector2.one * ChipTile);
            total += ChipGap + ew;
        }

        float x = -total * 0.5f;
        r.anchoredPosition = new Vector2(x + rw * 0.5f, 0f);
        if (e != null) e.anchoredPosition = new Vector2(x + rw + ChipGap + ew * 0.5f, 0f);
        return total;
    }

    /// <summary>칩 하나 — 테두리색 선 + 어두운 채움 + 한 줄 글자. 폭은 글자에 맞춘다. 가운데 기준으로 놓인다.</summary>
    public static RectTransform MakeChip(RectTransform parent, string name, string text, Color textCol, Color lineCol,
                                         float fontSize, out float width, float leading = 0f)
    {
        var fill = ShopUIStyle.MakeFrame(parent, name, lineCol, ChipFill, 1f);
        var rt = (RectTransform)fill.transform.parent;

        var label = ShopUIStyle.MakeText(fill.transform, "Label", fontSize, FontStyles.Normal, TextAlignmentOptions.Left, textCol);
        label.enableAutoSizing = false;   // 칩 폭을 글자에 맞추므로 줄일 이유가 없다(16 하한)
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.text = text;
        float h = Mathf.Max(ChipH, fontSize + 8f);
        float tw = label.GetPreferredValues(text, 999f, h).x;
        width = Mathf.Ceil(tw + ChipPad * 2f + leading);

        ShopUIStyle.Anchor(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                           Vector2.zero, new Vector2(width, h));
        ShopUIStyle.Stretch(label.rectTransform);
        label.rectTransform.offsetMin = new Vector2(ChipPad + leading, 0f);
        return rt;
    }

    // ── 모양 · 놓을 자리 ──

    /// <summary>지금 판에 이 룬을 놓을 자리가 있는가. 판정할 수 없으면 막지 않는다(true).</summary>
    public static bool CanPlace(RuntimeItemData data)
    {
        if (data == null) return true;
        var entry = Managers.RuneData?.GetShape(data.shapeId);
        if (entry == null) return true;
        var offsets = RuneDataManager.ParseCellOffsets(entry);
        if (offsets == null || offsets.Length == 0) return true;

        // 속성까지 넘긴다 — 룬은 자기 속성 존(레전드리는 중앙 제외)에만 놓인다.
        return MerlinRuneBridge.Instance == null
            || MerlinRuneBridge.Instance.CanPlaceShape(offsets, data.element, RuneZoneRule.NoCenter(data));
    }

    /// <summary>
    /// 작은 모양을 <paramref name="root"/> 안에 가운데 정렬로 그린다(판 위 블록과 같은 속성 타일). 이전 칸은 지운다.
    /// <paramref name="dim"/>이면 어둡게(놓을 자리 없음).
    /// </summary>
    public static void BuildShape(RectTransform root, RuntimeItemData data, float cellMax, float gap, bool dim = false)
    {
        if (root == null) return;
        var host = Rebuild(root, "Cells");
        if (data == null) return;

        var entry = Managers.RuneData?.GetShape(data.shapeId);
        if (entry == null) return;
        var offsets = RuneDataManager.ParseCellOffsets(entry);
        if (offsets == null || offsets.Length == 0) return;

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var o in offsets)
        {
            if (o.x < minX) minX = o.x;  if (o.x > maxX) maxX = o.x;
            if (o.y < minY) minY = o.y;  if (o.y > maxY) maxY = o.y;
        }
        int cols = maxX - minX + 1, rows = maxY - minY + 1;

        float boxW = root.rect.width  > 1f ? root.rect.width  : root.sizeDelta.x;
        float boxH = root.rect.height > 1f ? root.rect.height : root.sizeDelta.y;
        float fitW = (boxW - (cols - 1) * gap) / Mathf.Max(1, cols);
        float fitH = (boxH - (rows - 1) * gap) / Mathf.Max(1, rows);
        float cell = Mathf.Max(4f, Mathf.Min(cellMax, fitW, fitH));

        // 스프라이트/틴트 규칙은 RuneArt 한곳 — 판 위 블록과 같은 속성 타일이 있으면 그걸 쓴다.
        RuneArt.ResolveRuneCell(data.element, data.rarity, new Color(0.7f, 0.7f, 0.75f), out Sprite art, out Color tint);
        if (art == null) art = UISkin.RuneSelect?.runeTile;
        var blockTile = RuneArt.GetBlockTile(data.element);
        if (blockTile != null) { art = blockTile; tint = Color.white; }
        if (dim) tint = new Color(tint.r * 0.5f, tint.g * 0.5f, tint.b * 0.5f, 0.7f);

        float totalW = cols * cell + (cols - 1) * gap;
        float totalH = rows * cell + (rows - 1) * gap;
        foreach (var o in offsets)
        {
            int col = o.x - minX;
            int row = maxY - o.y;
            var img = ShopUIStyle.MakeImage(host, $"C{o.x}_{o.y}", tint);
            if (art != null) { img.sprite = art; img.preserveAspect = true; }
            ShopUIStyle.Anchor(img.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-totalW * 0.5f + cell * 0.5f + col * (cell + gap), totalH * 0.5f - cell * 0.5f - row * (cell + gap)),
                Vector2.one * cell);
        }
    }

    /// <summary>놓을 자리 배지 — 테두리·채움·글자 색과 문구를 상태에 맞춘다(룬 선택 카드와 같은 색).</summary>
    public static void ApplyFitBadge(Image border, Image fill, TMP_Text label, bool ok, string text)
    {
        var col = ok ? OkColor : NoColor;
        if (border != null) border.color = WithAlpha(col, 0.55f);
        if (fill   != null) fill.color   = new Color(col.r * 0.12f, col.g * 0.14f, col.b * 0.12f, 0.85f);
        if (label  != null) { label.color = col; label.text = text; }
    }

    /// <summary>모양 데이터의 칸 수. 모르면 0.</summary>
    public static int CellCount(RuntimeItemData data)
    {
        if (data == null || data.shapeId <= 0) return 0;
        var entry = Managers.RuneData?.GetShape(data.shapeId);
        var offsets = entry != null ? RuneDataManager.ParseCellOffsets(entry) : null;
        return offsets?.Length ?? 0;
    }

    // ── 코드로 그리는 도형 ──

    private static Sprite _disc;

    /// <summary>원 한 장(가장자리 1px만 부드럽게) — 버리기 같은 둥근 단추. 아트가 따로 없다.</summary>
    public static Sprite Disc => _disc != null ? _disc
        : (_disc = UI_RuneSelectPopup.MakeProcSprite("RuneCard_Disc", 64, 64, (u, v) =>
          {
              float d = new Vector2(u - 0.5f, v - 0.5f).magnitude * 64f;   // 중심에서 px
              return Mathf.Clamp01(31.5f - d);
          }));

    // ── 내부 ──

    /// <summary>이름이 같은 자식을 지우고(파괴는 프레임 끝이라 이름을 먼저 바꾼다) 부모 크기를 채우는 빈 컨테이너를 새로 만든다.</summary>
    private static RectTransform Rebuild(RectTransform parent, string name)
    {
        Retire(parent.Find(name));
        var rt = ShopUIStyle.MakeRect(parent, name).GetComponent<RectTransform>();
        ShopUIStyle.Stretch(rt);
        return rt;
    }

    private static void Retire(Transform t)
    {
        if (t == null) return;
        t.name = t.name + "_old";
        t.gameObject.SetActive(false);
        Object.Destroy(t.gameObject);
    }

    private static void Piece(RectTransform parent, string name, Sprite art, Vector2 anchor, Vector2 pivot,
                              Vector2 pos, Vector2 size, Vector2 flip)
    {
        var img = ShopUIStyle.MakeImage(parent, name, Color.white);
        img.sprite = art;
        img.preserveAspect = true;
        ShopUIStyle.Anchor(img.rectTransform, anchor, anchor, pivot, pos, size);
        img.rectTransform.localScale = new Vector3(flip.x, flip.y, 1f);
    }

    /// <summary>폴백 — 등급색 막대 4개(아트 스킨 미로드).</summary>
    private static void BuildColorFrame(RectTransform root, ItemRarity rarity)
    {
        float th = ShopUIStyle.RarityBorder(rarity);
        var col  = RewardPresentation.FrameColor(rarity);
        var specs = new[]
        {
            (new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, th)),
            (new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, th)),
            (new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(th, 0f)),
            (new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(th, 0f)),
        };
        for (int i = 0; i < specs.Length; i++)
        {
            var bar = ShopUIStyle.MakeImage(root, $"RarityBar{i}", col);
            ShopUIStyle.Anchor(bar.rectTransform, specs[i].Item1, specs[i].Item2, specs[i].Item3, Vector2.zero, specs[i].Item4);
        }
    }

    private static Color WithAlpha(Color c, float a) { c.a = a; return c; }
}
