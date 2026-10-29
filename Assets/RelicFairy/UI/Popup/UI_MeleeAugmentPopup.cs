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
/// 10-18 재련 개편(설계 기획/RelicFairy_재련개편_설계_20261018.md): 카드 장수는 화면마다(화로 「열기」면 3장 · 원거리 4장) ·
/// 다시 뽑기 = <see cref="MeleeAugmentDraft.RerollsAvailable"/>(런 몫 + 이번 방문 「풍요」) · 원거리 재련의 파츠 카드도 이 창이 그린다
/// (<see cref="ShowParts"/> — 새 파츠는 Lv1로 켜짐 · 가진 파츠는 다음 눈금까지 · 다시 뽑기는 카드 전부).
/// 10-18 힉스필드 틀(사용자 「증강 사용할 팝업 UI 테두리가 포함된 틀」 · 「뽑은 리소스 활용해야지」): 카드 = 세로 쇠 틀(그림 비율 300×472 그대로) ·
/// 창 = 금 매듭 틀(9칸). 등급은 틀 안쪽 1px 선 색으로 읽힌다. 틀(<see cref="CrucibleSkinSO.AugmentCardFrame"/>)이 없으면 남색 판 + 1px 테.
/// 10-24 전설(W7) — 화면마다 따로 낮은 확률로 굴려 카드 한 장만 전설로 나온다. 그 카드는 화면 등급이 아니라 <c>card.Def.Tier</c>로 그린다:
/// 전설 색(룬 Legendary와 같은 색) 테 · 「전설 · 공용/전용」 · 단계 없음(처음부터 완성) · 뒤집힐 때 정제소 전설 소리.
/// </summary>
public sealed class UI_MeleeAugmentPopup : UI_Popup
{
    // ── Constants ──
    private const float PanelW = 1120f, PanelH = 748f, Pad = 56f;
    // 10-18 세로 카드 — 틀 그림(524×824) 비율 그대로 300×472. 넷이어도 폭은 같고 간격만 좁힌다(4 × 300 + 3 × 32 + 112 = 1408)
    private const float CardW = 300f, CardH = 472f, CardGap = 48f, CardY = 156f, CardPad = 30f;
    private const int   MaxCards = 4;            // 원거리 3장 + 화로 「열기」 1장
    private const float NarrowGap = 32f;
    private const float FrameLineInset = 22f;    // 등급 1px 선 — 쇠 틀(테 ≈16px) 바로 안쪽
    private const float PanelCornerPx  = 48f;    // 창 틀 9칸 모서리 그림 크기(매듭이 제목 글자에 닿지 않게)
    private const float RerollW = 220f, RerollH = 40f, RerollY = CardY + CardH + 16f;
    private const float FlipStart = 0.25f, FlipHalf = 0.14f, FlipStagger = 0.15f;
    private const float ChosenHold = 0.35f;
    private const float PrismCycleSec = 12f;

    private static readonly Color VeilC  = new(0.02f, 0.02f, 0.035f, 0.72f);
    private static readonly Color PanelC = new(0.055f, 0.051f, 0.086f, 0.97f);
    private static readonly Color CardC  = new(0.090f, 0.086f, 0.140f, 1f);
    private static readonly Color SilverC = new(0.80f, 0.82f, 0.88f, 1f);
    private static readonly Color LegendC = new(1f, 0.52f, 0.36f, 1f);   // 전설 — 금(#FFCC4D)과 갈리는 주홍(룬 Legendary 금색과 같으면 금 카드와 구분이 안 됐다)
    private const string LegendHex = "#FF855C";

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

    private readonly Card[] _cards = new Card[MaxCards];
    private TMP_Text _eyebrow, _title, _tier, _sub, _foot;
    private RectTransform _panel;
    private MeleeAugmentOffer _offer;
    private bool _built, _busy, _revealed;
    private bool _parts;                         // 원거리 파츠 카드 화면(RangedPartsDraft)
    private int  _gen, _count, _laidCount;
    private Button   _rerollAll;                 // 파츠 — 카드 전부 다시 뽑기(가운데 하나)
    private TMP_Text _rerollAllLabel;

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
        if (_parts || !_revealed || _offer == null || _offer.Tier != AugmentTier.Prismatic) return;
        var c = PrismColor(Time.unscaledTime);
        for (int i = 0; i < _count; i++)
            if (CardTier(i) == AugmentTier.Prismatic) SetLines(_cards[i], c);   // 전설 카드는 제 색을 지킨다
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
        _parts    = false;
        _offer    = offer;
        _revealed = false;
        Layout(offer.Cards != null ? offer.Cards.Length : 2);
        int gen = ++_gen;
        var tc = TierColor(offer.Tier);
        _eyebrow.text = $"재련 {WeaponReforgeService.CountAt(offer.Level)}회째";   // 10-19 — 「+N 강화」 대신 재련 횟수
        _title.text   = "증강 하나를 고른다";
        _tier.text    = offer.HasLegend ? $"{MeleeAugmentDef.TierName(offer.Tier)} 등급 · <color={LegendHex}>전설 한 장</color>" : $"{MeleeAugmentDef.TierName(offer.Tier)} 등급";
        _tier.color   = tc;
        _tier.alpha   = 0f;
        _sub.text     = $"{offer.Weapon?.displayName}에 붙는다 · 같은 증강을 다시 고르면 단계가 오른다(최대 III)";
        _foot.text    = "카드를 누르면 고른다 · 다시 뽑기는 그 한 장만 바뀐다";
        Managers.Sound?.PlayUiEvent(SoundEvent.UiOpen);   // 10-29 UI 소리 — 열기(놓친 단계의 다음 화면도 한 번)
        for (int i = 0; i < _count; i++) ShowBack(_cards[i]);
        RefreshRerolls(false);
        RevealAsync(gen).Forget();
    }

    /// <summary>
    /// 원거리 재련 — 파츠 카드(<see cref="RangedPartsDraft.Offered"/>)를 그린다. 새 파츠 = Lv1로 켜짐 · 가진 파츠 = 다음 눈금(4 · 7 · 10)까지.
    /// 고르면 <see cref="RangedPartsDraft.Choose"/> · 다시 뽑기는 카드 전부(<see cref="RangedPartsDraft.Reroll"/>).
    /// </summary>
    public void ShowParts()
    {
        var offered = RangedPartsDraft.Offered;
        if (offered == null || offered.Count == 0) { ClosePopupUI(); return; }
        if (!_built) Build();
        _parts    = true;
        _offer    = null;
        _revealed = false;
        Layout(offered.Count);
        int gen = ++_gen;
        _eyebrow.text = "원거리 재련";
        _title.text   = "파츠 하나를 고른다";
        _tier.text    = string.Empty;
        _sub.text     = $"새 파츠는 Lv1로 켜진다 · 가진 파츠는 다음 눈금까지 오른다(최대 Lv{RangedPartsDraft.PartCap})";
        _foot.text    = "카드를 누르면 고른다 · 다시 뽑기는 카드 전부가 바뀐다";
        Managers.Sound?.PlayUiEvent(SoundEvent.UiOpen);   // 10-29 UI 소리 — 열기
        for (int i = 0; i < _count; i++) ShowBack(_cards[i]);
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

        var panelImg = ShopUIStyle.MakeImage(root, "Panel", PanelC, raycast: true);
        var panel = panelImg.rectTransform;
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
        panel.anchoredPosition = Vector2.zero;
        panel.sizeDelta = new Vector2(PanelW, PanelH);
        _panel = panel;
        var panelArt = UISkin.Crucible?.AugmentPanelFrame;
        if (panelArt != null)
        {
            ShopUIStyle.Skin(panelImg, panelArt, sliced: true);
            if (panelArt.border.x > 0f) panelImg.pixelsPerUnitMultiplier = panelArt.border.x / PanelCornerPx;
        }
        else UIThinBox.Create(panel, "Edge", Vector2.zero, Vector2.one, 0f, UITheme.Thin.LineActive, UITheme.Thin.TickGold, Color.clear);

        _eyebrow = Text(panel, "Eyebrow", 17f, UIWeight.Regular, TextAlignmentOptions.MidlineLeft, UITheme.Thin.InkA(0.6f));
        Put(_eyebrow.rectTransform, Pad, 36f, 600f, 26f);
        _title = Text(panel, "Title", UITheme.Thin.H2, UIWeight.Light, TextAlignmentOptions.MidlineLeft, UITheme.Thin.Ink);
        Put(_title.rectTransform, Pad, 62f, 700f, 46f);
        _tier = Text(panel, "Tier", UITheme.Thin.Title, UIWeight.Medium, TextAlignmentOptions.MidlineRight, SilverC);
        Put(_tier.rectTransform, PanelW - Pad - 320f, 62f, 320f, 46f);
        _sub = Text(panel, "Sub", 17f, UIWeight.Regular, TextAlignmentOptions.MidlineLeft, UITheme.Thin.InkA(0.62f));
        Put(_sub.rectTransform, Pad, 110f, PanelW - Pad * 2f, 28f);

        _foot = Text(panel, "Foot", 16f, UIWeight.Regular, TextAlignmentOptions.Center, UITheme.Thin.InkA(0.5f));
        _foot.text = "카드를 누르면 고른다 · 다시 뽑기는 그 한 장만 바뀐다";

        // 파츠 — 카드 전부 다시 뽑기(가운데 하나 · Layout이 자리를 잡는다)
        var ra = ShopUIStyle.MakeImage(panel, "RerollAll", UITheme.Thin.GlassFill, raycast: true);
        UIThinBox.Create(ra.rectTransform, "Edge", Vector2.zero, Vector2.one, 0f, UITheme.Thin.LineIdle, Color.clear, Color.clear);
        _rerollAllLabel = Text(ra.rectTransform, "Label", 16f, UIWeight.Regular, TextAlignmentOptions.Center, UITheme.Thin.InkA(0.85f));
        ShopUIStyle.Stretch(_rerollAllLabel.rectTransform);
        _rerollAll = ra.gameObject.AddComponent<Button>();
        _rerollAll.onClick.AddListener(() => OnRerollClicked(-1));
        Layout(2);
    }

    /// <summary>
    /// 카드 n장 자리 — 장수가 바뀌면 카드를 다시 짓고 판 폭을 맞춘다(세로 카드 300 고정 · 넷이면 간격 32). 다시 뽑기는
    /// 증강 = 카드마다 · 파츠 = 가운데 하나.
    /// </summary>
    private void Layout(int n)
    {
        n = Mathf.Clamp(n, 1, MaxCards);
        _count = n;
        float cw  = CardW;
        float gap = n <= 3 ? CardGap : NarrowGap;
        float pw  = Mathf.Max(PanelW, n * cw + (n - 1) * gap + Pad * 2f);
        _panel.sizeDelta = new Vector2(pw, PanelH);
        if (_laidCount != n)
        {
            for (int i = 0; i < MaxCards; i++)
            {
                if (_cards[i] == null) continue;
                Destroy(_cards[i].Root.gameObject);
                Destroy(_cards[i].Reroll.gameObject);
                _cards[i] = null;
            }
            float x0 = (pw - n * cw - (n - 1) * gap) * 0.5f;
            for (int i = 0; i < n; i++) _cards[i] = BuildCard(_panel, i, x0 + i * (cw + gap), cw);
            _laidCount = n;
        }
        Put(_title.rectTransform, Pad, 62f, pw - Pad * 2f - 340f, 46f);
        Put(_tier.rectTransform, pw - Pad - 320f, 62f, 320f, 46f);
        Put(_sub.rectTransform, Pad, 110f, pw - Pad * 2f, 28f);
        Put(_foot.rectTransform, Pad, PanelH - 44f, pw - Pad * 2f, 24f);
        Put((RectTransform)_rerollAll.transform, (pw - RerollW) * 0.5f, RerollY, RerollW, RerollH);
        for (int i = 0; i < n; i++) _cards[i].Reroll.gameObject.SetActive(!_parts);
        _rerollAll.gameObject.SetActive(_parts);
    }

    private Card BuildCard(RectTransform panel, int index, float x, float cw)
    {
        var c = new Card();
        var bg = ShopUIStyle.MakeImage(panel, $"Card{index}", CardC, raycast: true);
        c.Root = bg.rectTransform;
        Put(c.Root, x, CardY, cw, CardH);
        c.Root.pivot = new Vector2(0.5f, 0.5f);
        c.Root.anchoredPosition += new Vector2(cw * 0.5f, -CardH * 0.5f);   // 가운데 기준 — 뒤집기(가로 배율)가 제자리에서 돈다
        c.Group = bg.gameObject.AddComponent<CanvasGroup>();
        // 10-18 쇠 틀(바탕 포함) — 등급 1px 선은 틀 안쪽에(모서리 쇠 장식 위에 눈금을 겹치지 않는다)
        var cardArt = UISkin.Crucible?.AugmentCardFrame;
        if (cardArt != null) ShopUIStyle.Skin(bg, cardArt);
        var box = cardArt != null
            ? UIThinBox.Create(c.Root, "Edge", Vector2.zero, Vector2.one, FrameLineInset, UITheme.Thin.LineIdle, Color.clear, Color.clear)
            : UIThinBox.Create(c.Root, "Edge", Vector2.zero, Vector2.one, 0f, UITheme.Thin.LineIdle, UITheme.Thin.InkA(0.4f), Color.clear);
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
        // 세로 카드(10-18) — 위: 등급 · 종류 / 이름 · 단계(가운데) / 1px 선 / 효과(세로 자리를 넉넉히) / 지금 단계 · 파츠 설명 / 아래: 고르기 안내
        float w = cw - CardPad * 2f;
        c.Chip   = Text(face, "Chip", 16f, UIWeight.Medium, TextAlignmentOptions.MidlineLeft, SilverC);
        CardPut(c.Chip.rectTransform, CardPad, 40f, w - 80f, 24f);
        c.Kind   = Text(face, "Kind", 16f, UIWeight.Regular, TextAlignmentOptions.MidlineRight, UITheme.Thin.InkA(0.6f));
        CardPut(c.Kind.rectTransform, cw - CardPad - 80f, 40f, 80f, 24f);
        c.Name   = Text(face, "Name", 28f, UIWeight.Medium, TextAlignmentOptions.Center, UITheme.Thin.Ink);
        c.Name.enableAutoSizing = true;   // 긴 이름(「형 쉬었다 친 첫 타」)은 한 줄 안에서 줄인다
        c.Name.fontSizeMax = 28f;
        c.Name.fontSizeMin = 20f;
        CardPut(c.Name.rectTransform, CardPad, 78f, w, 42f);
        c.Rank   = Text(face, "Rank", 22f, UIWeight.Light, TextAlignmentOptions.Center, SilverC);
        CardPut(c.Rank.rectTransform, CardPad, 120f, w, 32f);
        var rule = ShopUIStyle.MakeImage(face, "Rule", UITheme.Thin.LineIdle);
        CardPut(rule.rectTransform, CardPad + 20f, 164f, w - 40f, 1f);
        c.Effect = Text(face, "Effect", UITheme.Thin.Lead, UIWeight.Regular, TextAlignmentOptions.TopLeft, UITheme.Thin.InkA(0.82f));
        c.Effect.textWrappingMode = TextWrappingModes.Normal;
        c.Effect.enableAutoSizing = true;
        c.Effect.fontSizeMax = UITheme.Thin.Lead;
        c.Effect.fontSizeMin = 16f;
        c.Effect.overflowMode = TextOverflowModes.Ellipsis;
        CardPut(c.Effect.rectTransform, CardPad, 178f, w, 160f);
        c.Now    = Text(face, "Now", 16f, UIWeight.Regular, TextAlignmentOptions.TopLeft, UITheme.Thin.InkA(0.5f));
        c.Now.textWrappingMode = TextWrappingModes.Normal;   // 세 줄까지(긴 규칙 글 · 파츠 설명)
        CardPut(c.Now.rectTransform, CardPad, 344f, w, 70f);
        c.Hint   = Text(face, "Hint", 16f, UIWeight.Regular, TextAlignmentOptions.Center, UITheme.Thin.InkA(0.55f));
        CardPut(c.Hint.rectTransform, CardPad, CardH - 30f - 24f, w, 24f);
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
        Put(rb.rectTransform, x + (cw - RerollW) * 0.5f, RerollY, RerollW, RerollH);
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
            var flips = new UniTask[_count];
            for (int i = 0; i < _count; i++)
            {
                flips[i] = FlipAsync(_cards[i], i, gen);
                if (i < _count - 1) await Hold(FlipStagger);
            }
            await UniTask.WhenAll(flips);
            if (gen != _gen) return;
            _revealed = true;
            if (_parts) { _busy = false; RefreshRerolls(true); return; }
            // 등급 이름은 카드가 다 드러난 뒤 — 금 · 프리즘이면 소리를 한 번 더(은은 조용히)
            if (_offer.Tier != AugmentTier.Silver || _offer.HasLegend) ShopUIStyle.PlaySfx("enhance_success");
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
        if (_parts)
        {
            var offered = RangedPartsDraft.Offered;
            if (index < offered.Count) FillPart(c, offered[index]);
            Blip(AugmentTier.Silver);
        }
        else
        {
            Fill(c, _offer.Cards[index]);
            var tier = CardTier(index);
            Blip(tier);
            if (tier == AugmentTier.Legend) ShopUIStyle.PlaySfx("refine_legend");   // 전설 카드가 드러나는 순간 — 정제소 전설 룬과 같은 소리
        }
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
        var tier = def != null && def.IsLegend ? AugmentTier.Legend : _offer.Tier;
        var tc  = TierColor(tier);
        c.Back.enabled = false;
        c.Face.alpha   = 1f;
        SetLines(c, tc);
        if (def == null)
        {
            c.Name.text = card.Id;
            c.Chip.text = c.Kind.text = c.Rank.text = c.Effect.text = c.Now.text = string.Empty;
            return;
        }
        string scope = def.Weapon == MeleeWeaponKind.None ? "공용" : "전용";   // 무기 이름은 창 부제 「…에 붙는다」가 말한다(세로 카드 폭 240)
        c.Chip.text  = $"{MeleeAugmentDef.TierName(tier)} · {scope}";
        c.Chip.color = tc;
        c.Kind.text  = card.IsUpgrade ? "강화" : "새 증강";
        c.Kind.color = card.IsUpgrade ? tc : UITheme.Thin.InkA(0.6f);
        c.Name.text  = def.Name;
        if (def.IsGrowth)   // 10-26 재련으로 자라는 증강(그림자 파트너 · 검풍) — 단계가 없다. 「확장은 체감 + UI」: 지금 값과 다음 재련 값을 같이 보인다
        {
            var w = _offer.Weapon;
            c.Rank.text   = $"재련 {MeleeAugmentCatalog.ReforgesSinceEvolution(w)}회";   // 「재련할수록 …」은 본문이 말한다(세로 카드 폭 240에 넘쳤다)
            c.Rank.color  = tc;
            c.Effect.text = UIKeywordInk.Values(w != null ? def.TextFor(w) : def.Text(1));
            c.Now.text    = GrowthLine(def, w);
            return;
        }
        c.Rank.text  = def.IsLegend ? "처음부터 완성"   // 전설은 단계가 없다(W7 D4)
                     : card.IsUpgrade ? $"단계 {Roman(card.Rank - 1)} → {Roman(card.Rank)}" : $"단계 {Roman(1)}";
        c.Rank.color = tc;
        c.Effect.text = UIKeywordInk.Values(def.Text(card.Rank));   // 수치만 Noto 500 · 조용한 잉크(10-15 설명 글 강조)
        c.Now.text    = card.IsUpgrade ? $"지금  {def.Text(card.Rank - 1)}" : string.Empty;
    }

    /// <summary>파츠 카드 앞면 — 새 파츠/재련 · 이름 · 단계(Lv a → b) · 바뀌는 값(재련소 목록과 같은 단위 글) · 파츠 설명.</summary>
    private void FillPart(Card c, RangedPartOffer offer)
    {
        var def = offer.Def;
        c.Back.enabled = false;
        c.Face.alpha   = 1f;
        var tc = offer.isNew ? SilverC : UITheme.Thin.Gold;
        SetLines(c, tc);
        c.Chip.text  = offer.isNew ? "새 파츠" : "재련";
        c.Chip.color = tc;
        c.Kind.text  = offer.toLevel >= RangedPartsDraft.PartCap ? "완성" : string.Empty;
        c.Kind.color = UITheme.Thin.Gold;
        if (def == null)
        {
            c.Name.text = offer.partId;
            c.Rank.text = c.Effect.text = c.Now.text = string.Empty;
            return;
        }
        c.Name.text  = def.part_name;
        c.Rank.text  = offer.isNew ? $"새로 켬 · Lv{offer.toLevel}" : $"Lv{offer.fromLevel} → Lv{offer.toLevel}";
        c.Rank.color = tc;
        string to = UI_CruciblePanel.PartValueText(def, def.ValueAt(offer.toLevel));
        c.Effect.text = offer.isNew ? UIKeywordInk.Values(to)
                                    : UIKeywordInk.Values($"{UI_CruciblePanel.PartValueText(def, def.ValueAt(offer.fromLevel))} → {to}");
        c.Now.text    = def.description ?? string.Empty;
    }

    // ── 고르기 · 다시 뽑기 ──

    private void OnCardClicked(int index)
    {
        if (_busy || !_revealed || (!_parts && _offer == null) || index >= _count) return;
        ChooseAsync(index).Forget();
    }

    private async UniTaskVoid ChooseAsync(int index)
    {
        _busy = true;
        var pick  = _cards[index];
        ShopUIStyle.PlaySfx("enhance_success");
        try
        {
            for (float t = 0f; t < ChosenHold; t += Time.unscaledDeltaTime)
            {
                float k = t / ChosenHold;
                pick.Root.localScale = Vector3.one * (1f + 0.04f * Mathf.Sin(k * Mathf.PI));
                for (int i = 0; i < _count; i++) if (i != index) _cards[i].Group.alpha = Mathf.Lerp(1f, 0.25f, k);
                await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
            }
        }
        catch (OperationCanceledException) { return; }
        pick.Root.localScale = Vector3.one;

        if (_parts)
        {
            RangedPartsDraft.Choose(index);
            ClosePopupUI();
            return;
        }
        MeleeAugmentDraft.Choose(index);
        var next = MeleeAugmentDraft.Pending;   // 놓친 단계가 남았으면 Choose 안에서 다음 화면이 열렸다
        if (next != null) { Show(next); return; }
        ClosePopupUI();
    }

    private void OnRerollClicked(int index)
    {
        if (_busy || !_revealed) return;
        if (_parts)
        {
            if (!RangedPartsDraft.Reroll()) { ShopUIStyle.PlaySfx("shop_reject"); return; }
            RerollAllAsync(_gen).Forget();
            return;
        }
        if (_offer == null || index < 0) return;
        if (!MeleeAugmentDraft.Reroll(index, new System.Random(unchecked(Environment.TickCount + index * 7919))))
        {
            ShopUIStyle.PlaySfx("shop_reject");
            return;
        }
        RerollAsync(index, _gen).Forget();
    }

    /// <summary>파츠 — 카드 전부를 접었다 펴며 새 카드로(장수는 그대로).</summary>
    private async UniTaskVoid RerollAllAsync(int gen)
    {
        _busy = true;
        RefreshRerolls(false);
        try
        {
            var flips = new UniTask[_count];
            for (int i = 0; i < _count; i++) flips[i] = FlipAsync(_cards[i], i, gen);
            await UniTask.WhenAll(flips);
        }
        catch (OperationCanceledException) { return; }
        if (gen != _gen) return;
        _busy = false;
        RefreshRerolls(true);
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
        int left = _parts ? RangedPartsDraft.RerollsLeft : MeleeAugmentDraft.RerollsAvailable;   // 런 몫 + 이번 방문 「풍요」
        if (_rerollAll != null)
        {
            bool onAll = _parts && usable && left > 0;
            _rerollAll.interactable  = onAll;
            _rerollAllLabel.text  = left > 0 ? $"카드 다시 뽑기 · 남은 {left}" : "다시 뽑기 · 다 썼다";
            _rerollAllLabel.color = onAll ? UITheme.Thin.InkA(0.85f) : UITheme.Thin.InkA(0.4f);
        }
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

    /// <summary>
    /// 자람 증강의 다음 재련 줄 — 「다음 재련  따라 치는 피해 19% → 23%」. 다 자랐으면(+21) 그렇다고 쓴다.
    /// 이름표(<see cref="MeleeAugmentCatalog.TryGrowth"/>의 now · now2): 그림자 파트너 = 따라 치는 피해 · 검풍 = 확률 · 검기 피해 ·
    /// 칼끝 베기 = 칼끝 추가 피해 · 휩쓸기 = 한 명당 R 대기(초) · 지속(초).
    /// </summary>
    private static string GrowthLine(MeleeAugmentDef def, WeaponData w)
    {
        if (w == null || !MeleeAugmentCatalog.TryGrowth(def.Id, w.enhanceLevel, out float now, out float next, out float now2, out float next2))
            return string.Empty;
        if (Mathf.Approximately(now, next) && Mathf.Approximately(now2, next2)) return "다 자랐다 — 더 재련해도 그대로";
        // 이름표 · 단위는 증강마다 — 휩쓸기는 초(10-26 대검 R 개편으로 자람 증강이 넷이 됐다)
        string body = def.Id switch
        {
            MeleeAugmentCatalog.KatanaShadow => $"따라 치는 피해 {now:0.#}% → {next:0.#}%",
            MeleeAugmentCatalog.KatanaGust   => $"확률 {now:0.#}% → {next:0.#}% · 검기 피해 {now2:0.#}% → {next2:0.#}%",
            MeleeAugmentCatalog.GreatTip     => $"칼끝 추가 피해 {now:0.#}% → {next:0.#}%",
            MeleeAugmentCatalog.GreatSweep   => $"한 명당 R 대기 −{now:0.##} → −{next:0.##}초 · 지속 +{now2:0.##} → +{next2:0.##}초",
            _                                => $"{now:0.##} → {next:0.##}",
        };
        return $"다음 재련  {body}";
    }

    /// <summary>카드 한 장의 등급 — 전설 카드만 화면 등급과 다르다(<c>card.Def.Tier</c>).</summary>
    private AugmentTier CardTier(int index)
    {
        var cards = _offer?.Cards;
        var def = cards != null && index < cards.Length ? cards[index].Def : null;
        return def != null && def.IsLegend ? AugmentTier.Legend : _offer != null ? _offer.Tier : AugmentTier.Silver;
    }

    private static Color TierColor(AugmentTier t) => t switch
    {
        AugmentTier.Legend    => LegendC,
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
        sound.PlayUiEvent(SoundEvent.UiHover, t == AugmentTier.Legend ? 1.45f : t == AugmentTier.Prismatic ? 1.3f : t == AugmentTier.Gold ? 1.15f : 1f, 0.6f);
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

    /// <summary>판(1120×748) 왼쪽 위 기준 px.</summary>
    private static void Put(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
    }

    /// <summary>카드(300×472 · 가운데 기준 루트) 안 왼쪽 위 기준 px.</summary>
    private static void CardPut(RectTransform rt, float x, float y, float w, float h) => Put(rt, x, y, w, h);

    private static void AddTrigger(EventTrigger trigger, EventTriggerType type, Action action)
    {
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(_ => action());
        trigger.triggers.Add(entry);
    }
}
