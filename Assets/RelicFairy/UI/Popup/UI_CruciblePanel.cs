using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 재련소 UI 패널 (Canvas_Popup, Addressable "UI_CruciblePanel").
///
/// 무기 강화/승급 화면(다크 판타지·유물 톤, ShopUIStyle 재사용):
///  - 상단: 재련공 이름/대사 + 강화재료 표시(실시간)
///  - 중앙: 무기 2슬롯 카드(대상 택1 + 제물 토글) + 강화 정보(성공률/재료/실패하락/공격 전→후)
///  - 하단: 스트릭/결과 문구 + [강화] 버튼 (+ 강화 MAX 시 승급 전설 버튼)
///
/// 데이터/계산/결정성은 CrucibleRoomController가 권위. 위젯은 절차 생성.
/// </summary>
public sealed class UI_CruciblePanel : UI_Popup
{
    public override bool BlocksGameplay => true; // 재련 중 시간정지 + 입력잠금

    private const float WindowW = 900f;
    private const float WindowH = 680f;

    private CrucibleRoomController _controller;
    private int _targetSlot = PlayerWeaponManager.Slot0;
    private int _sacrificeSlot = -1;

    // 헤더
    private TMP_Text _fuelText;
    private TMP_Text _dialogText;

    // 슬롯 카드(2)
    private readonly Image[]    _cardBg    = new Image[2];
    private readonly TMP_Text[] _cardName  = new TMP_Text[2];
    private readonly TMP_Text[] _cardLevel = new TMP_Text[2];
    private readonly TMP_Text[] _cardAtk   = new TMP_Text[2];
    private readonly TMP_Text[] _sacLabel  = new TMP_Text[2];

    // 정보
    private TMP_Text _successText;
    private TMP_Text _costText;
    private TMP_Text _dropText;
    private TMP_Text _previewText;
    private TMP_Text _streakText;
    private TMP_Text _resultText;

    private Button   _enhanceBtn;
    private TMP_Text _enhanceLabel;
    private RectTransform _promoteRow;
    private readonly List<Button> _legendBtns = new();

    private bool _built;
    private bool _closing;

    // ── Lifecycle ───────────────────────────────────────────

    public override void Init()
    {
        base.Init();
        BuildChrome();
    }

    private void Update()
    {
        if (!_closing && Input.GetKeyDown(KeyCode.Escape))
            ClosePopupUI();
    }

    private void OnDestroy()
    {
        if (_controller != null)
        {
            _controller.OnCrucibleChanged -= RefreshAll;
            _controller.NotifyPanelClosed();
        }
    }

    // ── Public API ──────────────────────────────────────────

    public void Bind(CrucibleRoomController controller)
    {
        _controller = controller;
        if (_controller != null)
            _controller.OnCrucibleChanged += RefreshAll;

        ShopUIStyle.PlaySfx("shop_open");
        _targetSlot = PlayerWeaponManager.Slot0;
        _sacrificeSlot = -1;
        BuildLegendButtons();
        _dialogText.text = _controller.HasEvent ? _controller.EventBanner : _controller.GetDialogue(CrucibleMood.Idle);
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

        var veil = ShopUIStyle.MakeImage(transform, "Veil", ShopUIStyle.Veil, raycast: true);
        ShopUIStyle.Stretch(veil.rectTransform);

        var fill = ShopUIStyle.MakeFrame(transform, "Window", ShopUIStyle.WindowBorder, ShopUIStyle.WindowFill, 4f, raycast: true);
        var windowRT = (RectTransform)fill.transform.parent;
        ShopUIStyle.Anchor(windowRT, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                           Vector2.zero, new Vector2(WindowW, WindowH));
        var w = fill.transform;

        BuildHeader(w);
        BuildCards(w);
        BuildInfo(w);
        BuildActions(w);
        BuildCloseButton(w);
    }

    private void BuildHeader(Transform w)
    {
        var header = ShopUIStyle.MakeImage(w, "Header", ShopUIStyle.BandFill);
        ShopUIStyle.Anchor(header.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
                           new Vector2(0, 0), new Vector2(0, 120));
        var h = header.transform;

        var line = ShopUIStyle.MakeImage(h, "Underline", ShopUIStyle.BronzeLine);
        ShopUIStyle.Anchor(line.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0),
                           new Vector2(0, 0), new Vector2(0, 3));

        var title = ShopUIStyle.MakeText(h, "Title", 28f, FontStyles.Bold,
                                         TextAlignmentOptions.MidlineLeft, ShopUIStyle.Gold);
        title.text = "재련소";
        ShopUIStyle.Anchor(title.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1),
                           new Vector2(32, -18), new Vector2(-360, 42));

        _dialogText = ShopUIStyle.MakeText(h, "Dialog", 16f, FontStyles.Italic,
                                           TextAlignmentOptions.MidlineLeft, ShopUIStyle.TextDim);
        _dialogText.text = "쇠는 두드릴수록 강해지지… 운이 따라준다면 말이야.";
        ShopUIStyle.Anchor(_dialogText.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1),
                           new Vector2(32, -62), new Vector2(-360, 40));

        // 강화재료 pill
        var pill = ShopUIStyle.MakeRect(h, "FuelPill", typeof(Image), typeof(HorizontalLayoutGroup));
        pill.GetComponent<Image>().color = ShopUIStyle.GoldPillBg;
        ShopUIStyle.Anchor((RectTransform)pill.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1),
                           new Vector2(-24, -24), new Vector2(300, 50));
        var phlg = pill.GetComponent<HorizontalLayoutGroup>();
        phlg.padding = new RectOffset(16, 16, 4, 4);
        phlg.spacing = 8f;
        phlg.childAlignment = TextAnchor.MiddleRight;
        phlg.childControlWidth = true; phlg.childControlHeight = true;
        phlg.childForceExpandWidth = false; phlg.childForceExpandHeight = false;
        _fuelText = ShopUIStyle.MakeText(pill.transform, "Fuel", 22f, FontStyles.Bold,
                                         TextAlignmentOptions.MidlineRight, ShopUIStyle.Gold);
        var le = _fuelText.gameObject.AddComponent<LayoutElement>();
        le.flexibleWidth = 1f;
    }

    private void BuildCards(Transform w)
    {
        for (int i = 0; i < 2; i++)
        {
            int slot = i;
            var card = ShopUIStyle.MakeFrame(w, $"Card{i}", ShopUIStyle.CardBorder, ShopUIStyle.CardFill, 3f, raycast: true);
            _cardBg[i] = (Image)card.transform.parent.GetComponent<Image>();
            var cardRT = (RectTransform)card.transform.parent;
            float x = i == 0 ? 24f : 24f + 410f + 12f;
            ShopUIStyle.Anchor(cardRT, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                               new Vector2(x, -132), new Vector2(410, 200));
            var c = card.transform;

            var slotLabel = ShopUIStyle.MakeText(c, "Slot", 15f, FontStyles.Bold,
                                                 TextAlignmentOptions.TopLeft, ShopUIStyle.TextDim);
            slotLabel.text = i == 0 ? "슬롯 0 · 근접" : "슬롯 1 · 원거리";
            ShopUIStyle.Anchor(slotLabel.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1),
                               new Vector2(16, -12), new Vector2(-32, 24));

            _cardName[i] = ShopUIStyle.MakeText(c, "Name", 22f, FontStyles.Bold,
                                                TextAlignmentOptions.TopLeft, ShopUIStyle.TextPrimary);
            ShopUIStyle.Anchor(_cardName[i].rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1),
                               new Vector2(16, -40), new Vector2(-32, 32));

            _cardLevel[i] = ShopUIStyle.MakeText(c, "Level", 26f, FontStyles.Bold,
                                                 TextAlignmentOptions.TopLeft, ShopUIStyle.Gold);
            ShopUIStyle.Anchor(_cardLevel[i].rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1),
                               new Vector2(16, -78), new Vector2(-32, 34));

            _cardAtk[i] = ShopUIStyle.MakeText(c, "Atk", 17f, FontStyles.Normal,
                                               TextAlignmentOptions.TopLeft, ShopUIStyle.TextPrimary);
            ShopUIStyle.Anchor(_cardAtk[i].rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1),
                               new Vector2(16, -116), new Vector2(-32, 28));

            // 대상 선택 (카드 전체 버튼)
            var selBtn = card.transform.parent.gameObject.AddComponent<Button>();
            selBtn.transition = Selectable.Transition.None;
            selBtn.onClick.AddListener(() => { _targetSlot = slot; if (_sacrificeSlot == slot) _sacrificeSlot = -1; UpdateTargetDialogue(); RefreshAll(); });

            // 제물 토글
            var sacBtn = ShopUIStyle.MakeRect(c, "Sac", typeof(Image), typeof(Button));
            sacBtn.GetComponent<Image>().color = ShopUIStyle.BuyDisabled;
            ShopUIStyle.Anchor((RectTransform)sacBtn.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0),
                               new Vector2(-14, 14), new Vector2(120, 36));
            _sacLabel[i] = ShopUIStyle.MakeText(sacBtn.transform, "Label", 14f, FontStyles.Bold,
                                                TextAlignmentOptions.Center, ShopUIStyle.TextPrimary);
            _sacLabel[i].text = "제물";
            ShopUIStyle.Stretch(_sacLabel[i].rectTransform);
            sacBtn.GetComponent<Button>().onClick.AddListener(() => { ToggleSacrifice(slot); });
        }
    }

    private void BuildInfo(Transform w)
    {
        var panel = ShopUIStyle.MakeImage(w, "Info", ShopUIStyle.BandFill);
        ShopUIStyle.Anchor(panel.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
                           new Vector2(0, -344), new Vector2(-48, 118));
        var p = panel.transform;

        _successText = ShopUIStyle.MakeText(p, "Success", 18f, FontStyles.Bold,
                                            TextAlignmentOptions.TopLeft, ShopUIStyle.TextPrimary);
        ShopUIStyle.Anchor(_successText.rectTransform, new Vector2(0, 1), new Vector2(0.5f, 1), new Vector2(0, 1),
                           new Vector2(20, -12), new Vector2(-20, 28));

        _costText = ShopUIStyle.MakeText(p, "Cost", 18f, FontStyles.Bold,
                                         TextAlignmentOptions.TopLeft, ShopUIStyle.Gold);
        ShopUIStyle.Anchor(_costText.rectTransform, new Vector2(0.5f, 1), new Vector2(1, 1), new Vector2(0, 1),
                           new Vector2(10, -12), new Vector2(-20, 28));

        _dropText = ShopUIStyle.MakeText(p, "Drop", 16f, FontStyles.Normal,
                                         TextAlignmentOptions.TopLeft, ShopUIStyle.RejectRed);
        ShopUIStyle.Anchor(_dropText.rectTransform, new Vector2(0, 1), new Vector2(0.5f, 1), new Vector2(0, 1),
                           new Vector2(20, -44), new Vector2(-20, 26));

        _previewText = ShopUIStyle.MakeText(p, "Preview", 16f, FontStyles.Normal,
                                            TextAlignmentOptions.TopLeft, ShopUIStyle.TextDim);
        ShopUIStyle.Anchor(_previewText.rectTransform, new Vector2(0.5f, 1), new Vector2(1, 1), new Vector2(0, 1),
                           new Vector2(10, -44), new Vector2(-20, 26));

        _streakText = ShopUIStyle.MakeText(p, "Streak", 16f, FontStyles.Bold,
                                           TextAlignmentOptions.TopLeft, ShopUIStyle.Gold);
        ShopUIStyle.Anchor(_streakText.rectTransform, new Vector2(0, 1), new Vector2(0.5f, 1), new Vector2(0, 1),
                           new Vector2(20, -76), new Vector2(-20, 30));

        _resultText = ShopUIStyle.MakeText(p, "Result", 17f, FontStyles.Bold,
                                           TextAlignmentOptions.TopRight, ShopUIStyle.TextPrimary);
        ShopUIStyle.Anchor(_resultText.rectTransform, new Vector2(0.5f, 1), new Vector2(1, 1), new Vector2(0, 1),
                           new Vector2(10, -76), new Vector2(-20, 30));
    }

    private void BuildActions(Transform w)
    {
        _enhanceBtn = MakeStyledButton(w, "Enhance", "강화", out _enhanceLabel);
        ShopUIStyle.Anchor((RectTransform)_enhanceBtn.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0),
                           new Vector2(0.5f, 0), new Vector2(0, 88), new Vector2(300, 60));
        _enhanceBtn.onClick.AddListener(OnEnhanceClicked);

        var rowGo = ShopUIStyle.MakeRect(w, "PromoteRow", typeof(HorizontalLayoutGroup));
        _promoteRow = (RectTransform)rowGo.transform;
        ShopUIStyle.Anchor(_promoteRow, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0),
                           new Vector2(0, 20), new Vector2(560, 52));
        var hlg = rowGo.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = 10f;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true; hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true; hlg.childForceExpandHeight = true;

        var exitBtn = MakeStyledButton(w, "Exit", "나가기", out _);
        ShopUIStyle.Anchor((RectTransform)exitBtn.transform, new Vector2(1, 0), new Vector2(1, 0),
                           new Vector2(1, 0), new Vector2(-24, 20), new Vector2(160, 48));
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

    private void BuildLegendButtons()
    {
        foreach (var b in _legendBtns) if (b != null) Destroy(b.gameObject);
        _legendBtns.Clear();
        if (_controller == null || _promoteRow == null) return;

        foreach (var legend in _controller.Legends)
        {
            string legendId = legend.legendId;
            var btn = MakeStyledButton(_promoteRow, $"Legend_{legendId}", $"승급: {legend.displayName}", out _);
            btn.onClick.AddListener(() => OnPromoteClicked(legendId));
            _legendBtns.Add(btn);
        }
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
        cb.disabledColor = ShopUIStyle.BuyDisabled;
        cb.fadeDuration = 0.08f;
        btn.colors = cb;
        labelText = ShopUIStyle.MakeText(go.transform, "Label", 18f, FontStyles.Bold,
                                         TextAlignmentOptions.Center, ShopUIStyle.Gold);
        labelText.text = label;
        ShopUIStyle.Stretch(labelText.rectTransform);
        return btn;
    }

    // ── 핸들러 ──────────────────────────────────────────────

    /// <summary>대상 전환 시 — 성공률 낮으면 재련공이 도발.</summary>
    private void UpdateTargetDialogue()
    {
        if (_controller == null) return;
        if (_controller.CanEnhance(_targetSlot) && _controller.SuccessChanceAt(_targetSlot) < 0.4f)
            _dialogText.text = _controller.GetDialogue(CrucibleMood.Taunt);
    }

    private void ToggleSacrifice(int slot)
    {
        if (slot == _targetSlot) return;                 // 대상은 제물 불가
        _sacrificeSlot = _sacrificeSlot == slot ? -1 : slot;
        RefreshAll();
    }

    private void OnEnhanceClicked()
    {
        if (_controller == null) return;
        var result = _controller.TryEnhance(_targetSlot, _sacrificeSlot);
        ShowEnhanceResult(result);
        RefreshAll();
    }

    private void OnPromoteClicked(string legendId)
    {
        if (_controller == null) return;
        var result = _controller.TryPromote(_targetSlot, legendId);
        ShowPromoteResult(result);
        RefreshAll();
    }

    private void ShowEnhanceResult(EnhanceResult r)
    {
        switch (r.outcome)
        {
            case EnhanceOutcome.Success:
                _resultText.text = _controller.LastJackpot
                    ? $"<color=#FFD24A>잭팟! +{_controller.LastRefund} 환불</color>"
                    : $"<color=#7AD46E>성공! +{r.afterLevel}</color>";
                _resultText.color = ShopUIStyle.Gold;
                _dialogText.text = _controller.LastJackpot ? _controller.GetDialogue(CrucibleMood.Jackpot)
                                 : _controller.Streak >= 2 ? _controller.GetDialogue(CrucibleMood.Streak)
                                 : _controller.GetDialogue(CrucibleMood.Success);
                ShopUIStyle.PlaySfx("shop_open");
                break;
            case EnhanceOutcome.FailAbsorbed:
                _resultText.text = "<color=#FF8A50>실패 — 제물이 흡수</color>";
                _dialogText.text = _controller.GetDialogue(CrucibleMood.Fail);
                ShopUIStyle.PlaySfx("shop_reject");
                break;
            case EnhanceOutcome.FailDropped:
                _resultText.text = $"<color=#FF5250>실패 — 하락 (+{r.afterLevel})</color>";
                _dialogText.text = _controller.GetDialogue(CrucibleMood.Fail);
                ShopUIStyle.PlaySfx("shop_reject");
                break;
            case EnhanceOutcome.RejectNoFuel:
                _resultText.text = "<color=#FF5250>강화재료 부족</color>";
                ShopUIStyle.PlaySfx("shop_reject");
                break;
            case EnhanceOutcome.RejectMaxed:
                _resultText.text = "<color=#8AB0D5>이미 최대 강화</color>";
                break;
            default:
                _resultText.text = "<color=#FF5250>강화 불가</color>";
                break;
        }
    }

    private void ShowPromoteResult(PromoteResult r)
    {
        _resultText.text = r.outcome switch
        {
            PromoteOutcome.Success             => "<color=#FFD24A>전설로 승급!</color>",
            PromoteOutcome.RejectNoFuel        => "<color=#FF5250>강화재료 부족</color>",
            PromoteOutcome.RejectNotMaxed      => "<color=#FF5250>강화 MAX 필요</color>",
            PromoteOutcome.RejectAlreadyLegend => "<color=#8AB0D5>이미 승급됨</color>",
            _                                  => "<color=#FF5250>승급 불가</color>",
        };
        if (r.IsSuccess) ShopUIStyle.PlaySfx("shop_open");
        else ShopUIStyle.PlaySfx("shop_reject");
    }

    // ── 렌더 ────────────────────────────────────────────────

    private void RefreshAll()
    {
        if (_controller == null) return;

        _fuelText.text = $"강화재료 {_controller.FuelAmount}";

        for (int i = 0; i < 2; i++)
        {
            var w = _controller.GetSlot(i);
            bool isTarget = i == _targetSlot;
            bool isSac    = i == _sacrificeSlot;

            _cardBg[i].color = isTarget ? new Color(0.20f, 0.16f, 0.09f, 1f)
                                        : (isSac ? new Color(0.16f, 0.10f, 0.10f, 1f) : ShopUIStyle.CardFill);

            if (w == null)
            {
                _cardName[i].text = "—";
                _cardLevel[i].text = "";
                _cardAtk[i].text = "";
                _sacLabel[i].text = "제물";
                continue;
            }

            int max = _controller.MaxAt(i);
            string legend = string.IsNullOrEmpty(w.legendId) ? "" : $"  <color=#FFD24A>[{LegendName(w.legendId)}]</color>";
            _cardName[i].text = $"{w.displayName}{legend}";
            _cardLevel[i].text = $"+{w.enhanceLevel} <size=60%><color=#9A98A0>/ {max}</color></size>";
            _cardAtk[i].text = $"공격 {w.baseAttack:F0}";
            _sacLabel[i].text = isSac ? "제물 ✓" : "제물";
        }

        RefreshInfo();
        RefreshPromoteRow();
    }

    private void RefreshInfo()
    {
        var w = _controller.GetSlot(_targetSlot);
        if (w == null) { _successText.text = _costText.text = _dropText.text = _previewText.text = ""; return; }

        bool maxed = !_controller.CanEnhance(_targetSlot);
        if (maxed)
        {
            _successText.text = "<color=#8AB0D5>최대 강화 도달</color>";
            _costText.text = "";
            _dropText.text = "";
            _previewText.text = "승급 가능" ;
        }
        else
        {
            float chance = _controller.SuccessChanceAt(_targetSlot);
            int cost = _controller.CostAt(_targetSlot);
            int drop = _controller.DropAt(_targetSlot);
            _successText.text = $"성공률 <color=#7AD46E>{chance * 100f:F0}%</color>";
            _costText.text = $"재료 {cost}";
            _dropText.text = drop > 0 ? $"실패 시 -{drop}" : "실패 시 유지";

            var table = _controller.Table;
            float cur  = w.baseAttack;
            float next = table != null ? (w.baseAttackRaw > 0f ? w.baseAttackRaw : w.baseAttack) * table.AttackMult(w.enhanceLevel + 1, w.legendId) : cur;
            _previewText.text = $"공격 {cur:F0} → <color=#7AD46E>{next:F0}</color>";
        }

        _streakText.text = _controller.Streak > 0 ? $"🔥 연속 성공 {_controller.Streak}" : "";
    }

    private void RefreshPromoteRow()
    {
        bool canPromote = _controller.CanPromote(_targetSlot);
        if (_promoteRow != null) _promoteRow.gameObject.SetActive(canPromote);
        if (!canPromote) return;

        var w = _controller.GetSlot(_targetSlot);
        for (int i = 0; i < _legendBtns.Count && i < _controller.Legends.Length; i++)
        {
            var legend = _controller.Legends[i];
            int cost = legend.promoteCost;
            bool afford = _controller.FuelAmount >= cost;
            _legendBtns[i].interactable = afford;
            var lbl = _legendBtns[i].GetComponentInChildren<TMP_Text>();
            if (lbl != null) lbl.text = $"{legend.displayName}\n<size=60%>재료 {cost}</size>";
        }
    }

    private string LegendName(string legendId)
    {
        if (_controller?.Table != null && _controller.Table.TryGetLegend(legendId, out var l))
            return l.displayName;
        return legendId;
    }
}
