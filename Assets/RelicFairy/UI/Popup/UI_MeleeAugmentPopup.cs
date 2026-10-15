using System;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 근접 강화 증강 고르기(10-15 · 설계 기획/RelicFairy_근접강화증강_설계_20261015.md · 사용자 「강화 단계마다 효과 2지선다를 랜덤으로 ·
/// 중복 선택은 강화 · 등급도 랜덤 · 무기마다 전용」).
/// 두 장이 뒷면에서 차례로 뒤집혀 등급(은 · 금 · 프리즘) 테가 드러난다 — 등급이 공개되는 순간이 핵심(사용자 「고정이면 기대를 하기 힘들다」).
/// 카드를 누르면 고른다 · 카드 아래 [다시 뽑기]는 그 한 장만(런당 <see cref="MeleeAugmentDraft.RerollsPerRun"/>) · 가진 증강이면 「II → III」.
/// 여는 쪽은 <see cref="MeleeAugmentDraftHost"/> — 이 창은 받은 화면을 그리고 고르기 · 다시 뽑기를 Draft에 넘긴다.
/// 놓친 단계가 이어지면(고른 직후 다음 화면) 창을 닫지 않고 다시 뒤집는다. 모양 = 톤 v2(남색 판 · 1px 선 · Noto 얇게).
/// </summary>
public sealed class UI_MeleeAugmentPopup : UI_Popup
{
    // ── Constants ──
    private const float PanelW = 1120f, PanelH = 612f, Pad = 56f;
    // 10-15 실측 — 360이면 아래 40%가 비었다 · 04 규칙형 증강(긴 글 ≈45자)에선 「지금 …」 줄이 카드 밖으로 넘쳐 두 줄 자리(+22)를 줬다
    private const float CardW = 480f, CardH = 332f, CardGap = 48f, CardY = 156f, CardPad = 28f;
    private const float RerollW = 220f, RerollH = 40f, RerollY = CardY + CardH + 16f;
    private const float FlipStart = 0.25f, FlipHalf = 0.14f, FlipStagger = 0.15f;
    private const float ChosenHold = 0.35f;
    private const float PrismCycleSec = 12f;

    private static readonly Color VeilC  = new(0.02f, 0.02f, 0.035f, 0.72f);
    private static readonly Color PanelC = new(0.055f, 0.051f, 0.086f, 0.97f);
    private static readonly Color CardC  = new(0.090f, 0.086f, 0.140f, 1f);
    private static readonly Color SilverC = new(0.80f, 0.82f, 0.88f, 1f);

    // ── Static ──
    /// <summary>열려 있는 창(없으면 null) — 호스트가 두 번 열지 않게 본다.</summary>
    public static UI_MeleeAugmentPopup Current { get; private set; }

    // ── Private ──
    private sealed class Card
    {
        public RectTransform Root;
        public CanvasGroup   Group, Face;
        public Image[]       Lines;
        public Image         Back;
        public TMP_Text      Chip, Kind, Name, Rank, Effect, Now, Hint;
        public Button        Reroll;
        public TMP_Text      RerollLabel;
    }

    private readonly Card[] _cards = new Card[2];
    private TMP_Text _eyebrow, _title, _tier, _sub, _foot;
    private MeleeAugmentOffer _offer;
    private bool _built, _busy, _revealed;
    private int  _gen;

    // ── Properties ──
    public override bool BlocksGameplay => true;
    public override bool CloseOnEscape  => false;   // 고르기 전에는 닫히지 않는다 — 보상 결정(룬 선택과 같은 규칙)

    // ── Lifecycle ──
    public override void Init()
    {
        base.Init();
        Current = this;
        Build();
    }

    private void Update()
    {
        if (!_revealed || _offer == null || _offer.Tier != AugmentTier.Prismatic) return;
        var c = PrismColor(Time.unscaledTime);
        for (int i = 0; i < _cards.Length; i++) SetLines(_cards[i], c);
        _tier.color = c;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (Current == this) Current = null;
    }

    // ── Public Methods ──

    /// <summary>화면 하나를 그린다 — 두 장을 뒷면으로 돌려 두고 차례로 뒤집는다.</summary>
    public void Show(MeleeAugmentOffer offer)
    {
        if (offer == null) { ClosePopupUI(); return; }
        if (!_built) Build();
        _offer    = offer;
        _revealed = false;
        int gen = ++_gen;
        var tc = TierColor(offer.Tier);
        _eyebrow.text = $"+{offer.Level} 강화";
        _title.text   = "증강 하나를 고른다";
        _tier.text    = $"{MeleeAugmentDef.TierName(offer.Tier)} 등급";
        _tier.color   = tc;
        _tier.alpha   = 0f;
        _sub.text     = $"{offer.Weapon?.displayName}에 붙는다 · 같은 증강을 다시 고르면 단계가 오른다(최대 III)";
        foreach (var c in _cards) ShowBack(c);
        RefreshRerolls(false);
        RevealAsync(gen).Forget();
    }

    // ── Private Methods ──

    private void Build()
    {
        if (_built) return;
        _built = true;
        var root = (RectTransform)transform;

        var veil = ShopUIStyle.MakeImage(root, "Veil", VeilC, raycast: true);
        ShopUIStyle.Stretch(veil.rectTransform);

        var panel = ShopUIStyle.MakeImage(root, "Panel", PanelC, raycast: true).rectTransform;
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
        panel.anchoredPosition = Vector2.zero;
        panel.sizeDelta = new Vector2(PanelW, PanelH);
        UIThinBox.Create(panel, "Edge", Vector2.zero, Vector2.one, 0f, UITheme.Thin.LineActive, UITheme.Thin.TickGold, Color.clear);

        _eyebrow = Text(panel, "Eyebrow", 17f, UIWeight.Regular, TextAlignmentOptions.MidlineLeft, UITheme.Thin.InkA(0.6f));
        Put(_eyebrow.rectTransform, Pad, 36f, 600f, 26f);
        _title = Text(panel, "Title", UITheme.Thin.H2, UIWeight.Light, TextAlignmentOptions.MidlineLeft, UITheme.Thin.Ink);
        Put(_title.rectTransform, Pad, 62f, 700f, 46f);
        _tier = Text(panel, "Tier", UITheme.Thin.Title, UIWeight.Medium, TextAlignmentOptions.MidlineRight, SilverC);
        Put(_tier.rectTransform, PanelW - Pad - 320f, 62f, 320f, 46f);
        _sub = Text(panel, "Sub", 17f, UIWeight.Regular, TextAlignmentOptions.MidlineLeft, UITheme.Thin.InkA(0.62f));
        Put(_sub.rectTransform, Pad, 110f, PanelW - Pad * 2f, 28f);

        float x0 = (PanelW - CardW * 2f - CardGap) * 0.5f;
        for (int i = 0; i < _cards.Length; i++)
            _cards[i] = BuildCard(panel, i, x0 + i * (CardW + CardGap));

        _foot = Text(panel, "Foot", 16f, UIWeight.Regular, TextAlignmentOptions.Center, UITheme.Thin.InkA(0.5f));
        Put(_foot.rectTransform, Pad, PanelH - 44f, PanelW - Pad * 2f, 24f);
        _foot.text = "카드를 누르면 고른다 · 다시 뽑기는 이번 런에서만 · 한 장만 바뀐다";
    }

    private Card BuildCard(RectTransform panel, int index, float x)
    {
        var c = new Card();
        var bg = ShopUIStyle.MakeImage(panel, $"Card{index}", CardC, raycast: true);
        c.Root = bg.rectTransform;
        Put(c.Root, x, CardY, CardW, CardH);
        c.Root.pivot = new Vector2(0.5f, 0.5f);
        c.Root.anchoredPosition += new Vector2(CardW * 0.5f, -CardH * 0.5f);   // 가운데 기준 — 뒤집기(가로 배율)가 제자리에서 돈다
        c.Group = bg.gameObject.AddComponent<CanvasGroup>();
        var box = UIThinBox.Create(c.Root, "Edge", Vector2.zero, Vector2.one, 0f, UITheme.Thin.LineIdle, UITheme.Thin.InkA(0.4f), Color.clear);
        var lines = box.GetComponentsInChildren<Image>(true);
        c.Lines = lines;

        c.Back = ShopUIStyle.MakeImage(c.Root, "Back", UITheme.Thin.InkA(0.35f));
        c.Back.sprite = UIThinShapes.Outline(ThinShape.Diamond, 56f, 56f);
        c.Back.rectTransform.anchorMin = c.Back.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        c.Back.rectTransform.sizeDelta = new Vector2(56f, 56f);

        var face = ShopUIStyle.MakeRect(c.Root, "Face", typeof(CanvasGroup)).GetComponent<RectTransform>();
        ShopUIStyle.Stretch(face);
        c.Face = face.GetComponent<CanvasGroup>();
        c.Face.blocksRaycasts = false;
        float w = CardW - CardPad * 2f;
        c.Chip   = Text(face, "Chip", 16f, UIWeight.Medium, TextAlignmentOptions.MidlineLeft, SilverC);
        CardPut(c.Chip.rectTransform, CardPad, 24f, 300f, 24f);
        c.Kind   = Text(face, "Kind", 16f, UIWeight.Regular, TextAlignmentOptions.MidlineRight, UITheme.Thin.InkA(0.6f));
        CardPut(c.Kind.rectTransform, CardW - CardPad - 160f, 24f, 160f, 24f);
        c.Name   = Text(face, "Name", 28f, UIWeight.Medium, TextAlignmentOptions.MidlineLeft, UITheme.Thin.Ink);
        CardPut(c.Name.rectTransform, CardPad, 62f, w, 42f);
        c.Rank   = Text(face, "Rank", 22f, UIWeight.Light, TextAlignmentOptions.MidlineLeft, SilverC);
        CardPut(c.Rank.rectTransform, CardPad, 106f, w, 32f);
        c.Effect = Text(face, "Effect", UITheme.Thin.Lead, UIWeight.Regular, TextAlignmentOptions.TopLeft, UITheme.Thin.InkA(0.82f));
        c.Effect.textWrappingMode = TextWrappingModes.Normal;
        c.Effect.enableAutoSizing = true;
        c.Effect.fontSizeMax = UITheme.Thin.Lead;
        c.Effect.fontSizeMin = 16f;
        c.Effect.overflowMode = TextOverflowModes.Ellipsis;
        CardPut(c.Effect.rectTransform, CardPad, 152f, w, 80f);
        c.Now    = Text(face, "Now", 16f, UIWeight.Regular, TextAlignmentOptions.TopLeft, UITheme.Thin.InkA(0.5f));
        c.Now.textWrappingMode = TextWrappingModes.Normal;   // 두 줄까지(긴 규칙 글)
        CardPut(c.Now.rectTransform, CardPad, 238f, w, 48f);
        c.Hint   = Text(face, "Hint", 16f, UIWeight.Regular, TextAlignmentOptions.MidlineRight, UITheme.Thin.InkA(0.55f));
        CardPut(c.Hint.rectTransform, CardPad, CardH - 20f - 24f, w, 24f);
        c.Hint.text  = "눌러서 고른다";
        c.Hint.alpha = 0f;

        var btn = bg.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(() => OnCardClicked(index));
        var hover = bg.gameObject.AddComponent<EventTrigger>();
        AddTrigger(hover, EventTriggerType.PointerEnter, () => { if (!_busy && _revealed) c.Hint.alpha = 1f; });
        AddTrigger(hover, EventTriggerType.PointerExit,  () => c.Hint.alpha = 0f);

        // 다시 뽑기 — 카드 아래 · 얇은 테 버튼(남은 횟수)
        var rb = ShopUIStyle.MakeImage(panel, $"Reroll{index}", UITheme.Thin.GlassFill, raycast: true);
        Put(rb.rectTransform, x + (CardW - RerollW) * 0.5f, RerollY, RerollW, RerollH);
        UIThinBox.Create(rb.rectTransform, "Edge", Vector2.zero, Vector2.one, 0f, UITheme.Thin.LineIdle, Color.clear, Color.clear);
        c.RerollLabel = Text(rb.rectTransform, "Label", 16f, UIWeight.Regular, TextAlignmentOptions.Center, UITheme.Thin.InkA(0.85f));
        ShopUIStyle.Stretch(c.RerollLabel.rectTransform);
        c.Reroll = rb.gameObject.AddComponent<Button>();
        c.Reroll.onClick.AddListener(() => OnRerollClicked(index));
        return c;
    }

    // ── 공개(뒤집기) ──

    private void ShowBack(Card c)
    {
        c.Root.localScale = Vector3.one;
        c.Group.alpha = 1f;
        c.Face.alpha  = 0f;
        c.Back.enabled = true;
        c.Hint.alpha  = 0f;
        SetLines(c, UITheme.Thin.LineIdle);
    }

    private async UniTaskVoid RevealAsync(int gen)
    {
        _busy = true;
        try
        {
            await Hold(FlipStart);
            if (gen != _gen) return;
            var first = FlipAsync(_cards[0], 0, gen);
            await Hold(FlipStagger);
            await UniTask.WhenAll(first, FlipAsync(_cards[1], 1, gen));
            if (gen != _gen) return;
            _revealed = true;
            // 등급 이름은 두 장이 다 드러난 뒤 — 금 · 프리즘이면 소리를 한 번 더(은은 조용히)
            if (_offer.Tier != AugmentTier.Silver) ShopUIStyle.PlaySfx("enhance_success");
            for (float t = 0f; t < 0.2f; t += Time.unscaledDeltaTime)
            {
                _tier.alpha = t / 0.2f;
                await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
            }
            _tier.alpha = 1f;
        }
        catch (OperationCanceledException) { return; }
        if (gen == _gen) { _busy = false; RefreshRerolls(true); }
    }

    /// <summary>한 장 뒤집기 — 가로로 접혔다가(뒷면) 펴지며 앞면 · 등급 테가 드러난다.</summary>
    private async UniTask FlipAsync(Card c, int index, int gen)
    {
        for (float t = 0f; t < FlipHalf; t += Time.unscaledDeltaTime)
        {
            if (gen != _gen) return;
            float k = t / FlipHalf;
            c.Root.localScale = new Vector3(1f - k * k, 1f, 1f);
            await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
        }
        Fill(c, _offer.Cards[index]);
        Blip(_offer.Tier);
        float up = FlipHalf * 1.3f;
        for (float t = 0f; t < up; t += Time.unscaledDeltaTime)
        {
            if (gen != _gen) return;
            float k = 1f - (1f - t / up) * (1f - t / up);
            c.Root.localScale = new Vector3(k, 1f, 1f);
            await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
        }
        c.Root.localScale = Vector3.one;
    }

    /// <summary>앞면 — 등급 · 공용/전용 · 새 증강/강화 · 이름 · 단계 · 효과(수치 강조) · 지금 단계(강화 카드).</summary>
    private void Fill(Card c, MeleeAugmentCard card)
    {
        var def = card.Def;
        var tc  = TierColor(_offer.Tier);
        c.Back.enabled = false;
        c.Face.alpha   = 1f;
        SetLines(c, tc);
        if (def == null)
        {
            c.Name.text = card.Id;
            c.Chip.text = c.Kind.text = c.Rank.text = c.Effect.text = c.Now.text = string.Empty;
            return;
        }
        string scope = def.Weapon == MeleeWeaponKind.None ? "공용" : $"{_offer.Weapon?.displayName} 전용";
        c.Chip.text  = $"{MeleeAugmentDef.TierName(_offer.Tier)} · {scope}";
        c.Chip.color = tc;
        c.Kind.text  = card.IsUpgrade ? "강화" : "새 증강";
        c.Kind.color = card.IsUpgrade ? tc : UITheme.Thin.InkA(0.6f);
        c.Name.text  = def.Name;
        c.Rank.text  = card.IsUpgrade ? $"단계 {Roman(card.Rank - 1)} → {Roman(card.Rank)}" : $"단계 {Roman(1)}";
        c.Rank.color = tc;
        c.Effect.text = UIKeywordInk.Values(def.Text(card.Rank));   // 수치만 Noto 500 · 조용한 잉크(10-15 설명 글 강조)
        c.Now.text    = card.IsUpgrade ? $"지금  {def.Text(card.Rank - 1)}" : string.Empty;
    }

    // ── 고르기 · 다시 뽑기 ──

    private void OnCardClicked(int index)
    {
        if (_busy || !_revealed || _offer == null) return;
        ChooseAsync(index).Forget();
    }

    private async UniTaskVoid ChooseAsync(int index)
    {
        _busy = true;
        var pick  = _cards[index];
        var other = _cards[1 - index];
        ShopUIStyle.PlaySfx("enhance_success");
        try
        {
            for (float t = 0f; t < ChosenHold; t += Time.unscaledDeltaTime)
            {
                float k = t / ChosenHold;
                pick.Root.localScale = Vector3.one * (1f + 0.04f * Mathf.Sin(k * Mathf.PI));
                other.Group.alpha    = Mathf.Lerp(1f, 0.25f, k);
                await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
            }
        }
        catch (OperationCanceledException) { return; }
        pick.Root.localScale = Vector3.one;

        MeleeAugmentDraft.Choose(index);
        var next = MeleeAugmentDraft.Pending;   // 놓친 단계가 남았으면 Choose 안에서 다음 화면이 열렸다
        if (next != null) { Show(next); return; }
        ClosePopupUI();
    }

    private void OnRerollClicked(int index)
    {
        if (_busy || !_revealed || _offer == null) return;
        if (!MeleeAugmentDraft.Reroll(index, new System.Random(unchecked(Environment.TickCount + index * 7919))))
        {
            ShopUIStyle.PlaySfx("shop_reject");
            return;
        }
        RerollAsync(index, _gen).Forget();
    }

    private async UniTaskVoid RerollAsync(int index, int gen)
    {
        _busy = true;
        RefreshRerolls(false);
        try
        {
            await FlipAsync(_cards[index], index, gen);   // 접혔다 펴지며 새 카드(등급은 그대로)
        }
        catch (OperationCanceledException) { return; }
        if (gen != _gen) return;
        _busy = false;
        RefreshRerolls(true);
    }

    private void RefreshRerolls(bool usable)
    {
        int left = MeleeAugmentDraft.RerollsLeft;
        foreach (var c in _cards)
        {
            if (c == null) continue;
            bool on = usable && left > 0;
            c.Reroll.interactable = on;
            c.RerollLabel.text  = left > 0 ? $"다시 뽑기 · 남은 {left}" : "다시 뽑기 · 다 썼다";
            c.RerollLabel.color = on ? UITheme.Thin.InkA(0.85f) : UITheme.Thin.InkA(0.4f);
        }
    }

    // ── 도우미 ──

    private static Color TierColor(AugmentTier t) => t switch
    {
        AugmentTier.Gold      => UITheme.Thin.Gold,
        AugmentTier.Prismatic => PrismColor(Time.unscaledTime),
        _                     => SilverC,
    };

    /// <summary>프리즘 — 옅은 무지개가 천천히 돈다(채도 낮게 · 12초 한 바퀴).</summary>
    private static Color PrismColor(float now) => Color.HSVToRGB(Mathf.Repeat(now / PrismCycleSec, 1f), 0.38f, 1f);

    /// <summary>단계 숫자 — 형 단련과 같은 로마 숫자 글리프(Ⅰ · Ⅱ · Ⅲ). 영문 I는 얇은 글꼴에서 세로줄처럼 보였다(10-15 실측).</summary>
    private static string Roman(int rank) => rank switch { 2 => "Ⅱ", 3 => "Ⅲ", _ => "Ⅰ" };

    private static void SetLines(Card c, Color color)
    {
        if (c?.Lines == null) return;
        for (int i = 0; i < c.Lines.Length; i++) if (c.Lines[i] != null) c.Lines[i].color = color;
    }

    private static void Blip(AugmentTier t)
    {
        var sound = Managers.Sound;
        if (sound == null) return;
        sound.PlayUiEvent(SoundEvent.UiHover, t == AugmentTier.Prismatic ? 1.3f : t == AugmentTier.Gold ? 1.15f : 1f, 0.6f);
    }

    private async UniTask Hold(float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
    }

    private static TMP_Text Text(Transform parent, string name, float size, UIWeight weight, TextAlignmentOptions align, Color color)
    {
        var t = ShopUIStyle.MakeText(parent, name, size, FontStyles.Normal, align, color);
        UITheme.StyleThinText(t, weight, size, color);
        t.enableAutoSizing = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.richText         = true;
        t.raycastTarget    = false;
        return t;
    }

    /// <summary>판(1120×640) 왼쪽 위 기준 px.</summary>
    private static void Put(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
    }

    /// <summary>카드(480×360 · 가운데 기준 루트) 안 왼쪽 위 기준 px.</summary>
    private static void CardPut(RectTransform rt, float x, float y, float w, float h) => Put(rt, x, y, w, h);

    private static void AddTrigger(EventTrigger trigger, EventTriggerType type, Action action)
    {
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(_ => action());
        trigger.triggers.Add(entry);
    }
}
