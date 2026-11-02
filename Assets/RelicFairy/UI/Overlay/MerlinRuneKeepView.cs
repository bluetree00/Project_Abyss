using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 멀린 룬 보관 고르기(11-02 Q26 · 11-02 밤 사용자 「새로운 팝업으로 얻은 룬들을 등급으로 정렬해서 보기 좋게」) — 사망 → 결과창 → 멀린 부활 연출 뒤,
/// 이번 런에서 얻은 룬(판 · 보관함 — 멀린이 돌려준 룬은 빠진다) 중 하나를 맡긴다.
/// 왼쪽 = 등급별 칸(전설 → 영웅 → 희귀 → 일반 · 칸 안은 속성 · 이름 순) · 룬 고르기 창과 같은 등급 카드 틀(힉스필드) ·
/// 오른쪽 = 고른 룬 크게(그림 · 이름 · 등급 · 속성 · 효과) + [이 룬을 맡긴다] · [맡기지 않는다]. 처음엔 가장 높은 등급 첫 장이 골라져 있다.
/// 카드는 등급 순서대로 차례로 맺힌다 · ←/→로 고르고 Enter로 맡긴다. 반환 = 고른 룬(맡기지 않으면 null). 저장은 부르는 쪽이 <see cref="MerlinRuneKeep.Keep"/>.
/// </summary>
public static class MerlinRuneKeepView
{
    // ── Constants ─────────────────────────────────────────────
    private const int   PerRow = 6;
    private const float CardW = 128f, CardH = 164f, Gap = 14f, HeadH = 44f, SectionGap = 18f;
    private const float ListW = 940f, ListH = 700f, ListX = -250f, DetailW = 440f, DetailX = 470f, PanelY = -20f;
    private const float CardFrameScale = 6f, BigFrameScale = 3.6f;   // 힉스필드 등급 틀 — 그림 px ÷ 이 값 = 테 두께(작은 카드는 얇게)
    private const float FadeIn = 0.35f, CardIn = 0.22f, CardStagger = 0.025f, CardStaggerMax = 0.7f, PickPop = 0.16f;
    private static readonly Color Lilac   = new(0.78f, 0.74f, 0.92f, 1f);
    private static readonly Color PanelC  = new(0.05f, 0.05f, 0.07f, 0.92f);
    private static readonly Color CardBg  = new(0.07f, 0.07f, 0.09f, 0.96f);
    private static readonly Color PickC   = new(1f, 0.82f, 0.30f, 1f);
    private static readonly ItemRarity[] Order = { ItemRarity.Legendary, ItemRarity.Epic, ItemRarity.Rare, ItemRarity.Common };

    private struct Card { public RectTransform Rt; public CanvasGroup Group; public Image Pick; public float At, Y; }

    /// <summary>고르기 창이 열려 있나 — ESC 처리기가 비킨다(11-02 Q32).</summary>
    public static bool IsOpen { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => IsOpen = false;

    // ── Public Methods ────────────────────────────────────────
    public static async UniTask<RuntimeItemData> PickAsync(IReadOnlyList<RuntimeItemData> runes, CancellationToken ct)
    {
        if (runes == null || runes.Count == 0) return null;
        var sorted = Sort(runes);

        var (go, root, group) = UICodeOverlay.Create("@MerlinRuneKeep", UISortingOrder.RunBoundary + 1);
        IsOpen = true;
        int picked = 0;
        bool done = false, keep = false;
        var cards = new List<Card>(sorted.Count);
        try
        {
            UICodeOverlay.Img(root, "BG", Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.94f), stretch: true);
            string merlin = StoryProgress.IsMerlinNamed ? Loc.T("멀린") : "???";
            UICodeOverlay.Txt(root, $"{merlin} —  {Loc.T("「네가 얻은 룬 하나는 내가 맡아 둘게. 다음 길에서 돌려줄게.」")}", 28f, Lilac, new Vector2(0f, 430f), 1500f, FontStyles.Italic);
            UICodeOverlay.Txt(root, "고른 룬은 다음 런을 시작할 때 보관함으로 돌아온다 · 돌려받은 룬은 다시 맡길 수 없다", 19f, UICodeOverlay.Mist, new Vector2(0f, 390f), 1300f);

            // 왼쪽 — 등급별 칸(스크롤)
            var list = (RectTransform)UICodeOverlay.Img(root, "List", new Vector2(ListX, PanelY), new Vector2(ListW, ListH), PanelC).transform;
            UICodeOverlay.Line(list, new Color(UIPalette.Gold.r, UIPalette.Gold.g, UIPalette.Gold.b, 0.35f));
            var content = BuildScroll(list);
            float y = -20f;
            int index = 0;
            foreach (var rarity in Order)
            {
                int count = 0;
                foreach (var r in sorted) if (r.rarity == rarity) count++;
                if (count == 0) continue;
                SectionHead(content, rarity, count, y);
                y -= HeadH;
                int col = 0;
                for (int i = 0; i < sorted.Count; i++)
                {
                    if (sorted[i].rarity != rarity) continue;
                    float x = -((PerRow - 1) * (CardW + Gap)) * 0.5f + col * (CardW + Gap);
                    int captured = index;
                    float at = 0.1f + Mathf.Min(CardStaggerMax, index * CardStagger);
                    cards.Add(BuildCard(content, sorted[i], new Vector2(x, y - CardH * 0.5f), at, () => { picked = captured; }));
                    index++;
                    if (++col == PerRow) { col = 0; y -= CardH + Gap; }
                }
                if (col != 0) y -= CardH + Gap;
                y -= SectionGap;
            }
            content.sizeDelta = new Vector2(0f, -y + 10f);

            // 오른쪽 — 고른 룬 크게
            var detail = (RectTransform)UICodeOverlay.Img(root, "Detail", new Vector2(DetailX, PanelY), new Vector2(DetailW, ListH), PanelC).transform;
            UICodeOverlay.Line(detail, new Color(UIPalette.Gold.r, UIPalette.Gold.g, UIPalette.Gold.b, 0.35f));
            var detailGroup = detail.gameObject.AddComponent<CanvasGroup>();
            var bigFrame = UICodeOverlay.Img(detail, "BigCard", new Vector2(0f, 190f), new Vector2(230f, 270f), CardBg);
            var bigIcon  = UICodeOverlay.Img((RectTransform)bigFrame.transform, "Icon", new Vector2(0f, 10f), new Vector2(150f, 150f), Color.white);
            bigIcon.preserveAspect = true;
            var name   = UICodeOverlay.Txt(detail, "", 28f, PickC, new Vector2(0f, 20f), DetailW - 40f);
            var sub    = UICodeOverlay.Txt(detail, "", 17f, UICodeOverlay.Mist, new Vector2(0f, -18f), DetailW - 40f);
            var rule   = UICodeOverlay.Img(detail, "Rule", new Vector2(0f, -46f), new Vector2(DetailW - 80f, 1f), new Color(1f, 1f, 1f, 0.12f));
            rule.raycastTarget = false;
            var desc   = UICodeOverlay.Txt(detail, "", 18f, UICodeOverlay.Ink, new Vector2(0f, -140f), DetailW - 60f, align: TextAlignmentOptions.Top);
            desc.rectTransform.sizeDelta = new Vector2(DetailW - 60f, 170f);
            desc.enableAutoSizing = true;
            desc.fontSizeMin = 15f;
            desc.fontSizeMax = 18f;
            UICodeOverlay.Btn(detail, "이 룬을 맡긴다", new Vector2(0f, -262f), new Vector2(300f, 54f), 23f, () => { keep = true; done = true; });
            UICodeOverlay.Btn(detail, "맡기지 않는다", new Vector2(0f, -318f), new Vector2(220f, 40f), 18f, () => { keep = false; done = true; }, quiet: true);
            UICodeOverlay.Txt(root, "카드를 눌러 고른다  ·  ← → 고르기  ·  Enter 맡기기", 15f, UICodeOverlay.Mist, new Vector2(0f, PanelY - ListH * 0.5f - 30f), 900f);

            float t0 = Time.unscaledTime, shownAt = 0f;
            int shown = -1;
            while (!done)
            {
                float now = Time.unscaledTime;
                group.alpha = Mathf.Clamp01((now - t0) / FadeIn);

                if (Input.GetKeyDown(KeyCode.RightArrow)) picked = Mathf.Min(cards.Count - 1, picked + 1);
                if (Input.GetKeyDown(KeyCode.LeftArrow))  picked = Mathf.Max(0, picked - 1);
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) { keep = true; done = true; }

                if (picked != shown)
                {
                    if (shown >= 0) Managers.Sound?.PlayUiEvent(SoundEvent.UiSelect);
                    shown = picked;
                    shownAt = now;
                    ShowDetail(sorted[picked], bigFrame, bigIcon, name, sub, desc);
                }
                // 카드 — 등급 순서대로 맺히고 · 고른 카드는 금 테 · 살짝 커진다
                for (int i = 0; i < cards.Count; i++)
                {
                    var c = cards[i];
                    float k = Mathf.Clamp01((now - t0 - c.At) / CardIn);
                    float e = 1f - (1f - k) * (1f - k);
                    c.Group.alpha = k;
                    c.Rt.anchoredPosition = new Vector2(c.Rt.anchoredPosition.x, c.Y - (1f - e) * 12f);
                    bool on = i == picked;
                    c.Pick.enabled = on;
                    c.Rt.localScale = Vector3.one * Mathf.MoveTowards(c.Rt.localScale.x, on ? 1.06f : 1f, Time.unscaledDeltaTime * 0.8f);
                }
                // 오른쪽 — 바뀔 때 살짝 옅었다 차오른다
                float d = Mathf.Clamp01((now - shownAt) / PickPop);
                detailGroup.alpha = Mathf.Lerp(0.35f, 1f, d);
                bigIcon.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.92f, 1f, 1f - (1f - d) * (1f - d));
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            for (float t = 0f; t < 0.25f; t += Time.unscaledDeltaTime) { group.alpha = 1f - t / 0.25f; await UniTask.Yield(PlayerLoopTiming.Update, ct); }
            return keep ? sorted[picked] : null;
        }
        catch (System.OperationCanceledException) { return null; }
        finally
        {
            IsOpen = false;
            if (go != null) Object.Destroy(go);
        }
    }

    // ── Private Methods ───────────────────────────────────────
    /// <summary>등급 높은 순 → 속성 순 → 이름 순.</summary>
    private static List<RuntimeItemData> Sort(IReadOnlyList<RuntimeItemData> runes)
    {
        var list = new List<RuntimeItemData>(runes.Count);
        foreach (var r in runes) if (r != null) list.Add(r);
        list.Sort((a, b) =>
        {
            int c = ((int)b.rarity).CompareTo((int)a.rarity);
            if (c != 0) return c;
            c = ElementIndex(a.element).CompareTo(ElementIndex(b.element));
            return c != 0 ? c : string.CompareOrdinal(a.displayName ?? "", b.displayName ?? "");
        });
        return list;
    }

    private static int ElementIndex(string id)
    {
        var order = ElementDef.Order;
        for (int i = 0; i < order.Count; i++) if (order[i] == id) return i;
        return order.Count;
    }

    private static RectTransform BuildScroll(RectTransform list)
    {
        var view = (RectTransform)UICodeOverlay.Img(list, "View", Vector2.zero, new Vector2(ListW - 24f, ListH - 24f), new Color(0f, 0f, 0f, 0f)).transform;
        view.gameObject.AddComponent<RectMask2D>();
        var content = (RectTransform)new GameObject("Content", typeof(RectTransform)).transform;
        content.SetParent(view, false);
        content.anchorMin = new Vector2(0f, 1f); content.anchorMax = new Vector2(1f, 1f); content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = Vector2.zero;
        var scroll = view.gameObject.AddComponent<ScrollRect>();
        scroll.content = content; scroll.viewport = view; scroll.horizontal = false; scroll.scrollSensitivity = 30f;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        return content;
    }

    /// <summary>등급 머리 — 「■ Legendary  2」 등급색 · 오른쪽으로 옅은 선.</summary>
    private static void SectionHead(RectTransform content, ItemRarity rarity, int count, float y)
    {
        var c = RewardPresentation.FrameColor(rarity);
        float left = -((PerRow - 1) * (CardW + Gap)) * 0.5f - CardW * 0.5f;
        var head = UICodeOverlay.Txt(content, $"{Loc.T(RewardPresentation.RarityLabel(rarity))}   <size=80%><color=#9A98A0>{count}</color></size>", 19f,
                                     rarity == ItemRarity.Common ? UICodeOverlay.Mist : c, new Vector2(left + 150f, y - HeadH * 0.5f + 4f), 300f,
                                     align: TextAlignmentOptions.Left);
        head.rectTransform.anchorMin = head.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        var line = UICodeOverlay.Img(content, "HeadRule", new Vector2(left + 300f + 270f, y - HeadH * 0.5f + 4f), new Vector2(540f, 1f), new Color(c.r, c.g, c.b, 0.35f));
        line.rectTransform.anchorMin = line.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        line.raycastTarget = false;
    }

    private static Card BuildCard(RectTransform content, RuntimeItemData rune, Vector2 pos, float at, System.Action onClick)
    {
        var card = UICodeOverlay.Img(content, "Card", pos, new Vector2(CardW, CardH), CardBg);
        var rt = card.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.anchoredPosition = pos;
        ApplyFrame(card, rune.rarity, CardFrameScale);
        var btn = card.gameObject.AddComponent<Button>();
        btn.targetGraphic = card;
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(() => onClick());
        var group = card.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;

        var icon = UICodeOverlay.Img(rt, "Icon", new Vector2(0f, 26f), new Vector2(76f, 76f), Color.white);
        icon.sprite = RuneArt.ResolveRuneIcon(rune);
        icon.preserveAspect = true;
        icon.raycastTarget  = false;
        if (icon.sprite == null) icon.color = Color.clear;

        var n = UICodeOverlay.Txt(rt, rune.displayName, 15f, UICodeOverlay.Ink, new Vector2(0f, -36f), CardW - 16f);
        n.rectTransform.sizeDelta = new Vector2(CardW - 16f, 24f);
        n.textWrappingMode = TextWrappingModes.NoWrap;
        n.overflowMode = TextOverflowModes.Ellipsis;
        n.enableAutoSizing = true;   // 긴 이름(「가벼운 그림자의 룬」)이 「…」로 잘렸다 — 줄여 한 줄에
        n.fontSizeMin = 11f;
        n.fontSizeMax = 15f;
        var e = ElementDef.GetById(rune.element);
        if (e != null)
        {
            var el = UICodeOverlay.Txt(rt, $"<color=#{ColorUtility.ToHtmlStringRGB(e.Color)}>●</color> {Loc.T(e.Name)}", 13f, UICodeOverlay.Mist, new Vector2(0f, -60f), CardW - 16f);
            el.rectTransform.sizeDelta = new Vector2(CardW - 16f, 20f);
        }
        var pick = UICodeOverlay.Line(rt, PickC);
        pick.enabled = false;
        return new Card { Rt = rt, Group = group, Pick = pick, At = at, Y = pos.y };
    }

    private static void ShowDetail(RuntimeItemData rune, Image frame, Image icon, TMP_Text name, TMP_Text sub, TMP_Text desc)
    {
        ApplyFrame(frame, rune.rarity, BigFrameScale);
        icon.sprite = RuneArt.ResolveRuneIcon(rune);
        icon.color  = icon.sprite != null ? Color.white : Color.clear;
        name.text   = Loc.T(rune.displayName);
        name.color  = rune.rarity == ItemRarity.Common ? UICodeOverlay.Ink : RewardPresentation.FrameColor(rune.rarity);
        var e = ElementDef.GetById(rune.element);
        string el = e != null ? $"<color=#{ColorUtility.ToHtmlStringRGB(e.Color)}>●</color> {Loc.T(e.Name)}" : "";
        sub.text  = string.IsNullOrEmpty(el) ? Loc.T(RewardPresentation.RarityLabel(rune.rarity)) : $"{Loc.T(RewardPresentation.RarityLabel(rune.rarity))}   ·   {el}";
        desc.text = UIKeywordInk.Values(UI_CodexPage.RuneDesc(rune) ?? "");
    }

    /// <summary>룬 고르기 창과 같은 힉스필드 등급 카드 틀(9칸) — 없으면 등급색 얇은 테.</summary>
    private static void ApplyFrame(Image img, ItemRarity rarity, float scale)
    {
        var art = UISkin.RuneSelect != null ? UISkin.RuneSelect.RarityCardFrame(rarity) : null;
        var edge = img.transform.Find("Edge");
        if (art != null)
        {
            img.sprite = art;
            img.type   = Image.Type.Sliced;
            img.color  = Color.white;
            img.pixelsPerUnitMultiplier = scale;
            if (edge != null) edge.gameObject.SetActive(false);
            return;
        }
        img.sprite = null;
        img.color  = CardBg;
        if (edge == null)
        {
            var line = UICodeOverlay.Line((RectTransform)img.transform, Color.white);
            line.name = "Edge";
            edge = line.transform;
        }
        edge.gameObject.SetActive(true);
        var c = RewardPresentation.FrameColor(rarity);
        c.a = 0.6f;
        edge.GetComponent<Image>().color = c;
    }
}
