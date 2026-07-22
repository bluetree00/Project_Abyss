using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 상점 UI 패널 (Canvas_Popup, Addressable "UI/Popup/UI_ShopPanel").
///
/// 다크 판타지·유물 톤의 상점 화면:
///  - 상단 헤더: NPC 초상화 + 이름 + 대사, 우측 골드 표시(코인+수량, 실시간)
///  - 중앙: 아이템 카드 그리드(등급 위계/hover/구매·거부 피드백 — UI_ShopSlotView)
///  - 하단 푸터: 리롤(피처 플래그 on일 때만) / 나가기, 우상단 닫기 X
///
/// 데이터/구매/리롤은 ShopRoomController가 권위. 위젯은 절차 생성(ShopUIStyle 사용),
/// 스프라이트/사운드는 ShopUIStyle 훅으로 나중에 교체.
/// </summary>
public sealed class UI_ShopPanel : UI_Popup
{
    public override bool BlocksGameplay => true; // 상점 이용 중 시간정지 + 입력잠금
    public override bool CloseOnEscape  => true; // ESC = 나가기(기존 동작, EscKeyListener 공용 경로)

    private const float WindowW = 1100f;
    // 카드 셀 높이를 308로 올려 셀 내부 여유(=308-8-292=8px)를 확보하고, 5개 이상(2행) 슬롯이
    // 그리드 영역(=WindowH-104-150=658)에 들어가도록 윈도우 높이를 912로 키운다.
    // (2행 필요 높이 = 308*2 + 22 spacing + 12 padding = 650 ≤ 658)
    private const float WindowH = 912f;
    private static readonly Vector2 CellSize = new(218f, 308f);

    private ShopRoomController _controller;
    private readonly List<UI_ShopSlotView> _views = new();

    private TMP_Text _goldText;
    private RectTransform _slotGrid;
    private Button _rerollButton;
    private TMP_Text _rerollLabel;
    private bool _built;
    private bool _closing;

    // ── Lifecycle ───────────────────────────────────────────

    public override void Init()
    {
        base.Init();
        BuildChrome();
    }

    private void OnDestroy()
    {
        if (_controller != null)
        {
            _controller.OnShopChanged -= RefreshAll;
            _controller.NotifyPanelClosed();
        }
    }

    // ── Public API ──────────────────────────────────────────

    public void Bind(ShopRoomController controller)
    {
        _controller = controller;
        if (_controller != null)
            _controller.OnShopChanged += RefreshAll;

        ShopUIStyle.PlaySfx("shop_open");
        SetupRerollButton();
        PopulateSlots();
        RefreshAll();
    }

    public override void ClosePopupUI()
    {
        if (_closing) return;
        _closing = true;
        ShopUIStyle.PlaySfx("shop_close");
        base.ClosePopupUI();
    }

    // ── 빌드 ────────────────────────────────────────────────

    private void BuildChrome()
    {
        if (_built) return;
        _built = true;

        ShopUIStyle.Stretch(GetComponent<RectTransform>());

        // 배경 베일(뒤 입력 차단)
        var veil = ShopUIStyle.MakeImage(transform, "Veil", ShopUIStyle.Veil, raycast: true);
        ShopUIStyle.Stretch(veil.rectTransform);

        // 윈도우(브론즈 테두리 프레임)
        var fill = ShopUIStyle.MakeFrame(transform, "Window", ShopUIStyle.WindowBorder, ShopUIStyle.WindowFill, 4f, raycast: true);
        var windowRT = (RectTransform)fill.transform.parent;
        ShopUIStyle.Anchor(windowRT, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                           Vector2.zero, new Vector2(WindowW, WindowH));
        var w = fill.transform;

        BuildHeader(w);
        BuildGrid(w);
        BuildFooter(w);
        BuildCloseButton(w);
    }

    private void BuildHeader(Transform w)
    {
        var header = ShopUIStyle.MakeImage(w, "Header", ShopUIStyle.BandFill);
        ShopUIStyle.Anchor(header.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
                           new Vector2(0, 0), new Vector2(0, 132));
        var h = header.transform;

        // 브론즈 밑줄
        var line = ShopUIStyle.MakeImage(h, "Underline", ShopUIStyle.BronzeLine);
        ShopUIStyle.Anchor(line.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0),
                           new Vector2(0, 0), new Vector2(0, 3));

        // NPC 초상화(플레이스홀더 프레임)
        var portrait = ShopUIStyle.MakeFrame(h, "Portrait", ShopUIStyle.BronzeLine, ShopUIStyle.PortraitBg, 2f);
        var portraitRT = (RectTransform)portrait.transform.parent;
        ShopUIStyle.Anchor(portraitRT, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f),
                           new Vector2(28, 0), new Vector2(96, 96));
        var pq = ShopUIStyle.MakeText(portrait.transform, "Q", 40f, FontStyles.Bold,
                                      TextAlignmentOptions.Center, ShopUIStyle.BronzeLine);
        pq.text = "유";
        ShopUIStyle.Stretch(pq.rectTransform);

        // NPC 이름
        var npcName = ShopUIStyle.MakeText(h, "NpcName", 26f, FontStyles.Bold,
                                           TextAlignmentOptions.MidlineLeft, ShopUIStyle.Gold);
        npcName.text = "유물상";
        ShopUIStyle.Anchor(npcName.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1),
                           new Vector2(140, -22), new Vector2(-420, 40));

        // NPC 대사
        var dialog = ShopUIStyle.MakeText(h, "Dialog", 16f, FontStyles.Italic,
                                          TextAlignmentOptions.MidlineLeft, ShopUIStyle.TextDim);
        dialog.text = "쓸 만한 물건이 좀 있지… 골드만 있다면.";
        ShopUIStyle.Anchor(dialog.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1),
                           new Vector2(140, -64), new Vector2(-420, 40));

        // 골드 표시 (코인 + 수량 pill)
        var pill = ShopUIStyle.MakeRect(h, "GoldPill", typeof(Image), typeof(HorizontalLayoutGroup));
        pill.GetComponent<Image>().color = ShopUIStyle.GoldPillBg;
        ShopUIStyle.Anchor((RectTransform)pill.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1),
                           new Vector2(-74, -26), new Vector2(248, 50));
        var phlg = pill.GetComponent<HorizontalLayoutGroup>();
        phlg.padding = new RectOffset(16, 16, 4, 4);
        phlg.spacing = 8f;
        phlg.childAlignment = TextAnchor.MiddleLeft;
        phlg.childControlWidth = true; phlg.childControlHeight = true;
        phlg.childForceExpandWidth = false; phlg.childForceExpandHeight = false;
        ShopUIStyle.MakeCoin(pill.transform, 24f);
        _goldText = ShopUIStyle.MakeText(pill.transform, "Gold", 24f, FontStyles.Bold,
                                         TextAlignmentOptions.MidlineLeft, ShopUIStyle.Gold);
        var goldLE = _goldText.gameObject.AddComponent<LayoutElement>();
        goldLE.flexibleWidth = 1f;
    }

    private void BuildGrid(Transform w)
    {
        var gridGo = ShopUIStyle.MakeRect(w, "SlotGrid", typeof(GridLayoutGroup));
        _slotGrid = (RectTransform)gridGo.transform;
        ShopUIStyle.StretchOffsets(_slotGrid, 44f, 104f, 44f, 150f);

        var grid = gridGo.GetComponent<GridLayoutGroup>();
        grid.cellSize = CellSize;
        grid.spacing = new Vector2(22f, 22f);
        grid.padding = new RectOffset(6, 6, 6, 6);
        grid.childAlignment = TextAnchor.UpperCenter;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 4;
    }

    private void BuildFooter(Transform w)
    {
        var footer = ShopUIStyle.MakeImage(w, "Footer", ShopUIStyle.BandFill);
        ShopUIStyle.Anchor(footer.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0),
                           new Vector2(0, 0), new Vector2(0, 84));
        var f = footer.transform;

        var topline = ShopUIStyle.MakeImage(f, "Topline", ShopUIStyle.BronzeLine);
        ShopUIStyle.Anchor(topline.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
                           new Vector2(0, 0), new Vector2(0, 3));

        // 리롤 (좌측, 피처 on일 때만 노출)
        _rerollButton = MakeStyledButton(f, "Reroll", "리롤", out _rerollLabel);
        ShopUIStyle.Anchor((RectTransform)_rerollButton.transform, new Vector2(0, 0.5f), new Vector2(0, 0.5f),
                           new Vector2(0, 0.5f), new Vector2(28, 0), new Vector2(220, 52));
        _rerollButton.onClick.AddListener(OnRerollClicked);
        _rerollButton.gameObject.SetActive(false);

        // 나가기 (우측)
        var exitBtn = MakeStyledButton(f, "Exit", "나가기", out _);
        ShopUIStyle.Anchor((RectTransform)exitBtn.transform, new Vector2(1, 0.5f), new Vector2(1, 0.5f),
                           new Vector2(1, 0.5f), new Vector2(-28, 0), new Vector2(180, 52));
        exitBtn.onClick.AddListener(ClosePopupUI);
    }

    private void BuildCloseButton(Transform w)
    {
        var close = MakeStyledButton(w, "Close", "✕", out var lbl);
        lbl.color = ShopUIStyle.TextPrimary;
        close.GetComponent<Image>().color = new Color(0.5f, 0.16f, 0.16f, 1f);
        ShopUIStyle.Anchor((RectTransform)close.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1),
                           new Vector2(-12, -12), new Vector2(50, 50));
        close.onClick.AddListener(ClosePopupUI);
    }

    private static Button MakeStyledButton(Transform parent, string name, string label, out TMP_Text labelText)
    {
        var go = ShopUIStyle.MakeRect(parent, name, typeof(Image), typeof(Button));
        go.GetComponent<Image>().color = ShopUIStyle.BuyFill;
        var btn = go.GetComponent<Button>();
        var cb = btn.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
        cb.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        cb.fadeDuration = 0.08f;
        btn.colors = cb;
        labelText = ShopUIStyle.MakeText(go.transform, "Label", 18f, FontStyles.Bold,
                                         TextAlignmentOptions.Center, ShopUIStyle.Gold);
        labelText.text = label;
        ShopUIStyle.Stretch(labelText.rectTransform);
        return btn;
    }

    private void SetupRerollButton()
    {
        if (_rerollButton == null) return;
        bool on = _controller != null && _controller.RerollEnabled;
        _rerollButton.gameObject.SetActive(on);
        if (on && _rerollLabel != null)
            _rerollLabel.text = _controller.RerollCost > 0 ? $"리롤  ●{_controller.RerollCost}" : "리롤";
    }

    private void PopulateSlots()
    {
        if (_controller == null || _slotGrid == null) return;
        int needed = _controller.Slots.Count;

        if (_views.Count != needed)
        {
            for (int i = _slotGrid.childCount - 1; i >= 0; i--)
                Destroy(_slotGrid.GetChild(i).gameObject);
            _views.Clear();
            for (int i = 0; i < needed; i++)
                _views.Add(UI_ShopSlotView.Create(_slotGrid, CellSize));
        }

        for (int i = 0; i < needed; i++)
            _views[i].Bind(_controller.Slots[i], i, OnBuyClicked);
    }

    // ── 핸들러 ──────────────────────────────────────────────

    private void OnBuyClicked(int index)
    {
        if (_controller == null) return;
        var result = _controller.TryPurchaseSlot(index);
        var view = index >= 0 && index < _views.Count ? _views[index] : null;

        switch (result)
        {
            case ShopPurchaseResult.Success:
                view?.PlayPurchasePop();
                break;
            case ShopPurchaseResult.InsufficientGold:
            case ShopPurchaseResult.Failed:
            case ShopPurchaseResult.Unavailable:
                view?.PlayRejectShake();
                break;
            case ShopPurchaseResult.PendingAsync:
                // 무기 교체 팝업 진행 — 결과는 OnShopChanged로 반영
                break;
        }
        RefreshAll();
    }

    private void OnRerollClicked()
    {
        if (_controller == null) return;
        if (_controller.TryReroll())
        {
            ShopUIStyle.PlaySfx("shop_reroll");
            PopulateSlots(); // 슬롯 객체 교체 → 재바인딩
        }
        else
        {
            ShopUIStyle.PlaySfx("shop_reject");
            Debug.Log("[ShopPanel] 리롤 실패(골드 부족/비활성)");
        }
        RefreshAll();
    }

    private void RefreshAll()
    {
        if (_controller == null) return;

        int gold = _controller.PlayerGold;
        if (_goldText != null)
            _goldText.text = gold.ToString();

        var slots = _controller.Slots;
        for (int i = 0; i < _views.Count && i < slots.Count; i++)
            _views[i].Refresh(slots[i], slots[i].Price <= gold);
    }
}
