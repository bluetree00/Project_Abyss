using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 유물 파츠 드래프트 팝업 (Canvas_Popup, Addressable "UI_RelicPartDraftPopup").
///
/// 보스 클리어 보상 = <b>파츠 후보 3개 중 1개 선택</b>(개화 = 런 내 임시 성장).
/// 룬 선택 팝업(<see cref="UI_RuneSelectPopup"/>)과 3지선다 골격을 공유하되,
/// 카드 내용이 다르다 — 모양/인벤토리 대신 <b>종류(코어/효과/행동/트리거) + 기능 설명</b>을 보여준다.
///
/// 파츠는 무료 성장이라 '넘기기'가 이득이 없다 → 택1 강제(넘기기 버튼 없음).
/// 후보가 비면 호출자가 팝업을 열지 않는다(이 팝업은 최소 1개 후보를 전제).
/// </summary>
public sealed class UI_RelicPartDraftPopup : UI_Popup
{
    public override bool BlocksGameplay => true;
    public override bool CloseOnEscape  => false;   // 보상 결정이라 실수로 닫히면 안 된다 — 선택으로만 종료

    // ── 레이아웃 ──
    private const float WindowW = 1100f;
    private const float WindowH = 600f;
    private const float CardW   = 300f;   // 카드 폭 상한 — 후보가 많으면 이 아래로 줄어든다
    private const float CardH   = 400f;
    private const float CardGap = 24f;
    private const float CardY   = -30f;
    private const float CardSideMargin = 40f;   // 카드 열 좌우 여백(창 안쪽)

    private static readonly Color CardSelected = new(0.20f, 0.17f, 0.10f, 1f);
    private static readonly Color SelectBorder = new(0.88f, 0.72f, 0.32f, 1f);

    // 종류별 강조색 — 코어(진화)만 금색으로 격상해 클라이맥스임을 알린다.
    private static readonly Color KindCore     = new(0.93f, 0.78f, 0.35f, 1f);
    private static readonly Color KindEffect   = new(0.72f, 0.55f, 0.92f, 1f);
    private static readonly Color KindBehavior = new(0.45f, 0.72f, 0.92f, 1f);
    private static readonly Color KindTrigger  = new(0.45f, 0.85f, 0.55f, 1f);

    // ── 상태 ──
    private readonly List<CardView> _cards = new();
    private List<RelicPartEntry> _candidates;
    private int _selected = -1;
    private bool _built;
    private float _cardW = CardW;   // 후보 수에 맞춰 산출된 실제 카드 폭

    private Transform _windowRoot;
    private TMP_Text  _subtitle;
    private Image     _confirmBtnImg;
    private TMP_Text  _confirmLabel;

    private UniTaskCompletionSource _interactionTcs;

    /// <summary>선택된 파츠. 미선택 종료(취소 경로)는 없다.</summary>
    public RelicPartEntry Result { get; private set; }

    private sealed class CardView
    {
        public Image      Border;
        public Image      Fill;
        public GameObject Root;
    }

    // ── Lifecycle ──

    public override void Init()
    {
        base.Init();
        BuildChrome();
    }

    // ── Public API ──

    public UniTask WaitForInteractionAsync(System.Threading.CancellationToken ct)
    {
        _interactionTcs = new UniTaskCompletionSource();
        return _interactionTcs.Task.AttachExternalCancellation(ct);
    }

    /// <summary>후보를 주입해 카드를 구성한다. ShowPopupUIAndGetAsync 직후 호출.</summary>
    public void Setup(List<RelicPartEntry> candidates)
    {
        _candidates = candidates;

        if (candidates == null || candidates.Count == 0)
        {
            Debug.LogWarning("[UI_RelicPartDraftPopup] 후보 없음 — 즉시 닫음");
            _interactionTcs?.TrySetResult();
            ClosePopupUI();
            return;
        }

        // 코어(진화) 드래프트면 부제를 바꿔 무게감을 준다.
        bool isCore = candidates[0].part_kind == RelicPartKind.Core;
        if (_subtitle != null)
            _subtitle.text = isCore
                ? "유물이 각성합니다 — 진화의 방향을 정하세요"
                : "이번 런의 유물을 키울 파츠를 하나 고르세요";

        BuildCards();
        SetSelected(-1);
    }

    // ── Build ──

    private void BuildChrome()
    {
        if (_built) return;
        _built = true;

        ShopUIStyle.Stretch(GetComponent<RectTransform>());

        var veil = ShopUIStyle.MakeImage(transform, "Veil", ShopUIStyle.Veil, raycast: true);
        ShopUIStyle.Stretch(veil.rectTransform);

        // MakeFrame은 inner(채움)를 반환한다 — 위치/크기는 부모(테두리)에 건다.
        var window = ShopUIStyle.MakeFrame(transform, "Window",
            ShopUIStyle.WindowBorder, ShopUIStyle.WindowFill, 2f, raycast: true);
        ShopUIStyle.Anchor((RectTransform)window.transform.parent,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(WindowW, WindowH));
        _windowRoot = window.transform;

        // 제목
        var title = ShopUIStyle.MakeText(_windowRoot, "Title", 26f, FontStyles.Bold,
            TextAlignmentOptions.Left, ShopUIStyle.TextPrimary);
        ShopUIStyle.Anchor(title.rectTransform,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(34f, -26f), new Vector2(600f, 36f));
        title.text = "유물 개화";

        // 부제(Setup에서 코어/기능에 맞게 갱신)
        _subtitle = ShopUIStyle.MakeText(_windowRoot, "Subtitle", 16f, FontStyles.Normal,
            TextAlignmentOptions.Left, ShopUIStyle.TextDim);
        ShopUIStyle.Anchor(_subtitle.rectTransform,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(35f, -60f), new Vector2(700f, 26f));
        _subtitle.text = "이번 런의 유물을 키울 파츠를 하나 고르세요";

        BuildFooter();
    }

    private void BuildFooter()
    {
        // [선택] — 파츠는 무료 성장이라 넘기기가 없다(택1 강제).
        var confirm = ShopUIStyle.MakeFrame(_windowRoot, "ConfirmBtn",
            ShopUIStyle.BronzeLine, ShopUIStyle.BandFill, 2f, raycast: true);
        ShopUIStyle.Anchor((RectTransform)confirm.transform.parent,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 30f), new Vector2(220f, 56f));
        _confirmBtnImg = confirm;
        AddClick(confirm.transform.parent.gameObject, OnConfirmClicked);

        _confirmLabel = ShopUIStyle.MakeText(confirm.transform, "Label", 20f, FontStyles.Bold,
            TextAlignmentOptions.Center, ShopUIStyle.TextPrimary);
        ShopUIStyle.Stretch(_confirmLabel.rectTransform);
        _confirmLabel.text = "장착";
    }

    private void BuildCards()
    {
        foreach (var c in _cards) if (c.Root != null) Destroy(c.Root);
        _cards.Clear();

        int n = _candidates.Count;

        // 카드 폭은 후보 수에 맞춰 줄인다. 300 고정이면 4지선다에서 카드가 창 밖으로 밀려난다.
        float avail = WindowW - CardSideMargin * 2f - (n - 1) * CardGap;
        _cardW = Mathf.Min(CardW, avail / Mathf.Max(1, n));

        float totalW = n * _cardW + (n - 1) * CardGap;
        float startX = -totalW * 0.5f + _cardW * 0.5f;

        for (int i = 0; i < n; i++)
        {
            int idx = i;   // 클로저 캡처
            var entry = _candidates[i];

            var card = ShopUIStyle.MakeFrame(_windowRoot, $"Card{i}",
                ShopUIStyle.CardBorder, ShopUIStyle.CardFill, 2f, raycast: true);
            var cardRT = (RectTransform)card.transform.parent;   // 위치/크기는 테두리(outer)에
            ShopUIStyle.Anchor(cardRT,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(startX + i * (_cardW + CardGap), CardY), new Vector2(_cardW, CardH));

            AddClick(cardRT.gameObject, () => SetSelected(idx));

            _cards.Add(new CardView
            {
                Root   = cardRT.gameObject,
                Border = cardRT.GetComponent<Image>(),
                Fill   = card,
            });

            BuildCardContent(card.transform, entry);
        }
    }

    private void BuildCardContent(Transform card, RelicPartEntry entry)
    {
        Color kindColor = KindColorOf(entry.part_kind);

        // 종류 리본(상단 띠)
        var ribbon = ShopUIStyle.MakeImage(card, "Ribbon", kindColor);
        ShopUIStyle.Anchor(ribbon.rectTransform,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            Vector2.zero, new Vector2(0f, 4f));

        // 종류 배지(칩)
        var kindBox = ShopUIStyle.MakeFrame(card, "KindChip", kindColor, ShopUIStyle.IconBg, 2f);
        ShopUIStyle.Anchor((RectTransform)kindBox.transform.parent,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -34f), new Vector2(150f, 40f));
        var kindLabel = ShopUIStyle.MakeText(kindBox.transform, "KindLabel", 17f, FontStyles.Bold,
            TextAlignmentOptions.Center, kindColor);
        ShopUIStyle.Stretch(kindLabel.rectTransform);
        kindLabel.text = KindLabelOf(entry.part_kind);

        // 이름
        var name = ShopUIStyle.MakeText(card, "Name", 22f, FontStyles.Bold,
            TextAlignmentOptions.Center, ShopUIStyle.TextPrimary);
        ShopUIStyle.Anchor(name.rectTransform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -92f), new Vector2(_cardW - 24f, 34f));
        name.text = entry.part_name ?? entry.part_id;

        // 구분선
        var divider = ShopUIStyle.MakeImage(card, "Divider", ShopUIStyle.BronzeLine);
        ShopUIStyle.Anchor(divider.rectTransform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -134f), new Vector2(_cardW - 60f, 2f));

        // 설명(기능) — 카드의 핵심. 여러 줄 허용.
        var desc = ShopUIStyle.MakeText(card, "Desc", 16f, FontStyles.Normal,
            TextAlignmentOptions.Top, ShopUIStyle.TextPrimary);
        ShopUIStyle.Anchor(desc.rectTransform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -150f), new Vector2(_cardW - 40f, 180f));
        desc.enableWordWrapping = true;
        desc.text = entry.description ?? string.Empty;
    }

    // ── 선택 상태 ──

    private void SetSelected(int index)
    {
        Managers.Sound.PlayUiAsync(SoundKey.Sfx.UiButton).Forget();
        _selected = index;

        for (int i = 0; i < _cards.Count; i++)
        {
            bool on = (i == index);
            if (_cards[i].Border != null)
                _cards[i].Border.color = on ? SelectBorder : ShopUIStyle.CardBorder;
            if (_cards[i].Fill != null)
                _cards[i].Fill.color = on ? CardSelected : ShopUIStyle.CardFill;
        }

        bool hasSel = index >= 0;
        if (_confirmLabel != null)
            _confirmLabel.color = hasSel ? ShopUIStyle.TextPrimary : ShopUIStyle.TextDim;
        if (_confirmBtnImg != null)
            _confirmBtnImg.color = hasSel ? ShopUIStyle.GoldPillBg : ShopUIStyle.BandFill;
    }

    // ── 버튼 ──

    private void OnConfirmClicked()
    {
        if (_selected < 0 || _candidates == null || _selected >= _candidates.Count)
            return;   // 미선택 — 아무 일도 하지 않는다

        Managers.Sound.PlayUiAsync(SoundKey.Sfx.UiButton).Forget();
        Result = _candidates[_selected];
        _interactionTcs?.TrySetResult();
        ClosePopupUI();
    }

    // ── Helpers ──

    private static void AddClick(GameObject go, Action onClick)
    {
        var btn = go.GetComponent<Button>() ?? go.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(() => onClick?.Invoke());
    }

    private static Color KindColorOf(string kind) => kind switch
    {
        RelicPartKind.Core     => KindCore,
        RelicPartKind.Effect   => KindEffect,
        RelicPartKind.Behavior => KindBehavior,
        RelicPartKind.Trigger  => KindTrigger,
        _                      => ShopUIStyle.TextDim,
    };

    private static string KindLabelOf(string kind) => kind switch
    {
        RelicPartKind.Core     => "◆ 진화",
        RelicPartKind.Effect   => "효과",
        RelicPartKind.Behavior => "행동",
        RelicPartKind.Trigger  => "트리거",
        _                      => "파츠",
    };
}
