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
    private const float CrestSize  = 40f;   // 제목 양옆 유물 문장
    private const float GroupGap = 22f, RowGap = 12f, LineGap = 3f;
    // 10-13 Artsystack — 묶음 머리 문양 24 · 밑줄(selected_line 비율 320:46) · 묶음 사이 구분선(Line_1)
    private const float GlyphSize = 24f, GlyphGap = 8f;
    private const float UnderlineW = 104f, UnderlineH = 15f;
    private const float DividerH = 8f;

    private static readonly Color LineLit    = UITheme.Ink;
    private static readonly Color LineLocked = new(0.62f, 0.60f, 0.64f, 0.55f);
    private static readonly Color AwakeColor = new(0.55f, 0.85f, 0.55f, 1f);
    // 단계 표 글자색(리치 텍스트) — 지금 단계 금 · 닿은 단계 잉크 · 아직 흐림
    private static readonly string GoldHex   = "#" + ColorUtility.ToHtmlStringRGB(UITheme.Gold);
    private static readonly string LitHex    = "#" + ColorUtility.ToHtmlStringRGB(UITheme.Ink);
    private static readonly string LockedHex = "#" + ColorUtility.ToHtmlStringRGBA(LineLocked);
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
        public string Anchor;   // 자리 키(문양) — 계단 머리 · 반응은 null/문양 없음
        public string Title;
        public string Note;
        public readonly List<Row> Rows = new();
    }

    // ── Private ──────────────────────────────────────────────
    private RectTransform _panel;
    private RectTransform _content;
    private ScrollRect    _scroll;
    private TMP_Text      _title;
    private Image         _crestL;
    private Image         _crestR;
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
        ApplyCrest(AppBootstrapper.Instance?.Loadout?.Relic);
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
        var evolution = EvolutionGroup(loadout);   // 10-22 E1 B6 — 맨 위 진화 진행(봉인기엔 없다)
        if (evolution != null) groups.Add(evolution);
        groups.Add(BaseRules(gawain, player));   // 10-12 4b 요청 6 — 맨 위 「기본 규칙」
        string[] order = gawain
            ? new[] { RelicPartAnchor.Dawn, RelicPartAnchor.Noon, RelicPartAnchor.Dusk, RelicPartAnchor.DawnNoon, RelicPartAnchor.NoonDusk, RelicPartAnchor.Reaction }
            : new[] { RelicPartAnchor.R10, RelicPartAnchor.R20, RelicPartAnchor.R30, RelicPartAnchor.R40, RelicPartAnchor.Frenzy, RelicPartAnchor.Judgment, RelicPartAnchor.Reaction };

        if (!gawain)
        {
            // 10-22 E1 D7 — 「10부터 이어진 계단」 → 「광기 칸 조각 수」(순서 · 빈틈 상관없음 · 효과 수치 그대로)
            int count = RelicResonance.LancelotCount(loadout);
            int tier  = RelicResonance.LancelotTier(count);
            groups.Add(new Group
            {
                Title = $"광기 10~40 조각 {count}개",
                Note  = "꼬리표가 광기 10 · 20 · 30 · 40인 조각(메아리 포함)이 2 · 3 · 4개면 한 단계씩 세진다\n"
                      + TierTable(tier, (2, RelicResonance.LadderText(2)), (3, RelicResonance.LadderText(3)), (4, RelicResonance.LadderText(4))),
            });
        }

        foreach (var anchor in order)
        {
            var g = new Group { Anchor = anchor, Title = RelicPartAnchor.Label(anchor) };
            foreach (var e in owned) if (e.anchor == anchor) g.Rows.Add(MakeRow(e, loadout, runes));
            int echo = loadout.GetRelicEcho(anchor);
            if (g.Rows.Count == 0 && echo <= 0) continue;
            if (gawain && (anchor == RelicPartAnchor.Dawn || anchor == RelicPartAnchor.Noon || anchor == RelicPartAnchor.Dusk))
            {
                int count = RelicResonance.GawainCount(loadout, anchor);
                int tier  = RelicResonance.GawainTier(count);
                string tierText = RelicResonance.GawainTierText(anchor, tier);
                g.Title += $"  ·  {count}개" + (tier >= 2 ? $"  ·  {tier}단계" : string.Empty);
                // 단계 효과는 누적이다 — 2단계 · 4단계를 함께 보이고 지금 단계만 금(10-12 4b 요청 6, 예전엔 지금 단계 글만 보여 2단계가 사라진 듯했다)
                g.Note = TierTable(tier, (2, RelicResonance.GawainTierText(anchor, 2)), (4, RelicResonance.GawainTierText(anchor, 4)));
            }
            if (echo > 0) g.Title += $"  ·  메아리 {echo}";
            if (anchor == RelicPartAnchor.Reaction) g.Note = CrossLightNote(gawain, player);
            groups.Add(g);
        }
        return groups;
    }

    /// <summary>맨 위 「기본 규칙」 — 조각 없이도 돌아가는 유물 규칙과 지금 상한(10-12 4b 요청 6).</summary>
    private static Group BaseRules(bool gawain, PlayerController player)
    {
        if (gawain)
        {
            int cap = GawainMemoryHub.SunmarkCapOf(player);
            return new Group
            {
                Title = "기본 규칙",
                Note  = $"태양흔 — 여명에 적을 맞힐 때마다 그 적에게 1개(같은 적은 0.5초에 한 번) · 최대 {cap}"
                      + (cap < GawainMemoryHub.DawnTier2Cap ? $"(여명 조각 2개면 {GawainMemoryHub.DawnTier2Cap})" : string.Empty)
                      + $"\n태양흔이 터지면 — 1개마다 반경 {GawainMemoryHub.BloomRadius:0.#} m 폭발(공격력 {GawainMemoryHub.BloomAtkRatio * 100f:0}%)"
                      // 10-22 E1 D8 — 「정오 수확」은 개화와 같은 일의 다른 이름이었다 → 문장으로
                      + $"\n정오가 열리면 — {GawainMemoryHub.HarvestRadius:0} m 안 적의 태양흔이 모두 터진다 · 이때부터 Q(낙일)",
            };
        }
        int brand = LancelotMemoryHub.BrandCapOf(player);
        return new Group
        {
            Title = "기본 규칙",
            Note  = $"낙인 — 광기 20부터 적을 맞힐 때마다 그 적에게 1개 · 최대 {brand}(광기의 눈 조각: {LancelotMemoryHub.EyeBrandCap} · {LancelotMemoryHub.EyeBrandCapMax})"
                  + $"\nQ(심판) 마지막 일격 — 맞은 적의 낙인을 터뜨린다: 낙인 1개마다 X자 베기(공격력 {LancelotMemoryHub.TearAtkRatio * 100f:0}%)"
                  + $"\n원한 — 최대치를 넘친 낙인이 모인다 · 최대 {LancelotMemoryHub.GrudgeCap}"
                  // 10-22 E1 D8 — 「참격파」 명사를 걷어 문장으로(원한의 쓰임새)
                  + $"\n원한 쓰기 — Q 마지막 일격 뒤 원한 1개마다 참격이 1줄 나간다({LancelotMemoryHub.WaveLength:0} m · 공격력 {LancelotMemoryHub.WaveAtkRatio * 100f:0}%)",
        };
    }

    /// <summary>
    /// 맨 위 「유물 진화」 — 진행 한 줄(<see cref="RelicEvolution.ProgressLine"/>) + 조건 한 줄 · 진화했으면 그 이름과 효과(10-22 E1 B6).
    /// 봉인기엔 null(진화를 말하지 않는다 — D2).
    /// </summary>
    private static Group EvolutionGroup(PlayerLoadout loadout)
    {
        string progress = RelicEvolution.ProgressLine(loadout);
        if (string.IsNullOrEmpty(progress)) return null;
        var def = RelicEvolution.ById(loadout.RelicEvolutionId);
        return def != null
            ? new Group { Title = $"유물 진화  ·  {def.Name}", Note = def.Desc }
            : new Group
            {
                Title = $"유물 진화  ·  {progress}",
                Note  = "보스를 쓰러뜨릴 때마다 진화 카드가 나온다(고르지 않으면 다음 보스에 다시) · 방향은 빌드에 맞춰 저절로 정해진다",
            };
    }

    /// <summary>단계 누적 표 — 닿은 단계는 밝게, 지금 단계는 금, 아직은 흐리게 「◇」.</summary>
    private static string TierTable(int current, params (int tier, string text)[] rows)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var (tier, text) in rows)
        {
            if (string.IsNullOrEmpty(text)) continue;
            if (sb.Length > 0) sb.Append('\n');
            string col = tier == current ? GoldHex : tier < current ? LitHex : LockedHex;
            sb.Append("<color=").Append(col).Append('>').Append(tier <= current ? "◆ " : "◇ ").Append(tier).Append("단계 — ").Append(text).Append("</color>");
        }
        return sb.ToString();
    }

    /// <summary>엇갈린 빛(두 유물 공통 · 10-12 A3) — 조건 한 줄 + 지금 대기 · 켜짐.</summary>
    private static string CrossLightNote(bool gawain, PlayerController player)
    {
        bool on = gawain ? GawainMemoryHub.Of(player)?.HasCrossLight == true : LancelotMemoryHub.Of(player)?.HasCrossLight == true;
        string next = gawain ? "다음에 정오가 열려 터질 때 태양흔을" : "다음에 낙인이 터질 때 낙인을";
        return $"엇갈린 빛 — 반응 조각을 2개 이상 가진 채 서로 다른 반응 둘을 10초 안에 일으키면 {next} 그대로 남긴다  "
             + (on ? $"<color={GoldHex}>◆ 켜짐</color>" : $"<color={LockedHex}>◇ 대기</color>");
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
        "fire" => "불", "ice" => "물", "electric" => "전기", "grass" => "풀", "light" => "빛", "dark" => "어둠", _ => el,
    };

    // ── View ─────────────────────────────────────────────────

    private void Build()
    {
        var panel = NewImage("Panel", transform, UITheme.Window);
        _panel = panel.rectTransform;
        _panel.anchorMin = _panel.anchorMax = _panel.pivot = new Vector2(0.5f, 0.5f);
        _panel.sizeDelta = new Vector2(PanelW, PanelH);
        UITheme.StylePanel(panel, UITheme.Window);
        ShopUIStyle.Skin(panel, UISkin.RelicPartDraft?.background, sliced: true);   // 10-13 Artsystack 남색 판(되살아난 기억 · 제단과 같은 판)

        _title = NewText("Title", _panel, "되찾은 기억", 32f, UITheme.Gold, TextAlignmentOptions.Center);
        Place(_title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(PanelW - 80f, 44f));
        _crestL = NewCrest("CrestL");   // 유물 문장(10-13 힉스필드) — 제목 양옆, Open에서 글 폭에 맞춰 놓는다
        _crestR = NewCrest("CrestR");

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
        var skin      = UISkin.RelicPartDraft;
        var underline = UISpriteMaster.Neutral(skin?.GroupUnderline);
        var divider   = UISpriteMaster.Neutral(skin?.GroupDivider);
        for (int gi = 0; gi < groups.Count; gi++)
        {
            var g = groups[gi];
            // 머리 — 자리 문양(있으면) + 이름 + 밑줄(10-13 Artsystack)
            var glyph = skin?.AnchorArt(g.Anchor);
            float hx = 0f;
            if (glyph != null)
            {
                AddSprite("Glyph", glyph, 0f, y + (HeaderSize * 1.25f - GlyphSize) * 0.5f, GlyphSize, GlyphSize, UITheme.Gold, true);
                hx = GlyphSize + GlyphGap;
            }
            y = AddLine(g.Title, HeaderSize, UITheme.Gold, hx, y, width - hx, FontStyles.Bold);
            if (underline != null)
            {
                AddSprite("Underline", underline, hx, y + 1f, UnderlineW, UnderlineH, UITheme.Thin.LineActive, false);
                y += UnderlineH + 2f;
            }
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
                    // 되찾은 줄만 수치 Noto 500(10-15 설명 글 강조) — 잠긴 줄은 흐린 채
                    y = AddLine($"{mark} {(lit ? UIKeywordInk.Values(r.Lines[n]) : r.Lines[n])}", LineSize, lit ? LineLit : LineLocked, 36f, y + LineGap, width - 36f, FontStyles.Normal);
                }
                if (r.RuneNote != null)
                    y = AddLine(r.RuneNote, LineSize, r.RuneAwake ? AwakeColor : LineLocked, 36f, y + LineGap, width - 36f, FontStyles.Normal);
            }
            if (divider != null && gi < groups.Count - 1)
            {
                AddSprite("Divider", divider, 0f, y + (GroupGap - DividerH) * 0.5f, width, DividerH, UITheme.Thin.InkA(0.35f), false);
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

    /// <summary>목록에 그림 한 장(왼쪽 위 기준 · 글자와 같은 좌표계). 글리프는 비율 유지.</summary>
    private void AddSprite(string name, Sprite sprite, float x, float y, float w, float h, Color color, bool keepAspect)
    {
        var img = NewImage(name, _content, color);
        img.sprite = sprite;
        img.preserveAspect = keepAspect;
        img.raycastTarget  = false;
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
    }

    // ── 도우미 ───────────────────────────────────────────────

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private Image NewCrest(string name)
    {
        var img = NewImage(name, _panel, Color.white);
        img.raycastTarget  = false;
        img.preserveAspect = true;
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(CrestSize, CrestSize);
        img.gameObject.SetActive(false);
        return img;
    }

    /// <summary>제목 양옆에 유물 문장을 유물 색으로. 문장이 없는 유물은 숨긴다.</summary>
    private void ApplyCrest(RelicClassSO relic)
    {
        var crest = relic != null ? relic.Crest : null;
        bool on = crest != null;
        _crestL.gameObject.SetActive(on);
        _crestR.gameObject.SetActive(on);
        if (!on) return;
        var color = relic.ThemeColor(UITheme.Gold);
        color.a = 0.9f;
        float half = _title.GetPreferredValues(_title.text).x * 0.5f + CrestSize * 0.5f + 14f;
        float y    = -30f - 22f;   // 제목 칸(위 30 · 높이 44)의 가운데
        _crestL.sprite = crest; _crestL.color = color; _crestL.rectTransform.anchoredPosition = new Vector2(-half, y);
        _crestR.sprite = crest; _crestR.color = color; _crestR.rectTransform.anchoredPosition = new Vector2(half, y);
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
