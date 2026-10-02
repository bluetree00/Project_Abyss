using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 일시정지 「되찾은 기억」 — 보유 조각 보기(유물 성장 v2 §6-5). <see cref="UI_EscMenu"/> 캔버스 안에 짓는다(같은 정지 · 같은 막).
/// <list type="bullet">
/// <item>가웨인은 해시계 차례(여명 → 정오 → 황혼 → 궤적 → 반응), 랜슬롯은 계단 차례(10 → 40 → 광란 → 심판 → 반응)로 묶는다.</item>
/// <item>묶음 머리 = 조각 수 · 공명 단계 한 줄. 조각마다 등급 · 세 줄(드러난 줄 밝게, 잠긴 줄 ◇ 흐리게).</item>
/// <item>반응 조각은 그 룬 1단계가 켜져 있는가(켜짐 / 휴면) — 룬 판을 바꿔 꺼지면 휴면으로 흐려진다.</item>
/// </list>
/// Provider = PlayerLoadout · RELIC_PARTS_DATA · 룬 효과 디스패처 / Presenter = <see cref="BuildGroups"/> / View = <see cref="Render"/>.
/// </summary>
public sealed class UI_RelicMemoryPage : MonoBehaviour
{
    // ── Constants ────────────────────────────────────────────
    private const float PanelW = 1180f, PanelH = 820f;
    private const float Pad    = 44f;
    private const float BtnW   = 260f, BtnH = 60f;
    private const float HeaderSize = 24f, NameSize = 22f, LineSize = 19f;
    private const float GroupGap = 22f, RowGap = 12f, LineGap = 3f;

    private static readonly Color LineLit    = UITheme.Ink;
    private static readonly Color LineLocked = new(0.62f, 0.60f, 0.64f, 0.55f);
    private static readonly Color AwakeColor = new(0.55f, 0.85f, 0.55f, 1f);
    private static readonly Color[] GradeColor =
    {
        new(0.62f, 0.60f, 0.64f), // 없음
        new(0.72f, 0.74f, 0.80f), // 흐릿
        new(0.55f, 0.80f, 1f),    // 선명
        new(1f, 0.84f, 0.40f),    // 찬란
    };

    // ── 묶음 모델(Presenter → View) ───────────────────────────
    private sealed class Row
    {
        public string Name;
        public RelicMemoryGrade Grade;
        public readonly string[] Lines = new string[3];
        public string RuneNote;   // 반응 조각만
        public bool   RuneAwake;
    }

    private sealed class Group
    {
        public string Title;
        public string Note;
        public readonly List<Row> Rows = new();
    }

    // ── Private ──────────────────────────────────────────────
    private RectTransform _panel;
    private RectTransform _content;
    private ScrollRect    _scroll;
    private TMP_Text      _title;
    private TMP_Text      _empty;
    private Action        _onBack;

    // ── Public ───────────────────────────────────────────────
    /// <summary>캔버스 루트 아래에 쪽을 짓는다(숨긴 채). 돌아가기를 누르면 <paramref name="onBack"/>.</summary>
    public static UI_RelicMemoryPage Create(Transform canvasRoot, Action onBack)
    {
        var go = new GameObject("RelicMemoryPage", typeof(RectTransform));
        go.transform.SetParent(canvasRoot, false);
        Stretch((RectTransform)go.transform);
        var page = go.AddComponent<UI_RelicMemoryPage>();
        page._onBack = onBack;
        page.Build();
        go.SetActive(false);
        return page;
    }

    public void Open()
    {
        // 켠 뒤에 쌓는다 — 꺼진 채로는 TMP가 글자 높이(GetPreferredValues)를 제대로 못 재 줄이 겹친다(10-02 실측)
        gameObject.SetActive(true);
        Render(BuildGroups(out string relicName));
        _title.text = string.IsNullOrEmpty(relicName) ? "되찾은 기억" : $"되찾은 기억 — {relicName}";
        if (_scroll != null) _scroll.verticalNormalizedPosition = 1f;
    }

    public void Close() => gameObject.SetActive(false);

    // ── Presenter ────────────────────────────────────────────

    /// <summary>지금 런의 유물 조각을 해시계 · 계단 차례로 묶는다. 유물 이름도 돌려준다.</summary>
    private static List<Group> BuildGroups(out string relicName)
    {
        var groups  = new List<Group>();
        var loadout = AppBootstrapper.Instance?.Loadout;
        var data    = Managers.RelicParts;
        relicName = loadout?.Relic != null ? loadout.Relic.DisplayName : null;
        if (loadout == null || data == null) return groups;

        // 가진 조각을 유물별로 — 지금 유물과 같은 것만(옛 저장의 다른 유물 조각은 보이지 않는다)
        string relicId = null;
        var owned = new List<RelicPartEntry>();
        foreach (var id in loadout.RelicPartIds)
        {
            var e = data.GetById(id);
            if (e == null || !e.IsV2) continue;
            relicId ??= e.relic_id;
            if (e.relic_id == relicId) owned.Add(e);
        }
        if (relicId == null) return groups;

        var player = GameRunBootstrapper.Instance?.Run?.Player;
        var runes  = player != null ? player.RuneEffectsOrNull : null;
        bool gawain = relicId == "gawain";
        string[] order = gawain
            ? new[] { RelicPartAnchor.Dawn, RelicPartAnchor.Noon, RelicPartAnchor.Dusk, RelicPartAnchor.DawnNoon, RelicPartAnchor.NoonDusk, RelicPartAnchor.Reaction }
            : new[] { RelicPartAnchor.R10, RelicPartAnchor.R20, RelicPartAnchor.R30, RelicPartAnchor.R40, RelicPartAnchor.Frenzy, RelicPartAnchor.Judgment, RelicPartAnchor.Reaction };

        if (!gawain)
        {
            int ladder = RelicResonance.LancelotLadder(loadout);
            string text = RelicResonance.LadderText(RelicResonance.LancelotTier(ladder));
            groups.Add(new Group { Title = $"이어진 계단 {ladder}", Note = string.IsNullOrEmpty(text) ? "광기 10부터 끊김 없이 조각을 달면 계단이 이어진다(2 · 3 · 4)" : text });
        }

        foreach (var anchor in order)
        {
            var g = new Group { Title = RelicPartAnchor.Label(anchor) };
            foreach (var e in owned) if (e.anchor == anchor) g.Rows.Add(MakeRow(e, loadout, runes));
            int echo = loadout.GetRelicEcho(anchor);
            if (g.Rows.Count == 0 && echo <= 0) continue;
            if (gawain && (anchor == RelicPartAnchor.Dawn || anchor == RelicPartAnchor.Noon || anchor == RelicPartAnchor.Dusk))
            {
                int count = RelicResonance.GawainCount(loadout, anchor);
                int tier  = RelicResonance.GawainTier(count);
                string tierText = RelicResonance.GawainTierText(anchor, tier);
                g.Title += $"  ·  {count}개" + (tier >= 2 ? $"  ·  공명 {tier}" : string.Empty);
                g.Note = tier >= 2 ? tierText : "같은 시간대에 2 · 4개를 모으면 공명";
            }
            if (echo > 0) g.Title += $"  ·  메아리 {echo}";
            groups.Add(g);
        }
        return groups;
    }

    private static Row MakeRow(RelicPartEntry e, PlayerLoadout loadout, RuneEffectDispatcher runes)
    {
        var grade = loadout.GetRelicPartGrade(e.part_id);
        var row = new Row { Name = e.part_name, Grade = grade };
        for (int n = 1; n <= 3; n++) row.Lines[n - 1] = e.Line(n);
        if (e.IsReaction)
        {
            string key = RelicDraftComposer.RuneTier1Key(e.rune_element);
            row.RuneAwake = runes != null && !string.IsNullOrEmpty(key) && runes.IsActive(key);
            row.RuneNote  = $"룬 {RuneElementLabel(e.rune_element)} 1단계 · {(row.RuneAwake ? "켜짐" : "휴면 — 그 룬이 꺼져 반응이 쉰다")}";
        }
        return row;
    }

    private static string RuneElementLabel(string el) => el switch
    {
        "fire" => "불", "ice" => "얼음", "electric" => "번개", "grass" => "독", "light" => "빛", "dark" => "어둠", _ => el,
    };

    // ── View ─────────────────────────────────────────────────

    private void Build()
    {
        var panel = NewImage("Panel", transform, UITheme.Window);
        _panel = panel.rectTransform;
        _panel.anchorMin = _panel.anchorMax = _panel.pivot = new Vector2(0.5f, 0.5f);
        _panel.sizeDelta = new Vector2(PanelW, PanelH);
        UITheme.StylePanel(panel, UITheme.Window);

        _title = NewText("Title", _panel, "되찾은 기억", 32f, UITheme.Gold, TextAlignmentOptions.Center);
        Place(_title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(PanelW - 80f, 44f));

        // 스크롤 판 — 제목 아래 · 버튼 위
        var viewport = NewRect("Viewport", _panel);
        viewport.anchorMin = new Vector2(0f, 0f);
        viewport.anchorMax = new Vector2(1f, 1f);
        viewport.offsetMin = new Vector2(Pad, BtnH + 44f);
        viewport.offsetMax = new Vector2(-Pad, -88f);
        viewport.gameObject.AddComponent<RectMask2D>();
        var hit = viewport.gameObject.AddComponent<Image>();   // 휠 · 끌기 받이(보이지 않게)
        hit.color = new Color(0f, 0f, 0f, 0f);

        _content = NewRect("Content", viewport);
        _content.anchorMin = new Vector2(0f, 1f);
        _content.anchorMax = new Vector2(1f, 1f);
        _content.pivot     = new Vector2(0.5f, 1f);
        _content.sizeDelta = new Vector2(0f, 10f);

        _scroll = viewport.gameObject.AddComponent<ScrollRect>();
        _scroll.content = _content;
        _scroll.viewport = viewport;
        _scroll.horizontal = false;
        _scroll.vertical = true;
        _scroll.movementType = ScrollRect.MovementType.Clamped;
        _scroll.scrollSensitivity = 40f;

        _empty = NewText("Empty", _panel, "아직 되찾은 기억이 없다 — 보스를 쓰러뜨리면 유물이 기억을 하나씩 떠올린다", 22f, UITheme.Mute, TextAlignmentOptions.Center);
        Place(_empty.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(PanelW - 160f, 60f));

        var btn = NewImage("Btn_Back", _panel, UITheme.SecondaryTint);
        var brt = btn.rectTransform;
        brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0f);
        brt.pivot = new Vector2(0.5f, 0f);
        brt.anchoredPosition = new Vector2(0f, 26f);
        brt.sizeDelta = new Vector2(BtnW, BtnH);
        UITheme.StyleButton(btn, UITheme.SecondaryTint);
        var b = btn.gameObject.AddComponent<Button>();
        b.targetGraphic = btn;
        b.onClick.AddListener(() => _onBack?.Invoke());
        btn.gameObject.AddComponent<UIButtonFeedback>();
        var lbl = NewText("Label", brt, "돌아가기", 24f, UITheme.Ink, TextAlignmentOptions.Center);
        Stretch(lbl.rectTransform);
    }

    /// <summary>묶음을 위에서부터 손으로 쌓는다(레이아웃 그룹은 높이를 무시하는 함정이 있어 쓰지 않는다 — 글자 높이는 GetPreferredValues로 잰다).</summary>
    private void Render(List<Group> groups)
    {
        for (int i = _content.childCount - 1; i >= 0; i--) Destroy(_content.GetChild(i).gameObject);
        _empty.gameObject.SetActive(!HasRows(groups));
        if (!HasRows(groups)) groups.Clear();   // 계단 머리만 남은 빈 쪽은 안내 한 줄만

        float width = _panel.sizeDelta.x - Pad * 2f;
        float y = 0f;
        foreach (var g in groups)
        {
            y = AddLine(g.Title, HeaderSize, UITheme.Gold, 0f, y, width, FontStyles.Bold);
            if (!string.IsNullOrEmpty(g.Note)) y = AddLine(g.Note, LineSize, UITheme.Mute, 0f, y + LineGap, width, FontStyles.Normal);
            foreach (var r in g.Rows)
            {
                y += RowGap;
                var gc = GradeColor[Mathf.Clamp((int)r.Grade, 0, 3)];
                y = AddLine($"{r.Name}  <size=80%><color=#{ColorUtility.ToHtmlStringRGB(gc)}>{RelicMemoryOdds.Label(r.Grade)}</color></size>",
                            NameSize, UITheme.Ink, 18f, y, width - 18f, FontStyles.Bold);
                for (int n = 0; n < 3; n++)
                {
                    if (string.IsNullOrEmpty(r.Lines[n])) continue;
                    bool lit = (int)r.Grade > n;
                    string mark = lit ? ((char)('①' + n)).ToString() : "◇";
                    y = AddLine($"{mark} {r.Lines[n]}", LineSize, lit ? LineLit : LineLocked, 36f, y + LineGap, width - 36f, FontStyles.Normal);
                }
                if (r.RuneNote != null)
                    y = AddLine(r.RuneNote, LineSize, r.RuneAwake ? AwakeColor : LineLocked, 36f, y + LineGap, width - 36f, FontStyles.Normal);
            }
            y += GroupGap;
        }
        _content.sizeDelta = new Vector2(0f, y + 10f);
    }

    private static bool HasRows(List<Group> groups)
    {
        foreach (var g in groups) if (g.Rows.Count > 0) return true;
        return false;
    }

    /// <summary>한 줄(넘치면 감김)을 y에 놓고 다음 y를 돌려준다.</summary>
    private float AddLine(string text, float size, Color color, float x, float y, float width, FontStyles style)
    {
        var t = NewText("Line", _content, text, size, color, TextAlignmentOptions.TopLeft);
        t.fontStyle = style;
        t.textWrappingMode = TextWrappingModes.Normal;
        t.ForceMeshUpdate();
        float h = Mathf.Max(size * 1.25f, Mathf.Ceil(t.GetPreferredValues(text, width, 0f).y));
        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(width, h);
        return y + h;
    }

    // ── 도우미 ───────────────────────────────────────────────

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, anchor.y);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static Image NewImage(string name, Transform parent, Color color)
    {
        var img = NewRect(name, parent).gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = true;
        return img;
    }

    private static TMP_Text NewText(string name, Transform parent, string text, float size, Color color, TextAlignmentOptions align)
    {
        var tmp = NewRect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        var font = TMP_Settings.defaultFontAsset;
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.alignment = align;
        tmp.raycastTarget = false;
        UITheme.StyleText(tmp, color);
        return tmp;
    }
}
