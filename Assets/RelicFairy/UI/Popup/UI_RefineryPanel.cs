using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 정제소 패널 (Canvas_Popup, Addressable "UI_RefineryPanel").
///
/// 원석을 넣고 돌려 판을 강화하는 특수 룬(존핵)을 만든다. 로직은 <see cref="RefineryService"/>(런 스코프),
/// 이 패널은 그 표현: 속성 젬 선택 → 돌리기 → 등급 리빌 → 결과/돌발 이벤트 표시 → 보관함으로.
/// 설계: 바탕화면 기획/RelicFairy_기획_정제소_속성응축.md
/// </summary>
public sealed class UI_RefineryPanel : UI_Popup
{
    public override bool BlocksGameplay => true;
    public override bool CloseOnEscape  => true;

    /// <summary>패널이 사라졌음을 알린다 — 방 컨트롤러의 중복 오픈 가드 해제용(상점/재련소와 동일 규약).</summary>
    public event Action OnClosed;

    // 창 크기는 배경 아트(정제소 바탕@2x 2089×1267)의 실치수 = 1044×634.
    // 요소 크기는 전부 각 아트의 실치수(@2x ÷ 2), 위치는 완성본 전체 사진(890×538)에서
    // 창 중심 기준 오프셋을 환산(가로 ×1.173 / 세로 ×1.178)한 값이다.
    // 완성본 목업(정제소 전체 이미지.png 1171×829)을 실측해 옮긴 값. 창 비율을 목업과 맞춘다
    // — 예전 1044×634(1.65)는 가로로 납작해서, 세로로 긴 육각 링과 사이드 패널이 들어가지 않았다.
    private const float WindowW   = 1170f;
    private const float WindowH   = 828f;
    private const float AltarSize = 135f;   // 중앙 최종룬 482@1x — 정사각 결과 틀
    private const float RingR     = 152f;   // 육각 링 반지름 — 목업 젬 중심 실측
    private const float AltarCx   = 0f;     // 목업은 링이 창 정중앙에 있다
    private const float AltarCy   = -4f;
    private const float HexW      = 91f;    // 목업의 젬 실측(약 90×103) — 원본 비율보다 살짝 납작하게 그려져 있다
    private const float HexH      = 103f;
    private const float ColX      = 390f;   // 우측 상태 컬럼 중심

    /// <summary>모든 요소를 창 중심 기준 오프셋으로 배치한다 — 완성본 좌표를 그대로 옮기기 위해.</summary>
    private static readonly Vector2 Half = new(0.5f, 0.5f);

    private static readonly Color AltarFill   = new(0.10f, 0.08f, 0.14f, 1f);
    private static readonly Color HeatDefault = new(0.92f, 0.64f, 0.29f, 1f);

    private RefineryService _svc;
    private string _selectedElement;
    private bool _busy;
    private bool _built;

    private Transform _root;
    private Image    _window;    // 창 배경(9-slice 대체 대상) — 정제소 바탕 아트
    private readonly List<(string id, Image jewel, Image frame, GameObject go)> _gems = new();
    private Image    _altarGlow;
    private Image    _resultBox;
    private Image    _resultSym;
    private TMP_Text _resultAmt;
    private TMP_Text _rarLine;
    private OddsBarView    _oddsBar;
    private FeverGaugeView _feverGauge;
    private TMP_Text _costText;
    private TMP_Text _oreText;    // 우상단 원석 보유 카운터(완성본)
    private TMP_Text _hint;
    private Image    _spinBtnImg;
    private Image    _costPlateImg;
    private Image    _altarRing;
    private GameObject    _freeBtn;      // 첫 돌리기 무료(방 특전) — 아트에 글자가 없어 라벨을 얹는다
    private TMP_Text      _freeLabel;
    private GameObject    _eventBanner;
    private RectTransform _eventBannerRT;
    private Image         _eventBannerImg;   // MakeFrame의 inner(채움) — 종류별 색/아트 스왑 대상
    private TMP_Text      _eventText;
    private TMP_Text      _eventTitle;
    private GameObject    _reforgeBtn;
    private TMP_Text      _reservedText;     // 과열·불티가 '다음 회 예약됨'을 알리는 표시

    // ── Lifecycle ──

    public override void Init()
    {
        base.Init();
        _svc = GameRunBootstrapper.Instance?.Run?.Refinery;
        BuildUI();
        Refresh();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        OnClosed?.Invoke();
    }

    /// <summary>방 특전은 이 패널을 닫으면 사라진다 — 상시 탭(룬판 버튼)으로 다시 열면 특전 없이 열려야 한다.</summary>
    public override void ClosePopupUI()
    {
        _svc?.ClearRoomPerk();
        base.ClosePopupUI();
    }

    /// <summary>
    /// 정제한 룬을 들고 배치 화면을 연다 — 정제소는 닫고 그 룬을 판에 올린다.
    /// 결과가 드러나고 잠시 뒤(PlayRevealAsync 끝) 자동 호출된다. 상점 구매와 같은 흐름.
    /// </summary>
    private void OpenGridForRune(RuntimeItemData rune)
    {
        if (rune == null) return;
        base.ClosePopupUI();   // 정제소 팝업을 먼저 닫고
        if (UI_GridPanel.Instance == null) Managers.UI?.ShowOverlayUI<UI_GridPanel>();
        if (UI_GridPanel.Instance == null) return;
        UI_GridPanel.Instance.ShowWithNewItem(rune);
    }

    // ── Build ──

    private void BuildUI()
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
        _root = window.transform;
        _window = window;

        // 제목바 — 완성본은 좌 제목 / 우 원석 한 줄뿐이다(부제 없음).
        var bar = ShopUIStyle.MakeImage(_root, "TitleBar", ShopUIStyle.BandFill);
        ShopUIStyle.Anchor(bar.rectTransform, Half, Half, Half,
            new Vector2(0f, 307f), new Vector2(965f, 90f));
        ShopUIStyle.Skin(bar, UISkin.Refinery?.titleBar, sliced: true);

        var title = ShopUIStyle.MakeText(bar.transform, "Title", 21f, FontStyles.Bold,
            TextAlignmentOptions.Left, ShopUIStyle.TextPrimary);
        ShopUIStyle.Anchor(title.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(24f, 0f), new Vector2(560f, 30f));
        title.text = "정제소 — 응축 재단";

        _oreText = ShopUIStyle.MakeText(bar.transform, "Ore", 19f, FontStyles.Bold,
            TextAlignmentOptions.Right, ShopUIStyle.TextPrimary);
        ShopUIStyle.Anchor(_oreText.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(-24f, 0f), new Vector2(300f, 30f));

        BuildGems();
        BuildAltar();
        BuildSide();
        BuildFooter();
        BuildEvent();
        ApplySkin();

        AddClick(veil.gameObject, () => { if (!_busy) ClosePopupUI(); });
    }

    private void BuildGems()
    {
        var order = ElementDef.Order;
        int n = order.Count;

        // 완성본: 중앙 원을 감싸는 육각 링. 인덱스별 각도 —
        // 불=상단, 얼음=좌상, 전기=우상, 풀=좌하, 빛=우하, 어둠=하단.
        float[] ang = { 90f, 150f, 30f, 210f, 330f, 270f };

        for (int i = 0; i < n; i++)
        {
            string id = order[i];
            Color col = ElementDef.IdColor(id, ShopUIStyle.TextDim);

            // 완성본에는 육각 뒤에 카드 상자가 없다 — 클릭 판정만 갖는 투명 컨테이너를 쓴다.
            var slot = ShopUIStyle.MakeImage(_root, $"Gem_{id}", Color.clear, raycast: true);
            var rt = slot.rectTransform;
            float a = ang[i < ang.Length ? i : 0] * Mathf.Deg2Rad;
            ShopUIStyle.Anchor(rt, Half, Half, Half,
                new Vector2(AltarCx + RingR * Mathf.Cos(a), AltarCy + RingR * Mathf.Sin(a)),
                new Vector2(HexW + 10f, HexH + 10f));

            // 사전 채색된 육각 각인이 곧 속성 표시다 — 완성본엔 노드 이름 라벨이 없다.
            var jewel = ShopUIStyle.MakeImage(slot.transform, "Jewel", col);
            ShopUIStyle.Anchor(jewel.rectTransform, Half, Half, Half,
                Vector2.zero, new Vector2(HexW, HexH));
            jewel.preserveAspect = true;

            string cap = id;
            AddClick(rt.gameObject, () => Select(cap));
            _gems.Add((id, jewel, slot, rt.gameObject));
        }
    }

    private void BuildAltar()
    {
        // 완성본의 제단은 "어두운 원 바탕 + 얇은 링" 두 장이다. 링 색이 곧 선택 속성 피드백이 된다.
        _altarGlow = ShopUIStyle.MakeImage(_root, "Altar", AltarFill);
        ShopUIStyle.Anchor(_altarGlow.rectTransform, Half, Half, Half,
            new Vector2(AltarCx, AltarCy), new Vector2(AltarSize, AltarSize));
        ShopUIStyle.Skin(_altarGlow, UISkin.Refinery?.altarCore);   // 중앙 원 바탕

        _altarRing = ShopUIStyle.MakeImage(_altarGlow.transform, "Ring", HeatDefault);
        ShopUIStyle.Stretch(_altarRing.rectTransform);
        ShopUIStyle.Skin(_altarRing, UISkin.Refinery?.slotFrame, tint: HeatDefault);   // 중앙 원 테두리
        _altarRing.preserveAspect = true;

        // 결과 룬 박스(초기 숨김)
        _resultBox = ShopUIStyle.MakeImage(_altarGlow.transform, "Result", Color.clear);
        ShopUIStyle.Anchor(_resultBox.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(96f, 96f));
        _resultSym = ShopUIStyle.MakeImage(_resultBox.transform, "Sym", Color.white);
        ShopUIStyle.Anchor(_resultSym.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 12f), new Vector2(38f, 38f));
        _resultAmt = ShopUIStyle.MakeText(_resultBox.transform, "Amt", 18f, FontStyles.Bold,
            TextAlignmentOptions.Center, Color.white);
        ShopUIStyle.Anchor(_resultAmt.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 12f), new Vector2(96f, 22f));
        _resultBox.gameObject.SetActive(false);

        _rarLine = ShopUIStyle.MakeText(_root, "RarLine", 15f, FontStyles.Bold,
            TextAlignmentOptions.Center, ShopUIStyle.TextDim);
        ShopUIStyle.Anchor(_rarLine.rectTransform, Half, Half, Half,
            new Vector2(AltarCx, -196f), new Vector2(560f, 24f));
    }

    /// <summary>우측 상태 컬럼 — 피버(위) / 확률(아래). 정제소 재미의 두 축을 눈에 보이게 세운다.</summary>
    private void BuildSide()
    {
        // 크기는 아트 실치수(피버칸 621×216 / 확률막대 바탕 609×255 @2x).
        _feverGauge = FeverGaugeView.Create(_root, Half, Half, Half,
            new Vector2(ColX, 117f), new Vector2(310f, 140f));

        _oddsBar = OddsBarView.Create(_root, Half, Half, Half,
            new Vector2(ColX, -69f), new Vector2(310f, 150f));
    }

    /// <summary>
    /// 완성본 하단 = [비용] [돌리기] [재점화] 한 줄 + 그 아래 [첫 돌리기 무료].
    /// 비용·돌리기·재점화 아트에는 글자가 이미 구워져 있어 라벨을 얹지 않는다(겹쳐 읽히지 않게).
    /// </summary>
    private void BuildFooter()
    {
        var skin = UISkin.Refinery;

        _costPlateImg = MakeArtButton("CostPlate", First(skin?.costPlate), null,
            new Vector2(-311f, -274f), new Vector2(250f, 69f), null);

        // 완성본 버튼 아트는 글자가 없는 빈 판이다 — 비용 숫자는 판 가운데에 놓는다.
        _costText = ShopUIStyle.MakeText(_costPlateImg.transform, "Cost", 18f, FontStyles.Bold,
            TextAlignmentOptions.Center, ShopUIStyle.TextPrimary);
        ShopUIStyle.Stretch(_costText.rectTransform, 10f);

        _spinBtnImg = MakeArtButton("SpinBtn", First(skin?.spinButton), "돌리기",
            new Vector2(2f, -274f), new Vector2(315f, 71f), OnSpinClicked);

        var re = MakeArtButton("ReforgeBtn", First(skin?.reforgeButton), "재점화",
            new Vector2(307f, -274f), new Vector2(255f, 70f), OnReforgeClicked);
        _reforgeBtn = re.gameObject;

        var free = MakeArtButton("FreeSpin", First(skin?.perkBadge), null,
            new Vector2(0f, -344f), new Vector2(250f, 46f), null);
        _freeBtn = free.gameObject;
        _freeLabel = ShopUIStyle.MakeText(free.transform, "Label", 14f, FontStyles.Bold,
            TextAlignmentOptions.Center, Color.white);
        ShopUIStyle.Stretch(_freeLabel.rectTransform);
        _freeBtn.SetActive(false);

        // 안내·거절 사유. 완성본에 상설 문구는 없으므로 할 말이 있을 때만 뜬다.
        _hint = ShopUIStyle.MakeText(_root, "Hint", 13f, FontStyles.Normal,
            TextAlignmentOptions.Center, ShopUIStyle.TextDim);
        ShopUIStyle.Anchor(_hint.rectTransform, Half, Half, Half,
            new Vector2(AltarCx, -224f), new Vector2(640f, 20f));
        _hint.text = "";
    }

    /// <summary>
    /// 아트 한 장짜리 버튼. <b>완성본 버튼 아트에는 글자가 없다</b>(빈 판) — 라벨은 항상 코드가 그린다.
    /// 예전 아트는 "돌리기/재점화/비용"이 구워져 있어 라벨을 억제했는데, 그대로 두면 이제 무지 버튼이 된다.
    /// </summary>
    private Image MakeArtButton(string name, Sprite sprite, string label,
                                Vector2 pos, Vector2 size, Action onClick)
    {
        var img = ShopUIStyle.MakeImage(_root, name, sprite != null ? Color.white : ShopUIStyle.GoldPillBg,
            raycast: onClick != null);
        ShopUIStyle.Anchor(img.rectTransform, Half, Half, Half, pos, size);
        ShopUIStyle.Skin(img, sprite, sliced: true);

        if (label != null)
        {
            var lbl = ShopUIStyle.MakeText(img.transform, "Label", 19f, FontStyles.Bold,
                TextAlignmentOptions.Center, ShopUIStyle.TextPrimary);
            ShopUIStyle.Stretch(lbl.rectTransform);
            lbl.text = label;
        }

        if (onClick != null) AddClick(img.gameObject, onClick);
        return img;
    }

    private static Sprite First(Sprite[] arr) => (arr != null && arr.Length > 0) ? arr[0] : null;

    private void BuildEvent()
    {
        // 완성본: 돌발 배너는 좌상단. 아트(육각 장식 포함)를 한 장으로 깔고 제목/설명 두 줄을 안쪽에 넣는다.
        _eventBannerImg = ShopUIStyle.MakeImage(_root, "EventBanner", ShopUIStyle.BandFill);
        _eventBanner   = _eventBannerImg.gameObject;
        _eventBannerRT = _eventBannerImg.rectTransform;
        ShopUIStyle.Anchor(_eventBannerRT, Half, Half, Half,
            new Vector2(-389f, 140f), new Vector2(236f, 186f));   // 아트 실치수 473×372@2x
        _eventBannerImg.preserveAspect = true;

        // 아트는 위아래 육각 장식이 본문 밖으로 뻗는다 — 글자는 안쪽 상자(대략 세로 62%)에만 넣는다.
        _eventTitle = ShopUIStyle.MakeText(_eventBanner.transform, "EvTitle", 19f, FontStyles.Bold,
            TextAlignmentOptions.Left, ShopUIStyle.RarityGlow(ItemRarity.Epic));
        ShopUIStyle.Anchor(_eventTitle.rectTransform, Half, Half, Half,
            new Vector2(-14f, 26f), new Vector2(160f, 26f));

        _eventText = ShopUIStyle.MakeText(_eventBanner.transform, "EvTxt", 12.5f, FontStyles.Normal,
            TextAlignmentOptions.TopLeft, ShopUIStyle.TextDim);
        ShopUIStyle.Anchor(_eventText.rectTransform, Half, Half, Half,
            new Vector2(-14f, -14f), new Vector2(160f, 44f));

        // 예약 배지 — 과열·불티는 배너가 사라진 뒤에도 다음 회까지 효과가 남는다.
        // 이게 없으면 "아까 뭐가 걸렸더라?"가 되어 이벤트의 값어치가 절반으로 준다.
        // 확률 패널 바로 아래 — 피버 게이지(위)와 겹치지 않는 유일한 빈 줄이다.
        _reservedText = ShopUIStyle.MakeText(_root, "Reserved", 12.5f, FontStyles.Bold,
            TextAlignmentOptions.Right, ShopUIStyle.RarityGlow(ItemRarity.Legendary));
        ShopUIStyle.Anchor(_reservedText.rectTransform, Half, Half, Half,
            new Vector2(ColX, -82f), new Vector2(304f, 20f));
        _reservedText.richText = true;

        _eventBanner.SetActive(false);
    }

    /// <summary>
    /// 디자이너 아트를 코드로 그린 박스 위에 얹는다. 스킨 미로드면 아무것도 안 하고 색 폴백을 유지한다
    /// (<see cref="UISkin.PreloadAsync"/>는 앱 부트에서 fire-and-forget — 런 중 여는 이 패널은 이미 로드됨).
    /// 위젯(피버·확률)과 돌발 배너는 각자 <see cref="UISkin.Refinery"/>를 참조하므로 여기서 다루지 않는다.
    /// </summary>
    private void ApplySkin()
    {
        var skin = UISkin.Refinery;
        if (skin == null) return;

        // 창 바탕 — 전면 일러스트(9-slice 아님)로 교체. 아트가 자체 가장자리를 갖고 있어
        // 코드가 그린 청동 테두리는 어둡게 낮춘다(완성본엔 굵은 금테가 없다).
        ShopUIStyle.Skin(_window, skin.windowFrame);
        if (_window.transform.parent != null &&
            _window.transform.parent.TryGetComponent<Image>(out var outer))
            outer.color = new Color(0.30f, 0.36f, 0.48f, 0.85f);

        // 속성 노드 — 사전 채색된 육각 각인 한 장뿐. 뒤에 상자를 두지 않는다.
        for (int i = 0; i < _gems.Count; i++)
        {
            var glyph = skin.Glyph(i);
            if (glyph != null && _gems[i].jewel != null)
                ShopUIStyle.Skin(_gems[i].jewel, glyph);   // 이미 채색된 각인 — 색 곱하지 않음
        }
    }

    // ── 상태 ──

    private void Select(string elementId)
    {
        if (_busy) return;
        _selectedElement = elementId;
        Color col = ElementDef.IdColor(elementId, HeatDefault);

        // 각인 아트는 이미 채색돼 있어 색으로는 선택을 알릴 수 없다 —
        // 고른 것은 살짝 키우고, 나머지는 흐려 링 전체가 "하나 골랐다"로 읽히게 한다.
        for (int i = 0; i < _gems.Count; i++)
        {
            bool on = _gems[i].id == elementId;
            var j = _gems[i].jewel;
            if (j == null) continue;
            j.transform.localScale = Vector3.one * (on ? 1.14f : 0.94f);
            var c = j.color;
            j.color = new Color(c.r, c.g, c.b, on ? 1f : 0.45f);
        }

        // 중앙 링이 고른 속성색으로 물든다 — 완성본의 초록 링이 그 자리다.
        if (_altarRing != null) _altarRing.color = col;

        _hint.text = "";
        Refresh();
    }

    private void Refresh()
    {
        if (_svc == null) return;
        var (rare, epic, leg) = _svc.CurrentOdds();
        _oddsBar?.SetOdds(rare, epic, leg, _svc.NextHeat);
        _feverGauge?.SetLevel(_svc.Fever);
        RefreshReserved();

        int cost = _svc.CurrentCost;
        _costText.text  = cost <= 0 ? "무료" : cost.ToString();
        _costText.color = cost <= 0 ? ShopUIStyle.Gold
                        : (_svc.CanAfford ? ShopUIStyle.TextPrimary : ShopUIStyle.RejectRed);
        if (_oreText != null) _oreText.text = $"원석 {_svc.OreOwned}";

        // 방 특전은 하단 '첫 돌리기 무료' 버튼으로 드러낸다(상시 탭이면 숨김).
        string perk = _svc.RoomPerkLabel;
        if (_freeBtn != null)
        {
            bool showPerk = !string.IsNullOrEmpty(perk);
            _freeBtn.SetActive(showPerk);
            if (showPerk && _freeLabel != null) _freeLabel.text = perk;
        }

        bool canSpin = _selectedElement != null && _svc.CanAfford && !_busy;
        Tint(_spinBtnImg, canSpin);

        // 재점화는 완성본처럼 상시 자리를 지키되, 쓸 수 없을 땐 흐려진다.
        if (_reforgeBtn != null)
            Tint(_reforgeBtn.GetComponent<Image>(), _svc.CanReforge && !_busy);
    }

    /// <summary>글자가 구워진 버튼 아트는 색을 갈아끼울 수 없어, 밝기로 활성/비활성을 알린다.</summary>
    private void Tint(Image img, bool enabled)
    {
        if (img == null) return;
        if (img.sprite != null) img.color = enabled ? Color.white : new Color(0.45f, 0.45f, 0.5f, 0.75f);
        else                    img.color = enabled ? ShopUIStyle.GoldPillBg : ShopUIStyle.BandFill;
    }

    // ── 돌리기 ──

    private void OnSpinClicked()
    {
        if (_busy || _svc == null || _selectedElement == null) return;
        if (!_svc.CanAfford) { _hint.text = "원석이 부족합니다"; return; }
        SpinAsync().Forget();
    }

    private async UniTaskVoid SpinAsync()
    {
        _busy = true;
        _eventBanner.SetActive(false);
        _resultBox.gameObject.SetActive(false);
        _rarLine.text = "";
        Refresh();

        var outcome = _svc.Craft(_selectedElement);
        if (!outcome.Success) { _hint.text = outcome.FailReason; _busy = false; Refresh(); return; }

        // 「정제 품질」 할인 조건 집계. RefineryService는 런 참조가 없어 호출부에서 센다.
        GameRunBootstrapper.Instance?.Run?.ReportRefineUse();

        await PlayRevealAsync(outcome);

        _busy = false;
        Refresh();

        // 재점화(다시 굴리기)를 쓸 수 있으면 그 선택을 기다린다 — 그 경우가 아니면
        // 결과를 잠깐 보여준 뒤 배치 화면으로 자동으로 넘긴다.
        if (!_svc.CanReforge)
            await HandOffToGridAsync(outcome.Rune);
    }

    private void OnReforgeClicked()
    {
        if (_busy || _svc == null || !_svc.CanReforge) return;
        ReforgeAsync().Forget();
    }

    private async UniTaskVoid ReforgeAsync()
    {
        _busy = true;
        _eventBanner.SetActive(false);
        _resultBox.gameObject.SetActive(false);
        Refresh();

        var outcome = _svc.Reforge(_selectedElement);
        if (outcome.Success) await PlayRevealAsync(outcome);

        _busy = false;
        Refresh();

        // 재점화는 1회뿐(이미 소진) → 결과를 잠깐 보여준 뒤 배치 화면으로 넘긴다.
        if (outcome.Success) await HandOffToGridAsync(outcome.Rune);
    }

    /// <summary>결과를 잠깐 보여준 뒤(≈0.9초) 정제소를 닫고 그 룬을 판에 올린다.</summary>
    private async UniTask HandOffToGridAsync(RuntimeItemData rune)
    {
        if (rune == null) return;
        try { await UniTask.Delay(900, ignoreTimeScale: true, cancellationToken: this.GetCancellationTokenOnDestroy()); }
        catch (OperationCanceledException) { return; }

        OpenGridForRune(rune);
    }

    private async UniTask PlayRevealAsync(RefineryOutcome outcome)
    {
        // 제단 달아오름
        Color heat = ElementDef.IdColor(_selectedElement, HeatDefault);
        var ct = this.GetCancellationTokenOnDestroy();
        try
        {
            // 달아오르는 건 링이다 — 원 바탕은 아트라 색을 건드리면 그림이 물든다.
            float t = 0f;
            while (t < 0.5f)
            {
                t += Time.unscaledDeltaTime;
                float k = 0.45f + 0.55f * Mathf.PingPong(t * 3f, 1f);
                if (_altarRing != null)
                    _altarRing.color = new Color(heat.r * k, heat.g * k, heat.b * k, 1f);
                await UniTask.Yield(ct);
            }
        }
        catch (OperationCanceledException) { return; }

        // 결과 룬
        Color rc = ShopUIStyle.RarityGlow(outcome.Rarity);
        _resultBox.gameObject.SetActive(true);
        _resultBox.color = new Color(rc.r, rc.g, rc.b, 0.28f);
        _resultSym.color = heat;
        _resultAmt.text  = $"+{AmtOf(outcome.Rune)}%";
        _resultAmt.color = rc;

        var e = ElementDef.GetById(_selectedElement);
        string en = e != null ? e.Name : _selectedElement;
        _rarLine.color = rc;
        _rarLine.text = $"{RarLabel(outcome.Rarity)} 존핵 — {en} 존 시너지 +{AmtOf(outcome.Rune)}%";
        if (_altarRing != null) _altarRing.color = heat;

        Managers.Sound?.PlayEvent(SoundEvent.ItemPickup);

        // 돌발 이벤트
        ShowEvent(outcome.EventKind);
    }

    /// <summary>
    /// 돌발 이벤트 배너. 4종이 <b>서로 다른 종류의 기쁨</b>이라 크기·색으로 구별한다.
    /// 특히 재점화는 이 화면을 다시 돌리게 만드는 유일한 장치라 나머지 셋보다 확실히 크게 띄운다
    /// (존핵은 속성당 사실상 한 번만 챙기면 되는 물건이라, 나쁜 결과를 뒤집을 수 있다는 게 핵심 유인).
    /// </summary>
    private void ShowEvent(RefineryEventKind ev)
    {
        if (ev == RefineryEventKind.None) return;

        var skin = UISkin.Refinery;
        bool  hero = ev == RefineryEventKind.Reignite;
        Color tone = ev switch
        {
            RefineryEventKind.Reignite => new Color(1.00f, 0.54f, 0.23f),   // 되돌리기 — 불씨
            RefineryEventKind.Overheat => new Color(0.88f, 0.42f, 0.16f),   // 예고 — 달아오름
            RefineryEventKind.Spark    => ShopUIStyle.RarityGlow(ItemRarity.Legendary),
            _                          => new Color(0.62f, 0.49f, 0.90f),   // 쌍생 — 분열
        };
        // 제목과 설명을 나눈다 — 완성본 배너가 "재점화 / 결과를 1회 무료로 다시 굴린다" 두 단이다.
        (string head, string body) = ev switch
        {
            RefineryEventKind.Reignite => ("재점화", "결과를 1회 무료로 다시 굴린다."),
            RefineryEventKind.Overheat => ("과열",   "다음 돌리기의 상위 등급 확률이 2배."),
            RefineryEventKind.Spark    => ("불티",   "다음 돌리기를 무료로 돌린다."),
            _                          => ("쌍생",   "존핵을 하나 더 얻었다."),
        };

        _eventBannerRT.sizeDelta = hero ? new Vector2(245f, 157f) : new Vector2(215f, 138f);
        _eventTitle.color        = tone;
        _eventTitle.text         = head;
        _eventText.text          = body;

        // 아트가 있으면 종류별 배너로 스왑 — 없으면 색 폴백이 그대로 남는다.
        var art = hero ? skin?.bannerReignite
                       : (skin?.bannerSmall != null && skin.bannerSmall.Length > 0
                          ? skin.bannerSmall[Mathf.Clamp((int)ev - 2, 0, skin.bannerSmall.Length - 1)]
                          : skin?.bannerReignite);
        ShopUIStyle.Skin(_eventBannerImg, art);

        _eventBanner.SetActive(true);
        Refresh();   // 재점화 가능 여부가 바뀌므로 하단 버튼 상태를 다시 칠한다
    }

    /// <summary>다음 회로 넘어간 효과(과열·불티)를 상시 표시한다. 소진되면 자동으로 사라진다.</summary>
    private void RefreshReserved()
    {
        if (_reservedText == null) return;
        if (_svc == null) { _reservedText.text = ""; return; }

        string s = "";
        if (_svc.NextHeat) s  = "<color=#FF8A3A>▲ 과열 예약</color>";
        if (_svc.NextFree) s += (s.Length > 0 ? "   " : "") + "<color=#FFCB5A>◆ 불티 예약</color>";
        _reservedText.text = s;
    }

    // ── Helpers ──

    private static int AmtOf(RuntimeItemData rune)
    {
        if (rune?.effects != null)
            foreach (var s in rune.effects)
                if (s.effectType == "AmplifyZone") return Mathf.RoundToInt(s.value);
        return 0;
    }

    private static string RarLabel(ItemRarity r) => r switch
    {
        ItemRarity.Legendary => "◆ Legendary",
        ItemRarity.Epic      => "Epic",
        _                    => "Rare",
    };

    private static string Pct(float f) => Mathf.RoundToInt(f * 100f) + "%";
    private static string HexOf(ItemRarity r) => ColorUtility.ToHtmlStringRGB(ShopUIStyle.RarityGlow(r));

    private static void AddClick(GameObject go, Action onClick)
    {
        var btn = go.GetComponent<Button>() ?? go.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(() => onClick?.Invoke());
    }
}
