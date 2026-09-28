using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 정제소 패널 (Canvas_Popup, Addressable "UI_RefineryPanel").
///
/// 원석을 넣고 돌리면 룬 하나가 무작위로 나온다. 로직은 <see cref="RefineryService"/>(런 스코프),
/// 이 패널은 그 표현: 돌리기 → 등급 리빌(젬 링의 불이 돌다 나온 룬의 속성에 멎는다) → 결과 → 룬판으로.
/// 09-28 사용자 「간단하게 줄여서 룬을 랜덤으로 뽑는 시스템으로」 — 속성 선택 · 피버 · 돌발 이벤트 · 재점화 · 방 특전을 걷었다.
/// 구운 프리팹에 남은 그 요소들은 <see cref="EnsureRuntimeLayout"/>가 숨긴다.
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
    // 목업 아트(빨간룬.png 283×339)를 전체샷에 템플릿 매칭한 실측(상관 0.85) — 96×115.
    // 예전 값 91×103은 눈으로 잰 것이라 12% 작았고 비율도 0.883으로 원본(0.835)과 달라 납작했다.
    private const float HexW      = 96f;
    private const float HexH      = 115f;
    private const float ColX      = 390f;   // 우측 상태 컬럼 중심
    private const float OddsY     = AltarCy; // 확률 막대 — 피버가 비운 자리로 올려 제단 높이에(09-28)
    private const float FooterY   = -274f;
    private const float CostX     = -170f;   // 하단 [비용][돌리기] 두 칸 — 재점화가 빠져 가운데로 모은다
    private const float SpinX     =  170f;
    private const string IdleHint = "원석을 넣고 돌리면 룬 하나가 나온다";

    /// <summary>걷은 기능(09-28)의 구운 요소 — 피버 · 돌발 배너 · 화살표 · 예약 표시 · 재점화 · 방 특전.</summary>
    private static readonly string[] RetiredNodes =
        { "FeverGauge", "EventBanner", "EvPointer", "Reserved", "ReforgeBtn", "FreeSpin" };

    /// <summary>모든 요소를 창 중심 기준 오프셋으로 배치한다 — 완성본 좌표를 그대로 옮기기 위해.</summary>
    private static readonly Vector2 Half = new(0.5f, 0.5f);

    private static readonly Color HeatDefault = new(0.92f, 0.64f, 0.29f, 1f);

    private RefineryService _svc;
    private bool _busy;
    private bool _built;
    private bool _themedSpin;   // [돌리기]에 공통 베벨을 입혔다 — 누를 수 있음/없음을 금 틴트로

    [SerializeField] private Transform _root;
    [SerializeField] private Image    _window;    // 창 배경(9-slice 대체 대상) — 정제소 바탕 아트
    private readonly List<(string id, Image jewel, Image frame, GameObject go)> _gems = new();
    [SerializeField] private Image    _altarGlow;
    [SerializeField] private Image    _resultBox;
    [SerializeField] private Image    _resultSym;
    [SerializeField] private TMP_Text _resultAmt;
    [SerializeField] private TMP_Text _rarLine;
    [SerializeField] private OddsBarView    _oddsBar;
    [SerializeField] private TMP_Text _costText;
    [SerializeField] private TMP_Text _oreText;    // 우상단 원석 보유 카운터(완성본)
    [SerializeField] private TMP_Text _hint;
    [SerializeField] private Image    _spinBtnImg;
    [SerializeField] private Image    _costPlateImg;
    [SerializeField] private Image    _altarRing;

    // ── 응축 3박자 (기획_정제소 §7 · 구현설계 R5) ──
    // 기획: "로 자체가 빛을 응결 · 등급이 높을수록 연출이 길고 화려". 예전엔 링이 0.5초 깜빡이고
    // 결과가 그냥 떠서, 전설이 나와도 흔한 것과 같은 그림이었다(09-19 검수).
    private const float GatherBase = 0.45f;   // 빛을 모으는 시간(+등급마다 0.26초)
    private const int   MoteCount  = 14;      // 제단으로 빨려드는 불티
    private const float SlipDur    = 0.22f;   // 결과가 판으로 넘어가며 빨려드는 시간

    [SerializeField] private RectTransform _fxRoot;       // 제단 위 연출 층(결과 틀보다 뒤)
    [SerializeField] private Image         _revealFlash;  // 공개 섬광(등급색)
    [SerializeField] private Image         _revealRays;   // 전설 전용 빛살
    private Image[] _motes;   // 코드로 그린 빛 — 프리팹에 저장되지 않아 런타임에 만든다

    // ── 좌측 「나올 룬 / 나온 룬」 판 (09-27 · 09-28) ── 프리팹이 구워져 있어 런타임에 짓는다.
    private Image    _infoGem;
    private TMP_Text _infoCaption, _infoName, _infoDesc, _infoFoot;

    // ── Lifecycle ──

    public override void Init()
    {
        base.Init();
        _svc = GameRunBootstrapper.Instance?.Run?.Refinery;
        BuildUI();
        EnsureRuntimeLayout();
        RefreshInfo();
        Refresh();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        OnClosed?.Invoke();
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

        // 프리팹이 구워져 있으면 <b>짓지 않고 잇기만 한다</b> — 다시 지으면 UI가 두 벌 겹친다.
        if (transform.childCount > 0) { BindBakedHierarchy(); return; }

        ShopUIStyle.Stretch(GetComponent<RectTransform>());

        var veil = ShopUIStyle.MakeImage(transform, "Veil", ShopUIStyle.Veil, raycast: true);
        ShopUIStyle.Stretch(veil.rectTransform);

        var window = ShopUIStyle.MakeFrame(transform, "Window",
            ShopUIStyle.WindowBorder, ShopUIStyle.WindowFill, 2f, raycast: true);
        ShopUIStyle.Anchor((RectTransform)window.transform.parent,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(WindowW, WindowH));
        _root = window.transform;
        // 화면 맞춤은 빌더가 붙인다 — 프리팹에만 붙이면 재굽기 때 사라진다.
        // 크기를 가진 것은 테두리(부모)라 거기에 붙인다.
        window.transform.parent.gameObject.AddComponent<UIWindowFitter>()
              .Configure(maxScale: UIWindowFitter.ContentScreen);
        _window = window;

        // 제목바 — 완성본은 좌 제목 / 우 원석 한 줄뿐이다(부제 없음).
        var bar = ShopUIStyle.MakeImage(_root, "TitleBar", ShopUIStyle.BandFill);
        // 폭 1020 = 아트 「타이틀 에리어」(3241×286 · 비율 11.33)를 높이 90에 맞춘 값.
        // 965면 비율 10.7이라 화살촉 양끝이 안으로 눌린다(완성본은 1035 폭).
        ShopUIStyle.Anchor(bar.rectTransform, Half, Half, Half,
            new Vector2(0f, 307f), new Vector2(1020f, 90f));
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
        BuildInfoPanel();
        ApplySkin();

        AddClick(veil.gameObject, () => { if (!_busy) ClosePopupUI(); });
    }

    /// <summary>
    /// 구워진 프리팹을 잇는다 — <b>계층·좌표·아트는 프리팹이 갖고, 코드는 배선만 한다.</b>
    ///
    /// <para>직렬화되지 않는 둘만 되살린다: 코드가 붙인 클릭 리스너와, 런타임 목록 <see cref="_gems"/>.
    /// 이름으로 찾는다 — 빌더가 붙이던 이름 그대로 프리팹에 굳어 있다.</para>
    /// </summary>
    private void BindBakedHierarchy()
    {
        _gems.Clear();

        // 젬 링은 이제 고르는 곳이 아니라 장식 — 돌리는 동안 불이 돌다 나온 룬의 속성에 멎는다.
        // 구울 때 붙은 버튼은 꺼서 누를 수 있어 보이지 않게 한다.
        foreach (string id in ElementDef.Order)
        {
            var slot = _root != null ? _root.Find($"Gem_{id}") : null;
            if (slot == null) continue;

            if (slot.TryGetComponent<Button>(out var gemBtn)) gemBtn.enabled = false;
            var frame = slot.GetComponent<Image>();
            if (frame != null) frame.raycastTarget = false;
            _gems.Add((id, slot.Find("Jewel")?.GetComponent<Image>(), frame, slot.gameObject));
        }

        var veil = transform.Find("Veil");
        if (veil != null) AddClick(veil.gameObject, () => { if (!_busy) ClosePopupUI(); });

        BindClick("SpinBtn", OnSpinClicked);
    }

    private void BindClick(string name, Action onClick)
    {
        var t = _root != null ? _root.Find(name) : null;
        if (t != null) AddClick(t.gameObject, onClick);
    }

    /// <summary>
    /// 창 배경을 <b>원본 비율 그대로 창을 덮도록</b> 깐다(cover). 비율이 다른 쪽은 마스크로 잘려 나간다.
    /// _window(모든 내용의 부모)에는 손대지 않고 그 첫 자식으로 마스크+배경을 둔다.
    /// 9-slice 보더가 있는 아트는 늘려도 되므로 예전처럼 Sliced로 그대로 입힌다.
    /// </summary>
    private void ApplyCoverBackground(Sprite art)
    {
        if (_window == null || art == null) return;

        if (art.border.sqrMagnitude > 0f)
        {
            ShopUIStyle.Skin(_window, art, sliced: true);
            return;
        }

        var mask = _window.transform.Find("BgMask") as RectTransform;
        if (mask == null)
        {
            var maskGo = new GameObject("BgMask", typeof(RectTransform), typeof(RectMask2D));
            mask = (RectTransform)maskGo.transform;
            mask.SetParent(_window.transform, false);
            mask.SetAsFirstSibling();                   // 내용보다 뒤에 그려진다
        }
        ShopUIStyle.Stretch(mask);

        var bg = mask.Find("Bg")?.GetComponent<Image>()
                 ?? ShopUIStyle.MakeImage(mask, "Bg", Color.white);
        bg.raycastTarget = false;
        ShopUIStyle.Skin(bg, art);

        // 창과 아트 중 더 좁은 축을 창에 맞추고 다른 축은 비율대로 키운다 → 항상 창을 덮는다.
        float winW = WindowW, winH = WindowH;
        float aspect = art.rect.width / Mathf.Max(1f, art.rect.height);
        float w, h;
        if (aspect > winW / winH) { h = winH; w = winH * aspect; }
        else                      { w = winW; h = winW / aspect; }
        ShopUIStyle.Anchor(bg.rectTransform, Half, Half, Half, Vector2.zero, new Vector2(w, h));
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

            // 완성본에는 육각 뒤에 카드 상자가 없다 — 투명 컨테이너(장식이라 클릭을 받지 않는다).
            var slot = ShopUIStyle.MakeImage(_root, $"Gem_{id}", Color.clear);
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

            _gems.Add((id, jewel, slot, rt.gameObject));
        }
    }

    private void BuildAltar()
    {
        // 제단 중앙은 「중앙 최종룬」 액자 한 장으로 선다 — 링 색이 곧 선택 속성 피드백이다.
        //
        // 바탕은 <b>투명</b>으로 만든다. 예전엔 불투명한 어두운 보라(AltarFill)로 깔고 그 위에
        // 「정제소 중앙 원 바탕@2x」(순수 검정 원판)를 얹었는데, 그 검은 원이 속이 비치는 액자 뒤에
        // 그대로 드러나 결과가 비어 있을 때 중앙이 <b>검은 구멍</b>으로 보였다.
        // 아트를 빼도 색이 남으면 이번엔 어두운 <b>사각형</b>이 남으므로 색까지 지워야 한다.
        // (altarCore에 아트를 다시 넣으면 Skin이 흰 틴트로 덮어써 정상 동작한다.)
        _altarGlow = ShopUIStyle.MakeImage(_root, "Altar", Color.clear);
        ShopUIStyle.Anchor(_altarGlow.rectTransform, Half, Half, Half,
            new Vector2(AltarCx, AltarCy), new Vector2(AltarSize, AltarSize));
        ShopUIStyle.Skin(_altarGlow, UISkin.Refinery?.altarCore);   // 중앙 원 바탕

        _altarRing = ShopUIStyle.MakeImage(_altarGlow.transform, "Ring", HeatDefault);
        ShopUIStyle.Stretch(_altarRing.rectTransform);
        ShopUIStyle.Skin(_altarRing, UISkin.Refinery?.slotFrame, tint: HeatDefault);   // 중앙 원 테두리
        _altarRing.preserveAspect = true;

        // 응축 연출 층 — 결과 틀보다 <b>먼저</b> 만든다(형제 순서 = 그리기 순서라 섬광·빛살이 결과 뒤로 간다).
        _fxRoot = ShopUIStyle.MakeRect(_altarGlow.transform, "CondenseFx").GetComponent<RectTransform>();
        ShopUIStyle.Anchor(_fxRoot, Half, Half, Half, Vector2.zero, new Vector2(AltarSize * 4f, AltarSize * 4f));

        _revealRays = ShopUIStyle.MakeImage(_fxRoot, "Rays", new Color(1f, 0.84f, 0.47f, 0f));
        ShopUIStyle.Stretch(_revealRays.rectTransform);
        _revealRays.gameObject.SetActive(false);

        _revealFlash = ShopUIStyle.MakeImage(_fxRoot, "Flash", Color.clear);
        ShopUIStyle.Stretch(_revealFlash.rectTransform);

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

        _rarLine = ShopUIStyle.MakeText(_root, "RarLine", 16f, FontStyles.Bold,
            TextAlignmentOptions.Center, ShopUIStyle.TextDim);
        ShopUIStyle.Anchor(_rarLine.rectTransform, Half, Half, Half,
            new Vector2(AltarCx, -196f), new Vector2(560f, 24f));
    }

    /// <summary>우측 상태 컬럼 — 등급 확률 막대 하나(제단 높이). 피버 게이지는 걷었다(09-28).</summary>
    private void BuildSide()
    {
        // 크기는 아트 실치수(확률막대 바탕 609×255 @2x).
        _oddsBar = OddsBarView.Create(_root, Half, Half, Half,
            new Vector2(ColX, OddsY), new Vector2(310f, 150f));
    }

    /// <summary>
    /// 하단 = [비용] [돌리기] 한 줄. 완성본의 [재점화]·[첫 돌리기 무료]는 걷었다(09-28 무작위 뽑기로 단순화).
    /// </summary>
    private void BuildFooter()
    {
        var skin = UISkin.Refinery;

        // 하단 세 버튼은 완성본 「정제소 전체 이미지」에서 색 분할로 잰 자리다(창 좌상단 기준):
        //   좌 (106,653) 298×70 · 중 (428,653) 316×70 · 우 (764,653) 278×70 → 창 중심 기준으로 환산.
        // 아트는 Sprites 세트의 「버튼 우측/중앙」(글자 없는 파란 베벨, 가로 9-slice 200)이라
        // 폭은 자유롭고 높이 70에 맞춘다. 예전 @2x 세트(글자 구워짐)는 라벨과 겹쳐 두 번 읽혔다.
        _costPlateImg = MakeArtButton("CostPlate", First(skin?.costPlate), null,
            new Vector2(CostX, FooterY), new Vector2(298f, 70f), null);

        // 완성본 버튼 아트는 글자가 없는 빈 판이다 — 비용 숫자는 판 가운데에 놓는다.
        _costText = ShopUIStyle.MakeText(_costPlateImg.transform, "Cost", 18f, FontStyles.Bold,
            TextAlignmentOptions.Center, ShopUIStyle.TextPrimary);
        ShopUIStyle.Stretch(_costText.rectTransform, 10f);

        _spinBtnImg = MakeArtButton("SpinBtn", First(skin?.spinButton), "돌리기",
            new Vector2(SpinX, FooterY), new Vector2(316f, 70f), OnSpinClicked);

        // 안내·거절 사유. 완성본에 상설 문구는 없으므로 할 말이 있을 때만 뜬다.
        _hint = ShopUIStyle.MakeText(_root, "Hint", 16f, FontStyles.Normal,
            TextAlignmentOptions.Center, ShopUIStyle.TextDim);
        ShopUIStyle.Anchor(_hint.rectTransform, Half, Half, Half,
            new Vector2(AltarCx, -224f), new Vector2(640f, 20f));
        // 열자마자 무엇을 하는 곳인지 — 돌리면 지운다.
        _hint.text = IdleHint;
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

    private void BuildInfoPanel()
    {
        // 좌측 상주 판 — 완성본의 세로 패널(세부지표 3, 0.72 비율). 의뢰서 "좌 = 무대". 안의 글은 EnsureRuntimeLayout이 짓는다.
        // 좌표는 창 중심 기준: 좌상단 (50,232) 198×276 → (-436, 44). 이름 「EventPanel」은 구운 프리팹과 맞춘다
        // (돌발 배너를 걷기 전 이름 — 09-28).
        var eventPanel = ShopUIStyle.MakeImage(_root, "EventPanel", ShopUIStyle.BandFill);
        ShopUIStyle.Anchor(eventPanel.rectTransform, Half, Half, Half,
            new Vector2(-436f, 44f), new Vector2(198f, 276f));
        eventPanel.raycastTarget = false;
    }

    /// <summary>
    /// 디자이너 아트를 코드로 그린 박스 위에 얹는다. 스킨 미로드면 아무것도 안 하고 색 폴백을 유지한다
    /// (<see cref="UISkin.PreloadAsync"/>는 앱 부트에서 fire-and-forget — 런 중 여는 이 패널은 이미 로드됨).
    /// 위젯(확률 막대)은 각자 <see cref="UISkin.Refinery"/>를 참조하므로 여기서 다루지 않는다.
    /// </summary>
    private void ApplySkin()
    {
        var skin = UISkin.Refinery;
        if (skin == null) return;

        // 창 바탕 — 전면 일러스트(9-slice 아님). 아트가 자체 가장자리를 갖고 있어
        // 코드가 그린 청동 테두리는 어둡게 낮춘다(완성본엔 굵은 금테가 없다).
        //
        // ⚠️ 창(1170×828 = 1.41)과 「정제소 바탕@2x」(2089×1267 = 1.65)는 비율이 다르다.
        // 완성본의 배경은 별도 파일로 납품되지 않았고, 이 아트는 구판의 다른 그림이다.
        // 예전엔 _window에 Simple로 직접 입혀 세로로 1.17배 눌렸다(나침반 문양이 찌그러짐).
        // 그림은 9-slice로 못 늘리므로 <b>원본 비율을 지킨 채 창을 덮고 넘치는 쪽을 잘라낸다</b>.
        // _window는 모든 내용의 부모라 크기를 건드리면 안 되니, 마스크 + 배경 노드를 따로 둔다.
        ApplyCoverBackground(skin.windowFrame);
        if (_window.transform.parent != null &&
            _window.transform.parent.TryGetComponent<Image>(out var outer))
            outer.color = new Color(0.30f, 0.36f, 0.48f, 0.85f);

        // 좌측 상주 판(세부지표 3). 아트가 없으면 BandFill 단색 판으로 남는다.
        var eventPanel = _root != null ? _root.Find("EventPanel")?.GetComponent<Image>() : null;
        if (eventPanel != null) ShopUIStyle.Skin(eventPanel, skin.eventPanel);

        // 속성 노드 — 사전 채색된 육각 각인 한 장뿐. 뒤에 상자를 두지 않는다.
        for (int i = 0; i < _gems.Count; i++)
        {
            var glyph = skin.Glyph(i);
            if (glyph != null && _gems[i].jewel != null)
                ShopUIStyle.Skin(_gems[i].jewel, glyph);   // 이미 채색된 각인 — 색 곱하지 않음
        }
    }

    // ── 상태 ──

    /// <summary>
    /// 젬 링에서 한 속성만 밝힌다(null이면 모두 같은 밝기). 돌리는 동안 불이 돌고, 결과가 나오면 그 룬의 속성에 멎는다.
    /// 각인 아트는 이미 채색돼 있어 색으로는 못 알리므로 크기·밝기로 가른다.
    /// </summary>
    private void LightGem(string elementId)
    {
        for (int i = 0; i < _gems.Count; i++)
        {
            var j = _gems[i].jewel;
            if (j == null) continue;
            bool on = elementId == null || _gems[i].id == elementId;
            j.transform.localScale = Vector3.one * (elementId == null ? 1f : on ? 1.14f : 0.94f);
            var c = j.color;
            j.color = new Color(c.r, c.g, c.b, on ? 1f : 0.45f);
        }
    }

    /// <summary>
    /// 구운 프리팹 위에 런타임으로 얹는 것들 — 좌측 「나올 룬 / 나온 룬」 판, 결과 칸(둥근 판 + 룬 그림 자리),
    /// 걷은 기능의 요소 숨기기 · 하단 두 칸 · 확률 막대 자리. BuildUI는 구운 프리팹이면 돌지 않으므로 여기서 한 번 짓는다.
    /// </summary>
    private void EnsureRuntimeLayout()
    {
        foreach (var gone in RetiredNodes)
        {
            var t = _root != null ? _root.Find(gone) : null;
            if (t != null) t.gameObject.SetActive(false);
        }
        PlaceIfFound("CostPlate", CostX, FooterY, 298f, 70f);
        PlaceIfFound("SpinBtn",   SpinX, FooterY, 316f, 70f);
        // [돌리기] = 전 화면 공통 베벨(금). [원석 N]은 누르는 것이 아니라 표시판인데 돌리기와 똑같은 청색 버튼이라
        // 눌러야 할 것처럼 보였다 → 글래스 판 + 금 가는 선(09-28 UI 톤 통일).
        if (_spinBtnImg != null && UITheme.ButtonBevel != null)
        {
            UITheme.StyleButton(_spinBtnImg, UITheme.CtaTint);
            _themedSpin = true;
        }
        if (_costPlateImg != null) UITheme.StylePanel(_costPlateImg, UITheme.Band, UITheme.GoldLine, 12f);
        if (_costText != null) TMPOutlineHelper.ApplySoftShadow(_costText);
        if (_oddsBar != null)
            PlaceProportional((RectTransform)_oddsBar.transform, ColX, OddsY, 310f, 150f, WindowW, WindowH);
        if (_hint != null) _hint.text = IdleHint;   // 구운 글은 옛 안내(「속성 젬을 고르세요」)

        if (_resultBox != null)
        {
            _resultBox.sprite = UIProceduralSprites.RoundedRect(radius: 14f, feather: 2f);
            _resultBox.type   = Image.Type.Sliced;
        }
        // 구운 프리팹은 요소마다 <b>비율 앵커</b>(창 크기를 따라감)다 — 위치도 비율로 준다(PlaceProportional).
        // 절대 좌표를 주면 앵커 기준으로 밀려 결과 줄이 버튼 아래로 내려갔다(09-27 실측).
        const float ResultBox = 96f;   // 결과 틀 설계 크기(제단 135 × 0.711)
        if (_resultSym != null)
        {
            _resultSym.preserveAspect = true;
            PlaceProportional(_resultSym.rectTransform, 0f, 14f, 50f, 50f, ResultBox, ResultBox);   // 보석은 위
        }
        if (_resultAmt != null)
        {
            PlaceProportional(_resultAmt.rectTransform, 0f, -31f, ResultBox, 22f, ResultBox, ResultBox);   // 칸 수는 아래(바닥에서 6~28)
            TMPOutlineHelper.ApplySoftShadow(_resultAmt);   // 등급색 칸 위 — 등급색 글자면 묻혔다(09-28)
        }
        // 결과 줄은 아래 보석(어둠, 아래끝 -213)과 버튼(위끝 -239) 사이 — -196이면 보석 위에 겹쳤다(09-21 캡처부터).
        if (_rarLine != null)
            PlaceProportional(_rarLine.rectTransform, AltarCx, -226f, 560f, 24f, WindowW, WindowH);

        var panel = _root != null ? _root.Find("EventPanel") : null;
        if (panel == null || _infoName != null) return;

        // 창 맞춤(UIWindowFitter)은 이 패널이 켜질 때 이미 글자 크기를 키웠다 — 뒤에 만드는 글자는 같은 배율을 직접 곱한다.
        float k = _root is RectTransform win && win.rect.height > 1f ? win.rect.height / WindowH : 1f;
        const float PanelW = 198f, PanelH = 276f;   // 좌측 판 설계 크기

        _infoCaption = InfoText(panel, "InfoCaption", 16f * k, ShopUIStyle.TextDim, 113f, 22f, PanelW, PanelH);

        _infoGem = ShopUIStyle.MakeImage(panel, "InfoGem", Color.white);
        PlaceProportional(_infoGem.rectTransform, 0f, 52f, 70f, 84f, PanelW, PanelH);
        _infoGem.preserveAspect = true;
        _infoGem.raycastTarget  = false;

        _infoName = InfoText(panel, "InfoName", 19f * k, ShopUIStyle.TextPrimary, -11f, 26f, PanelW, PanelH);
        // 효과는 두 줄 넘게 올 수 있다(영웅·조건부 룬) — 세 줄 높이를 주고, 넘치면 13까지만 줄인다.
        _infoDesc = InfoText(panel, "InfoDesc", 16f * k, ShopUIStyle.TextPrimary, -62f, 66f, PanelW, PanelH);
        _infoDesc.textWrappingMode = TextWrappingModes.Normal;
        _infoDesc.enableAutoSizing = true;
        _infoDesc.fontSizeMin      = 13f * k;
        _infoDesc.fontSizeMax      = 16f * k;
        _infoFoot = InfoText(panel, "InfoFoot", 16f * k, ShopUIStyle.TextDim, -112f, 22f, PanelW, PanelH);
    }

    /// <summary>창 바로 아래 요소를 설계 좌표(창 중심 기준)로 다시 놓는다 — 구운 비율 앵커 그대로.</summary>
    private void PlaceIfFound(string name, float cx, float cy, float w, float h)
    {
        if (_root != null && _root.Find(name) is RectTransform rt)
            PlaceProportional(rt, cx, cy, w, h, WindowW, WindowH);
    }

    /// <summary>
    /// 부모 설계 크기(<paramref name="parentW"/>×<paramref name="parentH"/>) 기준 중심 좌표·크기를 <b>비율 앵커</b>로 준다 —
    /// 창 맞춤이 창 크기를 바꿔도 자리가 같이 따라간다(구운 프리팹과 같은 방식).
    /// </summary>
    private static void PlaceProportional(RectTransform rt, float cx, float cy, float w, float h, float parentW, float parentH)
    {
        rt.anchorMin = new Vector2(0.5f + (cx - w * 0.5f) / parentW, 0.5f + (cy - h * 0.5f) / parentH);
        rt.anchorMax = new Vector2(0.5f + (cx + w * 0.5f) / parentW, 0.5f + (cy + h * 0.5f) / parentH);
        rt.pivot            = Half;
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta        = Vector2.zero;
    }

    private static TMP_Text InfoText(Transform parent, string name, float size, Color color,
                                     float cy, float height, float parentW, float parentH)
    {
        var t = ShopUIStyle.MakeText(parent, name, size, FontStyles.Normal, TextAlignmentOptions.Top, color);
        if (t.enableAutoSizing) t.fontSizeMax = size;
        PlaceProportional(t.rectTransform, 0f, cy, parentW - 20f, height, parentW, parentH);
        t.raycastTarget = false;
        TMPOutlineHelper.ApplySoftShadow(t);
        return t;
    }

    /// <summary>
    /// 좌측 판 — 돌리기 전엔 무엇이 나오는 곳인지, 돌린 뒤엔 <b>나온 룬</b>(얼굴 · 이름 · 효과 · 칸 수 · 속성).
    /// 효과 글은 CSV 원문(카드·보관함과 같은 말)이다.
    /// </summary>
    private void RefreshInfo(RuntimeItemData rune = null)
    {
        if (_infoName == null) return;

        if (rune == null)
        {
            _infoCaption.text = "나올 룬";
            _infoGem.sprite   = RuneArt.GetArt(ItemRarity.Rare);
            _infoGem.enabled  = _infoGem.sprite != null;
            _infoName.text    = "무작위 룬 하나";
            _infoName.color   = ShopUIStyle.TextPrimary;
            _infoDesc.text    = "희귀 이상 · 효과와 속성,\n모양은 룬마다 다르다";
            _infoFoot.text    = "";
            return;
        }

        _infoCaption.text = "나온 룬";
        _infoGem.sprite   = RuneArt.ResolveRuneIcon(rune);
        _infoGem.enabled  = _infoGem.sprite != null;
        _infoName.text    = rune.displayName;
        _infoName.color   = ShopUIStyle.RarityGlow(rune.rarity);

        var sb = new System.Text.StringBuilder();
        foreach (var s in rune.effects)
        {
            if (s == null) continue;
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(!string.IsNullOrEmpty(s.description) ? s.description : EffectDescriptionFormatter.Describe(s).Label);
        }
        _infoDesc.text = sb.ToString();

        var e     = string.IsNullOrEmpty(rune.element) ? null : ElementDef.GetById(rune.element);
        int cells = RuneCardKit.CellCount(rune);
        _infoFoot.text = (cells > 0 ? $"{cells}칸" : "") + (e != null ? $" · {e.Name} 존" : "");
    }

    private void Refresh()
    {
        if (_svc == null) return;
        var (rare, epic, leg) = _svc.CurrentOdds();
        _oddsBar?.SetOdds(rare, epic, leg, false);

        // 해금 몫을 확률 표 옆에 밝힌다 — 상위 등급이 왜 잘 나오는지가 보여야 한다.
        // <b>대입</b>이어야 한다. Refresh는 한 번의 돌리기에 여러 번 불리므로(오픈·젬 선택·
        // 돌리기·이벤트) 누적(+=)하면 같은 문구가 계속 덧붙어 줄이 끝없이 길어진다.
        if (_rarLine != null && MemoryAltarService.IsUnlocked(MemoryAltarCatalog.RefineQuality))
        {
            const string mark = "정제 등급 상승 +";
            string body = _rarLine.text;
            int cut = body.IndexOf(mark, System.StringComparison.Ordinal);
            if (cut >= 0) body = body.Substring(0, cut).TrimEnd();
            _rarLine.text = body +
                $"   <color=#C99C4F>정제 등급 상승 +{RefineryService.QualityUnlockEpic * 100f:F0}%p</color>";
        }
        // 숫자만 있으면 무엇으로 치르는지 안 읽힌다 — 우상단 보유량(「원석 N」)과 같은 말로 적는다.
        _costText.text  = $"원석 {_svc.CurrentCost}";
        _costText.color = _svc.CanAfford ? ShopUIStyle.TextPrimary : ShopUIStyle.RejectRed;
        CurrencyCounter.Apply(_oreText, _svc.OreOwned, "원석 ");

        Tint(_spinBtnImg, _svc.CanAfford && !_busy);
        UIAffordGlow.Set(_spinBtnImg, _svc.CanAfford && !_busy);   // 돌릴 수 있을 때만 은은한 불(09-29)
    }

    /// <summary>글자가 구워진 버튼 아트는 색을 갈아끼울 수 없어, 밝기로 활성/비활성을 알린다.</summary>
    private void Tint(Image img, bool enabled)
    {
        if (img == null) return;
        if (_themedSpin && img == _spinBtnImg) { img.color = enabled ? UITheme.CtaTint : UITheme.CtaTintOff; return; }
        if (img.sprite != null) img.color = enabled ? Color.white : new Color(0.45f, 0.45f, 0.5f, 0.75f);
        else                    img.color = enabled ? ShopUIStyle.GoldPillBg : ShopUIStyle.BandFill;
    }

    // ── 돌리기 ──

    private void OnSpinClicked()
    {
        if (_busy || _svc == null) return;

        // 거절에는 반드시 사유가 붙는다 — 흐린 버튼만으로는 "고장난 버튼"으로 읽혔다.
        if (!_svc.CanAfford) { _hint.text = "원석이 부족합니다"; ShopUIStyle.PlaySfx("shop_reject"); return; }
        SpinAsync().Forget();
    }

    private async UniTaskVoid SpinAsync()
    {
        _busy = true;
        _resultBox.gameObject.SetActive(false);
        _rarLine.text = "";
        _hint.text    = "";
        Refresh();

        var outcome = _svc.Craft();
        if (!outcome.Success) { _hint.text = outcome.FailReason; _busy = false; Refresh(); return; }

        // 「정제 품질」 할인 조건 집계. RefineryService는 런 참조가 없어 호출부에서 센다.
        GameRunBootstrapper.Instance?.Run?.ReportRefineUse();

        await PlayRevealAsync(outcome);

        // 결과를 잠깐 보여 준 뒤 배치 화면으로 넘긴다. <b>잠금은 풀지 않는다</b> — 풀면 넘어가기까지의 900ms 동안
        // [돌리기]가 다시 눌려, 두 번째 굴림은 원석만 빠진 채 결과 연출을 못 보고 화면이 닫힌다.
        Refresh();
        await HandOffToGridAsync(outcome.Rune);
    }

    /// <summary>결과를 잠깐 보여준 뒤(≈0.9초) 정제소를 닫고 그 룬을 판에 올린다.</summary>
    private async UniTask HandOffToGridAsync(RuntimeItemData rune)
    {
        if (rune == null) return;
        try
        {
            var ct = this.GetCancellationTokenOnDestroy();
            await UniTask.Delay(900, ignoreTimeScale: true, cancellationToken: ct);
            await SlipToGridAsync(ct);   // 결과가 아래로 빨려 들어간 뒤 판이 열린다 — 어디로 가는지가 보이게
        }
        catch (OperationCanceledException) { return; }

        OpenGridForRune(rune);
    }

    /// <summary>
    /// 응축 3박자 — ① 로가 빛을 모으고(젬 링의 불이 돈다) ② 섬광과 함께 공개하고(나온 룬의 속성에 멎는다) ③ 결과가 안착한다.
    /// 등급이 높을수록 모으는 시간이 길고 섬광이 세며, 전설에는 빛살이 돈다(기획_정제소 §7).
    /// </summary>
    private async UniTask PlayRevealAsync(RefineryOutcome outcome)
    {
        string elem = outcome.Rune?.element;
        Color heat = ElementDef.IdColor(elem, HeatDefault);
        Color rc   = ShopUIStyle.RarityGlow(outcome.Rarity);
        int   tier = RarityTier(outcome.Rarity);
        var ct = this.GetCancellationTokenOnDestroy();

        EnsureCondenseFx();
        ShopUIStyle.PlaySfx("refine_spin");
        try { await GatherAsync(HeatDefault, GatherBase + 0.26f * tier, ct); }   // 아직 속성을 모른다 — 로의 불빛으로
        catch (OperationCanceledException) { return; }
        LightGem(elem);

        FlashAsync(rc, tier).Forget();   // ② 공개 — 등급색 섬광(전설은 오버로드 + 빛살)

        // 결과 룬
        _resultBox.gameObject.SetActive(true);
        _resultBox.color = new Color(rc.r, rc.g, rc.b, 0.28f);
        // 결과 얼굴 = 그 룬이 보관함·판에서 쓸 얼굴. 아래 줄은 칸 수(판에서 차지할 크기).
        var icon = RuneArt.ResolveRuneIcon(outcome.Rune);
        _resultSym.sprite = icon;
        _resultSym.color  = icon != null ? Color.white : heat;
        int cells = RuneCardKit.CellCount(outcome.Rune);
        _resultAmt.text  = cells > 0 ? $"{cells}칸" : "";
        _resultAmt.color = ShopUIStyle.TextPrimary;   // 등급은 칸 색 · 결과 줄이 말한다

        _rarLine.color = rc;
        _rarLine.text  = $"{RarLabel(outcome.Rarity)} · {outcome.Rune?.displayName}";
        if (_altarRing != null) _altarRing.color = heat;
        RefreshInfo(outcome.Rune);

        Managers.Sound?.PlayEvent(SoundEvent.ItemPickup);
        ShopUIStyle.PlaySfx(tier >= 3 ? "refine_legend" : "refine_reveal");

        // ③ 안착 — 결과가 크게 튀어나와 제자리로. 등급이 높을수록 크게 튄다.
        try { await PopResultAsync(tier, ct); }
        catch (OperationCanceledException) { }
    }

    // ── 응축 연출 (표시층 전용 — 결과·등급은 RefineryService가 이미 확정했다) ──

    /// <summary>① 모음 — 불티가 돌며 제단으로 빨려들고, 링이 점점 빠르게 달아오르며, 젬 링의 불이 돌다 느려진다.</summary>
    private async UniTask GatherAsync(Color heat, float dur, CancellationToken ct)
    {
        if (_motes != null)
            for (int i = 0; i < _motes.Length; i++)
                if (_motes[i] != null) _motes[i].gameObject.SetActive(true);

        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / dur);
            if (_motes != null)
            {
                for (int i = 0; i < _motes.Length; i++)
                {
                    if (_motes[i] == null) continue;
                    float ang = i / (float)_motes.Length * Mathf.PI * 2f + k * 3.2f;   // 돌면서 빨려든다
                    float r   = Mathf.Lerp(230f, 6f, k * k);
                    _motes[i].rectTransform.anchoredPosition = new Vector2(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r);
                    _motes[i].color = new Color(heat.r, heat.g, heat.b, Mathf.Sin(k * Mathf.PI) * 0.9f);
                }
            }
            if (_altarRing != null)
            {
                // 달아오르는 건 링이다 — 원 바탕은 아트라 색을 건드리면 그림이 물든다.
                float p = 0.45f + 0.55f * Mathf.PingPong(t * (3f + 6f * k), 1f);
                _altarRing.color = new Color(heat.r * p, heat.g * p, heat.b * p, 1f);
            }
            // 젬 링의 불이 돈다 — 빠르게 시작해 끝에 느려진다(무작위 뽑기의 손맛). 멎는 자리는 PlayRevealAsync가 정한다.
            if (_gems.Count > 0) LightGem(_gems[(int)(k * (2f - k) * 14f) % _gems.Count].id);
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }

        if (_motes != null)
            for (int i = 0; i < _motes.Length; i++)
                if (_motes[i] != null) _motes[i].gameObject.SetActive(false);
        if (_altarRing != null) _altarRing.color = heat;
    }

    /// <summary>② 공개 — 등급색 섬광. 전설은 더 세고 길며 빛살이 함께 돈다.</summary>
    private async UniTaskVoid FlashAsync(Color rc, int tier)
    {
        if (_revealFlash == null) return;
        float dur  = 0.32f + 0.12f * tier;
        float peak = 0.45f + 0.18f * tier;
        bool  rays = tier >= 3 && _revealRays != null;
        if (rays) _revealRays.gameObject.SetActive(true);

        try
        {
            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / dur);
                _revealFlash.color = new Color(rc.r, rc.g, rc.b, peak * (1f - k));
                if (rays)
                {
                    _revealRays.color = new Color(rc.r, rc.g, rc.b, 0.75f * Mathf.Sin(k * Mathf.PI));
                    _revealRays.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -70f * k);
                }
                await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
            }
        }
        catch (OperationCanceledException) { return; }

        _revealFlash.color = Color.clear;
        if (_revealRays != null) _revealRays.gameObject.SetActive(false);
    }

    /// <summary>③ 안착 — 결과 틀이 크게 튀어나와 제자리로 줄어든다.</summary>
    private async UniTask PopResultAsync(int tier, CancellationToken ct)
    {
        if (_resultBox == null) return;
        var rt = _resultBox.rectTransform;
        float dur  = 0.26f + 0.06f * tier;
        float from = 1.9f + 0.4f * tier;
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / dur);
            float s = Mathf.Lerp(from, 1f, 1f - (1f - k) * (1f - k));
            rt.localScale = new Vector3(s, s, 1f);
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        rt.localScale = Vector3.one;
    }

    /// <summary>결과가 판 쪽(아래)으로 빨려 들어간다 — 룬판이 열리기 직전의 손짓.</summary>
    private async UniTask SlipToGridAsync(CancellationToken ct)
    {
        if (_resultBox == null || !_resultBox.gameObject.activeSelf) return;
        var rt = _resultBox.rectTransform;
        Vector2 from = rt.anchoredPosition;
        float t = 0f;
        while (t < SlipDur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / SlipDur);
            rt.anchoredPosition = from + new Vector2(0f, -200f * k * k);
            float s = Mathf.Lerp(1f, 0.4f, k);
            rt.localScale = new Vector3(s, s, 1f);
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        rt.anchoredPosition = from;      // 다음 돌리기를 위해 제자리로(가린 채 되돌린다)
        rt.localScale       = Vector3.one;
        _resultBox.gameObject.SetActive(false);
    }

    private static int RarityTier(ItemRarity r) => r switch
    {
        ItemRarity.Legendary => 3,
        ItemRarity.Epic      => 2,
        ItemRarity.Rare      => 1,
        _                    => 0,
    };

    /// <summary>코드로 그린 빛(불티·섬광·빛살)은 프리팹에 저장되지 않는다 — 처음 돌릴 때 만든다.</summary>
    private void EnsureCondenseFx()
    {
        if (_motes != null || _fxRoot == null) return;

        var dot = UI_RuneSelectPopup.SoftDot;
        if (_revealFlash != null) _revealFlash.sprite = dot;
        if (_revealRays  != null) _revealRays.sprite  = UI_RuneSelectPopup.Rays;

        _motes = new Image[MoteCount];
        for (int i = 0; i < MoteCount; i++)
        {
            var img = ShopUIStyle.MakeImage(_fxRoot, "Mote", Color.clear);
            img.sprite = dot;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = Half;
            rt.sizeDelta = new Vector2(18f, 18f);
            img.gameObject.SetActive(false);
            _motes[i] = img;
        }
    }

    // ── Helpers ──

    // 등급 표기는 한 곳(RewardPresentation)에서 — 획득 카드·룬판과 같은 기호(◇ Rare · ◆ Epic · ★ Legendary).
    private static string RarLabel(ItemRarity r) => RewardPresentation.RarityLabel(r);

    private static string Pct(float f) => Mathf.RoundToInt(f * 100f) + "%";
    private static string HexOf(ItemRarity r) => ColorUtility.ToHtmlStringRGB(ShopUIStyle.RarityGlow(r));

    private static void AddClick(GameObject go, Action onClick)
    {
        var btn = go.GetComponent<Button>() ?? go.AddComponent<Button>();
        ShopUIStyle.ApplyButtonColors(btn);
        btn.onClick.AddListener(() => onClick?.Invoke());
    }
}
