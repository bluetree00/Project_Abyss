using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 재련소 UI 패널 (Canvas_Popup, Addressable "UI_CruciblePanel").
///
/// 무기 강화/승급 화면(다크 판타지·유물 톤, ShopUIStyle 재사용). 크게·강화 몰입형:
///  - 상단: 재련공 이름/대사 + 강화재료 표시(실시간)
///  - 중앙: 무기 2슬롯 카드(대상 택1) — 큰 강화단계 + 강화 게이지 바 + 공격력
///  - 하단: 강화 정보(성공률/재료/실패하락/공격 전→후) + 스트릭/결과 + 큰 [강화] 버튼 (+ MAX 시 승급)
///
/// 제물(sacrifice) 메커닉은 제거됨 — 한 무기에 집중하는 강화. 데이터/계산/결정성은 CrucibleRoomController가 권위.
/// </summary>
public sealed class UI_CruciblePanel : UI_Popup
{
    public override bool BlocksGameplay => true; // 재련 중 시간정지 + 입력잠금

    private const float WindowW = 1040f;
    private const float WindowH = 780f;

    // 카드 배치
    private const float CardW  = 490f;
    private const float CardH  = 300f;
    private const float Card0X = 30f;
    private const float Card1X = 30f + CardW + 20f;
    private const float CardY  = -150f;

    // ── 연출 노브 (도파민 레이어; 표시층 전용, 결과/데이터 불변) ──
    private const float PunchScale        = 0.14f;  // 성공 카드 스케일 펀치 진폭
    private const float PunchDur          = 0.22f;
    private const float JackpotPunchScale = 0.28f;  // 잭팟 강한 펀치
    private const float JackpotPunchDur   = 0.34f;
    private const float CountStepDur      = 0.07f;  // 레벨 1단계 카운트 시간(초)
    private const float CountMaxDur       = 0.45f;  // 카운트 총 상한
    private const float FlashDur          = 0.28f;  // 카드 색 플래시 감쇠 시간
    private const float FailShakeDur      = 0.34f;
    private const float FailShakeAmp      = 12f;    // 실패 카드 좌우 흔들림(px)
    private const float NearMissShakeMult = 1.6f;   // 니어미스 시 흔들림 배수
    private const float NearMissBand      = 1.1f;   // roll < chance*이 배수면 니어미스
    private const float JackpotPulsePeak  = 0.7f;   // 전체화면 펄스 강도
    private const float JackpotPulseDur   = 0.4f;

    private static readonly Color SuccessFlash  = new(0.28f, 0.72f, 0.34f, 1f);
    private static readonly Color JackpotFlash  = new(1f,    0.78f, 0.30f, 1f);
    private static readonly Color FailFlash     = new(0.60f, 0.14f, 0.14f, 1f);
    private static readonly Color NearMissFlash = new(0.78f, 0.42f, 0.12f, 1f);

    private static readonly Color GaugeTrack = new(0.05f, 0.05f, 0.08f, 1f);
    private static readonly Color GaugeFillC = new(0.92f, 0.62f, 0.22f, 1f);
    private static readonly Color CardTargetBg = new(0.20f, 0.16f, 0.09f, 1f);

    private CrucibleRoomController _controller;
    private int _targetSlot = PlayerWeaponManager.Slot0;

    // 헤더
    private TMP_Text _fuelText;
    private TMP_Text _dialogText;

    // 슬롯 카드(2)
    private readonly Image[]         _cardBg        = new Image[2];
    private readonly TMP_Text[]      _cardName      = new TMP_Text[2];
    private readonly TMP_Text[]      _cardLevel     = new TMP_Text[2];
    private readonly TMP_Text[]      _cardAtk       = new TMP_Text[2];
    private readonly RectTransform[] _cardGaugeFill = new RectTransform[2];
    private readonly Vector2[]       _cardBasePos   = new Vector2[2]; // 카드 기준 앵커 위치(쉐이크 복원용)

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
    private bool _animating; // 연출 진행 중 재입력 잠금

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
                           new Vector2(0, 0), new Vector2(0, 130));
        var h = header.transform;

        var line = ShopUIStyle.MakeImage(h, "Underline", ShopUIStyle.BronzeLine);
        ShopUIStyle.Anchor(line.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0),
                           new Vector2(0, 0), new Vector2(0, 3));

        var title = ShopUIStyle.MakeText(h, "Title", 34f, FontStyles.Bold,
                                         TextAlignmentOptions.MidlineLeft, ShopUIStyle.Gold);
        title.text = "재련소";
        ShopUIStyle.Anchor(title.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1),
                           new Vector2(34, -18), new Vector2(-380, 48));

        _dialogText = ShopUIStyle.MakeText(h, "Dialog", 17f, FontStyles.Italic,
                                           TextAlignmentOptions.MidlineLeft, ShopUIStyle.TextDim);
        _dialogText.text = "쇠는 두드릴수록 강해지지… 운이 따라준다면 말이야.";
        ShopUIStyle.Anchor(_dialogText.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1),
                           new Vector2(34, -70), new Vector2(-380, 44));

        // 강화재료 pill
        var pill = ShopUIStyle.MakeRect(h, "FuelPill", typeof(Image), typeof(HorizontalLayoutGroup));
        pill.GetComponent<Image>().color = ShopUIStyle.GoldPillBg;
        ShopUIStyle.Anchor((RectTransform)pill.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1),
                           new Vector2(-26, -28), new Vector2(320, 56));
        var phlg = pill.GetComponent<HorizontalLayoutGroup>();
        phlg.padding = new RectOffset(18, 18, 4, 4);
        phlg.spacing = 8f;
        phlg.childAlignment = TextAnchor.MiddleRight;
        phlg.childControlWidth = true; phlg.childControlHeight = true;
        phlg.childForceExpandWidth = false; phlg.childForceExpandHeight = false;
        _fuelText = ShopUIStyle.MakeText(pill.transform, "Fuel", 24f, FontStyles.Bold,
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
            _cardBasePos[i] = new Vector2(i == 0 ? Card0X : Card1X, CardY);
            ShopUIStyle.Anchor(cardRT, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                               _cardBasePos[i], new Vector2(CardW, CardH));
            var c = card.transform;

            var slotLabel = ShopUIStyle.MakeText(c, "Slot", 16f, FontStyles.Bold,
                                                 TextAlignmentOptions.TopLeft, ShopUIStyle.TextDim);
            slotLabel.text = i == 0 ? "슬롯 0 · 근접" : "슬롯 1 · 원거리";
            ShopUIStyle.Anchor(slotLabel.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1),
                               new Vector2(20, -14), new Vector2(-40, 26));

            _cardName[i] = ShopUIStyle.MakeText(c, "Name", 24f, FontStyles.Bold,
                                                TextAlignmentOptions.TopLeft, ShopUIStyle.TextPrimary);
            ShopUIStyle.Anchor(_cardName[i].rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1),
                               new Vector2(20, -46), new Vector2(-40, 34));

            _cardLevel[i] = ShopUIStyle.MakeText(c, "Level", 44f, FontStyles.Bold,
                                                 TextAlignmentOptions.TopLeft, ShopUIStyle.Gold);
            ShopUIStyle.Anchor(_cardLevel[i].rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1),
                               new Vector2(20, -88), new Vector2(-40, 56));

            // 강화 게이지 바 (레벨/최대 채움) — "강화하는 느낌"
            var track = ShopUIStyle.MakeImage(c, "GaugeTrack", GaugeTrack);
            ShopUIStyle.Anchor(track.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1),
                               new Vector2(20, -160), new Vector2(-40, 24));
            var fill = ShopUIStyle.MakeImage(track.transform, "Fill", GaugeFillC);
            var fr = fill.rectTransform;
            fr.anchorMin = new Vector2(0f, 0f); fr.anchorMax = new Vector2(0f, 1f); fr.pivot = new Vector2(0f, 0.5f);
            fr.offsetMin = Vector2.zero; fr.offsetMax = Vector2.zero;
            _cardGaugeFill[i] = fr;

            _cardAtk[i] = ShopUIStyle.MakeText(c, "Atk", 20f, FontStyles.Bold,
                                               TextAlignmentOptions.TopLeft, ShopUIStyle.TextPrimary);
            ShopUIStyle.Anchor(_cardAtk[i].rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1),
                               new Vector2(20, -196), new Vector2(-40, 32));

            // 대상 선택 (카드 전체 버튼)
            var selBtn = card.transform.parent.gameObject.AddComponent<Button>();
            selBtn.transition = Selectable.Transition.None;
            selBtn.onClick.AddListener(() => { if (_animating) return; _targetSlot = slot; UpdateTargetDialogue(); RefreshAll(); });
        }
    }

    private void BuildInfo(Transform w)
    {
        var panel = ShopUIStyle.MakeImage(w, "Info", ShopUIStyle.BandFill);
        ShopUIStyle.Anchor(panel.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
                           new Vector2(0, -470), new Vector2(-60, 140));
        var p = panel.transform;

        _successText = ShopUIStyle.MakeText(p, "Success", 22f, FontStyles.Bold,
                                            TextAlignmentOptions.TopLeft, ShopUIStyle.TextPrimary);
        ShopUIStyle.Anchor(_successText.rectTransform, new Vector2(0, 1), new Vector2(0.5f, 1), new Vector2(0, 1),
                           new Vector2(24, -14), new Vector2(-24, 32));

        _costText = ShopUIStyle.MakeText(p, "Cost", 22f, FontStyles.Bold,
                                         TextAlignmentOptions.TopLeft, ShopUIStyle.Gold);
        ShopUIStyle.Anchor(_costText.rectTransform, new Vector2(0.5f, 1), new Vector2(1, 1), new Vector2(0, 1),
                           new Vector2(12, -14), new Vector2(-24, 32));

        _dropText = ShopUIStyle.MakeText(p, "Drop", 17f, FontStyles.Normal,
                                         TextAlignmentOptions.TopLeft, ShopUIStyle.RejectRed);
        ShopUIStyle.Anchor(_dropText.rectTransform, new Vector2(0, 1), new Vector2(0.5f, 1), new Vector2(0, 1),
                           new Vector2(24, -52), new Vector2(-24, 28));

        _previewText = ShopUIStyle.MakeText(p, "Preview", 17f, FontStyles.Normal,
                                            TextAlignmentOptions.TopLeft, ShopUIStyle.TextDim);
        ShopUIStyle.Anchor(_previewText.rectTransform, new Vector2(0.5f, 1), new Vector2(1, 1), new Vector2(0, 1),
                           new Vector2(12, -52), new Vector2(-24, 28));

        _streakText = ShopUIStyle.MakeText(p, "Streak", 18f, FontStyles.Bold,
                                           TextAlignmentOptions.TopLeft, ShopUIStyle.Gold);
        ShopUIStyle.Anchor(_streakText.rectTransform, new Vector2(0, 1), new Vector2(0.5f, 1), new Vector2(0, 1),
                           new Vector2(24, -88), new Vector2(-24, 34));

        _resultText = ShopUIStyle.MakeText(p, "Result", 20f, FontStyles.Bold,
                                           TextAlignmentOptions.TopRight, ShopUIStyle.TextPrimary);
        ShopUIStyle.Anchor(_resultText.rectTransform, new Vector2(0.5f, 1), new Vector2(1, 1), new Vector2(0, 1),
                           new Vector2(12, -88), new Vector2(-24, 34));
    }

    private void BuildActions(Transform w)
    {
        _enhanceBtn = MakeStyledButton(w, "Enhance", "강 화", out _enhanceLabel);
        _enhanceLabel.fontSize = 26f;
        ShopUIStyle.Anchor((RectTransform)_enhanceBtn.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0),
                           new Vector2(0.5f, 0), new Vector2(0, 100), new Vector2(400, 76));
        _enhanceBtn.onClick.AddListener(OnEnhanceClicked);

        var rowGo = ShopUIStyle.MakeRect(w, "PromoteRow", typeof(HorizontalLayoutGroup));
        _promoteRow = (RectTransform)rowGo.transform;
        ShopUIStyle.Anchor(_promoteRow, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0),
                           new Vector2(0, 22), new Vector2(680, 54));
        var hlg = rowGo.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = 10f;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true; hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true; hlg.childForceExpandHeight = true;

        var exitBtn = MakeStyledButton(w, "Exit", "나가기", out _);
        ShopUIStyle.Anchor((RectTransform)exitBtn.transform, new Vector2(1, 0), new Vector2(1, 0),
                           new Vector2(1, 0), new Vector2(-26, 24), new Vector2(170, 50));
        exitBtn.onClick.AddListener(ClosePopupUI);
    }

    private void BuildCloseButton(Transform w)
    {
        var close = MakeStyledButton(w, "Close", "✕", out var lbl);
        lbl.color = ShopUIStyle.TextPrimary;
        close.GetComponent<Image>().color = new Color(0.5f, 0.16f, 0.16f, 1f);
        ShopUIStyle.Anchor((RectTransform)close.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1),
                           new Vector2(-14, -14), new Vector2(52, 52));
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

    private void OnEnhanceClicked()
    {
        if (_controller == null || _animating) return;
        // 컨트롤러가 결과를 즉시 확정(OnCrucibleChanged→RefreshAll 동기 발화). 연출은 표시층만 재생.
        var result = _controller.TryEnhance(_targetSlot);
        PlayEnhanceSequence(result).Forget();
    }

    private void OnPromoteClicked(string legendId)
    {
        if (_controller == null || _animating) return;
        var result = _controller.TryPromote(_targetSlot, legendId);
        ShowPromoteResult(result);
        RefreshAll();
    }

    // ── 도파민 연출 시퀀스 (표시층 전용; 결과/데이터/세이브 불변) ──

    /// <summary>강화 결과를 비동기 연출로 재생. 연출 종료 후 RefreshAll로 최종 확정.</summary>
    private async UniTaskVoid PlayEnhanceSequence(EnhanceResult r)
    {
        if (r.IsReject)
        {
            ShowResultText(r);
            if (r.outcome != EnhanceOutcome.RejectMaxed) ShopUIStyle.PlaySfx("crucible_fail");
            RefreshAll();
            return;
        }

        _animating = true;
        SetActionsInteractable(false);
        ShowResultText(r);

        int slot = _targetSlot;
        int max  = slot >= 0 ? _controller.MaxAt(slot) : 0;
        // 카운트 연출이 레벨 셀을 소유하도록 시작 단계로 되돌림
        // (RefreshAll이 이미 afterLevel로 세팅했으나 다음 렌더 전 동일 프레임에서 덮어씀 → 깜빡임 없음).
        if (IsCardSlot(slot)) { _cardLevel[slot].text = FormatLevel(r.beforeLevel, max); SetGauge(slot, r.beforeLevel, max); }

        try
        {
            switch (r.outcome)
            {
                case EnhanceOutcome.Success:
                    if (_controller.LastJackpot) await JackpotSequence(slot, r, max);
                    else                         await SuccessSequence(slot, r, max);
                    break;
                case EnhanceOutcome.FailDropped:
                    await FailSequence(slot, r, max);
                    break;
            }
        }
        catch (OperationCanceledException) { return; } // 패널 파괴 — 정적 서비스는 자립적, 정리 불필요

        _animating = false;
        SetActionsInteractable(true);
        RefreshAll(); // 연출 후 최종 확정
    }

    private async UniTask SuccessSequence(int slot, EnhanceResult r, int max)
    {
        ShopUIStyle.PlaySfx("crucible_success");
        HitFeelService.HitStop(0.6f, 0.05f); // 시간정지 팝업에선 timeScale 무효(무해) — 카메라측 반응만
        await CountLevel(slot, r.beforeLevel, r.afterLevel, max);
        await UniTask.WhenAll(PunchCard(slot, PunchScale, PunchDur),
                              FlashCard(slot, SuccessFlash));
    }

    private async UniTask JackpotSequence(int slot, EnhanceResult r, int max)
    {
        ShopUIStyle.PlaySfx("crucible_jackpot");
        VolumePulseService.Pulse(JackpotPulsePeak, JackpotPulseDur); // 전체화면 크로매틱+블룸(unscaled)
        HitFeelService.Heavy();
        await CountLevel(slot, r.beforeLevel, r.afterLevel, max);
        await UniTask.WhenAll(PunchCard(slot, JackpotPunchScale, JackpotPunchDur),
                              FlashCard(slot, JackpotFlash));
    }

    private async UniTask FailSequence(int slot, EnhanceResult r, int max)
    {
        bool nearMiss = IsNearMiss(r);
        ShopUIStyle.PlaySfx("crucible_fail");
        HitFeelService.Light();
        if (r.beforeLevel != r.afterLevel) // 하락분이 있으면 카운트다운
            await CountLevel(slot, r.beforeLevel, r.afterLevel, max);
        float amp   = nearMiss ? FailShakeAmp * NearMissShakeMult : FailShakeAmp;
        Color flash = nearMiss ? NearMissFlash : FailFlash;
        await UniTask.WhenAll(ShakeCard(slot, amp, FailShakeDur),
                              FlashCard(slot, flash));
    }

    private void ShowResultText(EnhanceResult r)
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
                break;
            case EnhanceOutcome.FailDropped:
                _resultText.text = IsNearMiss(r) ? $"<color=#FF7A3A>아슬아슬! 하락 (+{r.afterLevel})</color>"
                                                 : $"<color=#FF5250>실패 — 하락 (+{r.afterLevel})</color>";
                _dialogText.text = _controller.GetDialogue(CrucibleMood.Fail);
                break;
            case EnhanceOutcome.RejectNoFuel:
                _resultText.text = "<color=#FF5250>강화재료 부족</color>";
                break;
            case EnhanceOutcome.RejectMaxed:
                _resultText.text = "<color=#8AB0D5>이미 최대 강화</color>";
                break;
            default:
                _resultText.text = "<color=#FF5250>강화 불가</color>";
                break;
        }
    }

    /// <summary>실패가 성공확률에 아슬아슬했는지(roll이 chance의 NearMissBand배 이내).</summary>
    private static bool IsNearMiss(EnhanceResult r)
        => r.outcome == EnhanceOutcome.FailDropped
           && r.chance > 0f && r.roll < r.chance * NearMissBand;

    private bool IsCardSlot(int slot) => slot >= 0 && slot < 2 && _cardLevel[slot] != null;

    private static string FormatLevel(int level, int max)
        => $"+{level} <size=55%><color=#9A98A0>/ {max}</color></size>";

    private void SetGauge(int slot, int level, int max)
    {
        if (slot < 0 || slot >= 2 || _cardGaugeFill[slot] == null) return;
        float ratio = max > 0 ? Mathf.Clamp01((float)level / max) : 0f;
        var f = _cardGaugeFill[slot];
        f.anchorMin = new Vector2(0f, 0f);
        f.anchorMax = new Vector2(ratio, 1f);
        f.offsetMin = Vector2.zero; f.offsetMax = Vector2.zero;
    }

    private void SetActionsInteractable(bool on)
    {
        if (_enhanceBtn != null) _enhanceBtn.interactable = on;
        foreach (var b in _legendBtns) if (b != null) b.interactable = on;
    }

    /// <summary>레벨 셀 + 게이지를 before→after로 한 단계씩 표기(카운트업/다운).</summary>
    private async UniTask CountLevel(int slot, int before, int after, int max)
    {
        if (!IsCardSlot(slot)) return;
        int step  = after >= before ? 1 : -1;
        int steps = Mathf.Max(1, Mathf.Abs(after - before));
        float perStep = Mathf.Min(CountStepDur, CountMaxDur / steps);

        int lvl = before;
        _cardLevel[slot].text = FormatLevel(lvl, max);
        SetGauge(slot, lvl, max);
        while (lvl != after)
        {
            await Hold(perStep);
            lvl += step;
            _cardLevel[slot].text = FormatLevel(lvl, max);
            SetGauge(slot, lvl, max);
        }
    }

    /// <summary>카드 스케일 펀치(ShopSlot PopAsync 이식). unscaledDeltaTime.</summary>
    private async UniTask PunchCard(int slot, float amp, float dur)
    {
        if (slot < 0 || slot >= 2 || _cardBg[slot] == null) return;
        var rt = _cardBg[slot].rectTransform;
        float t = 0f;
        while (t < 1f)
        {
            t = Mathf.Min(t + Time.unscaledDeltaTime / dur, 1f);
            float s = 1f + amp * Mathf.Sin(t * Mathf.PI);
            rt.localScale = Vector3.one * s;
            await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
        }
        rt.localScale = Vector3.one;
    }

    /// <summary>카드 배경색 플래시 → 원색 복귀(감쇠). unscaledDeltaTime.</summary>
    private async UniTask FlashCard(int slot, Color flash)
    {
        if (slot < 0 || slot >= 2 || _cardBg[slot] == null) return;
        var img = _cardBg[slot];
        Color baseCol = img.color;
        float t = 0f;
        while (t < FlashDur)
        {
            t += Time.unscaledDeltaTime;
            img.color = Color.Lerp(flash, baseCol, t / FlashDur);
            await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
        }
        img.color = baseCol;
    }

    /// <summary>카드 좌우 흔들림(ShopSlot ShakeAsync 이식) — 기준 앵커 복원. unscaledDeltaTime.</summary>
    private async UniTask ShakeCard(int slot, float amp, float dur)
    {
        if (slot < 0 || slot >= 2 || _cardBg[slot] == null) return;
        var rt = _cardBg[slot].rectTransform;
        Vector2 basePos = _cardBasePos[slot];
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float damp = 1f - (t / dur);
            rt.anchoredPosition = basePos + new Vector2(Mathf.Sin(t * 60f) * amp * damp, 0f);
            await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
        }
        rt.anchoredPosition = basePos;
    }

    private async UniTask Hold(float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
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

            _cardBg[i].color = isTarget ? CardTargetBg : ShopUIStyle.CardFill;

            if (w == null)
            {
                _cardName[i].text = "—";
                _cardLevel[i].text = "";
                _cardAtk[i].text = "";
                SetGauge(i, 0, 1);
                continue;
            }

            int max = _controller.MaxAt(i);
            string legend = string.IsNullOrEmpty(w.legendId) ? "" : $"  <color=#FFD24A>[{LegendName(w.legendId)}]</color>";
            _cardName[i].text = $"{w.displayName}{legend}";
            _cardLevel[i].text = FormatLevel(w.enhanceLevel, max);
            _cardAtk[i].text = $"공격 {w.baseAttack:F0}";
            SetGauge(i, w.enhanceLevel, max);
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
            _previewText.text = _controller.CanPromote(_targetSlot) ? "승급 가능" : "";
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
