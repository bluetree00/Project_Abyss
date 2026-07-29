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
    private TMP_Text _oreText;
    private TMP_Text _dialogText;

    // 슬롯 카드(2)
    private readonly Image[]         _cardBg        = new Image[2];
    private readonly TMP_Text[]      _cardName      = new TMP_Text[2];
    private readonly TMP_Text[]      _cardLevel     = new TMP_Text[2];
    private readonly TMP_Text[]      _cardAtk       = new TMP_Text[2];
    private readonly RectTransform[] _cardGaugeFill = new RectTransform[2];
    private readonly Vector2[]       _cardBasePos   = new Vector2[2]; // 카드 기준 앵커 위치(쉐이크 복원용)

    // 원거리 파츠 — 완성본 원거리 탭은 4슬롯(활·분열·관통·빈). 슬롯 UI는 레이아웃, 로직은 Phase 3.
    private readonly Image[]    _partsSlots = new Image[4];
    private readonly TMP_Text[] _partsMark  = new TMP_Text[4];

    // 전체화면 탭형 재설계 — 스킨/탭/스테이지 컨테이너
    private CrucibleSkinSO _skin;
    private RectTransform  _meleeStage, _rangedStage;
    private Image          _tabWeaponImg, _tabRangedImg;
    private int            _activeTab;   // 0=무기강화 1=원거리 파츠

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
        btn.transition = Selectable.Transition.None;
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

        // 강화 버튼 — 탭별 아트(글자 구워짐). 아트가 없을 때만 남는 라벨도 같이 맞춰 둔다.
        if (_enhanceLabel != null) _enhanceLabel.text = tab == 0 ? "강화하기" : "파츠 강화하기";
        SkinButton(_enhanceBtn, tab == 0 ? _skin?.enhanceButton : _skin?.partEnhanceButton, _enhanceLabel);

        _targetSlot = tab == 0 ? PlayerWeaponManager.Slot0 : 1;
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

        // 하단 강화 게이지 — 테두리 아트 1588×129@2x(가로세로비 12.3)를 지켜 납작하게 눌리지 않게 한다.
        _cardGaugeFill[0] = BuildGauge(stage, _skin?.gaugeTrack, _skin?.gaugeFill, _skin?.gaugeFrame,
                                       30f, 76f, 1100f);
    }

    /// <summary>원거리 파츠 탭 — 파츠 슬롯 4칸 + 파츠 레벨(큰 라벨) + 게이지. 연출 대상 _cardBg[1].</summary>
    private void BuildRangedCard(RectTransform stage)
    {
        _dockTypeText = ShopUIStyle.MakeText(stage, "TypeTier", 15f, FontStyles.Bold,
                                             TextAlignmentOptions.TopLeft, ShopUIStyle.Gold);
        ShopUIStyle.Anchor(_dockTypeText.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1),
                           new Vector2(20, -14), new Vector2(-40, 22));

        // 연출 대상(파츠 강화 본체)
        var body = ShopUIStyle.MakeImage(stage, "RangedBody", new Color(1f, 1f, 1f, 0f));
        ShopUIStyle.Anchor(body.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                           new Vector2(0, 6f), new Vector2(760, 360));
        _cardBg[1] = body;
        _cardBasePos[1] = new Vector2(0, 6f);
        var c = body.transform;

        BuildRangedParts(c);

        _cardName[1] = ShopUIStyle.MakeText(c, "Name", 26f, FontStyles.Bold,
                                            TextAlignmentOptions.Center, ShopUIStyle.TextPrimary);
        ShopUIStyle.Anchor(_cardName[1].rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
                           new Vector2(0, -124), new Vector2(-40, 34));

        _cardLevel[1] = ShopUIStyle.MakeText(c, "Level", 22f, FontStyles.Bold,
                                             TextAlignmentOptions.Center, ShopUIStyle.Gold);
        ShopUIStyle.Anchor(_cardLevel[1].rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
                           new Vector2(0, -156), new Vector2(-40, 28));

        _cardGaugeFill[1] = BuildGauge(c, _skin?.gaugeTrack, _skin?.gaugeFill,
                                       _skin?.rangedGaugeFrame ?? _skin?.gaugeFrame, 46f, 52f, 700f);

        _cardAtk[1] = ShopUIStyle.MakeText(c, "Atk", 14f, FontStyles.Bold,
                                           TextAlignmentOptions.Center, ShopUIStyle.TextDim);
        ShopUIStyle.Anchor(_cardAtk[1].rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0),
                           new Vector2(0, 6), new Vector2(-40, 22));
    }

    /// <summary>파츠 슬롯 4칸(활·분열 선택·관통·빈). 슬롯 UI만 — 로직은 Phase 3.</summary>
    private void BuildRangedParts(Transform c)
    {
        string[] labels = { "활", "분열 선택", "관통", "빈" };
        const float box = 150f, h = 96f, gap = 20f;
        int n = _partsSlots.Length;
        float totalW = n * box + (n - 1) * gap;
        float startX = -totalW * 0.5f + box * 0.5f;

        for (int i = 0; i < n; i++)
        {
            var slot = ShopUIStyle.MakeImage(c, $"PartSlot{i}", new Color(0.09f, 0.07f, 0.10f, 1f));
            ShopUIStyle.Anchor(slot.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                               new Vector2(startX + i * (box + gap), -14), new Vector2(box, h));
            ShopUIStyle.Skin(slot, _skin?.slotFrame, sliced: true);
            _partsSlots[i] = slot;

            var mark = ShopUIStyle.MakeText(slot.transform, "Mark", 16f, FontStyles.Bold,
                                            TextAlignmentOptions.Center, ShopUIStyle.TextDim);
            mark.text = i < labels.Length ? labels[i] : "";
            ShopUIStyle.Stretch(mark.rectTransform);
            _partsMark[i] = mark;
        }
    }

    /// <summary>우측 강화정보 컬럼 — 완성본의 세로 정보창. 성공률/재료/성공시/실패시/잭팟/진화까지 + 스트릭/결과/이벤트.</summary>
    private void BuildInfoColumn(Transform w)
    {
        var panel = ShopUIStyle.MakeImage(w, "InfoPanel", ShopUIStyle.BandFill);
        // 강화정보창 643×680@2x → 세로비 유지(0.945)로 잡아야 팔각 프레임이 찌그러지지 않는다.
        ShopUIStyle.Anchor(panel.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1),
                           new Vector2(-Margin, -(TopBarH + Margin)), new Vector2(InfoColW, 466));
        ShopUIStyle.Skin(panel, _skin?.infoPanel, sliced: true);
        var p = panel.transform;

        var h = ShopUIStyle.MakeText(p, "InfoTitle", 20f, FontStyles.Bold,
                                     TextAlignmentOptions.Center, ShopUIStyle.Gold);
        h.text = "강화정보";
        ShopUIStyle.Anchor(h.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
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

        float halfW = (InfoColW - 14f) / 2f;

        var switchBtn = MakeStyledButton(w, "Switch", "전환", out var switchLbl);
        ShopUIStyle.Anchor((RectTransform)switchBtn.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0),
                           new Vector2(colRight - halfW - 14f, Margin), new Vector2(halfW, 103));
        SkinButton(switchBtn, _skin?.switchButton, switchLbl);
        switchBtn.onClick.AddListener(() => SelectTab(_activeTab == 0 ? 1 : 0));

        var exitBtn = MakeStyledButton(w, "Exit", "나가기", out var exitLbl);
        ShopUIStyle.Anchor((RectTransform)exitBtn.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0),
                           new Vector2(colRight, Margin), new Vector2(halfW, 103));
        SkinButton(exitBtn, _skin?.exitButton, exitLbl);
        exitBtn.onClick.AddListener(ClosePopupUI);

        // 진화 버튼 — 조건 충족 시에만 노출(RefreshAll). 전용 아트가 없어 색 버튼 그대로 둔다.
        _evolveBtn = MakeStyledButton(w, "Evolve", "진화", out _);
        _evolveBtn.GetComponent<Image>().color = new Color(0.40f, 0.28f, 0.62f, 1f);
        ShopUIStyle.Anchor((RectTransform)_evolveBtn.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0),
                           new Vector2(colRight, Margin + 245), new Vector2(InfoColW, 46));
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
        pick.transition = Selectable.Transition.None;
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
        if (!WeaponEvolutionSO.IsUnlocked(b, wd.enhanceLevel))
        {
            if (_resultText != null)
                _resultText.text = $"<color=#C7554A>강화 {b.requiredEnhanceLevel} 이상이어야 한다</color>";
            ShopUIStyle.PlaySfx("shop_reject");
            return;
        }

        // 조건: 재료(설정된 경우만 차감)
        var fuel = _controller.Run?.FuelBank;
        if (b.cost > 0 && !(fuel?.TrySpend(FuelKind.EnhanceMaterial, b.cost) ?? false))
        {
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
        if (_oreText != null)
            _oreText.text = (_controller.Run?.FuelBank?.RuneOre ?? 0).ToString();

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

            // i==0=실제 무기 슬롯 카드(테두리): 스킨이면 흰색, 아니면 대상 하이라이트 색.
            // i==1=원거리 연출용 투명 컨테이너(RangedBody): 스프라이트 없으니 투명 유지(불투명 블록 방지).
            if (i == 0)
                _cardBg[i].color = _skin != null ? Color.white : (isTarget ? CardTargetBg : ShopUIStyle.CardFill);
            else
                _cardBg[i].color = Color.clear;
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
            if (_cardAtk[i] != null) _cardAtk[i].text = $"공격 {w.baseAttack:F1}";
            if (typeText != null)    typeText.text = TypeTierLabel(w, max, i);
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
                var table = _controller.Table;
                float cur  = w.baseAttack;
                float next = table != null ? (w.baseAttackRaw > 0f ? w.baseAttackRaw : w.baseAttack) * table.AttackMult(w.enhanceLevel + 1, w.legendId) : cur;
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
            _successText.text = $"성공률 <color=#7AD46E>{chance * 100f:F0}%</color>";
            _costText.text = $"재료 {cost}";
        }

        _streakText.text = _controller.Streak > 0 ? $"▲ 연속 {_controller.Streak}" : "";
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
