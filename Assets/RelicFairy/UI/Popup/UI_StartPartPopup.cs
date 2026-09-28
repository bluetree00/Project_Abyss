using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 시작 원거리 파츠 선택(베이스캠프 파츠 공방, 09-26 플레이어 레인 의뢰). 5종 중 하나를 Lv1로 들고 런을 시작한다.
///
/// <para>유물 파츠 드래프트(<see cref="UI_RelicPartDraftPopup"/>)와 <b>같은 규격</b> — 카드 폭은 개수로 산출하고,
/// 잠긴 파츠는 <see cref="UILockedSlot"/>에 파츠 이름과 해금처 한 줄. 검증된 그 창은 흔들지 않고 따로 짓는다
/// (그쪽은 유물 파츠 전용 · 택1 강제 · 「장착」).</para>
///
/// <para><b>강제 선택이 아니다</b> — ESC·닫기로 그냥 닫힌다(<see cref="ResultPartId"/> = null).
/// 시간정지 팝업이라 열려 있는 동안엔 연출이 돌지 않는다 → 호출측은 창이 닫힌 뒤 결과를 읽고 획득 연출을 튼다.</para>
///
/// <para>게임 쪽 서비스 이름에 묶이지 않게 카드 타입(<see cref="Card"/>)을 창이 갖는다.</para>
/// </summary>
public sealed class UI_StartPartPopup : UI_Popup
{
    // ── Constants ────────────────────────────────────────────
    private const float WindowW = 1440f;
    private const float WindowH = 760f;
    private const float CardW   = 300f;   // 폭 상한 — 5장이면 약 249로 줄어든다
    private const float CardH   = 520f;
    private const float CardGap = 28f;
    private const float CardY   = -14f;
    private const float CardSideMargin = 40f;
    private const float SelectedCardScale = 1.04f;

    private const float DiscTop   = -34f;
    private const float DiscSize  = 132f;
    private const float NameTop   = -186f;
    private const float DivTop    = -228f;
    private const float DescTop   = -242f;
    private const float DescH     = 150f;
    private const float EffectTop = -400f;
    private const float EffectH   = 44f;
    private const float BadgeTop  = -456f;
    private const float BadgeH    = 34f;

    private static readonly Color SelectBorder = new(0.88f, 0.72f, 0.32f, 1f);
    private static readonly Color CardSelected = new(0.20f, 0.17f, 0.10f, 1f);

    // ── Types ────────────────────────────────────────────────
    /// <summary>카드 한 장. 잠긴 파츠는 <see cref="Unlocked"/>=false · <see cref="UnlockHint"/>에 해금처 한 줄.</summary>
    public struct Card
    {
        public WeaponPartEntry Part;
        public bool            Unlocked;
        public string          UnlockHint;
    }

    private sealed class CardView
    {
        public string        PartId;
        public Image         Border;
        public Image         Fill;
        public RectTransform Rt;
    }

    // ── Private ──────────────────────────────────────────────
    private readonly List<CardView>   _cards       = new();
    private readonly List<GameObject> _lockedRoots = new();
    private string _currentPartId;
    private int    _selected = -1;
    private bool   _built;
    private bool   _skinned;
    private bool   _themed;   // 아트 없음 — 공통 언어(둥근 창·베벨 버튼)를 입혔다
    private Transform _windowRoot;
    private Image     _confirmBtnImg;
    private TMP_Text  _confirmLabel;
    private UniTaskCompletionSource _interactionTcs;

    // ── Properties ───────────────────────────────────────────
    public override bool BlocksGameplay => true;
    public override bool CloseOnEscape  => true;   // 강제 선택 아님 — 닫으면 대기도 끝난다(ClosePopupUI)

    /// <summary>고른 파츠 id. 그냥 닫았으면 null. 창이 닫힌 뒤에 읽는다.</summary>
    public string ResultPartId { get; private set; }

    // ── Lifecycle ────────────────────────────────────────────
    public override void Init()
    {
        base.Init();
        BuildChrome();
    }

    /// <summary>씬 전환의 일괄 정리처럼 ClosePopupUI를 거치지 않고 사라져도 대기는 끝낸다.</summary>
    protected override void OnDestroy()
    {
        _interactionTcs?.TrySetResult();
        base.OnDestroy();
    }

    // ── Public Methods ───────────────────────────────────────
    public UniTask WaitForInteractionAsync(System.Threading.CancellationToken ct)
    {
        _interactionTcs = new UniTaskCompletionSource();
        return _interactionTcs.Task.AttachExternalCancellation(ct);
    }

    /// <summary>카드를 주입한다. <paramref name="currentPartId"/>는 이번 판에 이미 고른 파츠(없으면 null) — 「장착 중」 표시.</summary>
    public void Setup(IReadOnlyList<Card> cards, string currentPartId)
    {
        _currentPartId = currentPartId;
        ResultPartId   = null;
        BuildCards(cards);

        int cur = _cards.FindIndex(c => c.PartId == currentPartId);
        SetSelected(cur, playSound: false);
    }

    /// <summary>
    /// 어떤 경로로 닫히든(ESC·닫기·확정) 대기를 끝낸다 — 호출측이 영영 기다리지 않게.
    /// <b>닫기가 먼저</b>다: 대기를 먼저 풀면 호출측 연출이 아직 스택에 남은 이 창의 시간정지 아래서 시작한다.
    /// </summary>
    public override void ClosePopupUI()
    {
        base.ClosePopupUI();
        _interactionTcs?.TrySetResult();
    }


    // ── Private Methods ──────────────────────────────────────
    private void BuildChrome()
    {
        if (_built) return;
        _built = true;

        ShopUIStyle.Stretch(GetComponent<RectTransform>());

        var veil = ShopUIStyle.MakeImage(transform, "Veil", ShopUIStyle.Veil, raycast: true);
        ShopUIStyle.Stretch(veil.rectTransform);

        var window = ShopUIStyle.MakeFrame(transform, "Window",
            ShopUIStyle.WindowBorder, ShopUIStyle.WindowFill, 2f, raycast: true);
        ShopUIStyle.Anchor((RectTransform)window.transform.parent,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(WindowW, WindowH));
        _windowRoot = window.transform;
        window.transform.parent.gameObject.AddComponent<UIWindowFitter>().Configure();

        var titleBar = ShopUIStyle.MakeImage(_windowRoot, "TitleBar", ShopUIStyle.BandFill);
        ShopUIStyle.Anchor(titleBar.rectTransform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -14f), new Vector2(900f, 72f));
        var title = ShopUIStyle.MakeText(titleBar.transform, "Title", 30f, FontStyles.Bold,
            TextAlignmentOptions.Center, ShopUIStyle.TextPrimary);
        ShopUIStyle.Stretch(title.rectTransform);
        title.text = "파츠 공방";

        var sub = ShopUIStyle.MakeText(_windowRoot, "Subtitle", 17f, FontStyles.Normal,
            TextAlignmentOptions.Center, ShopUIStyle.TextDim);
        ShopUIStyle.Anchor(sub.rectTransform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -96f), new Vector2(900f, 26f));
        sub.text = "원거리 무기에 끼울 파츠를 하나 고르세요 — 1단계로 시작하고, 심연에 들기 전까지 바꿀 수 있습니다";

        // 닫기 — 강제 선택이 아니다
        var close = ShopUIStyle.MakeFrame(_windowRoot, "CloseBtn",
            ShopUIStyle.BronzeLine, ShopUIStyle.BandFill, 2f, raycast: true);
        ShopUIStyle.Anchor((RectTransform)close.transform.parent,
            new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-20f, -26f), new Vector2(96f, 44f));
        AddClick(close.transform.parent.gameObject, OnCloseClicked);
        var closeLabel = ShopUIStyle.MakeText(close.transform, "Label", 18f, FontStyles.Bold,
            TextAlignmentOptions.Center, ShopUIStyle.TextPrimary);
        ShopUIStyle.Stretch(closeLabel.rectTransform);
        closeLabel.text = "닫기";

        var confirm = ShopUIStyle.MakeFrame(_windowRoot, "ConfirmBtn",
            ShopUIStyle.BronzeLine, ShopUIStyle.BandFill, 2f, raycast: true);
        ShopUIStyle.Anchor((RectTransform)confirm.transform.parent,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 30f), new Vector2(240f, 56f));
        _confirmBtnImg = confirm;
        AddClick(confirm.transform.parent.gameObject, OnConfirmClicked);
        _confirmLabel = ShopUIStyle.MakeText(confirm.transform, "Label", 20f, FontStyles.Bold,
            TextAlignmentOptions.Center, ShopUIStyle.TextPrimary);
        ShopUIStyle.Stretch(_confirmLabel.rectTransform);
        _confirmLabel.text = "이 파츠로";

        // 유물 개화와 같은 아트(창·띠·카드·버튼) — 미납품이면 색 박스 그대로
        var skin = UISkin.RelicPartDraft;
        _skinned = skin != null && skin.cardFrame != null;
        ShopUIStyle.Skin(window.transform.parent.GetComponent<Image>(), skin?.background);
        ShopUIStyle.Skin(titleBar, skin?.titleBar, sliced: true);
        ShopUIStyle.Skin(_confirmBtnImg, skin?.confirmButton, sliced: true);

        // 아트가 없을 때의 공통 언어 — 둥근 창 + 금 가는 선 · 공통 베벨 버튼(09-28 UI 톤 진단 D4, 개화와 같은 규칙)
        if (!_skinned)
        {
            UITheme.RoundFrame(window, 18f);
            if (window.transform.parent.TryGetComponent<Image>(out var edge)) edge.color = UITheme.GoldLine;
            _themed = UITheme.StyleFrameButton(_confirmBtnImg, UITheme.CtaTintOff);
            UITheme.StyleFrameButton(close, UITheme.SecondaryTint);
            TMPOutlineHelper.ApplySoftShadow(_confirmLabel);
            TMPOutlineHelper.ApplySoftShadow(closeLabel);
            TMPOutlineHelper.ApplySoftShadow(title);
        }
    }

    private void BuildCards(IReadOnlyList<Card> cards)
    {
        foreach (var c in _cards) if (c.Rt != null) Destroy(c.Rt.gameObject);
        _cards.Clear();
        foreach (var g in _lockedRoots) if (g != null) Destroy(g);
        _lockedRoots.Clear();
        if (cards == null || cards.Count == 0) return;

        var layer = UIProportional.EnsureScaledLayer(_windowRoot, "CardLayer", WindowW, WindowH) ?? (RectTransform)_windowRoot;

        // 잠긴 파츠도 줄의 한 칸 — 데이터 순서 그대로 둔다(해금하면 「그 자리」가 열린다).
        int slots = cards.Count;
        float avail = WindowW - CardSideMargin * 2f - (slots - 1) * CardGap;
        float cardW = Mathf.Min(CardW, avail / slots);
        float startX = -(slots * cardW + (slots - 1) * CardGap) * 0.5f + cardW * 0.5f;

        for (int i = 0; i < slots; i++)
        {
            var c = cards[i];
            if (c.Part == null) continue;
            var pos = new Vector2(startX + i * (cardW + CardGap), CardY);

            if (!c.Unlocked)
            {
                var hint = string.IsNullOrEmpty(c.UnlockHint) ? "기억의 제단에서 해금" : c.UnlockHint;
                var locked = UILockedSlot.Build(layer, $"Locked_{c.Part.part_id}", pos, new Vector2(cardW, CardH), hint);
                var lockTitle = ShopUIStyle.FindDeep(locked.transform, "LockTitle");
                if (lockTitle != null && lockTitle.TryGetComponent<TMP_Text>(out var lt))
                    lt.text = c.Part.part_name ?? c.Part.part_id;   // 무엇이 잠겼는지 보여야 열러 갈 이유가 생긴다
                _lockedRoots.Add(locked);
                continue;
            }

            var fill = ShopUIStyle.MakeFrame(layer, $"Card_{c.Part.part_id}",
                ShopUIStyle.CardBorder, ShopUIStyle.CardFill, 2f, raycast: true);
            var rt = (RectTransform)fill.transform.parent;
            ShopUIStyle.Anchor(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                pos, new Vector2(cardW, CardH));

            var view = new CardView { PartId = c.Part.part_id, Border = rt.GetComponent<Image>(), Fill = fill, Rt = rt };
            var skin = UISkin.RelicPartDraft;
            ShopUIStyle.Skin(view.Border, skin?.cardFrame, sliced: true);
            ShopUIStyle.Skin(view.Fill,   skin?.cardFill,  sliced: true);
            if (!_skinned) UITheme.RoundFrame(view.Fill, 14f);
            int idx = _cards.Count;
            _cards.Add(view);
            AddClick(rt.gameObject, () => SetSelected(idx, playSound: true));

            BuildCardContent(fill.transform, c.Part, cardW, c.Part.part_id == _currentPartId);
        }
    }

    private static void BuildCardContent(Transform card, WeaponPartEntry def, float cardW, bool isCurrent)
    {
        Color kc = KindColor(def.Kind);

        var ribbon = ShopUIStyle.MakeImage(card, "Ribbon", kc);
        ShopUIStyle.Anchor(ribbon.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -3f), new Vector2(-28f, 6f));
        ribbon.sprite = UITheme.SoftBand;   // 끝이 흐려지는 빛줄기(09-28 UI 톤 통일)

        // 계열 문양 — 전용 아트가 아직 없다(09-26 리소스 확인). 계열색 원반 + 계열 이름.
        var disc = ShopUIStyle.MakeImage(card, "KindDisc", new Color(kc.r * 0.22f, kc.g * 0.22f, kc.b * 0.22f, 1f));
        ShopUIStyle.Anchor(disc.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, DiscTop), new Vector2(DiscSize, DiscSize));
        disc.sprite = UIProceduralSprites.RoundedRect(radius: 52f, feather: 26f, size: 168);
        var mark = ShopUIStyle.MakeText(disc.transform, "KindMark", 34f, FontStyles.Bold, TextAlignmentOptions.Center, kc);
        ShopUIStyle.Stretch(mark.rectTransform);
        mark.text = KindName(def.Kind);

        var name = ShopUIStyle.MakeText(card, "Name", 24f, FontStyles.Bold, TextAlignmentOptions.Center, ShopUIStyle.TextPrimary);
        ShopUIStyle.Anchor(name.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, NameTop), new Vector2(cardW - 24f, 34f));
        name.text = def.part_name ?? def.part_id;

        var divider = ShopUIStyle.MakeImage(card, "Divider", ShopUIStyle.BronzeLine);
        ShopUIStyle.Anchor(divider.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, DivTop), new Vector2(cardW - 60f, 2f));

        var desc = ShopUIStyle.MakeText(card, "Desc", 17f, FontStyles.Normal, TextAlignmentOptions.Top, ShopUIStyle.TextPrimary);
        ShopUIStyle.Anchor(desc.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, DescTop), new Vector2(cardW - 36f, DescH));
        desc.textWrappingMode = TextWrappingModes.Normal;
        desc.text = def.description ?? string.Empty;

        // 1단계 효과 — 재련소 파츠 줄과 같은 문구(「2발」「관통 1」…)
        var band = ShopUIStyle.MakeImage(card, "EffectBand", new Color(kc.r * 0.16f, kc.g * 0.16f, kc.b * 0.16f, 1f));
        ShopUIStyle.Anchor(band.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, EffectTop), new Vector2(cardW - 24f, EffectH));
        band.sprite = UIProceduralSprites.RoundedRect(radius: 12f, feather: 6f, size: 64);
        var eff = ShopUIStyle.MakeText(band.transform, "Effect", 18f, FontStyles.Bold, TextAlignmentOptions.Center, kc);
        ShopUIStyle.Stretch(eff.rectTransform);
        eff.text = $"1단계 · {UI_CruciblePanel.PartValueText(def, def.ValueAt(1))}";

        if (isCurrent)
        {
            var badge = ShopUIStyle.MakeImage(card, "CurrentBadge", ShopUIStyle.GoldPillBg);
            ShopUIStyle.Anchor(badge.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, BadgeTop), new Vector2(120f, BadgeH));
            badge.sprite = UIProceduralSprites.RoundedRect(radius: 12f, feather: 4f, size: 64);
            var bl = ShopUIStyle.MakeText(badge.transform, "Label", 17f, FontStyles.Bold, TextAlignmentOptions.Center, ShopUIStyle.Gold);
            ShopUIStyle.Stretch(bl.rectTransform);
            bl.text = "장착 중";
        }
    }

    private void SetSelected(int index, bool playSound)
    {
        if (playSound && index >= 0) Managers.Sound.PlayUiAsync(SoundKey.Sfx.UiButton).Forget();
        _selected = index;
        for (int i = 0; i < _cards.Count; i++)
        {
            bool on = i == index;
            var v = _cards[i];
            if (v.Border != null)
                v.Border.color = _skinned ? (on ? Color.white : new Color(0.62f, 0.62f, 0.66f))
                                          : (on ? SelectBorder : ShopUIStyle.CardBorder);
            if (v.Fill != null)
                v.Fill.color = _skinned ? Color.white : (on ? CardSelected : ShopUIStyle.CardFill);
            if (v.Rt != null) v.Rt.localScale = Vector3.one * (on ? SelectedCardScale : 1f);
        }
        RefreshConfirm();
    }

    private void RefreshConfirm()
    {
        bool has = _selected >= 0;
        bool same = has && _cards[_selected].PartId == _currentPartId;
        if (_confirmLabel != null)
        {
            _confirmLabel.text  = !has ? "파츠를 고르세요" : same ? "장착 중" : (_currentPartId != null ? "이 파츠로 바꾸기" : "이 파츠로");
            _confirmLabel.color = has && !same ? ShopUIStyle.TextPrimary : ShopUIStyle.TextDim;
        }
        if (_confirmBtnImg != null && !_skinned)
            _confirmBtnImg.color = _themed ? (has && !same ? UITheme.CtaTint : UITheme.CtaTintOff)
                                           : (has && !same ? ShopUIStyle.GoldPillBg : ShopUIStyle.BandFill);
    }

    private void OnConfirmClicked()
    {
        if (_selected < 0 || _selected >= _cards.Count || _cards[_selected].PartId == _currentPartId)
        {
            ShopUIStyle.PlaySfx("shop_reject");   // 말없이 무시하면 버튼이 죽은 것으로 읽힌다 — 라벨이 사유를 말한다
            return;
        }
        Managers.Sound.PlayUiAsync(SoundKey.Sfx.UiButton).Forget();
        ResultPartId = _cards[_selected].PartId;
        ClosePopupUI();   // 창이 먼저 닫혀야 호출측 획득 연출이 돈다(시간정지 해제)
    }

    private void OnCloseClicked()
    {
        Managers.Sound.PlayUiAsync(SoundKey.Sfx.UiButton).Forget();
        ClosePopupUI();
    }

    private static void AddClick(GameObject go, Action onClick)
    {
        if (!go.TryGetComponent<Button>(out var btn)) btn = go.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(() => onClick?.Invoke());
    }

    private static string KindName(RangedPartKind k) => k switch
    {
        RangedPartKind.Split   => "분열",
        RangedPartKind.Pierce  => "관통",
        RangedPartKind.Explode => "작렬",
        RangedPartKind.Homing  => "추적",
        _                      => "거력",
    };

    // 계열색 — 원색(특히 빨강)은 피한다(09-26 전환 연출 피드백과 같은 기준).
    private static Color KindColor(RangedPartKind k) => k switch
    {
        RangedPartKind.Split   => new Color(0.95f, 0.78f, 0.40f, 1f),   // 호박
        RangedPartKind.Pierce  => new Color(0.60f, 0.78f, 0.96f, 1f),   // 강철 하늘
        RangedPartKind.Explode => new Color(0.96f, 0.62f, 0.45f, 1f),   // 산호
        RangedPartKind.Homing  => new Color(0.50f, 0.90f, 0.80f, 1f),   // 청록
        _                      => new Color(0.82f, 0.68f, 0.96f, 1f),   // 연보라
    };
}
