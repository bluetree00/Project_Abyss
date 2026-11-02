using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 도감(11-02 Q2 사용자 「빌드를 볼 수 있는 도감 팝업 — 모든 룬 정보 · 유물 진화 · 서약 · 지금 활성화한 것 · 써 본 것은 체크」).
/// ESC 메뉴 「도감」 쪽(장비 · 되찾은 기억과 같은 자리 · 같은 정지). 탭 = 룬 · 근접 증강 · 서약 · 유물 기억.
/// 왼쪽 목록(이름 · 등급/갈래 · 「지금」 · 「써 봄」) → 줄을 누르면 오른쪽에 설명. 탭 이름에 「써 본 수 / 전체」.
/// </summary>
public sealed class UI_CodexPage : MonoBehaviour
{
    // ── Constants ─────────────────────────────────────────────
    private const float PanelW = 1400f, PanelH = 820f, ListW = 860f, RowH = 44f;
    private static readonly string[] TabNames = { "룬", "근접 증강", "서약", "유물 기억" };
    private static readonly Color RowIdle = new(1f, 1f, 1f, 0.03f);
    private static readonly Color RowPick = new(1f, 0.82f, 0.28f, 0.14f);

    private struct Entry
    {
        public string Name, Sub, Desc;
        public Sprite Icon;
        public bool Active, Seen;
    }

    // ── Private Fields ────────────────────────────────────────
    private Action _onBack;
    private RectTransform _content;
    private TMP_Text _detailName, _detailSub, _detailBody;
    private readonly TMP_Text[] _tabLabels = new TMP_Text[4];
    private readonly List<Entry> _entries = new();
    private readonly List<Image> _rows = new();
    private HashSet<string> _active = new();
    private int _tab;

    // ── Public Methods ────────────────────────────────────────
    public static UI_CodexPage Create(Transform canvasRoot, Action onBack)
    {
        var go = new GameObject("CodexPage", typeof(RectTransform));
        go.transform.SetParent(canvasRoot, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        var page = go.AddComponent<UI_CodexPage>();
        page._onBack = onBack;
        page.Build();
        go.SetActive(false);
        return page;
    }

    public void Open()
    {
        var run = GameRunBootstrapper.Instance?.Run;
        CodexLog.Sync(run);              // 지금 지닌 것을 「써 봄」에 먼저 더한다
        _active = CodexLog.Active(run);
        gameObject.SetActive(true);
        ShowTab(_tab);
    }

    public void Close() => gameObject.SetActive(false);

    // ── Private Methods ───────────────────────────────────────
    private void Build()
    {
        var root = (RectTransform)transform;
        var panel = (RectTransform)UICodeOverlay.Img(root, "Panel", Vector2.zero, new Vector2(PanelW, PanelH), UITheme.Window).transform;
        UICodeOverlay.Line(panel, new Color(UIPalette.Gold.r, UIPalette.Gold.g, UIPalette.Gold.b, 0.45f));
        UICodeOverlay.Txt(panel, "도감", 34f, new Color(0.96f, 0.90f, 0.72f), new Vector2(0f, PanelH * 0.5f - 46f), 600f);
        UICodeOverlay.Txt(panel, "<color=#FFD147>◆ 지금</color> 이번 런에 켜져 있다   ·   ◇ 써 봄 한 번이라도 지녀 봤다", 17f, UICodeOverlay.Mist,
                          new Vector2(0f, PanelH * 0.5f - 84f), 1000f);

        float tabY = PanelH * 0.5f - 130f;
        for (int i = 0; i < TabNames.Length; i++)
        {
            int idx = i;
            var b = UICodeOverlay.Btn(panel, TabNames[i], new Vector2(-PanelW * 0.5f + 150f + i * 250f, tabY), new Vector2(230f, 44f), 19f, () => ShowTab(idx));
            _tabLabels[i] = b.GetComponentInChildren<TMP_Text>();
        }

        // 목록(스크롤) — 왼쪽
        float listTop = tabY - 40f, listBottom = -PanelH * 0.5f + 80f;
        var view = (RectTransform)UICodeOverlay.Img(panel, "List", new Vector2(-PanelW * 0.5f + 40f + ListW * 0.5f, (listTop + listBottom) * 0.5f),
                                                    new Vector2(ListW, listTop - listBottom), new Color(0f, 0f, 0f, 0.25f)).transform;
        view.gameObject.AddComponent<RectMask2D>();
        var contentGo = new GameObject("Content", typeof(RectTransform));
        _content = (RectTransform)contentGo.transform;
        _content.SetParent(view, false);
        _content.anchorMin = new Vector2(0f, 1f); _content.anchorMax = new Vector2(1f, 1f); _content.pivot = new Vector2(0.5f, 1f);
        _content.sizeDelta = Vector2.zero;
        var scroll = view.gameObject.AddComponent<ScrollRect>();
        scroll.content = _content; scroll.viewport = view; scroll.horizontal = false; scroll.scrollSensitivity = 30f;
        scroll.movementType = ScrollRect.MovementType.Clamped;

        // 설명 — 오른쪽
        float dx = -PanelW * 0.5f + 40f + ListW + 30f + (PanelW - ListW - 110f) * 0.5f, dw = PanelW - ListW - 110f;
        _detailName = UICodeOverlay.Txt(panel, "", 26f, new Color(0.96f, 0.90f, 0.72f), new Vector2(dx, listTop - 30f), dw);
        _detailSub  = UICodeOverlay.Txt(panel, "", 17f, UICodeOverlay.Mist, new Vector2(dx, listTop - 70f), dw);
        _detailBody = UICodeOverlay.Txt(panel, "줄을 누르면 설명이 여기에 뜬다", 19f, UICodeOverlay.Ink, new Vector2(dx, listTop - 250f), dw,
                                        align: TextAlignmentOptions.TopLeft);
        _detailBody.rectTransform.sizeDelta = new Vector2(dw, 300f);

        UICodeOverlay.Btn(panel, "돌아가기", new Vector2(0f, -PanelH * 0.5f + 40f), new Vector2(200f, 46f), 20f, () => _onBack?.Invoke());
    }

    private void ShowTab(int tab)
    {
        _tab = tab;
        for (int i = 0; i < _tabLabels.Length; i++)
        {
            int seen = 0, total = 0;
            foreach (var e in Collect(i)) { total++; if (e.Seen) seen++; }
            if (_tabLabels[i] != null)
                _tabLabels[i].text = (i == tab ? $"<color=#FFD147>{Loc.T(TabNames[i])}</color>" : Loc.T(TabNames[i])) + $"  <size=80%>{seen}/{total}</size>";
        }
        _entries.Clear();
        _entries.AddRange(Collect(tab));
        for (int i = _content.childCount - 1; i >= 0; i--) Destroy(_content.GetChild(i).gameObject);
        _rows.Clear();
        _content.sizeDelta = new Vector2(0f, _entries.Count * RowH + 8f);
        for (int i = 0; i < _entries.Count; i++) _rows.Add(Row(i, _entries[i]));
        _content.anchoredPosition = Vector2.zero;
        _detailName.text = ""; _detailSub.text = "";
        _detailBody.text = Loc.T(_entries.Count == 0 ? "아직 데이터를 읽지 못했다" : "줄을 누르면 설명이 여기에 뜬다");
    }

    private Image Row(int i, Entry e)
    {
        var bg = UICodeOverlay.Img(_content, "Row", Vector2.zero, Vector2.zero, RowIdle);
        var rt = bg.rectTransform;
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(6f, -(i + 1) * RowH + 2f); rt.offsetMax = new Vector2(-6f, -i * RowH - 2f);
        var btn = bg.gameObject.AddComponent<Button>();
        btn.targetGraphic = bg;
        btn.onClick.AddListener(() => Pick(i));

        if (e.Icon != null)
        {
            var icon = UICodeOverlay.Img(rt, "Icon", new Vector2(-ListW * 0.5f + 34f, 0f), new Vector2(32f, 32f), Color.white);
            icon.sprite = e.Icon; icon.preserveAspect = true; icon.raycastTarget = false;
        }
        var name = UICodeOverlay.Txt(rt, e.Seen || e.Active ? e.Name : $"<color=#8A8A92>{e.Name}</color>", 19f, UICodeOverlay.Ink,
                                     new Vector2(-ListW * 0.5f + 64f + 170f, 0f), 340f, align: TextAlignmentOptions.MidlineLeft);
        name.enableWordWrapping = false; name.overflowMode = TextOverflowModes.Ellipsis;
        UICodeOverlay.Txt(rt, e.Sub, 15f, UICodeOverlay.Mist, new Vector2(110f, 0f), 260f, align: TextAlignmentOptions.MidlineLeft).enableWordWrapping = false;
        string mark = e.Active ? $"<color=#FFD147>{Loc.T("◆ 지금")}</color>" : e.Seen ? Loc.T("◇ 써 봄") : "<color=#5C5C66>—</color>";
        UICodeOverlay.Txt(rt, mark, 16f, UICodeOverlay.Mist, new Vector2(ListW * 0.5f - 70f, 0f), 120f, align: TextAlignmentOptions.MidlineRight);
        return bg;
    }

    private void Pick(int i)
    {
        if (i < 0 || i >= _entries.Count) return;
        Managers.Sound?.PlayUiEvent(SoundEvent.UiClick);
        for (int k = 0; k < _rows.Count; k++) _rows[k].color = k == i ? RowPick : RowIdle;
        var e = _entries[i];
        _detailName.text = e.Name;
        _detailSub.text  = e.Sub + (e.Active ? $"   <color=#FFD147>{Loc.T("◆ 지금")}</color>" : e.Seen ? "   " + Loc.T("◇ 써 봄") : "");
        _detailBody.text = string.IsNullOrEmpty(e.Desc) ? Loc.T("설명 없음") : UIGlossary.Append(Loc.T(e.Desc));
    }

    // ── 데이터 ────────────────────────────────────────────────
    private IEnumerable<Entry> Collect(int tab)
    {
        switch (tab)
        {
            case 0:
                var runes = new List<ItemSO>(ItemSORegistry.All);
                runes.Sort((a, b) => a.rarity != b.rarity ? a.rarity.CompareTo(b.rarity) : string.CompareOrdinal(a.displayName, b.displayName));
                foreach (var so in runes)
                {
                    if (so == null || string.IsNullOrEmpty(so.itemId)) continue;
                    var d = RuntimeItemData.FromSO(so);
                    var el = ElementDef.GetById(d?.element);
                    yield return new Entry
                    {
                        Name = Loc.T(so.displayName), Icon = RuneIcon(so, d),
                        Sub  = so.rarity + (el != null && so.rarity >= ItemRarity.Epic ? $" · <color=#{ColorUtility.ToHtmlStringRGB(el.Color)}>{el.Name}</color>" : ""),
                        Desc = RuneDesc(d),
                        Active = _active.Contains("r:" + so.itemId), Seen = CodexLog.IsSeen('r', so.itemId),
                    };
                }
                break;
            case 1:
                foreach (var def in MeleeAugmentCatalog.All)
                    yield return new Entry
                    {
                        Name = Loc.T(def.Name), Sub = $"{MeleeAugmentDef.TierName(def.Tier)} · {Loc.T(WeaponName(def.Weapon))}", Desc = def.Text(1),
                        Active = _active.Contains("a:" + def.Id), Seen = CodexLog.IsSeen('a', def.Id),
                    };
                break;
            case 2:
                foreach (var id in CovenantPalette.CauseIds)
                    if (CovenantPalette.TryGetCause(id, out var c))
                        yield return new Entry { Name = Loc.T(c.name), Sub = Loc.T("원인 — 언제"), Desc = c.desc, Active = _active.Contains("c:" + id), Seen = CodexLog.IsSeen('c', id) };
                foreach (var id in CovenantPalette.EffectIds)
                    if (CovenantPalette.TryGetEffect(id, out var f))
                        yield return new Entry { Name = Loc.T(f.name), Sub = Loc.T("결과 — 무엇이"), Desc = f.desc, Active = _active.Contains("e:" + id), Seen = CodexLog.IsSeen('e', id) };
                break;
            default:
                foreach (var relic in new[] { "gawain", "lancelot" })
                {
                    var parts = Managers.RelicParts?.GetByRelic(relic);
                    if (parts == null) continue;
                    foreach (var p in parts)
                    {
                        if (p == null || string.IsNullOrEmpty(p.effect_key)) continue;
                        string desc = string.Join("\n", new[] { p.line1, p.line2, p.line3 }).Trim('\n');
                        yield return new Entry
                        {
                            Name = Loc.T(p.part_name), Sub = Loc.T(relic == "gawain" ? "가웨인" : "랜슬롯") + " · " + Loc.T("기억 조각"),
                            Desc = string.IsNullOrEmpty(desc) ? p.description : desc,
                            Active = _active.Contains("p:" + p.effect_key), Seen = CodexLog.IsSeen('p', p.effect_key),
                        };
                    }
                }
                foreach (var v in RelicEvolution.All)
                    yield return new Entry
                    {
                        Name = Loc.T(v.Name), Sub = Loc.T(v.RelicId == "gawain" ? "가웨인" : "랜슬롯") + " · " + Loc.T("진화"), Desc = v.Desc,
                        Active = _active.Contains("v:" + v.Id), Seen = CodexLog.IsSeen('v', v.Id),
                    };
                break;
        }
    }

    /// <summary>룬 그림 — SO 아이콘이 비어 있으면(룬 대부분) 룬판과 같은 효과 문양, 그것도 없으면 등급 룬 그림.</summary>
    private static Sprite RuneIcon(ItemSO so, RuntimeItemData d)
    {
        if (so.icon != null) return so.icon;
        var byEffect = d != null ? RuneArt.GetIconByEffect(RuneArt.EffectTypeOf(d)) : null;
        return byEffect != null ? byEffect : RuneArt.GetArt(so.rarity);
    }

    /// <summary>룬 효과 줄 — CSV 원문이 있으면 그것, 없으면 「조건 이름 값」 조립(룬 카드와 같은 출처).</summary>
    internal static string RuneDesc(RuntimeItemData d)   // 멀린 룬 보관 창도 쓴다(11-02)
    {
        if (d?.effects == null) return null;
        var sb = new System.Text.StringBuilder();
        foreach (var slot in d.effects)
        {
            var e = EffectDescriptionFormatter.Describe(slot);
            string line = !string.IsNullOrEmpty(e.Description) ? e.Description
                        : $"{(string.IsNullOrEmpty(e.TriggerText) ? "" : e.TriggerText + " ")}{e.Label} {e.ValueText}".Trim();
            if (string.IsNullOrEmpty(line)) continue;
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(line);
        }
        return sb.ToString();
    }

    private static string WeaponName(MeleeWeaponKind k) => k switch
    {
        MeleeWeaponKind.Nameless   => "무형검",
        MeleeWeaponKind.Katana     => "카타나",
        MeleeWeaponKind.Greatsword => "대검",
        _                          => "공용",
    };
}
