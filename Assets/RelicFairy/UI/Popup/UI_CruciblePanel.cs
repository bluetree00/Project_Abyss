using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
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
    public override bool CloseOnEscape  => true; // ESC = 나가기(기존 동작, EscKeyListener 공용 경로)

    // 전체화면 탭형 레이아웃(디자이너 완성본 기준). 창=캔버스 꽉, 좌 스테이지 / 우 정보 컬럼 / 하단 대사 밴드.
    private const float Margin   = 28f;
    private const float TopBarH  = 92f;
    private const float InfoColW = 440f;    // 우측 강화정보 컬럼 폭
    private const float ColGap   = 26f;
    private const float DialogH  = 118f;    // 하단 대사 밴드 높이
    private const float StageW   = 1257f;   // 재련소 배경 아트 실치수(1676×798@2x ÷ 2 × 1.5)
    private const float StageH   = 599f;

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

    // ── 스테이지 중앙 결과 팝업 (판정을 시선 위에서 크게) ──
    private const float ResultPopDur    = 0.12f;  // 등장(큰 글씨 → 제자리)
    private const float ResultHoldDur   = 0.85f;
    private const float ResultFadeDur   = 0.30f;
    private const float ResultY         = 220f;   // 스테이지 중앙 기준 — 카드 윗변(+159)과 타입 줄(+263) 사이
    private const float SuccessFontSize = 40f;
    private const float SuccessPopScale = 1.35f;
    private const float JackpotFontSize = 56f;    // 잭팟은 한눈에 다르게
    private const float JackpotPopScale = 1.9f;
    private const float RejectFontSize  = 28f;    // 거부(재료 부족 등)는 판정이 아니라 안내
    private const float RejectPopScale  = 1.12f;

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
    [SerializeField] private TMP_Text _fuelText;
    [SerializeField] private TMP_Text _oreText;
    [SerializeField] private TMP_Text _dialogText;

    // 슬롯 카드(2)
    private readonly Image[]         _cardBg        = new Image[2];
    private readonly TMP_Text[]      _cardName      = new TMP_Text[2];
    private readonly TMP_Text[]      _cardLevel     = new TMP_Text[2];
    private readonly TMP_Text[]      _cardAtk       = new TMP_Text[2];
    private readonly RectTransform[] _cardGaugeFill = new RectTransform[2];
    private readonly Vector2[]       _cardBasePos   = new Vector2[2]; // 카드 기준 앵커 위치(쉐이크 복원용)

    // 원거리 파츠 — 완성본 원거리 탭은 4슬롯(활·분열·관통·빈). 슬롯 UI는 레이아웃, 로직은 Phase 3.
    // 파츠 5종 = 5행. 내장형이라 '슬롯'이 아니라 파츠 정의 목록의 인덱스가 곧 행 번호다.
    private const int PartRows = 5;
    private readonly Image[]    _partsSlots = new Image[PartRows];
    private readonly TMP_Text[] _partsMark  = new TMP_Text[PartRows];

    // 전체화면 탭형 재설계 — 스킨/탭/스테이지 컨테이너
    private CrucibleSkinSO _skin;
    [SerializeField] private RectTransform  _meleeStage, _rangedStage;
    [SerializeField] private TMP_Text       _stageResult;      // 스테이지 중앙 결과 팝업(두 탭 공용)
    private int            _stageResultSeq;   // 연타 시 이전 페이드가 새 결과를 지우지 않게 하는 세대 번호
    [SerializeField] private Image          _tabWeaponImg, _tabRangedImg;
    private int            _activeTab;   // 0=무기강화 1=원거리 파츠

    // 근접 집중 카드 — 안전/도박 구간 배지 + 진화 마일스톤(실데이터: DropAt/레벨/승급)
    [SerializeField] private Image    _zoneBg;
    [SerializeField] private TMP_Text _zoneTag;
    [SerializeField] private TMP_Text _milestoneLabel;

    // 이벤트 배너(HasEvent — Discount/Fever) + 진화 선택 패널(선택 연출)
    [SerializeField] private GameObject _eventBanner;
    [SerializeField] private TMP_Text   _eventBannerText;
    [SerializeField] private GameObject _evolvePanel;
    [SerializeField] private Button     _evolveBtn;
    [SerializeField] private TMP_Text   _branchAName, _branchBName;
    [SerializeField] private Button     _branchABtn,  _branchBBtn;

    // 디테일 콘텐츠 — 무기 타입·티어 라인 + "다음 강화 상세"(성공/실패/잭팟) + 정보 잭팟·이벤트효과
    [SerializeField] private TMP_Text _focusTypeText;
    [SerializeField] private TMP_Text _detailSuccess, _detailFail, _detailJackpot;
    [SerializeField] private TMP_Text _dockTypeText;
    [SerializeField] private TMP_Text _eventEffectText;

    // 정보
    [SerializeField] private TMP_Text _successText;
    [SerializeField] private TMP_Text _costText;
    [SerializeField] private TMP_Text _streakText;
    [SerializeField] private TMP_Text _resultText;

    [SerializeField] private Button   _enhanceBtn;
    [SerializeField] private TMP_Text _enhanceLabel;
    [SerializeField] private TMP_Text _jackpotHint;

    // 원거리 무기(슬롯1) 강화 행 — 파츠 슬롯을 여는 유일한 투자 경로.

    // ── 원거리 탭(2차 레이아웃) ──
    [SerializeField] private TMP_Text _slotSummary;
    [SerializeField] private TMP_Text _partKindText;
    [SerializeField] private TMP_Text _partGrowthText;
    [SerializeField] private TMP_Text _partDescText;
    [SerializeField] private TMP_Text _partSynergyText;
    [SerializeField] private TMP_Text _partCostText;
    [SerializeField] private Button   _partEnhanceBtn;
    [SerializeField] private TMP_Text _partEnhanceLabel;

    // 슬롯 카드 내부 — 클릭 없이 조합을 읽으려면 카드가 스스로 내용을 말해야 한다.
    private readonly TMP_Text[] _slotKind = new TMP_Text[PartRows];
    private readonly TMP_Text[] _slotVal  = new TMP_Text[PartRows];
    private readonly TMP_Text[] _slotName = new TMP_Text[PartRows];
    private readonly TMP_Text[] _slotLv   = new TMP_Text[PartRows];
    private readonly RectTransform[] _slotBar = new RectTransform[PartRows];
    // 행마다 강화 버튼을 둔다 — 이 화면의 질문이 "어디에 몰아줄까"라, 비용이 한 번에
    // 하나씩만 보이면(예전의 상세 패널 버튼 1개) 5종 비교가 성립하지 않는다.
    private readonly Button[]   _slotBtn   = new Button[PartRows];
    private readonly TMP_Text[] _slotBtnLbl= new TMP_Text[PartRows];
    [SerializeField] private RectTransform _promoteRow;
    private readonly List<Button> _legendBtns = new();

    private bool _built;
    private bool _closing;
    private bool _animating;   // 연출 재생 중(결과는 이미 확정 — 표시층만 진행 중)
    private bool _skipAnim;    // 재입력이 들어와 진행 중 연출을 즉시 끝내는 중
    private Action _queued;    // 연출 중 눌린 다음 강화(항상 최신 1개만 유지)

    // ── Lifecycle ───────────────────────────────────────────

    public override void Init()
    {
        base.Init();
        BuildChrome();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();   // 차단 잠금 누수 방지(UI_Popup)
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
        // 프리팹이 구워져 있으면 <b>짓지 않고 잇기만 한다</b> — 다시 지으면 UI가 두 벌 겹친다.
        if (transform.childCount > 0) { BindBakedHierarchy(); return; }

        _skin = UISkin.Crucible;

        ShopUIStyle.Stretch(GetComponent<RectTransform>());

        var veil = ShopUIStyle.MakeImage(transform, "Veil", ShopUIStyle.Veil, raycast: true);
        ShopUIStyle.Stretch(veil.rectTransform);

        // 전체화면 창(캔버스 꽉) — 배경 일러스트가 아트, 단색은 폴백.
        var window = ShopUIStyle.MakeImage(transform, "Window", ShopUIStyle.WindowFill, raycast: true);
        ShopUIStyle.Stretch(window.rectTransform);
        ShopUIStyle.Skin(window, _skin?.background);
        var w = window.transform;

        BuildTopBar(w);
        BuildStages(w);
        BuildInfoColumn(w);
        BuildActionsColumn(w);
        BuildDialogueBand(w);
        BuildEventBanner(w);
        BuildEvolvePanel(w);   // 선택 연출 오버레이(최상단, 기본 비활성)
        // 별도 ✕ 버튼은 두지 않는다 — 우상단 재화 칸과 겹치고, 닫기는 [나가기] 버튼과 ESC로 이미 두 경로가 있다.

        // 승급행은 폐기 예정이나 BuildLegendButtons가 non-null을 요구 → 숨긴 컨테이너만 유지.
        var promo = ShopUIStyle.MakeRect(w, "PromoteRow", typeof(HorizontalLayoutGroup));
        _promoteRow = (RectTransform)promo.transform;
        _promoteRow.gameObject.SetActive(false);

        SelectTab(0);
    }

    /// <summary>
    /// 구워진 프리팹을 잇는다 — <b>계층·좌표·아트는 프리팹이 갖고, 코드는 배선만 한다.</b>
    ///
    /// <para>파츠 행 배열들은 <c>readonly</c>라 직렬화되지 않는다(Unity는 readonly 필드를 저장하지 않는다).
    /// 행 이름 <c>PartRow{i}</c>로 찾아 다시 채우고, 클릭·호버도 그때 같이 건다.</para>
    /// </summary>
    private void BindBakedHierarchy()
    {
        _skin = UISkin.Crucible;

        for (int i = 0; i < PartRows; i++)
        {
            var row = FindDeep($"PartRow{i}");
            if (row == null) continue;

            _partsSlots[i] = row.GetComponent<Image>();
            _slotKind[i]   = Txt(row, "Mark");
            _slotName[i]   = Txt(row, "Nm");
            _slotVal[i]    = Txt(row, "Val");
            _slotLv[i]     = Txt(row, "Lv");
            _slotBar[i]    = row.Find("LvBar") as RectTransform;
            _partsMark[i]  = Txt(row, "Lock");

            var enhance = row.Find("RowEnhance");
            if (enhance != null && enhance.TryGetComponent<Button>(out var eb))
            {
                int bi = i;
                _slotBtn[i]    = eb;
                _slotBtnLbl[i] = enhance.GetComponentInChildren<TMP_Text>(true);
                eb.onClick.RemoveAllListeners();
                eb.onClick.AddListener(() => EnhancePartRow(bi));
            }

            int idx = i;
            if (row.TryGetComponent<Button>(out var rowBtn))
            {
                rowBtn.onClick.RemoveAllListeners();
                rowBtn.onClick.AddListener(() => SelectPartSlot(idx));
            }
            if (row.TryGetComponent<EventTrigger>(out var hover))
            {
                hover.triggers.Clear();
                AddTrigger(hover, EventTriggerType.PointerEnter, () => { _hoverPartSlot = idx; RefreshRangedCard(); });
                AddTrigger(hover, EventTriggerType.PointerExit,  () => { _hoverPartSlot = -1;  RefreshRangedCard(); });
            }
        }

        Rewire(_enhanceBtn, OnEnhanceClicked);
        Rewire(_evolveBtn,  ShowEvolvePanel);
        Rewire(_branchABtn, () => OnBranchClicked(0));
        Rewire(_branchBBtn, () => OnBranchClicked(1));
        Rewire(FindDeep("Exit")?.GetComponent<Button>(),          ClosePopupUI);
        Rewire(FindDeep("EvolveCancel")?.GetComponent<Button>(),  HideEvolvePanel);
    }

    private Transform FindDeep(string name)
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    private static TMP_Text Txt(Transform p, string n) => p.Find(n)?.GetComponent<TMP_Text>();

    /// <summary>같은 팝업이 다시 열려도 리스너가 겹쳐 쌓이지 않도록 지우고 건다.</summary>
    private static void Rewire(Button btn, UnityEngine.Events.UnityAction fn)
    {
        if (btn == null) return;
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(fn);
    }

    private void BuildTopBar(Transform w)
    {
        // 타이틀 — 모루 아이콘 + 재련소
        var anvil = ShopUIStyle.MakeImage(w, "Anvil", ShopUIStyle.Gold);
        ShopUIStyle.Anchor(anvil.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                           new Vector2(Margin + 6, -Margin - 4), new Vector2(50, 50));
        anvil.preserveAspect = true;
        ShopUIStyle.Skin(anvil, _skin?.anvilIcon);

        var title = ShopUIStyle.MakeText(w, "Title", 30f, FontStyles.Bold,
                                         TextAlignmentOptions.MidlineLeft, ShopUIStyle.Gold);
        title.text = "재련소";
        ShopUIStyle.Anchor(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                           new Vector2(Margin + 72, -Margin - 4), new Vector2(150, 46));

        // 탭 — 무기강화 / 원거리 파츠. 아트 실치수 390×131@2x → 293×98, 여기선 0.8배로 상단바에 맞춘다.
        _tabWeaponImg = MakeTab(w, "TabWeapon", "무기강화",    Margin + 240, () => SelectTab(0));
        _tabRangedImg = MakeTab(w, "TabRanged", "원거리 파츠", Margin + 490, () => SelectTab(1));

        // 재화 — 원석 젬(우측 끝) + 강화재료(그 왼쪽). 칸 크기는 재화 칸 아트 실치수 175×71.
        var gem = ShopUIStyle.MakeImage(w, "GemPill", ShopUIStyle.GoldPillBg);
        ShopUIStyle.Anchor(gem.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1),
                           new Vector2(-Margin, -Margin), new Vector2(175, 71));
        ShopUIStyle.Skin(gem, _skin?.currencySlot, sliced: true);
        var gemIcon = ShopUIStyle.MakeImage(gem.transform, "GemIcon", ShopUIStyle.Gold);
        ShopUIStyle.Anchor(gemIcon.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f),
                           new Vector2(14, 0), new Vector2(38, 30));
        gemIcon.preserveAspect = true;
        ShopUIStyle.Skin(gemIcon, _skin?.currencyGem);
        _oreText = ShopUIStyle.MakeText(gem.transform, "Ore", 21f, FontStyles.Bold,
                                        TextAlignmentOptions.Right, ShopUIStyle.TextPrimary);
        ShopUIStyle.Anchor(_oreText.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f),
                           new Vector2(-18, 0), new Vector2(104, 30));

        var pill = ShopUIStyle.MakeImage(w, "FuelPill", ShopUIStyle.GoldPillBg);
        ShopUIStyle.Anchor(pill.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1),
                           new Vector2(-Margin - 187, -Margin), new Vector2(175, 71));
        ShopUIStyle.Skin(pill, _skin?.currencySlot, sliced: true);
        _fuelText = ShopUIStyle.MakeText(pill.transform, "Fuel", 20f, FontStyles.Bold,
                                         TextAlignmentOptions.Center, ShopUIStyle.Gold);
        ShopUIStyle.Stretch(_fuelText.rectTransform);
    }

    /// <summary>상단 탭 하나 — 아트(선택/미선택)는 SelectTab에서 갈아끼운다.</summary>
    private Image MakeTab(Transform w, string name, string label, float xFromLeft, Action onClick)
    {
        var tab = ShopUIStyle.MakeImage(w, name, ShopUIStyle.BandFill, raycast: true);
        ShopUIStyle.Anchor(tab.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                           new Vector2(xFromLeft, -Margin + 4), new Vector2(234, 79));
        var t = ShopUIStyle.MakeText(tab.transform, "L", 18f, FontStyles.Bold,
                                     TextAlignmentOptions.Center, ShopUIStyle.TextPrimary);
        t.text = label;
        ShopUIStyle.Stretch(t.rectTransform);
        var btn = tab.gameObject.AddComponent<Button>();
        ShopUIStyle.ApplyButtonColors(btn, tab);
        btn.onClick.AddListener(() => onClick?.Invoke());
        return tab;
    }

    /// <summary>탭 전환 — 스테이지 표시 토글 + 대상 슬롯 전환 + 탭 아트 갱신 + 갱신.</summary>
    private void SelectTab(int tab)
    {
        _activeTab = tab;
        if (_meleeStage != null)  _meleeStage.gameObject.SetActive(tab == 0);
        if (_rangedStage != null) _rangedStage.gameObject.SetActive(tab == 1);

        ApplyTab(_tabWeaponImg, _skin?.tabWeaponOn, _skin?.tabWeaponOff, tab == 0);
        ApplyTab(_tabRangedImg, _skin?.tabRangedOn, _skin?.tabRangedOff, tab == 1);

        // 액션 컬럼의 강화·진화 버튼은 <b>근접 탭 전용</b>이다.
        // 파츠 탭에는 행마다 강화 버튼이 있고, 진화(승급)는 근접 무기 대상이라
        // 원거리 화면에 남겨두면 "지금 무엇을 올리는 버튼인지"가 어긋난다.
        if (_enhanceBtn != null) _enhanceBtn.gameObject.SetActive(tab == 0);
        if (_evolveBtn  != null && tab == 1) _evolveBtn.gameObject.SetActive(false);
        if (tab == 0)
        {
            if (_enhanceLabel != null) _enhanceLabel.text = "강화하기";
            SkinButton(_enhanceBtn, _skin?.enhanceButton, _enhanceLabel);
        }

        // 강화 대상 슬롯은 <b>항상 근접</b>이다. 원거리 무기 강화를 폐지했으므로 탭을 옮겨도 바뀌지 않는다.
        // 예전엔 탭1에서 Slot1로 바꿨는데, 연출 큐에 남은 예약이 탭 전환 뒤 실행되면
        // 엉뚱한 슬롯을 강화할 수 있는 경로였다.
        _targetSlot = PlayerWeaponManager.Slot0;

        // 정보창은 탭 전환에도 살아 있는 공용 컬럼이다 — 파츠 탭에서는 발사 미리보기로 쓴다.
        if (_infoTitle != null) _infoTitle.text = tab == 0 ? "강화정보" : "발사 미리보기";

        if (tab == 1) RefreshRangedParts();   // 파츠 레벨·비용을 열 때마다 최신으로
        if (_controller != null) { UpdateTargetDialogue(); RefreshAll(); }
    }

    /// <summary>탭 아트에도 탭 이름이 구워져 있다 — 아트가 붙으면 코드 라벨을 끈다.</summary>
    private static void ApplyTab(Image img, Sprite on, Sprite off, bool active)
    {
        if (img == null) return;
        var s = active ? on : off;
        var lbl = img.GetComponentInChildren<TMP_Text>(true);
        if (s != null)
        {
            ShopUIStyle.Skin(img, s, sliced: true);
            if (lbl != null) lbl.gameObject.SetActive(false);
        }
        else
        {
            img.color = active ? ShopUIStyle.GoldPillBg : ShopUIStyle.BandFill;
            if (lbl != null) lbl.gameObject.SetActive(true);
        }
    }

    private void BuildStages(Transform w)
    {
        var area = ShopUIStyle.MakeRect(w, "StageArea").GetComponent<RectTransform>();
        area.anchorMin = new Vector2(0, 0);
        area.anchorMax = new Vector2(1, 1);
        area.pivot     = new Vector2(0.5f, 0.5f);
        area.offsetMin = new Vector2(Margin, DialogH + Margin);
        area.offsetMax = new Vector2(-(InfoColW + ColGap + Margin), -(TopBarH + Margin));

        _meleeStage  = MakeStage(area, "MeleeStage");
        _rangedStage = MakeStage(area, "RangedStage");

        BuildMeleeCard(_meleeStage);
        BuildRangedCard(_rangedStage);

        BuildStageResult(area);   // 마지막 형제 = 두 스테이지 위에 겹쳐 그려진다
    }

    /// <summary>
    /// 스테이지 중앙 결과 팝업. 성공/실패/잭팟 판정이 반대편 정보창 여덟째 줄에만 뜨면
    /// 시선(카드·게이지)과 어긋나 판정을 놓친다 — 같은 문구를 카드 바로 위에 크게 겹쳐 띄운다.
    /// 두 탭 스테이지가 같은 자리에 겹치므로 탭 컨테이너가 아니라 StageArea에 직접 붙인다.
    /// </summary>
    private void BuildStageResult(Transform area)
    {
        var t = ShopUIStyle.MakeText(area, "StageResult", SuccessFontSize, FontStyles.Bold,
                                     TextAlignmentOptions.Center, ShopUIStyle.TextPrimary);
        var half = new Vector2(0.5f, 0.5f);
        ShopUIStyle.Anchor(t.rectTransform, half, half, half, new Vector2(0f, ResultY), new Vector2(900f, 62f));
        _stageResult = t;
        t.gameObject.SetActive(false);
    }

    /// <summary>
    /// 결과 팝업 표시. 문구는 정보창(<see cref="_resultText"/>)이 방금 만든 것을 그대로 쓴다 —
    /// 두 곳에서 각자 문장을 만들면 판정이 어긋날 수 있다.
    /// </summary>
    private void ShowStageResult(string richText, float fontSize, float popScale)
    {
        if (_stageResult == null || string.IsNullOrEmpty(richText)) return;
        _stageResult.text     = richText;
        _stageResult.fontSize = fontSize;
        _stageResultSeq++;
        StageResultAsync(_stageResultSeq, popScale).Forget();
    }

    /// <summary>등장 → 유지 → 페이드. 연출 잠금 바깥에서 굴러 연타를 막지 않는다(세대가 바뀌면 즉시 물러남).</summary>
    private async UniTaskVoid StageResultAsync(int seq, float popScale)
    {
        var rt = _stageResult.rectTransform;
        _stageResult.gameObject.SetActive(true);
        _stageResult.alpha = 1f;

        try
        {
            float t = 0f;
            while (t < 1f)
            {
                if (seq != _stageResultSeq) return;
                t = Mathf.Min(t + Time.unscaledDeltaTime / ResultPopDur, 1f);
                rt.localScale = Vector3.one * Mathf.Lerp(popScale, 1f, t);
                await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
            }

            t = 0f;
            while (t < ResultHoldDur)
            {
                if (seq != _stageResultSeq) return;
                t += Time.unscaledDeltaTime;
                await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
            }

            t = 0f;
            while (t < ResultFadeDur)
            {
                if (seq != _stageResultSeq) return;
                t += Time.unscaledDeltaTime;
                _stageResult.alpha = 1f - t / ResultFadeDur;
                await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
            }
        }
        catch (OperationCanceledException) { return; }   // 패널 파괴

        if (seq != _stageResultSeq) return;
        _stageResult.gameObject.SetActive(false);
        _stageResult.alpha = 1f;
    }

    /// <summary>탭별 스테이지 컨테이너 — StageArea를 꽉 채우는 재련소 바탕 패널.</summary>
    private RectTransform MakeStage(Transform parent, string name)
    {
        var img = ShopUIStyle.MakeImage(parent, name, new Color(0.09f, 0.07f, 0.10f, 0.96f), raycast: true);
        // 재련소 배경 아트 실치수 1257×599 — 늘리면 문양이 뭉개져서, 영역을 채우지 않고 중앙에 그대로 놓는다.
        var half = new Vector2(0.5f, 0.5f);
        ShopUIStyle.Anchor(img.rectTransform, half, half, half, Vector2.zero, new Vector2(StageW, StageH));
        ShopUIStyle.Skin(img, _skin?.stagePanel, sliced: true);
        return img.rectTransform;
    }

    /// <summary>무기 넣는칸(무기 슬롯) 1장 — 완성본대로 두 슬롯 동일(테두리 + 검은 채움). 반환 Image = 연출 대상 _cardBg.</summary>
    private Image MakeSlotCard(Transform stage, string name, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        var frame = ShopUIStyle.MakeFrame(stage, name, ShopUIStyle.CardBorder, ShopUIStyle.CardFill, 3f, raycast: false);
        var bg = frame.transform.parent.GetComponent<Image>();
        ShopUIStyle.Anchor((RectTransform)bg.transform, anchor, anchor, anchor, pos, size);
        ShopUIStyle.Skin(bg,    _skin?.slotFrame, sliced: true);
        ShopUIStyle.Skin(frame, _skin?.slotFill,  sliced: true);
        return bg;
    }

    /// <summary>무기 게이지(하단 스팬) — 검은 바탕 + 빨강 채움 + 테두리 + 마일스톤 틱. fill의 RectTransform 반환.</summary>
    private RectTransform BuildGauge(Transform stage, Sprite trackArt, Sprite fillArt, Sprite frameArt,
                                     float y, float h, float width)
    {
        var track = ShopUIStyle.MakeImage(stage, "GaugeTrack", GaugeTrack);
        ShopUIStyle.Anchor(track.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0),
                           new Vector2(0, y), new Vector2(width, h));
        ShopUIStyle.Skin(track, trackArt, sliced: true);

        var fill = ShopUIStyle.MakeImage(track.transform, "Fill", GaugeFillC);
        var fr = fill.rectTransform;
        fr.anchorMin = new Vector2(0f, 0f); fr.anchorMax = new Vector2(0f, 1f); fr.pivot = new Vector2(0f, 0.5f);
        fr.offsetMin = Vector2.zero; fr.offsetMax = Vector2.zero;
        ShopUIStyle.Skin(fill, fillArt, sliced: true);

        if (frameArt != null)
        {
            var frame = ShopUIStyle.MakeImage(track.transform, "GaugeFrame", Color.white);
            ShopUIStyle.Stretch(frame.rectTransform);
            frame.raycastTarget = false;
            ShopUIStyle.Skin(frame, frameArt, sliced: true);
        }

        var tick = ShopUIStyle.MakeImage(track.transform, "MilestoneTick", ShopUIStyle.Gold);
        var tr = tick.rectTransform;
        tr.anchorMin = new Vector2(1f, -0.25f); tr.anchorMax = new Vector2(1f, 1.25f); tr.pivot = new Vector2(0.5f, 0.5f);
        tr.sizeDelta = new Vector2(4f, 0f); tr.anchoredPosition = Vector2.zero;

        return fr;
    }

    /// <summary>무기강화 탭 — 무기 카드(좌) ▸ 결과 카드(우) + 하단 게이지 + 위험/안전 배지. 상세/마일스톤은 우측 정보창으로.</summary>
    private void BuildMeleeCard(RectTransform stage)
    {
        // 위험/안전 배지(우상단)
        // 위험/안전 배지. 아트(위험 하락)에는 "위험-하락" 글자가 구워져 있어 도박구간에서만 쓴다 —
        // 안전구간에는 아트가 없으므로 색 알약 + 글자로 그린다(둘이 겹치지 않게 서로 배타).
        _zoneBg = ShopUIStyle.MakeImage(stage, "ZoneBg", new Color(0.15f, 0.32f, 0.20f, 1f));
        ShopUIStyle.Anchor(_zoneBg.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1),
                           new Vector2(-20, -18), new Vector2(135, 48));
        _zoneTag = ShopUIStyle.MakeText(_zoneBg.transform, "ZoneTag", 15f, FontStyles.Bold,
                                        TextAlignmentOptions.Center, ShopUIStyle.TextPrimary);
        _zoneTag.text = "안전구간";
        ShopUIStyle.Stretch(_zoneTag.rectTransform);

        // 타입·티어(스테이지 좌상단)
        _focusTypeText = ShopUIStyle.MakeText(stage, "TypeTier", 15f, FontStyles.Bold,
                                              TextAlignmentOptions.TopLeft, ShopUIStyle.Gold);
        ShopUIStyle.Anchor(_focusTypeText.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1),
                           new Vector2(20, -14), new Vector2(-260, 22));

        // 무기 카드 ▸ 결과 카드. 두 장을 중앙 가까이 모은다 —
        // 스테이지 양끝에 붙여 두면 가운데가 통째로 비어 "빈 화면"으로 읽힌다.
        var cardSize = new Vector2(340f, 251f);   // 무기 넣는칸 테두리 547×404@2x
        var half = new Vector2(0.5f, 0.5f);
        _cardBg[0]      = MakeSlotCard(stage, "TargetCard", half, new Vector2(-236f, 34f), cardSize);
        _cardBasePos[0] = new Vector2(-236f, 34f);
        var resultCard  = MakeSlotCard(stage, "ResultCard", half, new Vector2(236f, 34f), cardSize);

        // 화살표(가운데) — 재련소 화살표 86×98@2x
        if (_skin?.arrow != null)
        {
            var arrow = ShopUIStyle.MakeImage(stage, "Arrow", Color.white);
            ShopUIStyle.Anchor(arrow.rectTransform, half, half, half, new Vector2(0, 34f), new Vector2(65, 74));
            arrow.preserveAspect = true;
            ShopUIStyle.Skin(arrow, _skin.arrow);
        }
        else
        {
            var arrow = ShopUIStyle.MakeText(stage, "Arrow", 40f, FontStyles.Bold, TextAlignmentOptions.Center, ShopUIStyle.Gold);
            arrow.text = "▶";
            ShopUIStyle.Anchor(arrow.rectTransform, half, half, half, new Vector2(0, 34f), new Vector2(65, 74));
        }

        // 무기 예시 아이콘(좌 카드) — 무기 예시 233×232@2x
        var wpn = ShopUIStyle.MakeImage(_cardBg[0].transform, "Weapon", new Color(1f, 1f, 1f, 0.85f));
        ShopUIStyle.Anchor(wpn.rectTransform, half, half, half,
                           new Vector2(0, 16f), new Vector2(160, 160));
        wpn.preserveAspect = true;
        if (_skin?.weaponExample != null) ShopUIStyle.Skin(wpn, _skin.weaponExample);
        else wpn.color = new Color(1f, 1f, 1f, 0.12f);

        _cardName[0] = ShopUIStyle.MakeText(_cardBg[0].transform, "Name", 20f, FontStyles.Bold,
                                            TextAlignmentOptions.Center, ShopUIStyle.TextPrimary);
        ShopUIStyle.Anchor(_cardName[0].rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0),
                           new Vector2(0, 12), new Vector2(-16, 28));

        // 결과 카드에 강화 레벨(큰 숫자)
        _cardLevel[0] = ShopUIStyle.MakeText(resultCard.transform, "Level", 44f, FontStyles.Bold,
                                             TextAlignmentOptions.Center, ShopUIStyle.Gold);
        ShopUIStyle.Stretch(_cardLevel[0].rectTransform);

        // 공격력 전→후. 카드가 보여주는 건 강화 단계뿐이라 "그래서 얼마나 세지나"가 화면에 없었다.
        // 두 카드(아래끝 -91)와 게이지(-193.5) 사이의 빈 띠에 한 줄로 깐다.
        _cardAtk[0] = ShopUIStyle.MakeText(stage, "Atk", 19f, FontStyles.Bold,
                                           TextAlignmentOptions.Center, ShopUIStyle.TextPrimary);
        ShopUIStyle.Anchor(_cardAtk[0].rectTransform, half, half, half,
                           new Vector2(0f, -136f), new Vector2(900f, 30f));

        // 하단 강화 게이지 — 테두리 아트 1588×129@2x(가로세로비 12.3)를 지켜 납작하게 눌리지 않게 한다.
        _cardGaugeFill[0] = BuildGauge(stage, _skin?.gaugeTrack, _skin?.gaugeFill, _skin?.gaugeFrame,
                                       30f, 76f, 1100f);
    }

    // ── 원거리 탭 레이아웃 상수 (스테이지 1257×599, 좌상단 기준) ──
    //
    // 이 탭에는 <b>행동이 하나</b>다 — 파츠 강화. 원거리 무기 강화는 폐지했다:
    // 그건 파츠 슬롯 게이지를 채우려고 붙은 것이고, 내장형 전환으로 슬롯이 사라지면서 이유도 사라졌다
    // (원래 기획 2026-07-18도 "무기 자체 레벨 강화 없음 · 성장은 파츠로만"이었다).
    // 공격력·공격속도는 캐릭터 스탯이 담당하므로 파츠는 '발사 형태'만 다룬다.
    private const float RxPad     = 40f;    // 스테이지 좌우 여백
    private const float RxHeadY   = 24f;    // 헤더 띠 y
    private const float RxHeadH   = 52f;
    private const float RxBodyY   = 96f;    // 본문 y
    // 무기 강화 행(y502 h68)을 폐지하면서 그 자리를 본문이 흡수했다.
    // 그냥 비워 두면 화면 아래가 뚫린 것처럼 읽혀, 행 높이를 키워 여백을 안으로 돌렸다.
    private const float RxBodyH   = 458f;
    private const float RxColLW   = 520f;   // 좌 — 조합
    private const float RxColRX   = 600f;   // 우 — 상세 시작 x
    private const float RxColRW   = 617f;
    private const float RxRowH    = 82f;    // 파츠 행 — 5행 × 82 + 4갭 × 12 = 458 (본문 높이와 정확히 일치)
    private const float RxRowGap  = 12f;

    /// <summary>스테이지 좌상단 기준 배치(anchor·pivot 모두 좌상단).</summary>
    private static void PlaceTL(RectTransform rt, float x, float y, float w, float h)
    {
        var tl = new Vector2(0f, 1f);
        ShopUIStyle.Anchor(rt, tl, tl, tl, new Vector2(x, -y), new Vector2(w, h));
    }

    /// <summary>
    /// 원거리 파츠 탭 — 3단 구성.
    ///  · 헤더  : 제목 + 슬롯 요약
    ///  · 본문  : 좌 조합 4칸(2×2) / 우 선택 파츠 상세 + 파츠 강화 버튼
    ///  · 무기행: 원거리 무기 정보 + 투자 게이지 + 무기 강화 버튼
    /// </summary>
    private void BuildRangedCard(RectTransform stage)
    {
        _dockTypeText = ShopUIStyle.MakeText(stage, "RangedTitle", 26f, FontStyles.Bold,
                                             TextAlignmentOptions.MidlineLeft, ShopUIStyle.Gold);
        PlaceTL(_dockTypeText.rectTransform, RxPad, RxHeadY, 520f, RxHeadH);
        _dockTypeText.text = "원거리 파츠";

        _slotSummary = ShopUIStyle.MakeText(stage, "SlotSummary", 16f, FontStyles.Normal,
                                            TextAlignmentOptions.MidlineRight, ShopUIStyle.TextDim);
        PlaceTL(_slotSummary.rectTransform, RxPad + 560f, RxHeadY, 1257f - RxPad * 2f - 560f, RxHeadH);

        BuildRangedParts(stage);
        BuildPartDetail(stage);
    }

    /// <summary>
    /// 우측 상세 패널. 고른 파츠 하나를 깊게 보여주고 <b>같은 패널 안에서 강화</b>한다 —
    /// 읽은 자리에서 바로 누르고, 결과(레벨·효과)도 이 패널에서 갱신되므로 연타 시 시선이 왕복하지 않는다.
    /// 연출 대상 <c>_cardBg[1]</c>도 이 패널이라 성공 펀치·플래시가 누른 자리에서 터진다.
    /// </summary>
    private void BuildPartDetail(RectTransform stage)
    {
        var panel = ShopUIStyle.MakeImage(stage, "PartDetail", new Color(0.11f, 0.09f, 0.14f, 1f));
        PlaceTL(panel.rectTransform, RxColRX, RxBodyY, RxColRW, RxBodyH);
        ShopUIStyle.Skin(panel, _skin?.slotFrame, sliced: true);
        _cardBg[1]      = panel;
        _cardBasePos[1] = panel.rectTransform.anchoredPosition;
        var c = panel.transform;

        const float padX = 26f;
        float innerW = RxColRW - padX * 2f;

        _cardName[1] = ShopUIStyle.MakeText(c, "Name", 30f, FontStyles.Bold,
                                            TextAlignmentOptions.TopLeft, ShopUIStyle.TextPrimary);
        PlaceTL(_cardName[1].rectTransform, padX, 20f, innerW, 38f);

        _partKindText = ShopUIStyle.MakeText(c, "Kind", 16f, FontStyles.Normal,
                                             TextAlignmentOptions.TopLeft, ShopUIStyle.Gold);
        PlaceTL(_partKindText.rectTransform, padX, 58f, innerW, 22f);

        _cardLevel[1] = ShopUIStyle.MakeText(c, "Level", 20f, FontStyles.Bold,
                                             TextAlignmentOptions.TopLeft, ShopUIStyle.Gold);
        PlaceTL(_cardLevel[1].rectTransform, padX, 96f, innerW, 28f);

        _cardGaugeFill[1] = BuildBar(c, "LevelBar", padX, 130f, innerW, 18f);

        _cardAtk[1] = ShopUIStyle.MakeText(c, "Delta", 26f, FontStyles.Bold,
                                           TextAlignmentOptions.TopLeft, ShopUIStyle.TextPrimary);
        PlaceTL(_cardAtk[1].rectTransform, padX, 162f, innerW, 40f);

        _partGrowthText = ShopUIStyle.MakeText(c, "Growth", 15f, FontStyles.Normal,
                                               TextAlignmentOptions.TopLeft, ShopUIStyle.TextDim);
        PlaceTL(_partGrowthText.rectTransform, padX, 208f, innerW, 24f);

        _partDescText = ShopUIStyle.MakeText(c, "Desc", 16f, FontStyles.Normal,
                                             TextAlignmentOptions.TopLeft, ShopUIStyle.TextPrimary);
        PlaceTL(_partDescText.rectTransform, padX, 244f, innerW, 84f);

        _partSynergyText = ShopUIStyle.MakeText(c, "Synergy", 15f, FontStyles.Normal,
                                                TextAlignmentOptions.TopLeft, new Color(0.50f, 0.89f, 1f));
        PlaceTL(_partSynergyText.rectTransform, padX, 334f, innerW, 24f);

        // 강화 버튼은 좌열의 각 행으로 옮겼다 — 이 패널은 이제 <b>결과 전담</b>이다.
        // (_partEnhanceBtn / _partCostText 는 null로 남으며, 갱신 함수들이 null을 걸러낸다.)
        _partCostText = ShopUIStyle.MakeText(c, "PartCost", 14f, FontStyles.Normal,
                                             TextAlignmentOptions.TopLeft, ShopUIStyle.TextDim);
        PlaceTL(_partCostText.rectTransform, padX, RxBodyH - 62f, innerW, 26f);
    }

    /// <summary>단순 진행 막대(트랙 + 채움). 채움 RectTransform을 돌려준다.</summary>
    private RectTransform BuildBar(Transform parent, string name, float x, float y, float w, float h)
    {
        var track = ShopUIStyle.MakeImage(parent, name, new Color(0.10f, 0.09f, 0.12f, 1f));
        PlaceTL(track.rectTransform, x, y, w, h);
        ShopUIStyle.Skin(track, _skin?.gaugeTrack, sliced: true);

        var fill = ShopUIStyle.MakeImage(track.transform, "Fill", ShopUIStyle.Gold);
        var fr = fill.rectTransform;
        fr.anchorMin = new Vector2(0f, 0f); fr.anchorMax = new Vector2(0f, 1f); fr.pivot = new Vector2(0f, 0.5f);
        fr.offsetMin = Vector2.zero; fr.offsetMax = Vector2.zero;
        ShopUIStyle.Skin(fill, _skin?.gaugeFill, sliced: true);
        return fr;
    }


    /// <summary>
    /// 파츠 슬롯 4칸. 내용은 <see cref="RangedPartsState"/>가 정하고, 클릭하면 강화 대상으로 선택된다.
    /// 슬롯 해금 수는 원거리 무기 강화 레벨에 종속되므로 잠긴 칸은 회색으로 남는다.
    /// </summary>
    private void BuildRangedParts(Transform c)
    {
        // 5행 리스트. 행 = 파츠 정의 목록의 인덱스이며 슬롯이 아니다 —
        // 파츠는 항상 5종 전부 존재하고 레벨(0=꺼짐)만 다르다.
        var data = Managers.WeaponParts;

        for (int i = 0; i < PartRows; i++)
        {
            float ry = RxBodyY + i * (RxRowH + RxRowGap);

            var row = ShopUIStyle.MakeImage(c, $"PartRow{i}", new Color(0.10f, 0.08f, 0.13f, 1f));
            PlaceTL(row.rectTransform, RxPad, ry, RxColLW, RxRowH);
            ShopUIStyle.Skin(row, _skin?.slotFrame, sliced: true);
            _partsSlots[i] = row;

            _slotKind[i] = ShopUIStyle.MakeText(row.transform, "Mark", 17f, FontStyles.Bold,
                                                TextAlignmentOptions.Center, ShopUIStyle.Gold);
            PlaceTL(_slotKind[i].rectTransform, 14f, 32f, 22f, 20f);

            _slotName[i] = ShopUIStyle.MakeText(row.transform, "Nm", 19f, FontStyles.Bold,
                                                TextAlignmentOptions.MidlineLeft, ShopUIStyle.TextPrimary);
            PlaceTL(_slotName[i].rectTransform, 44f, 14f, 150f, 28f);

            _slotVal[i] = ShopUIStyle.MakeText(row.transform, "Val", 15f, FontStyles.Normal,
                                               TextAlignmentOptions.MidlineLeft, ShopUIStyle.TextDim);
            PlaceTL(_slotVal[i].rectTransform, 44f, 46f, 150f, 24f);

            _slotLv[i] = ShopUIStyle.MakeText(row.transform, "Lv", 16f, FontStyles.Bold,
                                              TextAlignmentOptions.MidlineLeft, ShopUIStyle.Gold);
            PlaceTL(_slotLv[i].rectTransform, 200f, 30f, 56f, 24f);

            _slotBar[i] = BuildBar(row.transform, "LvBar", 262f, 35f, 132f, 14f);

            // 행 강화 버튼 — 누른 자리에서 결과(레벨·게이지)가 바로 갱신된다.
            _slotBtn[i] = MakeStyledButton(row.transform, "RowEnhance", "강화", out _slotBtnLbl[i]);
            _slotBtnLbl[i].fontSize = 15f;
            PlaceTL((RectTransform)_slotBtn[i].transform, 406f, 15f, 100f, 52f);
            int bi = i;
            _slotBtn[i].onClick.AddListener(() => EnhancePartRow(bi));

            // 잠금·미해금 문구용 중앙 텍스트. 평시엔 꺼둔다.
            _partsMark[i] = ShopUIStyle.MakeText(row.transform, "Lock", 15f, FontStyles.Normal,
                                                 TextAlignmentOptions.Center, ShopUIStyle.TextDim);
            ShopUIStyle.Stretch(_partsMark[i].rectTransform);
            _partsMark[i].gameObject.SetActive(false);

            int idx = i;
            var btn = row.gameObject.AddComponent<Button>();
            ShopUIStyle.ApplyButtonColors(btn, row);
            btn.onClick.AddListener(() => SelectPartSlot(idx));

            // 호버 미리보기 — 손만 올려도 상세가 그 파츠를 보여준다. 클릭은 대상 고정 전용.
            var hover = row.gameObject.AddComponent<EventTrigger>();
            AddTrigger(hover, EventTriggerType.PointerEnter, () => { _hoverPartSlot = idx; RefreshRangedCard(); });
            AddTrigger(hover, EventTriggerType.PointerExit,  () => { _hoverPartSlot = -1;  RefreshRangedCard(); });
        }

        RefreshRangedParts();
    }

    private static void AddTrigger(EventTrigger t, EventTriggerType type, System.Action fn)
    {
        var e = new EventTrigger.Entry { eventID = type };
        e.callback.AddListener(_ => fn());
        t.triggers.Add(e);
    }

    // ── 투자 진척 · 파츠 지급 ───────────────────────────────

    /// <summary>마우스가 올라간 슬롯. -1이면 없음. 선택(_selectedPartSlot)과 별개로 상세만 미리 보여준다.</summary>
    private int _hoverPartSlot = -1;



    /// <summary>강화 대상으로 고른 파츠 행. -1이면 미선택.</summary>
    private int _selectedPartSlot = -1;

    /// <summary>
    /// 파츠 행 클릭 — 강화 대상 선택 전용(다시 누르면 해제). 강화는 행의 버튼이 한다.
    /// index는 파츠 정의 목록의 인덱스다(슬롯이 아니다).
    /// </summary>
    private void SelectPartSlot(int index)
    {
        var all = Managers.WeaponParts?.All;
        if (all == null || index < 0 || index >= all.Count) return;

        if (_selectedPartSlot == index)
        {
            _selectedPartSlot = -1;
            RefreshRangedParts();
            RefreshRangedCard();
            return;
        }

        _selectedPartSlot = index;
        RefreshRangedParts();
        RefreshRangedCard();
    }

    /// <summary>행 버튼 — 그 행을 대상으로 고정하고 곧바로 강화한다(연출·큐는 기존 경로 재사용).</summary>
    private void EnhancePartRow(int index)
    {
        _selectedPartSlot = index;
        RefreshRangedCard();
        EnhanceSelectedPart();
    }

    /// <summary>파츠 탭 안내 문구. 전용 슬롯이 없어 NPC 대사창을 그대로 쓴다(정보가 한 곳에 모임).</summary>
    private void SetPartHint(string msg)
    {
        if (_dialogText != null) _dialogText.text = msg;
    }

    /// <summary>5행을 파츠 정의 목록으로 채운다. 파츠 탭을 열거나 강화한 뒤 호출.</summary>
    private void RefreshRangedParts()
    {
        var state = RangedPartsState.Current;
        var all   = Managers.WeaponParts?.All;
        int have  = _controller != null ? _controller.FuelAmount : 0;

        if (_slotSummary != null && state != null)
            _slotSummary.text = $"켠 파츠 <color=#FFD24A>{state.ActiveCount}</color> / {(all?.Count ?? 0)}"
                              + $"   ·   투입 {state.TotalSpent}   ·   보유 <color=#FFD24A>{have}</color>";

        for (int i = 0; i < PartRows; i++)
        {
            if (_partsSlots[i] == null) continue;

            var def = all != null && i < all.Count ? all[i] : null;
            bool exists = def != null;

            SetSlotDetailVisible(i, exists);
            _partsMark[i].gameObject.SetActive(!exists);
            if (_slotBtn[i] != null) _slotBtn[i].gameObject.SetActive(exists);

            if (!exists)
            {
                // 정의가 없는 남는 행 — 데이터가 5종보다 적을 때만 보인다.
                _partsMark[i].text   = "";
                _partsSlots[i].color = new Color(0.09f, 0.07f, 0.11f, 1f);
                continue;
            }

            int level = state != null ? state.LevelOf(def.part_id) : 0;
            int max   = def.max_level > 0 ? def.max_level : Mathf.Max(1, level);
            bool on   = level > 0;
            bool maxed = def.max_level > 0 && level >= def.max_level;

            // 꺼짐(Lv0)도 목록에 그대로 남긴다 — 없는 것과 안 켠 것은 다르다.
            _slotKind[i].text  = on ? "◆" : "◇";
            _slotKind[i].color = on ? ShopUIStyle.Gold : ShopUIStyle.TextDim;
            _slotName[i].text  = def.part_name;
            _slotVal[i].text   = on ? PartValueText(def, def.ValueAt(level)) : "꺼짐";
            _slotLv[i].text    = on ? $"Lv.{level}" : "—";
            SetBar(_slotBar[i], max > 0 ? (float)level / max : 0f);

            int cost = _controller != null ? _controller.PartCostAt(def.part_id) : 0;
            if (_slotBtnLbl[i] != null)
                _slotBtnLbl[i].text = maxed ? "최대" : (on ? $"강화 ◆{cost}" : $"켜기 ◆{cost}");
            if (_slotBtn[i] != null)
                _slotBtn[i].interactable = !maxed && have >= cost && !_animating;

            bool sel = (i == _selectedPartSlot);
            _partsSlots[i].color = sel  ? new Color(0.18f, 0.14f, 0.09f, 1f)
                                 : on   ? new Color(0.12f, 0.10f, 0.15f, 1f)
                                        : new Color(0.09f, 0.07f, 0.11f, 1f);
            _slotName[i].color   = sel ? ShopUIStyle.Gold
                                 : on  ? ShopUIStyle.TextPrimary : ShopUIStyle.TextDim;
        }
    }

    private void SetSlotDetailVisible(int i, bool on)
    {
        if (_slotKind[i] != null) _slotKind[i].gameObject.SetActive(on);
        if (_slotVal[i]  != null) _slotVal[i].gameObject.SetActive(on);
        if (_slotName[i] != null) _slotName[i].gameObject.SetActive(on);
        if (_slotLv[i]   != null) _slotLv[i].gameObject.SetActive(on);
        if (_slotBar[i]  != null) _slotBar[i].parent.gameObject.SetActive(on);
    }

    private static void SetBar(RectTransform fill, float ratio01)
    {
        if (fill == null) return;
        fill.anchorMin = new Vector2(0f, 0f);
        fill.anchorMax = new Vector2(Mathf.Clamp01(ratio01), 1f);
        fill.offsetMin = Vector2.zero;
        fill.offsetMax = Vector2.zero;
    }

    /// <summary>카드용 짧은 종류 이름(카드 폭이 좁아 설명 부분은 뺀다).</summary>
    private static string PartKindShort(WeaponPartEntry def) => def.Kind switch
    {
        RangedPartKind.Split   => "분열",
        RangedPartKind.Pierce  => "관통",
        RangedPartKind.Explode => "폭발",
        RangedPartKind.Homing  => "유도",
        _                      => "거력",
    };

    /// <summary>우측 강화정보 컬럼 — 완성본의 세로 정보창. 성공률/재료/성공시/실패시/잭팟/진화까지 + 스트릭/결과/이벤트.</summary>
    private void BuildInfoColumn(Transform w)
    {
        var panel = ShopUIStyle.MakeImage(w, "InfoPanel", ShopUIStyle.BandFill);
        // 강화정보창 643×680@2x → 세로비 유지(0.945)로 잡아야 팔각 프레임이 찌그러지지 않는다.
        ShopUIStyle.Anchor(panel.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1),
                           new Vector2(-Margin, -(TopBarH + Margin)), new Vector2(InfoColW, 466));
        ShopUIStyle.Skin(panel, _skin?.infoPanel, sliced: true);
        var p = panel.transform;

        _infoTitle = ShopUIStyle.MakeText(p, "InfoTitle", 20f, FontStyles.Bold,
                                          TextAlignmentOptions.Center, ShopUIStyle.Gold);
        _infoTitle.text = "강화정보";
        ShopUIStyle.Anchor(_infoTitle.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
                           new Vector2(0, -16), new Vector2(-24, 28));

        _successText     = InfoRow(p, 0);
        _costText        = InfoRow(p, 1);
        _detailSuccess   = InfoRow(p, 2);
        _detailFail      = InfoRow(p, 3);
        _detailJackpot   = InfoRow(p, 4);
        _milestoneLabel  = InfoRow(p, 5);
        _streakText      = InfoRow(p, 6);
        _resultText      = InfoRow(p, 7);
        _eventEffectText = InfoRow(p, 8);
    }

    /// <summary>정보 컬럼의 한 줄. 위에서부터 34px 간격.</summary>
    [SerializeField] private TMP_Text _infoTitle;

    // 탭별 행 높이 분기(ApplyInfoRowLayout)는 제거했다 — 파츠 설명 문장을 이 좁은 격자에 끼우느라
    // 필요했던 것인데, 상세가 스테이지 우측 패널로 옮겨가고 정보창은 수치 표(발사 미리보기)만 남아
    // 무기 탭과 같은 34px 고정 격자로 돌아왔다.

    private static TMP_Text InfoRow(Transform p, int i)
    {
        var t = ShopUIStyle.MakeText(p, "Row", 16f, FontStyles.Normal,
                                     TextAlignmentOptions.Left, ShopUIStyle.TextPrimary);
        t.richText = true;
        ShopUIStyle.Anchor(t.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1),
                           new Vector2(24, -54 - i * 34), new Vector2(-40, 30));
        return t;
    }

    /// <summary>우측 하단 조작 컬럼 — 강화하기(큰) + 전환/나가기(2열) + 진화(조건부).</summary>
    private void BuildActionsColumn(Transform w)
    {
        float colRight = -Margin;

        // 크기는 각 아트 실치수(강화하기 629×172 · 전환/나가기 304×147 @2x, 화면 배율 0.75).
        _enhanceBtn = MakeStyledButton(w, "Enhance", "강화하기", out _enhanceLabel);
        _enhanceLabel.fontSize = 24f;
        ShopUIStyle.Anchor((RectTransform)_enhanceBtn.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0),
                           new Vector2(colRight, Margin + 115), new Vector2(InfoColW, 120));
        SkinButton(_enhanceBtn, _skin?.enhanceButton, _enhanceLabel);
        _enhanceBtn.onClick.AddListener(OnEnhanceClicked);

        // 잭팟 확률은 지를지 말지를 가르는 수치인데 정보창 다섯째 줄에만 있어, 버튼을 보는 순간엔 시야 밖이었다.
        _jackpotHint = ShopUIStyle.MakeText(w, "JackpotHint", 15f, FontStyles.Bold,
                                            TextAlignmentOptions.Center, ShopUIStyle.Gold);
        ShopUIStyle.Anchor(_jackpotHint.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0),
                           new Vector2(colRight, Margin + 240), new Vector2(InfoColW, 26));

        // 탭 전환은 상단 탭 두 개가 이미 한다 — 여기 있던 [전환] 버튼은 같은 동작의 두 번째 입구였고,
        // 강화하기 바로 아래 큰 버튼이라 "슬롯 전환"으로도 읽혔다. 지워서 하단은 [나가기] 하나만 둔다.
        // 버튼 아트가 304×147@2x(비 2.07)라 폭을 컬럼 전체로 늘리면 찌그러진다 — 크기는 두고 컬럼 가운데로.
        float btnW = (InfoColW - 14f) / 2f;

        var exitBtn = MakeStyledButton(w, "Exit", "나가기", out var exitLbl);
        ShopUIStyle.Anchor((RectTransform)exitBtn.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0),
                           new Vector2(colRight - (InfoColW - btnW) * 0.5f, Margin), new Vector2(btnW, 103));
        SkinButton(exitBtn, _skin?.exitButton, exitLbl);
        exitBtn.onClick.AddListener(ClosePopupUI);

        // 진화 버튼 — 조건 충족 시에만 노출(RefreshAll). 전용 아트가 없어 색 버튼 그대로 둔다.
        // 잭팟 줄(Margin+240~266) 위로 올려 겹치지 않게 한다.
        _evolveBtn = MakeStyledButton(w, "Evolve", "진화", out _);
        _evolveBtn.GetComponent<Image>().color = new Color(0.40f, 0.28f, 0.62f, 1f);
        ShopUIStyle.Anchor((RectTransform)_evolveBtn.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0),
                           new Vector2(colRight, Margin + 278), new Vector2(InfoColW, 46));
        _evolveBtn.onClick.AddListener(ShowEvolvePanel);
        _evolveBtn.gameObject.SetActive(false);
    }

    /// <summary>하단 대사 밴드(좌) — 재련공 초상 + 대사. 스테이지 폭에 맞춰 깐다.</summary>
    private void BuildDialogueBand(Transform w)
    {
        var band = ShopUIStyle.MakeImage(w, "DialogueBand", ShopUIStyle.BandFill);
        var bandRT = band.rectTransform;
        bandRT.anchorMin = new Vector2(0, 0); bandRT.anchorMax = new Vector2(1, 0); bandRT.pivot = new Vector2(0.5f, 0);
        bandRT.offsetMin = new Vector2(Margin, Margin);
        bandRT.offsetMax = new Vector2(-(InfoColW + ColGap + Margin), Margin + DialogH - 8);

        var portrait = ShopUIStyle.MakeImage(band.transform, "Portrait", ShopUIStyle.PortraitBg);
        ShopUIStyle.Anchor(portrait.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f),
                           new Vector2(12, 0), new Vector2(84, DialogH - 26));

        _dialogText = ShopUIStyle.MakeText(band.transform, "Dialog", 17f, FontStyles.Italic,
                                           TextAlignmentOptions.MidlineLeft, ShopUIStyle.TextPrimary);
        var dtRT = _dialogText.rectTransform;
        dtRT.anchorMin = new Vector2(0, 0); dtRT.anchorMax = new Vector2(1, 1); dtRT.pivot = new Vector2(0.5f, 0.5f);
        dtRT.offsetMin = new Vector2(112, 8); dtRT.offsetMax = new Vector2(-16, -8);
        _dialogText.text = "쇠는 두드릴수록 강해지지… 운이 따라준다면 말이야.";
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
        sub.text = "◇ 되돌릴 수 없는 선택 ◇";
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
        ShopUIStyle.ApplyButtonColors(pick);
        return pick;
    }

    /// <summary>현재 장착 무기의 진화 분기(WeaponEvolutionSO). 진화 불가면 빈 목록.</summary>
    private IReadOnlyList<WeaponEvolutionSO.Branch> CurrentBranches
    {
        get
        {
            var wd = _controller?.Run?.Player?.WeaponManager?.CurrentWeaponData;
            return wd?.evolution != null ? wd.evolution.Branches : System.Array.Empty<WeaponEvolutionSO.Branch>();
        }
    }

    private void ShowEvolvePanel()
    {
        if (_evolvePanel == null || _controller == null) return;

        var wd       = _controller.Run?.Player?.WeaponManager?.CurrentWeaponData;
        var branches = CurrentBranches;

        if (branches.Count == 0)
        {
            if (_resultText != null) _resultText.text = "<color=#9A8FB5>이 무기는 더 진화하지 않는다</color>";
            ShopUIStyle.PlaySfx("shop_reject");
            return;
        }

        int lv = wd?.enhanceLevel ?? 0;
        SetBranchLabel(_branchAName, branches.Count > 0 ? branches[0] : null, lv);
        SetBranchLabel(_branchBName, branches.Count > 1 ? branches[1] : null, lv);

        _evolvePanel.SetActive(true);
        ShopUIStyle.PlaySfx("shop_open");
    }

    /// <summary>분기 라벨 — 진화 후 이름 + 잠금 조건(강화 레벨 미달 시 회색 안내).</summary>
    private static void SetBranchLabel(TMP_Text label, WeaponEvolutionSO.Branch b, int enhanceLevel)
    {
        if (label == null) return;
        if (b == null || !b.IsValid) { label.text = "—"; return; }

        string name = b.target != null && !string.IsNullOrEmpty(b.target.displayName)
            ? b.target.displayName : b.branchId;

        if (!WeaponEvolutionSO.IsUnlocked(b, enhanceLevel))
            label.text = $"<color=#7A7290>{name}\n<size=70%>강화 {b.requiredEnhanceLevel} 필요</size></color>";
        else
            label.text = $"{name}\n<size=70%>{(b.cost > 0 ? $"재료 {b.cost}" : "진화 가능")}</size>";
    }

    private void HideEvolvePanel()
    {
        if (_evolvePanel != null) _evolvePanel.SetActive(false);
    }

    private void OnBranchClicked(int branch) => EvolveAsync(branch).Forget();

    /// <summary>진화 실행 — 무기를 통째로 교체하고 강화 레벨은 계승된다(PlayerWeaponManager가 처리).</summary>
    private async UniTaskVoid EvolveAsync(int branchIndex)
    {
        var branches = CurrentBranches;
        if (branchIndex < 0 || branchIndex >= branches.Count)
        {
            HideEvolvePanel();
            if (_resultText != null) _resultText.text = "<color=#C7554A>진화 분기가 없다</color>";
            ShopUIStyle.PlaySfx("shop_reject");
            return;
        }

        var b  = branches[branchIndex];
        var wm = _controller?.Run?.Player?.WeaponManager;
        var wd = wm?.CurrentWeaponData;
        if (wm == null || wd == null) { HideEvolvePanel(); return; }

        // 조건: 강화 레벨
        // 거절 사유는 진화 패널이 <b>덮고 있는</b> 정보창에 뜬다 — 패널을 닫아야 그 줄이 보인다
        // (성공 경로와 같은 처리).
        if (!WeaponEvolutionSO.IsUnlocked(b, wd.enhanceLevel))
        {
            HideEvolvePanel();
            if (_resultText != null)
                _resultText.text = $"<color=#C7554A>강화 {b.requiredEnhanceLevel} 이상이어야 한다</color>";
            ShopUIStyle.PlaySfx("shop_reject");
            return;
        }

        // 조건: 재료(설정된 경우만 차감)
        var fuel = _controller.Run?.FuelBank;
        if (b.cost > 0 && !(fuel?.TrySpend(FuelKind.EnhanceMaterial, b.cost) ?? false))
        {
            HideEvolvePanel();
            if (_resultText != null) _resultText.text = "<color=#C7554A>재료가 부족하다</color>";
            ShopUIStyle.PlaySfx("shop_reject");
            return;
        }

        HideEvolvePanel();

        bool ok;
        try { ok = await wm.EvolveCurrentWeaponAsync(b, this.GetCancellationTokenOnDestroy()); }
        catch (System.OperationCanceledException) { return; }

        if (ok)
        {
            string name = b.target != null ? b.target.displayName : b.branchId;
            if (_resultText != null) _resultText.text = $"<color=#9D7EE6>진화 — {name}</color>";
            ShopUIStyle.PlaySfx("enhance_success");
        }
        else
        {
            // 실패 시 재료 환불(차감했다면)
            if (b.cost > 0) fuel?.Add(FuelKind.EnhanceMaterial, b.cost);
            if (_resultText != null) _resultText.text = "<color=#C7554A>진화에 실패했다</color>";
            ShopUIStyle.PlaySfx("shop_reject");
        }

        RefreshAll();
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
        var img = go.GetComponent<Image>();
        img.color = ShopUIStyle.BuyFill;
        var btn = go.GetComponent<Button>();
        ShopUIStyle.ApplyButtonColors(btn, img);
        labelText = ShopUIStyle.MakeText(go.transform, "Label", 18f, FontStyles.Bold,
                                         TextAlignmentOptions.Center, ShopUIStyle.Gold);
        labelText.text = label;
        ShopUIStyle.Stretch(labelText.rectTransform);
        return btn;
    }

    /// <summary>
    /// 버튼에 아트를 얹는다. 재련소 버튼 아트(강화하기·전환·나가기·파츠 강화하기)는 <b>글자가 이미 구워져 있어</b>
    /// 아트가 붙는 순간 코드 라벨을 꺼야 한다 — 안 그러면 "강화하가기"처럼 두 벌이 겹쳐 읽힌다.
    /// 아트가 없을 때만 라벨이 남아 색 폴백에서도 무슨 버튼인지 알 수 있다.
    /// </summary>
    private static void SkinButton(Button btn, Sprite art, TMP_Text label = null)
    {
        if (btn == null) return;
        if (art == null) return;

        var img = btn.GetComponent<Image>();
        ShopUIStyle.Skin(img, art, sliced: true);
        var lbl = label != null ? label : btn.GetComponentInChildren<TMP_Text>(true);
        if (lbl != null) lbl.gameObject.SetActive(false);
    }

    // ── 핸들러 ──────────────────────────────────────────────

    /// <summary>대상 전환 시 — 성공률 낮으면 재련공이 도발.</summary>
    private void UpdateTargetDialogue()
    {
        if (_controller == null) return;
        if (_controller.CanEnhance(_targetSlot) && _controller.SuccessChanceAt(_targetSlot) < 0.4f)
            _dialogText.text = _controller.GetDialogue(CrucibleMood.Taunt);
    }

    /// <summary>
    /// 연출 중 재입력 처리. 강화는 반복 행위라 매회 연출이 끝날 때까지 잠그면 대기가 곧 불편이 된다 —
    /// 다시 누르면 진행 중 연출을 즉시 완료(<see cref="_skipAnim"/>)시키고 그 동작을 예약해 이어서 실행한다.
    /// 결과·재료 차감은 컨트롤러가 이미 확정한 뒤이고 연출은 표시층 전용이라, 스킵해도 데이터는 그대로다.
    /// </summary>
    private bool QueueWhileAnimating(Action again)
    {
        if (!_animating) return false;
        _skipAnim = true;
        _queued   = again;   // 덮어쓰기 = 예약은 1개 — 연타가 쌓여 나중에 몰아서 터지지 않게
        return true;
    }

    private void OnEnhanceClicked()
    {
        if (_controller == null) return;
        if (QueueWhileAnimating(OnEnhanceClicked)) return;

        // 파츠 탭은 무기가 아니라 '장착된 파츠'를 올린다 — 대상이 다르므로 경로를 가른다.
        if (_activeTab == 1) { EnhanceSelectedPart(); return; }

        // 컨트롤러가 결과를 즉시 확정(OnCrucibleChanged→RefreshAll 동기 발화). 연출은 표시층만 재생.
        var result = _controller.TryEnhance(_targetSlot);
        PlayEnhanceSequence(result, EnhanceView.MeleeWeapon).Forget();
    }

    /// <summary>
    /// 선택한 파츠를 1레벨 올린다. 임계를 넘으면 효과가 계단 상승한다(분열 갈래 +1 등).
    /// 판정은 <b>확정</b>이고 재료만 든다 — 실패 도박은 무기 강화의 축이고, 계단형 파츠에 겹치면
    /// 체감이 탁해진다. 재료 차감·저장은 컨트롤러(<see cref="CrucibleRoomController.TryEnhancePart"/>)가 소유한다.
    /// </summary>
    private void EnhanceSelectedPart()
    {
        if (_controller == null) return;
        // 행 버튼에 직결돼 있어 연출 중에도 클릭이 들어온다 — 근접 강화와 같은 큐를 태운다
        // (연출을 건너뛰고 다음 강화로 이어지되, 예약은 1개만 남아 나중에 몰아서 터지지 않는다).
        if (QueueWhileAnimating(EnhanceSelectedPart)) return;

        string partId = SelectedPartId();
        if (partId == null)
        {
            SetPartHint("강화할 파츠를 먼저 선택하세요");
            ShopUIStyle.PlaySfx("shop_reject");
            return;
        }

        var result = _controller.TryEnhancePart(partId);
        PlayEnhanceSequence(result, EnhanceView.Part).Forget();
    }

    /// <summary>지금 강화 대상으로 선택된 파츠 id. 미선택/무효면 null.</summary>
    private string SelectedPartId()
    {
        var all = Managers.WeaponParts?.All;
        if (all == null || _selectedPartSlot < 0 || _selectedPartSlot >= all.Count) return null;
        return all[_selectedPartSlot].part_id;
    }

    /// <summary>선택 파츠의 정의. 미선택이면 null.</summary>
    private WeaponPartEntry SelectedPartDef()
    {
        string id = SelectedPartId();
        return id == null ? null : Managers.WeaponParts?.GetById(id);
    }

    private void OnPromoteClicked(string legendId)
    {
        if (_controller == null || _animating) return;
        var result = _controller.TryPromote(_targetSlot, legendId);
        ShowPromoteResult(result);
        RefreshAll();
    }

    // ── 도파민 연출 시퀀스 (표시층 전용; 결과/데이터/세이브 불변) ──

    /// <summary>
    /// 한 시퀀스를 공유하는 세 강화 대상. 탭만으로는 갈리지 않는다 —
    /// 원거리 탭에는 <b>파츠</b>와 <b>원거리 무기</b> 두 대상이 함께 있다.
    /// </summary>
    private enum EnhanceView { MeleeWeapon, RangedWeapon, Part }

    /// <summary>강화 결과를 비동기 연출로 재생. 연출 종료 후 RefreshAll로 최종 확정.</summary>
    private async UniTaskVoid PlayEnhanceSequence(EnhanceResult r, EnhanceView view)
    {
        if (r.IsReject)
        {
            ShowResultText(r, view);
            ShowStageResult(_resultText.text, RejectFontSize, RejectPopScale);
            if (r.outcome != EnhanceOutcome.RejectMaxed) ShopUIStyle.PlaySfx("crucible_fail");
            RefreshAll();
            return;
        }

        _animating = true;
        ShowResultText(r, view);

        // 파츠 탭은 1번 카드에 파츠를 그리므로 상한도 파츠 정의에서 온다(무기 강화 상한이 아님).
        bool isPart = view == EnhanceView.Part;
        bool isMelee = view == EnhanceView.MeleeWeapon;
        int slot = isMelee ? _targetSlot : RangedCard;
        int max  = isPart ? (SelectedPartDef()?.max_level ?? 1)
                          : _controller.MaxAt(isMelee ? PlayerWeaponManager.Slot0 : PlayerWeaponManager.Slot1);
        // 원거리 무기는 자기 레벨 셀이 없다 — 1번 카드는 파츠를 그린다. 남의 셀에 무기 단계를 찍으면
        // 연출 0.5초 동안 파츠가 엉뚱한 레벨로 보인다. 카운트만 생략하고 카드 반응은 그대로 준다.
        bool count = view != EnhanceView.RangedWeapon;

        // 카운트 연출이 레벨 셀을 소유하도록 시작 단계로 되돌림
        // (RefreshAll이 이미 afterLevel로 세팅했으나 다음 렌더 전 동일 프레임에서 덮어씀 → 깜빡임 없음).
        if (count && IsCardSlot(slot)) { _cardLevel[slot].text = FormatLevel(r.beforeLevel, max, isPart); SetGauge(slot, r.beforeLevel, max); }

        try
        {
            switch (r.outcome)
            {
                case EnhanceOutcome.Success:
                    if (_controller.LastJackpot) await JackpotSequence(slot, r, max, count);
                    else                         await SuccessSequence(slot, r, max, count);
                    break;
                case EnhanceOutcome.FailDropped:
                    await FailSequence(slot, r, max, count);
                    break;
            }
        }
        catch (OperationCanceledException) { return; } // 패널 파괴 — 정적 서비스는 자립적, 정리 불필요

        _animating = false;
        _skipAnim  = false;
        RefreshAll(); // 연출 후 최종 확정

        // 연출 중 눌린 재입력을 여기서 이어 실행한다 — 결과가 확정·반영된 뒤라야 다음 강화가 올바른 값에 걸린다.
        var next = _queued;
        _queued = null;
        next?.Invoke();
    }

    private async UniTask SuccessSequence(int slot, EnhanceResult r, int max, bool count)
    {
        ShopUIStyle.PlaySfx("crucible_success");
        HitFeelService.HitStop(0.6f, 0.05f); // 시간정지 팝업에선 timeScale 무효(무해) — 카메라측 반응만
        if (count) await CountLevel(slot, r.beforeLevel, r.afterLevel, max);
        ShowStageResult(_resultText.text, SuccessFontSize, SuccessPopScale);   // 카운트가 멎는 순간 = 판정 순간
        await UniTask.WhenAll(PunchCard(slot, PunchScale, PunchDur),
                              FlashCard(slot, SuccessFlash));
    }

    private async UniTask JackpotSequence(int slot, EnhanceResult r, int max, bool count)
    {
        ShopUIStyle.PlaySfx("crucible_jackpot");
        VolumePulseService.Pulse(JackpotPulsePeak, JackpotPulseDur); // 전체화면 크로매틱+블룸(unscaled)
        HitFeelService.Heavy();
        if (count) await CountLevel(slot, r.beforeLevel, r.afterLevel, max);
        ShowStageResult(_resultText.text, JackpotFontSize, JackpotPopScale);
        await UniTask.WhenAll(PunchCard(slot, JackpotPunchScale, JackpotPunchDur),
                              FlashCard(slot, JackpotFlash));
    }

    private async UniTask FailSequence(int slot, EnhanceResult r, int max, bool count)
    {
        bool nearMiss = IsNearMiss(r);
        ShopUIStyle.PlaySfx("crucible_fail");
        HitFeelService.Light();
        if (count && r.beforeLevel != r.afterLevel) // 하락분이 있으면 카운트다운
            await CountLevel(slot, r.beforeLevel, r.afterLevel, max);
        float amp   = nearMiss ? FailShakeAmp * NearMissShakeMult : FailShakeAmp;
        Color flash = nearMiss ? NearMissFlash : FailFlash;
        // 쉐이크와 같은 프레임에 띄운다 — 흔들림이 곧 판정이라 문구가 늦으면 둘이 따로 논다.
        ShowStageResult(_resultText.text, SuccessFontSize, nearMiss ? JackpotPopScale : SuccessPopScale);
        await UniTask.WhenAll(ShakeCard(slot, amp, FailShakeDur),
                              FlashCard(slot, flash));
    }

    private void ShowResultText(EnhanceResult r, EnhanceView view)
    {
        // 파츠는 확정 상승이라 잭팟·스트릭·니어미스가 없다 — 무기 문구를 그대로 쓰면 있지도 않은 도박을 암시한다.
        if (view == EnhanceView.Part) { ShowPartResultText(r); return; }

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

    /// <summary>파츠 강화 결과 문구. 임계를 넘어 효과가 실제로 오른 경우를 따로 짚어준다.</summary>
    private void ShowPartResultText(EnhanceResult r)
    {
        var def = SelectedPartDef();

        switch (r.outcome)
        {
            case EnhanceOutcome.Success:
                bool stepped = def != null &&
                               !Mathf.Approximately(def.ValueAt(r.beforeLevel), def.ValueAt(r.afterLevel));
                _resultText.text = stepped
                    ? $"<color=#FFD24A>단계 상승! {(def != null ? PartValueText(def, def.ValueAt(r.afterLevel)) : "")}</color>"
                    : $"<color=#7AD46E>강화 성공 — Lv.{r.afterLevel}</color>";
                _resultText.color = ShopUIStyle.Gold;
                _dialogText.text = stepped
                    ? "형태가 바뀌었군. 이제 다르게 날아갈 거다."
                    : "아직이다. 조금 더 두들겨야 모양이 잡힌다.";
                break;

            case EnhanceOutcome.RejectNoFuel:
                _resultText.text = "<color=#FF5250>강화재료 부족</color>";
                _dialogText.text = "재료 없이는 손도 못 댄다.";
                break;

            case EnhanceOutcome.RejectMaxed:
                _resultText.text = "<color=#8AB0D5>이미 최대 강화</color>";
                _dialogText.text = "이 파츠는 여기가 끝이다.";
                break;

            default:
                _resultText.text = "<color=#FF5250>강화할 파츠를 선택하세요</color>";
                break;
        }
    }

    /// <summary>실패가 성공확률에 아슬아슬했는지(roll이 chance의 NearMissBand배 이내).</summary>
    private static bool IsNearMiss(EnhanceResult r)
        => r.outcome == EnhanceOutcome.FailDropped
           && r.chance > 0f && r.roll < r.chance * NearMissBand;

    private bool IsCardSlot(int slot) => slot >= 0 && slot < 2 && _cardLevel[slot] != null;

    /// <summary>1번 카드는 원거리 <b>파츠</b>를 그린다(무기가 아님). 강화 단위가 달라 표기도 갈린다.</summary>
    private const int RangedCard = 1;

    private static string FormatLevel(int level, int max, bool isPart = false)
        => isPart
            ? $"Lv.{level} <size=55%><color=#9A98A0>/ {max}</color></size>"
            : $"+{level} <size=55%><color=#9A98A0>/ {max}</color></size>";

    private void SetGauge(int slot, int level, int max)
    {
        if (slot < 0 || slot >= 2 || _cardGaugeFill[slot] == null) return;
        float ratio = max > 0 ? Mathf.Clamp01((float)level / max) : 0f;
        var f = _cardGaugeFill[slot];
        f.anchorMin = new Vector2(0f, 0f);
        f.anchorMax = new Vector2(ratio, 1f);
        f.offsetMin = Vector2.zero; f.offsetMax = Vector2.zero;
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
            if (_skipAnim) break;
            await Hold(perStep);
            lvl += step;
            _cardLevel[slot].text = FormatLevel(lvl, max, slot == RangedCard);
            SetGauge(slot, lvl, max);
        }

        // 스킵으로 중간에 끊겨도 표기는 최종값이어야 한다(RefreshAll이 뒤에 오지만, 그 사이 프레임이 남는다).
        _cardLevel[slot].text = FormatLevel(after, max, slot == RangedCard);
        SetGauge(slot, after, max);
    }

    /// <summary>카드 스케일 펀치(ShopSlot PopAsync 이식). unscaledDeltaTime.</summary>
    private async UniTask PunchCard(int slot, float amp, float dur)
    {
        if (slot < 0 || slot >= 2 || _cardBg[slot] == null) return;
        var rt = _cardBg[slot].rectTransform;
        float t = 0f;
        while (t < 1f)
        {
            if (_skipAnim) break;
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
            if (_skipAnim) break;
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
            if (_skipAnim) break;
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
            if (_skipAnim) return;
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
        if (_oreText != null)
            _oreText.text = (_controller.Run?.FuelBank?.RuneOre ?? 0).ToString();

        if (_eventBanner != null)
        {
            bool ev = _controller.HasEvent;
            _eventBanner.SetActive(ev);
            if (ev && _eventBannerText != null) _eventBannerText.text = _controller.EventBanner;
        }
        // 진화는 근접 무기 대상이라 파츠 탭에서는 감춘다(대상과 화면이 어긋나는 것을 막는다).
        if (_evolveBtn != null)
            _evolveBtn.gameObject.SetActive(_activeTab == 0 && _controller.CanPromote(PlayerWeaponManager.Slot0));

        // 잭팟은 근접 무기 강화(도박)에만 걸린다 — 파츠 강화는 확정이라 굴리지 않는다.
        if (_jackpotHint != null)
            _jackpotHint.text = $"잭팟 <color=#FFD24A>{_controller.JackpotChance * 100f:F0}%</color>"
                              + (_controller.Streak > 0
                                 ? $"  <size=85%><color=#9A98A0>연속 {_controller.Streak} — 오를수록 커진다</color></size>"
                                 : "  <size=85%><color=#9A98A0>연속 성공마다 커진다</color></size>");

        // 0번 카드 = 근접 무기(실제 슬롯 카드). 스킨이면 흰색, 아니면 대상 하이라이트 색.
        {
            const int i = 0;
            var w = _controller.GetSlot(i);
            _cardBg[i].color = _skin != null ? Color.white : (i == _targetSlot ? CardTargetBg : ShopUIStyle.CardFill);

            if (w == null)
            {
                _cardName[i].text = "—";
                _cardLevel[i].text = "";
                if (_cardAtk[i] != null)      _cardAtk[i].text = "";
                if (_focusTypeText != null)   _focusTypeText.text = "";
                SetGauge(i, 0, 1);
            }
            else
            {
                int max = _controller.MaxAt(i);
                string legend = string.IsNullOrEmpty(w.legendId) ? "" : $"  <color=#FFD24A>[{LegendName(w.legendId)}]</color>";
                _cardName[i].text  = $"{w.displayName}{legend}";
                _cardLevel[i].text = FormatLevel(w.enhanceLevel, max);
                if (_cardAtk[i] != null)    _cardAtk[i].text = AttackLine(w, !_controller.CanEnhance(i));
                if (_focusTypeText != null) _focusTypeText.text = TypeTierLabel(w, max, i);
                SetGauge(i, w.enhanceLevel, max);
            }
        }

        RefreshRangedParts();   // 파츠 5행 — 강화·연료 변동으로도 버튼 상태가 바뀐다
        RefreshRangedCard();
        RefreshFocusExtras();
        RefreshInfo();
        RefreshPromoteRow();
    }

    /// <summary>
    /// 1번 카드 = 선택한 <b>파츠</b>. 예전엔 여기에 원거리 무기를 그려서 탭 이름("원거리 파츠")과
    /// 내용이 어긋났고, 강화 버튼이 올리는 대상과 카드가 보여주는 대상이 달랐다.
    /// </summary>
    private void RefreshRangedCard()
    {
        if (_cardName[RangedCard] == null) return;

        // 호버가 있으면 그쪽을 임시로 보여준다 — 대상을 바꾸지 않고 4칸을 훑어볼 수 있게.
        var def = HoveredPartDef() ?? SelectedPartDef();
        var state = RangedPartsState.Current;

        if (def == null)
        {
            _cardName[RangedCard].text = "파츠를 고르세요";
            _partKindText.text = "";
            _cardLevel[RangedCard].text = "";
            _cardAtk[RangedCard].text = "";
            _partGrowthText.text = "";
            _partSynergyText.text = "";
            _partDescText.text = "왼쪽 목록에서 파츠를 고르면 여기에 결과가 나온다.";
            SetBar(_cardGaugeFill[RangedCard], 0f);
            SetPartButton(null, 0, 0, false);
            return;
        }

        int level = state.LevelOf(def.part_id);
        int max   = def.max_level > 0 ? def.max_level : level;
        bool maxed = def.max_level > 0 && level >= def.max_level;

        _cardName[RangedCard].text  = def.part_name;
        _partKindText.text          = PartKindLabel(def);
        _cardLevel[RangedCard].text = $"Lv.{level} <size=70%><color=#9A98A0>/ {max}</color></size>";
        _cardAtk[RangedCard].text   = PartEffectLine(def, level);
        _partGrowthText.text        = PartGrowthLine(def, level, maxed);
        _partDescText.text          = def.description;
        _partSynergyText.text       = PartSynergyLine(def, state);
        SetBar(_cardGaugeFill[RangedCard], max > 0 ? (float)level / max : 0f);

        // 호버 중(선택과 다른 파츠)에는 버튼을 대상 밖으로 두어 오조작을 막는다.
        bool isSelected = SelectedPartDef() == def;
        SetPartButton(def, _controller != null ? _controller.PartCostAt(def.part_id) : 0,
                      _controller != null ? _controller.FuelAmount : 0, isSelected && !maxed);
    }

    /// <summary>마우스가 올라간 슬롯의 파츠. 없으면 null.</summary>
    private WeaponPartEntry HoveredPartDef()
    {
        var all = Managers.WeaponParts?.All;
        if (all == null || _hoverPartSlot < 0 || _hoverPartSlot >= all.Count) return null;
        return all[_hoverPartSlot];
    }

    /// <summary>
    /// 상세 패널의 비용 줄. 강화 버튼 자체는 좌열 각 행으로 옮겼으므로(<c>_partEnhanceBtn</c>은 null)
    /// 여기서는 "지금 얼마 드는가"만 적는다 — 누르는 곳 옆에는 비용이 이미 붙어 있고,
    /// 이 줄은 고른 파츠를 읽는 동안 같은 정보를 잃지 않게 하는 용도다.
    /// </summary>
    private void SetPartButton(WeaponPartEntry def, int cost, int have, bool usable)
    {
        if (_partCostText != null)
        {
            if (def == null) _partCostText.text = "";
            else if (def.max_level > 0 && RangedPartsState.Current.LevelOf(def.part_id) >= def.max_level)
                _partCostText.text = "<color=#8AB0D5>더 올릴 수 없다</color>";
            else
                _partCostText.text = have >= cost
                    ? $"강화 비용 <color=#FFD24A>{cost}</color> · 보유 {have} · <color=#7AD46E>실패 없음</color>"
                    : $"<color=#FF5250>강화 비용 {cost}</color> · 보유 {have}";
        }

        if (_partEnhanceBtn == null) return;
        _partEnhanceBtn.interactable = def != null && usable && have >= cost && !_animating;
    }

    /// <summary>파츠 효과 한 줄 — 지금 값과 다음 레벨 값. 계단형은 임계 전까지 값이 그대로라 남은 강수도 함께 보인다.</summary>
    private static string PartEffectLine(WeaponPartEntry def, int level)
    {
        bool maxed = def.max_level > 0 && level >= def.max_level;
        float cur  = def.ValueAt(level);
        if (maxed) return $"{PartValueText(def, cur)}  <color=#8AB0D5>(최대)</color>";

        float next = def.ValueAt(level + 1);
        if (Mathf.Approximately(cur, next))
        {
            int toStep = def.milestone_every > 0 ? def.milestone_every - ((level - 1) % def.milestone_every) : 0;
            return $"{PartValueText(def, cur)}  <size=85%><color=#9A98A0>다음 단계까지 {toStep}강</color></size>";
        }
        return $"{PartValueText(def, cur)} <color=#7AD46E>→ {PartValueText(def, next)}</color>";
    }

    /// <summary>파츠 종류별 단위를 붙인 효과값 표기 — 숫자만 띄우면 무엇이 오르는지 읽히지 않는다.</summary>
    private static string PartValueText(WeaponPartEntry def, float v) => def.Kind switch
    {
        RangedPartKind.Split   => $"{Mathf.RoundToInt(v) + 1}발",
        RangedPartKind.Pierce  => $"관통 {Mathf.RoundToInt(v)}",
        RangedPartKind.Explode => $"반경 {v:F2}",
        RangedPartKind.Homing  => $"유도 {v:F0}°/s",
        _                      => $"위력·크기 +{v * 100f:F0}%",
    };

    /// <summary>
    /// 강화하면 어떻게 자라는지 한 줄. 계단형(분열·관통)은 임계마다 한 단계씩 오르므로
    /// "왜 올렸는데 안 변하지"가 생긴다 — 규칙을 명시해 그 구간을 납득시킨다.
    /// </summary>
    private static string PartGrowthLine(WeaponPartEntry def, int level, bool maxed)
    {
        if (maxed) return "<color=#8AB0D5>강화</color>  더 올릴 수 없다";

        int left = def.max_level > 0 ? def.max_level - level : 0;
        string cap = left > 0 ? $"  <size=85%><color=#9A98A0>상한까지 {left}강</color></size>" : "";

        return def.milestone_every > 0
            ? $"<color=#FFD24A>계단형</color>  {def.milestone_every}강마다 한 단계 오른다{cap}"
            : $"<color=#7FE3FF>연속형</color>  1강마다 꾸준히 오른다{cap}";
    }

    /// <summary>
    /// 다른 파츠와의 관계. 이미 낀 조합은 <b>발동 중</b>으로, 아직 아닌 조합은 힌트로 보여준다 —
    /// 슬롯이 유한하므로 무엇과 같이 낄지가 실제 결정이다.
    /// </summary>
    private static string PartSynergyLine(WeaponPartEntry def, RangedPartsState state)
    {
        bool hasPierce = HasKind(state, RangedPartKind.Pierce);
        bool hasHoming = HasKind(state, RangedPartKind.Homing);
        bool hasSplit  = HasKind(state, RangedPartKind.Split);

        switch (def.Kind)
        {
            case RangedPartKind.Homing:
                return hasPierce
                    ? "<color=#7AD46E>연계 발동</color>  관통을 다 쓰면 되돌아와 다시 때린다"
                    : "<size=90%><color=#9A98A0>관통과 함께 끼면 되돌아와 재타격한다</color></size>";

            case RangedPartKind.Pierce:
                return hasHoming
                    ? "<color=#7AD46E>연계 발동</color>  유도와 맞물려 되돌아온다"
                    : "<size=90%><color=#9A98A0>유도와 함께 끼면 꿰뚫은 뒤 되돌아온다</color></size>";

            case RangedPartKind.Explode:
                return hasSplit
                    ? "<color=#7AD46E>연계 발동</color>  갈래마다 터진다 — 분열과 곱해진다"
                    : "<size=90%><color=#9A98A0>분열과 함께 끼면 갈래마다 터진다</color></size>";

            case RangedPartKind.Split:
                return HasKind(state, RangedPartKind.Power)
                    ? "<color=#FF9A5A>상충</color>  거력은 단발 강공, 분열은 다발 약공 — 축이 반대다"
                    : "<size=90%><color=#9A98A0>폭발과 함께 끼면 갈래마다 터진다</color></size>";

            default:   // Power
                return hasSplit
                    ? "<color=#FF9A5A>상충</color>  분열과 축이 반대다 — 한쪽에 몰아주는 편이 세다"
                    : "<size=90%><color=#9A98A0>단발 강공 축 — 분열과는 서로 깎아먹는다</color></size>";
        }
    }

    private static bool HasKind(RangedPartsState state, RangedPartKind kind)
    {
        if (state == null) return false;
        var data = Managers.WeaponParts;
        var list = state.Equipped_;
        for (int i = 0; i < list.Count; i++)
        {
            var d = data?.GetById(list[i].partId);
            if (d != null && d.Kind == kind) return true;
        }
        return false;
    }

    private static string PartKindLabel(WeaponPartEntry def) => def.Kind switch
    {
        RangedPartKind.Split   => "분열 — 투사체 수",
        RangedPartKind.Pierce  => "관통 — 적 통과",
        RangedPartKind.Explode => "폭발 — 착탄 범위",
        RangedPartKind.Homing  => "유도 — 궤적 조작",
        _                      => "거력 — 위력·크기",
    };

    /// <summary>다음 강화가 성공했을 때의 공격력. 테이블이 없으면 현재값 그대로.</summary>
    private float NextAttack(WeaponData w)
    {
        if (w == null) return 0f;
        var table = _controller?.Table;
        if (table == null) return w.baseAttack;

        float raw = w.baseAttackRaw > 0f ? w.baseAttackRaw : w.baseAttack;
        return raw * table.AttackMult(w.enhanceLevel + 1, w.legendId);
    }

    /// <summary>카드 아래 공격력 한 줄 — 지금 값과 성공 시 값. 파츠 카드의 효과 줄과 같은 형식.</summary>
    private string AttackLine(WeaponData w, bool maxed)
    {
        if (w == null) return "";
        if (maxed) return $"공격 {w.baseAttack:F1}  <size=85%><color=#8AB0D5>(최대)</color></size>";

        float next = NextAttack(w);
        return $"공격 {w.baseAttack:F1} <color=#7AD46E>→ {next:F1}</color>"
             + $" <size=80%><color=#9A98A0>(+{next - w.baseAttack:F1})</color></size>";
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

        // 도박구간 = 아트("위험-하락" 글자 포함) 한 장 / 안전구간 = 색 알약 + 글자. 둘은 배타다.
        if (_zoneBg != null && _skin?.riskBadge != null)
        {
            if (danger)
            {
                ShopUIStyle.Skin(_zoneBg, _skin.riskBadge);
                _zoneTag.gameObject.SetActive(false);
            }
            else
            {
                _zoneBg.sprite = null;
                _zoneBg.color  = new Color(0.15f, 0.32f, 0.20f, 1f);
                _zoneTag.gameObject.SetActive(true);
                _zoneTag.text  = "안전구간";
            }
        }
        else
        {
            _zoneTag.text = danger ? "도박구간" : "안전구간";
            if (_zoneBg != null)
                _zoneBg.color = danger ? new Color(0.42f, 0.14f, 0.16f, 1f) : new Color(0.15f, 0.32f, 0.2f, 1f);
        }

        if (_milestoneLabel != null)
            _milestoneLabel.text = MilestoneText(w, max, maxed);

        // 다음 강화 상세(성공 공격증가 / 실패 하락 / 잭팟 확률)
        if (_detailSuccess != null)
        {
            if (maxed)
            {
                _detailSuccess.text = w.CanEvolve ? "이 구간의 최대 — 진화로 다음 구간이 열린다" : "최대 강화 도달";
                _detailFail.text    = MasteryText(w);
                _detailJackpot.text = "";
            }
            else
            {
                float cur  = w.baseAttack;
                float next = NextAttack(w);
                int drop = _controller.DropAt(PlayerWeaponManager.Slot0);
                // F0으로 찍으면 저스케일 무기에서 "6 → 6"으로 보여 강화가 무의미해 보인다 — 소수 1자리 + 누적 %.
                float raw   = w.baseAttackRaw > 0f ? w.baseAttackRaw : w.baseAttack;
                float total = raw > 0f ? (next / raw - 1f) * 100f : 0f;
                _detailSuccess.text = $"<color=#7AD46E>성공</color>  공격 {cur:F1} → {next:F1} " +
                                      $"<size=80%>(+{next - cur:F1} · 원본 대비 +{total:F0}%)</size>";
                _detailFail.text    = drop > 0 ? $"<color=#FF7A6A>실패</color>  강화 -{drop}"
                                               : "<color=#7AD46E>실패</color>  하락 없음 (안전)";
                _detailJackpot.text = $"<color=#FFD24A>잭팟</color>  {_controller.JackpotChance * 100f:F0}% <size=80%>스트릭 {_controller.Streak}</size>";
            }
        }
    }

    /// <summary>
    /// 마일스톤 한 줄. 진화 전에는 "진화까지", 진화 후에는 마스터리 구간을 가리킨다.
    /// 진화가 강화의 종점이 아니라는 걸 이 줄에서 읽히게 하는 게 목적.
    /// </summary>
    private string MilestoneText(WeaponData w, int max, bool maxed)
    {
        int left = Mathf.Max(0, max - w.enhanceLevel);

        if (w.CanEvolve)
            return maxed ? "◆ 진화 가능!" : $"◆ 진화까지 {left}강";

        if (w.evolutionStage > 0)
        {
            int mastery = WeaponEnhanceService.MasteryLevel(w, _controller.Table);
            if (maxed)      return $"◆ 마스터리 {mastery}단계 (최종)";
            if (mastery > 0) return $"◆ 마스터리 {mastery}단계 · 앞으로 {left}강";
            return $"◆ 마스터리 개방까지 {Mathf.Max(0, WeaponEnhanceService.BaseEnhanceCap(w, _controller.Table) - w.enhanceLevel)}강";
        }

        return maxed ? "" : $"◆ 최대까지 {left}강";
    }

    /// <summary>마스터리 누적 효과(스킬 확장) 한 줄. 없으면 빈 문자열.</summary>
    private string MasteryText(WeaponData w)
    {
        var table = _controller.Table;
        int mastery = WeaponEnhanceService.MasteryLevel(w, table);
        if (table == null || mastery <= 0) return "";
        return $"<color=#C9A6FF>마스터리 {mastery}</color>  스킬 피해 +{table.MasterySkillDamage(mastery) * 100f:F0}%" +
               $" · 쿨다운 -{table.MasterySkillCdr(mastery) * 100f:F0}%";
    }

    private void RefreshInfo()
    {
        // 파츠 탭은 무기가 아니라 파츠를 올린다 — 무기 성공률/재료를 띄우면 실제로 드는 비용과 다른 숫자가 된다.
        if (_activeTab == 1)
        {
            // 정보창은 공용이라 RefreshFocusExtras가 방금 채운 근접 무기 문구가 남는다
            // ("◆ 진화까지 3강" 같은 것이 파츠 탭에 그대로 보였다).
            if (_milestoneLabel != null) _milestoneLabel.text = "";
            RefreshPartInfo();
            return;
        }

        // 무기 탭 복귀 — 예전엔 여기서 무조건 켰다. 무기가 없거나 최대치거나 재료가 모자라도 눌리는 버튼이라
        // 누르면 거절 문구만 뜨는 '고장난 버튼'이 됐다. 아래 분기가 조건을 만족할 때만 다시 켠다
        // (파츠 강화·원거리 강화 버튼과 같은 규약).
        if (_enhanceBtn != null) _enhanceBtn.interactable = false;

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
            _successText.text = w.CanEvolve
                ? "<color=#8AB0D5>진화 대기 — 다음 구간이 열린다</color>"
                : "<color=#8AB0D5>최대 강화 도달</color>";
            _costText.text = MasteryText(w);
        }
        else
        {
            float chance = _controller.SuccessChanceAt(_targetSlot);
            int cost = _controller.CostAt(_targetSlot);
            int have = _controller.FuelAmount;
            _successText.text = $"성공률 <color=#7AD46E>{chance * 100f:F0}%</color>";
            _costText.text = have >= cost
                ? $"재료 {cost} <color=#9A98A0>/ 보유 {have}</color>"
                : $"<color=#FF5250>재료 {cost}</color> <color=#9A98A0>/ 보유 {have}</color>";

            // 연출 중에도 켜 둔다 — 재입력이 연출을 건너뛰고 다음 강화로 이어지는 경로(QueueWhileAnimating).
            if (_enhanceBtn != null) _enhanceBtn.interactable = have >= cost;
        }

        _streakText.text = _controller.Streak > 0 ? $"▲ 연속 {_controller.Streak}" : "";
        if (_eventEffectText != null)
            _eventEffectText.text = _controller.HasEvent ? $"↗ {_controller.EventEffectDesc}" : "";
    }

    /// <summary>
    /// 파츠 탭 정보창. 확정 상승이라 성공률 칸에는 확률 대신 <b>무엇이 바뀌는지</b>를 쓴다.
    /// 대신 재료가 유한하다는 사실이 긴장을 만들므로 비용과 보유량을 나란히 보여준다.
    /// </summary>
    private void RefreshPartInfo()
    {
        // 개별 파츠 상세는 스테이지 우측 패널이 맡는다. 여기서는 <b>조합의 총합</b>만 말한다 —
        // 파츠를 여럿 끼웠을 때 "내 화살이 결국 몇 발 나가고 뭘 뚫는가"를 볼 자리가 그동안 없었다.
        //
        // 계산은 실제 발사 퍼널을 그대로 돌린다(표시용 수식을 따로 두면 코드가 바뀔 때 화면만 옛 값을 말한다).
        var req = ProjectileRequest.Create("preview", Vector3.zero, Vector3.forward, 100f, null,
                                           RangedParts.RangedSlot);
        RangedParts.Apply(ref req);

        int  shots  = Mathf.Max(1, req.count);
        bool hasExp = req.explodeRadius > 0f;
        bool hasHom = req.homingStrength > 0f;

        _successText.text = "<color=#FFD24A>발사 미리보기</color>  <size=80%><color=#9A98A0>현재 조합의 총합</color></size>";
        _costText.text    = $"투사체 <color=#FFD24A>{shots}발</color>"
                          + (shots > 1 ? $" <size=85%><color=#9A98A0>· 확산 {req.spreadDeg:F0}°</color></size>" : "");

        _detailSuccess.text = req.pierce > 0
            ? $"관통 <color=#FFD24A>{req.pierce}회</color>"
            : "<color=#5A5072>관통 없음</color>";

        _detailFail.text = hasExp
            ? $"폭발 반경 <color=#FFD24A>{req.explodeRadius:F2}</color>"
              + $" <size=85%><color=#9A98A0>· 본체 피해의 {req.explodeDamageRatio * 100f:F0}%</color></size>"
            : "<color=#5A5072>폭발 없음</color>";

        _detailJackpot.text = hasHom
            ? $"유도 <color=#FFD24A>{req.homingStrength:F0}</color>°/s"
              + (req.returnOnPierce ? " <size=85%><color=#7FE3FF>· 관통 후 복귀</color></size>" : "")
            : "<color=#5A5072>유도 없음</color>";

        _milestoneLabel.text = $"피해 ×{req.damageMult:F2}  ·  크기 ×{req.sizeMult:F2}";
        _streakText.text     = "<size=90%><color=#9A98A0>2번 키(원거리)로 쏠 때만 적용된다</color></size>";

        // 강화 버튼은 상세 패널 안으로 옮겼다 — 액션 컬럼의 버튼은 파츠 탭에서 쓰지 않는다.
        if (_eventEffectText != null)
            _eventEffectText.text = _controller.HasEvent ? $"↗ {_controller.EventEffectDesc}" : "";
    }

    /// <summary>
    /// 무기 타입·티어 라벨(카드 상단 디테일). 티어는 강화 상한(6/9/12/15)으로 추정.
    ///
    /// 두 예외를 둔다.
    ///  · <b>무형검</b>(근접 슬롯 · 진화 0단계): weaponType이 Katana라 그대로 찍으면 "카타나 · 티어 1"이 된다.
    ///    하지만 이 무기의 정체성은 <b>아직 형태가 정해지지 않았다</b>는 것 자체다 — 진화 전에는 타입을 말하지 않는다.
    ///  · <b>원거리</b>: 진화 트리가 없고 공용 파츠 강화로만 성장한다. 티어를 붙이면 없는 승급 체계를 암시한다.
    /// </summary>
    private string TypeTierLabel(WeaponData w, int max, int slotIndex)
    {
        if (w == null) return "";

        // 진화 전 주무기 = 무형검. 타입·티어를 숨긴다.
        if (slotIndex == PlayerWeaponManager.Slot0 && w.evolutionStage <= 0)
            return "무형 · 형태 없음";

        string type = w.weaponType switch
        {
            WeaponType.Katana     => "카타나",
            WeaponType.Greatsword => "대검",
            WeaponType.Bow        => "활",
            WeaponType.Crossbow   => "석궁",
            WeaponType.Staff      => "지팡이",
            _                     => w.weaponType.ToString(),
        };

        if (slotIndex != PlayerWeaponManager.Slot0) return type;   // 원거리 — 티어 개념 없음

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
