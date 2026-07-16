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

    private const float WindowW = 1280f;
    private const float WindowH = 900f;

    // 단일 집중 레이아웃 — 슬롯0(근접) 큰 집중 카드 + 슬롯1(원거리) 도킹 카드(둘 다 확대)
    private const float FocusW = 760f, FocusH = 450f, FocusX =  40f, FocusY = -186f;
    private const float DockW  = 340f, DockH  = 450f, DockX  = -40f, DockY  = -186f;

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

    // 원거리 파츠(도킹 카드) — 진화로 해금(활2/석궁3), 재련소에서 제작/구매. 슬롯 UI는 레이아웃, 로직은 Phase 3.
    private readonly Image[]    _partsSlots = new Image[3];
    private readonly TMP_Text[] _partsMark  = new TMP_Text[3];
    private Button _craftPartBtn;

    // 근접 집중 카드 — 안전/도박 구간 배지 + 진화 마일스톤(실데이터: DropAt/레벨/승급)
    private Image    _zoneBg;
    private TMP_Text _zoneTag;
    private TMP_Text _milestoneLabel;

    // 이벤트 배너(HasEvent — Discount/Fever) + 진화 선택 패널(선택 연출)
    private GameObject _eventBanner;
    private TMP_Text   _eventBannerText;
    private GameObject _evolvePanel;
    private Button     _evolveBtn;
    private TMP_Text   _branchAName, _branchBName;
    private Button     _branchABtn,  _branchBBtn;

    // 디테일 콘텐츠 — 무기 타입·티어 라인 + "다음 강화 상세"(성공/실패/잭팟) + 정보 잭팟·이벤트효과
    private TMP_Text _focusTypeText;
    private TMP_Text _detailSuccess, _detailFail, _detailJackpot;
    private TMP_Text _dockTypeText;
    private TMP_Text _eventEffectText;

    // 정보
    private TMP_Text _successText;
    private TMP_Text _costText;
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
        _dialogText.text = _controller.GetDialogue(CrucibleMood.Idle);   // 이벤트는 이제 전용 배너로 표시
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
        BuildEventBanner(w);
        BuildCards(w);
        BuildInfo(w);
        BuildActions(w);
        BuildEvolvePanel(w);   // 선택 연출 오버레이(최상단, 기본 비활성)
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
        BuildCard(w, 0, focus: true);   // 근접 — 집중(큼)
        BuildCard(w, 1, focus: false);  // 원거리 — 도킹(작음)
    }

    /// <summary>슬롯 카드 1장. focus=true면 큰 집중 카드(근접), false면 도킹 카드(원거리).</summary>
    private void BuildCard(Transform w, int i, bool focus)
    {
        int slot = i;
        var card = ShopUIStyle.MakeFrame(w, $"Card{i}", ShopUIStyle.CardBorder, ShopUIStyle.CardFill, 3f, raycast: true);
        _cardBg[i] = (Image)card.transform.parent.GetComponent<Image>();
        var cardRT = (RectTransform)card.transform.parent;

        Vector2 anchor = focus ? new Vector2(0, 1) : new Vector2(1, 1);
        Vector2 pos    = focus ? new Vector2(FocusX, FocusY) : new Vector2(DockX, DockY);
        Vector2 size   = focus ? new Vector2(FocusW, FocusH) : new Vector2(DockW, DockH);
        _cardBasePos[i] = pos;
        ShopUIStyle.Anchor(cardRT, anchor, anchor, anchor, pos, size);
        var c = card.transform;

        float pad = focus ? 24f : 14f;
        var slotLabel = ShopUIStyle.MakeText(c, "Slot", focus ? 16f : 13f, FontStyles.Bold,
                                             TextAlignmentOptions.TopLeft, ShopUIStyle.TextDim);
        slotLabel.text = focus ? "근접 · 집중 강화" : "원거리";
        ShopUIStyle.Anchor(slotLabel.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1),
                           new Vector2(pad, -14), new Vector2(-pad * 2, 22));

        if (focus) BuildFocusCard(c, pad);
        else       BuildDockCard(c, pad);

        // 대상 선택 (카드 전체 버튼)
        var selBtn = card.transform.parent.gameObject.AddComponent<Button>();
        selBtn.transition = Selectable.Transition.None;
        selBtn.onClick.AddListener(() => { if (_animating) return; _targetSlot = slot; UpdateTargetDialogue(); RefreshAll(); });
    }

    /// <summary>근접 집중 카드 — 타입·티어 · 이름 · 레벨 · 게이지+마일스톤 · 안전/도박 배지 · 다음 강화 상세 패널.</summary>
    private void BuildFocusCard(Transform c, float pad)
    {
        _focusTypeText = ShopUIStyle.MakeText(c, "TypeTier", 15f, FontStyles.Bold, TextAlignmentOptions.TopLeft, ShopUIStyle.Gold);
        ShopUIStyle.Anchor(_focusTypeText.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1),
                           new Vector2(pad, -44), new Vector2(-pad * 2, 22));

        _zoneBg = ShopUIStyle.MakeImage(c, "ZoneBg", new Color(0.15f, 0.32f, 0.2f, 1f));
        ShopUIStyle.Anchor(_zoneBg.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1),
                           new Vector2(-pad, -12), new Vector2(150, 30));
        _zoneTag = ShopUIStyle.MakeText(_zoneBg.transform, "ZoneTag", 14f, FontStyles.Bold, TextAlignmentOptions.Center, ShopUIStyle.TextPrimary);
        _zoneTag.text = "안전구간";
        ShopUIStyle.Stretch(_zoneTag.rectTransform);

        _cardName[0] = ShopUIStyle.MakeText(c, "Name", 30f, FontStyles.Bold, TextAlignmentOptions.TopLeft, ShopUIStyle.TextPrimary);
        ShopUIStyle.Anchor(_cardName[0].rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1),
                           new Vector2(pad, -80), new Vector2(-pad * 2, 40));

        _cardLevel[0] = ShopUIStyle.MakeText(c, "Level", 50f, FontStyles.Bold, TextAlignmentOptions.TopLeft, ShopUIStyle.Gold);
        ShopUIStyle.Anchor(_cardLevel[0].rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1),
                           new Vector2(pad, -132), new Vector2(-pad * 2, 60));

        var track = ShopUIStyle.MakeImage(c, "GaugeTrack", GaugeTrack);
        ShopUIStyle.Anchor(track.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1),
                           new Vector2(pad, -214), new Vector2(-pad * 2, 30));
        var fill = ShopUIStyle.MakeImage(track.transform, "Fill", GaugeFillC);
        var fr = fill.rectTransform;
        fr.anchorMin = new Vector2(0f, 0f); fr.anchorMax = new Vector2(0f, 1f); fr.pivot = new Vector2(0f, 0.5f);
        fr.offsetMin = Vector2.zero; fr.offsetMax = Vector2.zero;
        _cardGaugeFill[0] = fr;

        var tick = ShopUIStyle.MakeImage(track.transform, "MilestoneTick", ShopUIStyle.Gold);
        var tr = tick.rectTransform;
        tr.anchorMin = new Vector2(1f, -0.25f); tr.anchorMax = new Vector2(1f, 1.25f); tr.pivot = new Vector2(0.5f, 0.5f);
        tr.sizeDelta = new Vector2(4f, 0f); tr.anchoredPosition = Vector2.zero;

        _milestoneLabel = ShopUIStyle.MakeText(c, "MilestoneLabel", 15f, FontStyles.Bold, TextAlignmentOptions.TopLeft, ShopUIStyle.Gold);
        ShopUIStyle.Anchor(_milestoneLabel.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1),
                           new Vector2(pad, -250), new Vector2(-pad * 2, 24));

        // 다음 강화 상세 패널(성공/실패/잭팟)
        var detail = ShopUIStyle.MakeImage(c, "Detail", ShopUIStyle.BandFill);
        ShopUIStyle.Anchor(detail.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1),
                           new Vector2(pad, -288), new Vector2(-pad * 2, 146));
        var dt = detail.transform;
        var dh = ShopUIStyle.MakeText(dt, "H", 13f, FontStyles.Bold, TextAlignmentOptions.TopLeft, ShopUIStyle.TextDim);
        dh.text = "다음 강화";
        ShopUIStyle.Anchor(dh.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1),
                           new Vector2(16, -10), new Vector2(-32, 20));
        _detailSuccess = MakeDetailRow(dt, -36);
        _detailFail    = MakeDetailRow(dt, -72);
        _detailJackpot = MakeDetailRow(dt, -108);
    }

    private TMP_Text MakeDetailRow(Transform dt, float y)
    {
        var t = ShopUIStyle.MakeText(dt, "Row", 16f, FontStyles.Normal, TextAlignmentOptions.TopLeft, ShopUIStyle.TextPrimary);
        ShopUIStyle.Anchor(t.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1),
                           new Vector2(16, y), new Vector2(-32, 30));
        return t;
    }

    /// <summary>원거리 도킹 카드 — 타입·티어 · 이름 · 레벨 · 게이지 · 공격 · 파츠 워크벤치.</summary>
    private void BuildDockCard(Transform c, float pad)
    {
        _dockTypeText = ShopUIStyle.MakeText(c, "TypeTier", 12f, FontStyles.Bold, TextAlignmentOptions.TopLeft, ShopUIStyle.Gold);
        ShopUIStyle.Anchor(_dockTypeText.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1),
                           new Vector2(pad, -38), new Vector2(-pad * 2, 20));

        _cardName[1] = ShopUIStyle.MakeText(c, "Name", 20f, FontStyles.Bold, TextAlignmentOptions.TopLeft, ShopUIStyle.TextPrimary);
        ShopUIStyle.Anchor(_cardName[1].rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1),
                           new Vector2(pad, -66), new Vector2(-pad * 2, 28));

        _cardLevel[1] = ShopUIStyle.MakeText(c, "Level", 30f, FontStyles.Bold, TextAlignmentOptions.TopLeft, ShopUIStyle.Gold);
        ShopUIStyle.Anchor(_cardLevel[1].rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1),
                           new Vector2(pad, -100), new Vector2(-pad * 2, 42));

        var track = ShopUIStyle.MakeImage(c, "GaugeTrack", GaugeTrack);
        ShopUIStyle.Anchor(track.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1),
                           new Vector2(pad, -152), new Vector2(-pad * 2, 16));
        var fill = ShopUIStyle.MakeImage(track.transform, "Fill", GaugeFillC);
        var fr = fill.rectTransform;
        fr.anchorMin = new Vector2(0f, 0f); fr.anchorMax = new Vector2(0f, 1f); fr.pivot = new Vector2(0f, 0.5f);
        fr.offsetMin = Vector2.zero; fr.offsetMax = Vector2.zero;
        _cardGaugeFill[1] = fr;

        _cardAtk[1] = ShopUIStyle.MakeText(c, "Atk", 15f, FontStyles.Bold, TextAlignmentOptions.TopLeft, ShopUIStyle.TextPrimary);
        ShopUIStyle.Anchor(_cardAtk[1].rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1),
                           new Vector2(pad, -176), new Vector2(-pad * 2, 26));

        BuildRangedParts(c);
    }

    /// <summary>원거리 도킹 카드에 파츠 슬롯 행 + 제작 버튼(진화로 해금 · 재련소 제작). 슬롯 UI만 — 로직은 Phase 3.</summary>
    private void BuildRangedParts(Transform c)
    {
        var label = ShopUIStyle.MakeText(c, "PartsLabel", 13f, FontStyles.Bold,
                                         TextAlignmentOptions.TopLeft, ShopUIStyle.TextDim);
        label.text = "파츠 · 진화로 해금";
        ShopUIStyle.Anchor(label.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1),
                           new Vector2(14, -214), new Vector2(-28, 20));

        const float box = 52f, gap = 14f;
        for (int i = 0; i < _partsSlots.Length; i++)
        {
            var slot = ShopUIStyle.MakeImage(c, $"PartSlot{i}", GaugeTrack);
            ShopUIStyle.Anchor(slot.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                               new Vector2(14 + i * (box + gap), -238), new Vector2(box, box));
            _partsSlots[i] = slot;

            var mark = ShopUIStyle.MakeText(slot.transform, "Mark", 16f, FontStyles.Bold,
                                            TextAlignmentOptions.Center, ShopUIStyle.TextDim);
            mark.text = i < 2 ? "＋" : "잠금";       // i<2=해금(빈칸), 그 외=잠금(석궁 진화 시 해제)
            ShopUIStyle.Stretch(mark.rectTransform);
            _partsMark[i] = mark;
        }

        var hint = ShopUIStyle.MakeText(c, "PartsHint", 12f, FontStyles.Normal,
                                        TextAlignmentOptions.TopLeft, ShopUIStyle.TextDim);
        hint.text = "슬롯: 활 2 / 석궁 3 · 연발·관통·멀티샷";
        ShopUIStyle.Anchor(hint.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1),
                           new Vector2(14, -300), new Vector2(-28, 20));

        _craftPartBtn = MakeStyledButton(c, "CraftPart", "파츠 제작", out _);
        ShopUIStyle.Anchor((RectTransform)_craftPartBtn.transform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
                           new Vector2(0, -332), new Vector2(-28, 46));
        _craftPartBtn.onClick.AddListener(OnCraftPartClicked);
    }

    private void OnCraftPartClicked()
    {
        // TODO(파츠 시스템): 재련소 파츠 제작/구매 UI 연결 (Phase 3)
        if (_dialogText != null) _dialogText.text = "파츠 제작은 곧 열립니다 — 진화로 슬롯을 먼저 여세요.";
        ShopUIStyle.PlaySfx("shop_reject");
    }

    private void BuildInfo(Transform w)
    {
        var panel = ShopUIStyle.MakeImage(w, "Info", ShopUIStyle.BandFill);
        ShopUIStyle.Anchor(panel.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
                           new Vector2(0, -654), new Vector2(-60, 130));
        var p = panel.transform;

        // 상단 행: 성공률 · 재료 · 스트릭 (3열)
        _successText = InfoCell(p, 0f,    0.34f, -16f, 22f, ShopUIStyle.TextPrimary,           FontStyles.Bold, false);
        _costText    = InfoCell(p, 0.34f, 0.64f, -16f, 22f, ShopUIStyle.Gold,                  FontStyles.Bold, false);
        _streakText  = InfoCell(p, 0.64f, 1f,    -16f, 20f, ShopUIStyle.Gold,                  FontStyles.Bold, false);
        // 하단 행: 이벤트 효과(좌 넓게) · 결과(우)
        _eventEffectText = InfoCell(p, 0f,    0.64f, -60f, 17f, new Color(0.95f, 0.66f, 0.22f, 1f), FontStyles.Bold, false);
        _resultText      = InfoCell(p, 0.64f, 1f,    -60f, 20f, ShopUIStyle.TextPrimary,            FontStyles.Bold, true);
    }

    private static TMP_Text InfoCell(Transform p, float x0, float x1, float y, float size, Color color, FontStyles style, bool right)
    {
        var t = ShopUIStyle.MakeText(p, "Cell", size, style,
                                     right ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft, color);
        ShopUIStyle.Anchor(t.rectTransform, new Vector2(x0, 1), new Vector2(x1, 1), new Vector2(0, 1),
                           new Vector2(24, y), new Vector2(-24, size + 14));
        return t;
    }

    private void BuildActions(Transform w)
    {
        _enhanceBtn = MakeStyledButton(w, "Enhance", "강 화", out _enhanceLabel);
        _enhanceLabel.fontSize = 26f;
        ShopUIStyle.Anchor((RectTransform)_enhanceBtn.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0),
                           new Vector2(0.5f, 0), new Vector2(0, 26), new Vector2(440, 76));
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

        // 진화 버튼(좌하단) — 진화 조건 충족 시에만 노출, 클릭 시 선택 연출 패널
        _evolveBtn = MakeStyledButton(w, "Evolve", "진화", out _);
        _evolveBtn.GetComponent<Image>().color = new Color(0.40f, 0.28f, 0.62f, 1f);   // 프리즘 톤
        ShopUIStyle.Anchor((RectTransform)_evolveBtn.transform, new Vector2(0, 0), new Vector2(0, 0),
                           new Vector2(0, 0), new Vector2(26, 24), new Vector2(170, 50));
        _evolveBtn.onClick.AddListener(ShowEvolvePanel);
        _evolveBtn.gameObject.SetActive(false);
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

    // ── 이벤트 배너 (헤더 아래 · HasEvent 시 표시) ───────────
    private void BuildEventBanner(Transform w)
    {
        var banner = ShopUIStyle.MakeImage(w, "EventBanner", new Color(0.30f, 0.16f, 0.05f, 1f));
        ShopUIStyle.Anchor(banner.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
                           new Vector2(0, -142), new Vector2(-60, 36));
        _eventBanner = banner.gameObject;
        _eventBannerText = ShopUIStyle.MakeText(banner.transform, "Text", 16f, FontStyles.Bold,
                                                TextAlignmentOptions.Center, ShopUIStyle.Gold);
        ShopUIStyle.Stretch(_eventBannerText.rectTransform);
        _eventBanner.SetActive(false);
    }

    // ── 진화 선택 패널 (선택 연출 오버레이) ──────────────────
    private void BuildEvolvePanel(Transform w)
    {
        var dim = ShopUIStyle.MakeImage(w, "EvolveDim", new Color(0.02f, 0.02f, 0.04f, 0.90f), raycast: true);
        ShopUIStyle.Stretch(dim.rectTransform);
        _evolvePanel = dim.gameObject;
        var d = dim.transform;

        var title = ShopUIStyle.MakeText(d, "EvolveTitle", 28f, FontStyles.Bold,
                                         TextAlignmentOptions.Center, ShopUIStyle.Gold);
        title.text = "무기가 형태를 선택한다";
        ShopUIStyle.Anchor(title.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                           new Vector2(0, -120), new Vector2(760, 48));

        var sub = ShopUIStyle.MakeText(d, "EvolveSub", 15f, FontStyles.Italic,
                                       TextAlignmentOptions.Center, ShopUIStyle.TextDim);
        sub.text = "✧ 되돌릴 수 없는 선택 ✧";
        ShopUIStyle.Anchor(sub.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                           new Vector2(0, -172), new Vector2(760, 28));

        _branchABtn = BuildBranchCard(d, -200f, out _branchAName);
        _branchBBtn = BuildBranchCard(d,  200f, out _branchBName);
        _branchABtn.onClick.AddListener(() => OnBranchClicked(0));
        _branchBBtn.onClick.AddListener(() => OnBranchClicked(1));

        var cancel = MakeStyledButton(d, "EvolveCancel", "취소", out _);
        ShopUIStyle.Anchor((RectTransform)cancel.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0),
                           new Vector2(0, 56), new Vector2(200, 50));
        cancel.onClick.AddListener(HideEvolvePanel);

        _evolvePanel.SetActive(false);
    }

    private Button BuildBranchCard(Transform d, float xCenter, out TMP_Text nameText)
    {
        var card = ShopUIStyle.MakeFrame(d, "Branch", ShopUIStyle.CardBorder, ShopUIStyle.CardFill, 3f, raycast: true);
        var rt = (RectTransform)card.transform.parent;
        ShopUIStyle.Anchor(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                           new Vector2(xCenter, 10f), new Vector2(340, 300));
        var c = card.transform;

        nameText = ShopUIStyle.MakeText(c, "Name", 24f, FontStyles.Bold,
                                        TextAlignmentOptions.Center, ShopUIStyle.TextPrimary);
        ShopUIStyle.Anchor(nameText.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
                           new Vector2(0, -110), new Vector2(-24, 70));

        var hint = ShopUIStyle.MakeText(c, "Hint", 14f, FontStyles.Normal,
                                        TextAlignmentOptions.Center, ShopUIStyle.TextDim);
        hint.text = "클릭하여 이 형태로 진화";
        ShopUIStyle.Anchor(hint.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0),
                           new Vector2(0, 30), new Vector2(-24, 24));

        var pick = card.transform.parent.gameObject.AddComponent<Button>();
        pick.transition = Selectable.Transition.None;
        return pick;
    }

    private void ShowEvolvePanel()
    {
        if (_evolvePanel == null || _controller == null) return;
        // 분기 이름 — WeaponEvolutionSO 미연결이라 무기 타입 기준 임시. Phase 3에서 실제 분기 데이터로 대체.
        var wp = _controller.GetSlot(PlayerWeaponManager.Slot0);
        bool sword = wp != null && (wp.weaponType == WeaponType.Katana || wp.weaponType == WeaponType.Greatsword);
        if (_branchAName != null) _branchAName.text = sword ? "카타나\n<size=70%>아론다이트</size>" : "활\n<size=70%>연발형</size>";
        if (_branchBName != null) _branchBName.text = sword ? "대검\n<size=70%>갈라틴</size>"   : "석궁\n<size=70%>관통형</size>";
        _evolvePanel.SetActive(true);
        ShopUIStyle.PlaySfx("shop_open");
    }

    private void HideEvolvePanel()
    {
        if (_evolvePanel != null) _evolvePanel.SetActive(false);
    }

    private void OnBranchClicked(int branch)
    {
        // TODO(진화): WeaponEvolutionSO 분기 → PlayerWeaponManager.EvolveCurrentWeaponAsync 연결 (Phase 3)
        HideEvolvePanel();
        if (_resultText != null) _resultText.text = "<color=#9D7EE6>진화 연결 예정 (Phase 3)</color>";
        ShopUIStyle.PlaySfx("shop_reject");
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

        if (_eventBanner != null)
        {
            bool ev = _controller.HasEvent;
            _eventBanner.SetActive(ev);
            if (ev && _eventBannerText != null) _eventBannerText.text = _controller.EventBanner;
        }
        if (_evolveBtn != null)
            _evolveBtn.gameObject.SetActive(_controller.CanPromote(PlayerWeaponManager.Slot0));

        for (int i = 0; i < 2; i++)
        {
            var w = _controller.GetSlot(i);
            bool isTarget = i == _targetSlot;

            _cardBg[i].color = isTarget ? CardTargetBg : ShopUIStyle.CardFill;
            var typeText = i == 0 ? _focusTypeText : _dockTypeText;

            if (w == null)
            {
                _cardName[i].text = "—";
                _cardLevel[i].text = "";
                if (_cardAtk[i] != null) _cardAtk[i].text = "";
                if (typeText != null)    typeText.text = "";
                SetGauge(i, 0, 1);
                continue;
            }

            int max = _controller.MaxAt(i);
            string legend = string.IsNullOrEmpty(w.legendId) ? "" : $"  <color=#FFD24A>[{LegendName(w.legendId)}]</color>";
            _cardName[i].text = $"{w.displayName}{legend}";
            _cardLevel[i].text = FormatLevel(w.enhanceLevel, max);
            if (_cardAtk[i] != null) _cardAtk[i].text = $"공격 {w.baseAttack:F0}";
            if (typeText != null)    typeText.text = TypeTierLabel(w, max);
            SetGauge(i, w.enhanceLevel, max);
        }

        RefreshFocusExtras();
        RefreshInfo();
        RefreshPromoteRow();
    }

    /// <summary>근접 집중 카드의 안전/도박 구간 배지 + 진화 마일스톤 라벨(실데이터).</summary>
    private void RefreshFocusExtras()
    {
        if (_zoneTag == null) return;
        var w = _controller.GetSlot(PlayerWeaponManager.Slot0);
        if (w == null) { _zoneTag.text = ""; if (_milestoneLabel != null) _milestoneLabel.text = ""; return; }

        int  max    = _controller.MaxAt(PlayerWeaponManager.Slot0);
        bool maxed  = !_controller.CanEnhance(PlayerWeaponManager.Slot0);
        bool danger = _controller.DropAt(PlayerWeaponManager.Slot0) > 0;   // 실패 하락 시작 = 도박구간

        _zoneTag.text = danger ? "도박구간" : "안전구간";
        if (_zoneBg != null)
            _zoneBg.color = danger ? new Color(0.42f, 0.14f, 0.16f, 1f) : new Color(0.15f, 0.32f, 0.2f, 1f);

        if (_milestoneLabel != null)
        {
            if (_controller.CanPromote(PlayerWeaponManager.Slot0)) _milestoneLabel.text = "◆ 진화 가능!";
            else if (!maxed) _milestoneLabel.text = $"◆ 진화까지 {Mathf.Max(0, max - w.enhanceLevel)}강";
            else _milestoneLabel.text = "";
        }

        // 다음 강화 상세(성공 공격증가 / 실패 하락 / 잭팟 확률)
        if (_detailSuccess != null)
        {
            if (maxed)
            {
                _detailSuccess.text = "최대 강화 도달";
                _detailFail.text    = _controller.CanPromote(PlayerWeaponManager.Slot0) ? "진화 가능" : "";
                _detailJackpot.text = "";
            }
            else
            {
                var table = _controller.Table;
                float cur  = w.baseAttack;
                float next = table != null ? (w.baseAttackRaw > 0f ? w.baseAttackRaw : w.baseAttack) * table.AttackMult(w.enhanceLevel + 1, w.legendId) : cur;
                int drop = _controller.DropAt(PlayerWeaponManager.Slot0);
                _detailSuccess.text = $"<color=#7AD46E>성공</color>  공격 {cur:F0} → {next:F0} <size=80%>(+{next - cur:F0})</size>";
                _detailFail.text    = drop > 0 ? $"<color=#FF7A6A>실패</color>  강화 -{drop}"
                                               : "<color=#7AD46E>실패</color>  하락 없음 (안전)";
                _detailJackpot.text = $"<color=#FFD24A>잭팟</color>  {_controller.JackpotChance * 100f:F0}% <size=80%>스트릭 {_controller.Streak}</size>";
            }
        }
    }

    private void RefreshInfo()
    {
        var w = _controller.GetSlot(_targetSlot);
        if (w == null)
        {
            if (_successText != null)     _successText.text = "";
            if (_costText != null)        _costText.text = "";
            if (_streakText != null)      _streakText.text = "";
            if (_eventEffectText != null) _eventEffectText.text = "";
            return;
        }

        bool maxed = !_controller.CanEnhance(_targetSlot);
        if (maxed)
        {
            _successText.text = "<color=#8AB0D5>최대 강화 도달</color>";
            _costText.text = _controller.CanPromote(_targetSlot) ? "진화 가능" : "";
        }
        else
        {
            float chance = _controller.SuccessChanceAt(_targetSlot);
            int cost = _controller.CostAt(_targetSlot);
            _successText.text = $"성공률 <color=#7AD46E>{chance * 100f:F0}%</color>";
            _costText.text = $"재료 {cost}";
        }

        _streakText.text = _controller.Streak > 0 ? $"🔥 연속 {_controller.Streak}" : "";
        if (_eventEffectText != null)
            _eventEffectText.text = _controller.HasEvent ? $"⚡ {_controller.EventEffectDesc}" : "";
    }

    /// <summary>무기 타입·티어 라벨(카드 상단 디테일). 티어는 강화 상한(6/9/12/15)으로 추정.</summary>
    private string TypeTierLabel(WeaponData w, int max)
    {
        if (w == null) return "";
        string type = w.weaponType switch
        {
            WeaponType.Katana     => "카타나",
            WeaponType.Greatsword => "대검",
            _                     => w.weaponType.ToString(),
        };
        int tier = max <= 6 ? 1 : max <= 9 ? 2 : max <= 12 ? 3 : 4;
        return $"{type} · 티어 {tier}";
    }

    private void RefreshPromoteRow()
    {
        // legendId 승급행 폐기 예정 — 진화 패널(WeaponEvolutionSO)로 대체. 지금은 숨겨 레이아웃 정리.
        if (_promoteRow != null) _promoteRow.gameObject.SetActive(false);
    }

    private string LegendName(string legendId)
    {
        if (_controller?.Table != null && _controller.Table.TryGetLegend(legendId, out var l))
            return l.displayName;
        return legendId;
    }
}
