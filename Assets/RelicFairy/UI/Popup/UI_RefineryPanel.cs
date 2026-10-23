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
/// 이 패널은 그 표현: 열면 전체 화면 제단(v4 10-14 — <c>UI_RefineryPanel.Altar.cs</c>) → [정제하기] → 같은 하늘에서 「아발론의 밤」 기원 연출
/// (시간표 <c>UI_RefineryPanel.Gacha.cs</c>, 무대 <c>UI_RefineryPanel.Wish.cs</c>: 떠오름 · 낙성 · 착지 · 화면 가득 공개) → 끝 상태에서 [룬판에 놓기] · [다시 정제 · 원석 N].
/// 구운 1170×828 창은 v4부터 숨긴다 — 값 · 버튼 처리만 그대로 쓴다.
/// 「두 장 중 하나」(기억의 제단)면 두 장을 나란히 공개한 뒤 룬 선택 화면으로 넘긴다.
/// 09-28 사용자 「간단하게 줄여서 룬을 랜덤으로 뽑는 시스템으로」 — 속성 선택 · 피버 · 돌발 이벤트 · 재점화 · 방 특전을 걷었다.
/// 구운 프리팹에 남은 그 요소들은 <see cref="EnsureRuntimeLayout"/>가 숨긴다.
/// </summary>
public sealed partial class UI_RefineryPanel : UI_Popup
{
    public override bool BlocksGameplay => true;
    // 굴린 뒤(_busy)엔 ESC로 닫지 않는다 — 공개 · 넘김 중에 창이 파괴되면 넘김이 취소돼 원석만 빠지고 룬이 사라졌다(10-11).
    // 배경 클릭 닫기와 같은 가드. ESC는 그래도 소비된다(일시정지가 열리지 않는다).
    public override bool CloseOnEscape  => !_busy;

    /// <summary>패널이 사라졌음을 알린다 — 방 컨트롤러의 중복 오픈 가드 해제용(상점/재련소와 동일 규약).</summary>
    public event Action OnClosed;

    // 창 크기는 배경 아트(정제소 바탕@2x 2089×1267)의 실치수 = 1044×634.
    // 요소 크기는 전부 각 아트의 실치수(@2x ÷ 2), 위치는 완성본 전체 사진(890×538)에서
    // 창 중심 기준 오프셋을 환산(가로 ×1.173 / 세로 ×1.178)한 값이다.
    // 완성본 목업(정제소 전체 이미지.png 1171×829)을 실측해 옮긴 값. 창 비율을 목업과 맞춘다
    // — 예전 1044×634(1.65)는 가로로 납작해서, 세로로 긴 육각 링과 사이드 패널이 들어가지 않았다.
    private const float WindowW   = 1170f;
    private const float WindowH   = 828f;
    // v3(10-14 설계 1-1 ①) — 제단을 크게 가운데에: 결과 틀 135 → 156 · 링 152 → 175 · 육각 +10%. 결과는 이제 전체 화면에서
    // 공개하므로 창 안 결과 줄 · 결과 칸 자리가 비었다 — 링을 8 올리고 버튼을 16 내려 그 빈 곳을 제단이 쓴다.
    private const float AltarSize = 156f;   // 중앙 최종룬 482@1x — 정사각 결과 틀
    private const float RingR     = 175f;   // 육각 링 반지름(목업 실측 152의 1.15배)
    private const float AltarCx   = 0f;     // 목업은 링이 창 정중앙에 있다
    private const float AltarCy   = 8f;
    // 목업 아트(빨간룬.png 283×339)를 전체샷에 템플릿 매칭한 실측(상관 0.85) 96×115의 1.1배 — 비율 0.835 그대로.
    // 예전 값 91×103은 눈으로 잰 것이라 12% 작았고 비율도 0.883으로 원본(0.835)과 달라 납작했다.
    private const float HexW      = 106f;
    private const float HexH      = 127f;
    private const float ColX      = 390f;   // 우측 상태 컬럼 중심
    private const float OddsY     = AltarCy; // 확률 막대 — 피버가 비운 자리로 올려 제단 높이에(09-28)
    private const float FooterY   = -290f;
    // 하단은 [정제하기  원석 N] 한 버튼 — 따로 있던 [원석 N] 표시판이 버튼처럼 보여 둘로 읽혔다(09-29 사용자).
    private const float SpinX     =    0f;
    // v3 — 큰 [정제하기 · 원석 N](설계 1-1 ①). 평판 + 1px 금(v2) 그대로, 칸만 380×58 → 440×72(1080 화면 약 540×88).
    private const float SpinW     =  440f;
    private const float SpinH     =   72f;
    private const float ButtonFont = 24f;   // 버튼 글 · 비용 숫자
    private const float CostIcon  =   22f;  // 원석 아이콘
    private const float CostIconX =  124f;  // 버튼 안 오른쪽(버튼 중심 기준)
    private const float CostNumX  =  174f;
    // 좌측 「나올 룬」 판 — 1080 화면 300×420 · 그림 140(C §2) = 창 맞춤(×1.23) 전 설계 245×343 · 그림 114. 왼쪽 끝은 예전 그대로.
    private const float InfoW     =  245f;
    private const float InfoH     =  343f;
    private const float InfoCx    = -535f + InfoW * 0.5f;
    private const float InfoCy    =   44f;
    private const float InfoArt   =  114f;
    private const float MinFont   =   16f;  // 글자 최소(1080 화면 px — 설계 v3 3장 CHECK)
    private const float HintY     = -350f;  // 안내 · 거절 사유는 버튼 아래(버튼 아래끝 -326)
    private string IdleHint => _svc != null && _svc.TwoCards
        ? "원석으로 정제하면 룬 두 장 중 하나를 고른다"
        : "원석으로 정제하면 룬 하나가 나온다";

    /// <summary>육각 링 각도 — <see cref="ElementDef.Order"/> 순(불=상단, 얼음=좌상, 전기=우상, 풀=좌하, 빛=우하, 어둠=하단).</summary>
    private static readonly float[] GemAngles = { 90f, 150f, 30f, 210f, 330f, 270f };

    /// <summary>걷은 기능(09-28)의 구운 요소 — 피버 · 돌발 배너 · 화살표 · 예약 표시 · 재점화 · 방 특전.</summary>
    private static readonly string[] RetiredNodes =
        { "FeverGauge", "EventBanner", "EvPointer", "Reserved", "ReforgeBtn", "FreeSpin" };

    /// <summary>모든 요소를 창 중심 기준 오프셋으로 배치한다 — 완성본 좌표를 그대로 옮기기 위해.</summary>
    private static readonly Vector2 Half = new(0.5f, 0.5f);

    private static readonly Color HeatDefault = new(0.92f, 0.64f, 0.29f, 1f);
    // 청 수정 동굴 바탕을 인디고로 누른다 — 룬 획득 화면과 같은 값(톤 통일 ④, 09-29).
    private static readonly Color CaveTint    = new(0.66f, 0.62f, 0.80f, 1f);
    // 평판 버튼 바탕 — 동굴 그림 위에서도 글이 읽히게 유리보다 짙게.
    private static readonly Color ButtonFill  = new(0.075f, 0.068f, 0.095f, 0.88f);

    private RefineryService _svc;
    private bool _busy;
    private bool _built;

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

    // ── 응축 연출 층 (기획_정제소 §7 · 구현설계 R5) ── v3부터 연출은 전체 화면(UI_RefineryPanel.Wish.cs)이 맡는다 — 창 안 층은 꺼 둔다.
    private const float SlipDur    = 0.22f;   // 결과가 판으로 넘어가며 빨려드는 시간

    [SerializeField] private RectTransform _fxRoot;       // 제단 위 연출 층(결과 틀보다 뒤)
    [SerializeField] private Image         _revealFlash;  // 착지 뒷빛(등급색)
    [SerializeField] private Image         _revealRays;   // 전설 전용 빛살

    // ── 좌측 「나올 룬」 판 (09-27 · 09-28) ── 프리팹이 구워져 있어 런타임에 짓는다. 나온 룬은 v3부터 전체 화면 공개가 보여 준다.
    private Image    _infoGem;
    private TMP_Text _infoCaption, _infoName, _infoDesc, _infoFoot;
    private TMP_Text _spinLabel;   // [정제하기] 글

    // ── 하단 평판 버튼 (10-11 v2) ──
    private TMP_Text _spinCost;    // 버튼 안 오른쪽 비용 숫자
    private Image    _spinIcon;    // 그 앞 원석 아이콘
    private Image[]  _spinLine;    // 1px 네 변
    private RuntimeItemData _pendingRune;   // 끝 상태의 룬 — 이미 보관함에 있고, [룬판에 놓기]면 판을 연다

    // ── Lifecycle ──

    public override void Init()
    {
        base.Init();
        ShopUIStyle.PlaySfx(SoundEvent.UiOpen);
        _svc = GameRunBootstrapper.Instance?.Run?.Refinery;
        BuildUI();
        EnsureRuntimeLayout();
        HideBakedWindow();
        EnsureWish();   // 전체 화면 무대 = 제단 화면(v4) — 열 때 짓는다
        ApplyIdleLook();
    }

    protected override void OnDestroy()
    {
        StopAmbient();
        base.OnDestroy();
        OnClosed?.Invoke();
    }

    /// <summary>닫기 버튼 · 막 클릭 · ESC — 닫기 소리. 다른 화면으로 넘기는 길(룬판 · 룬 선택)은 base.ClosePopupUI를 직접 불러 소리 없이 닫는다.</summary>
    public override void ClosePopupUI()
    {
        ShopUIStyle.PlaySfx(SoundEvent.UiClose);
        base.ClosePopupUI();
    }

    /// <summary>
    /// 정제한 룬을 들고 배치 화면을 연다 — 정제소는 닫고 그 룬을 판에 올린다.
    /// 끝 상태에서 [룬판에 놓기]를 누르면(HandOffToGridAsync) 부른다. 상점 구매와 같은 흐름.
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
        bg.color = CaveTint;

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

        // 완성본: 중앙 원을 감싸는 육각 링(각도는 GemAngles).
        for (int i = 0; i < n; i++)
        {
            string id = order[i];
            Color col = ElementDef.IdColor(id, ShopUIStyle.TextDim);

            // 완성본에는 육각 뒤에 카드 상자가 없다 — 투명 컨테이너(장식이라 클릭을 받지 않는다).
            var slot = ShopUIStyle.MakeImage(_root, $"Gem_{id}", Color.clear);
            var rt = slot.rectTransform;
            float a = GemAngles[i < GemAngles.Length ? i : 0] * Mathf.Deg2Rad;
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
        // 비용은 [돌리기] 글에 함께 적는다(Refresh) — 따로 표시판을 두지 않는다.
        _spinBtnImg = MakeArtButton("SpinBtn", First(skin?.spinButton), "정제하기",
            new Vector2(SpinX, FooterY), new Vector2(SpinW, 70f), OnSpinClicked);

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
    /// 구운 프리팹 위에 런타임으로 얹는 것들 — 좌측 「나올 룬」 판, 하단 평판 [정제하기], 걷은 기능의 요소 숨기기 ·
    /// 확률 막대 자리 · 제단 크기 · 대기 화면 지연 요소. BuildUI는 구운 프리팹이면 돌지 않으므로 여기서 짓는다.
    /// 10-11 v2(비교 리서치 C §2)의 대기 화면 위계 — 보석 0.45 · 「나올 룬」 판 300×420 · 평판 버튼 · 결과 틀 후광 0 · 확률판 1px.
    /// v3(10-14) — 제단을 키우고 버튼을 [정제하기 · 원석 N]으로 크게. 창 안 결과 줄 · 결과 칸은 쓰지 않는다(공개는 전체 화면).
    /// </summary>
    private void EnsureRuntimeLayout()
    {
        foreach (var gone in RetiredNodes)
        {
            var t = _root != null ? _root.Find(gone) : null;
            if (t != null) t.gameObject.SetActive(false);
        }
        // 구운 프리팹의 [원석 N] 표시판은 숨긴다 — 비용은 [정제하기] 안에 있다.
        if (_costPlateImg != null) _costPlateImg.gameObject.SetActive(false);

        // 창 맞춤(UIWindowFitter)은 이 패널이 켜질 때 이미 글자 크기를 키웠다 — 여기서 새로 주는 글자 크기는 같은 배율을 직접 곱한다.
        float k = FitK();

        PlaceAltarRing();
        PlaceIfFound("SpinBtn", SpinX, FooterY, SpinW, SpinH);
        if (_spinBtnImg != null)
        {
            _spinLabel = _spinBtnImg.transform.Find("Label")?.GetComponent<TMP_Text>();
            // [정제하기] = 평판 + 1px 금 + 버튼 안 오른쪽 원석 아이콘 · 숫자. 베벨 아트 + 「원석 12」 글은 금 강조가 버튼 · 비용 둘로 갈렸다.
            _spinLine = StyleFlatButton(_spinBtnImg);
            if (_spinLabel != null)
            {
                UITheme.StyleThinText(_spinLabel, UIWeight.Medium, ButtonFont * k, UITheme.Thin.Gold);
                _spinLabel.textWrappingMode = TextWrappingModes.NoWrap;
                PlaceProportional(_spinLabel.rectTransform, -60f, 0f, 260f, SpinH, SpinW, SpinH);
            }
            EnsureSpinCost(k);
        }

        if (_oddsBar != null)
        {
            PlaceProportional((RectTransform)_oddsBar.transform, ColX, OddsY, 310f, 150f, WindowW, WindowH);
            _oddsBar.LayoutScale = k;   // 처음 SetOdds(Refresh)에서 지을 때 글자 · 간격을 창 배율로
        }

        if (_hint != null)
        {
            UITheme.StyleThinText(_hint, UIWeight.Regular, 17f * k, UITheme.Thin.InkA(0.62f), shadow: true);
            PlaceProportional(_hint.rectTransform, AltarCx, HintY, 640f, 24f, WindowW, WindowH);
            _hint.text = IdleHint;   // 구운 글은 옛 안내(「속성 젬을 고르세요」)
        }

        // 제목은 유물 콘셉트 DNF 한 줄 그대로 — 가짜 굵게만 뺀다(v2). 원석 보유는 Noto 500.
        var title = _root != null ? _root.Find("TitleBar/Title") : null;
        if (title != null && title.TryGetComponent<TMP_Text>(out var titleText)) titleText.fontStyle = FontStyles.Normal;
        if (_oreText != null) UITheme.StyleThinText(_oreText, UIWeight.Medium, 20f * k, UITheme.Thin.Ink, shadow: true);

        // 창 안 결과 칸 · 결과 줄은 v3부터 쓰지 않는다 — 나온 룬은 전체 화면이 크게 공개한다(UI_RefineryPanel.Wish.cs).
        if (_resultBox != null) { _resultBox.color = Color.clear; _resultBox.gameObject.SetActive(false); }
        if (_resultAmt != null) _resultAmt.text = "";
        if (_rarLine   != null) { _rarLine.text = ""; _rarLine.gameObject.SetActive(false); }

        EnsureInfoPanel(k);
        EnsureIdleParts();
    }

    /// <summary>
    /// 제단(결과 틀)과 육각 링을 v3 크기로 다시 놓는다 — 구운 프리팹의 비율 앵커 그대로(보석 그림은 칸 비율 앵커라 같이 커진다).
    /// 각 보석 자리는 그 속성의 <see cref="ElementDef.Order"/> 순번 각도(빌더와 같은 표).
    /// </summary>
    private void PlaceAltarRing()
    {
        PlaceIfFound("Altar", AltarCx, AltarCy, AltarSize, AltarSize);
        var order = ElementDef.Order;
        for (int i = 0; i < _gems.Count; i++)
        {
            int idx = -1;
            for (int j = 0; j < order.Count; j++) if (order[j] == _gems[i].id) { idx = j; break; }
            if (idx < 0 || _gems[i].go == null) continue;
            float a = GemAngles[idx < GemAngles.Length ? idx : 0] * Mathf.Deg2Rad;
            PlaceProportional((RectTransform)_gems[i].go.transform,
                AltarCx + RingR * Mathf.Cos(a), AltarCy + RingR * Mathf.Sin(a), HexW + 10f, HexH + 10f, WindowW, WindowH);
        }
    }

    /// <summary>창 맞춤 배율 — 창(Fill)의 지금 높이 ÷ 설계 높이. 런타임에 새로 주는 글자 크기 · 점 앵커 좌표에 곱한다.</summary>
    private float FitK() => _root is RectTransform win && win.rect.height > 1f ? win.rect.height / WindowH : 1f;

    /// <summary>
    /// 좌측 「나올 룬 / 나온 룬」 판 — 1080 화면 300×420 · 그림 140(C §2 — 예전 242×338 · 그림 80, 「얻을 것」이 1순위여야 한다).
    /// 판 테 1px 금 30%. 안의 글은 Noto(이름 500 · 효과 400). 구운 프리팹엔 판만 있어 글 · 그림은 여기서 짓는다(이름으로 찾고 없으면 만든다).
    /// </summary>
    private void EnsureInfoPanel(float k)
    {
        var panel = _root != null ? _root.Find("EventPanel") as RectTransform : null;
        if (panel == null) return;
        PlaceProportional(panel, InfoCx, InfoCy, InfoW, InfoH, WindowW, WindowH);
        if (panel.Find("ThinFrame") == null)
            UIThinBox.Create(panel, "ThinFrame", Vector2.zero, Vector2.one, 0f, UITheme.Thin.LineGold, Color.clear, Color.clear);
        if (_infoName != null) return;

        // 글자 최소 16(v3 3장) — 캡션 · 아랫줄 15 → 16, 효과 자동 크기 하한 15 → 16.
        _infoCaption = InfoText(panel, "InfoCaption", UIWeight.Medium, MinFont * k, UITheme.Thin.InkA(0.6f), 147.5f, 20f);

        var gemT = panel.Find("InfoGem");
        _infoGem = gemT != null ? gemT.GetComponent<Image>() : null;
        if (_infoGem == null) _infoGem = ShopUIStyle.MakeImage(panel, "InfoGem", Color.white);
        PlaceProportional(_infoGem.rectTransform, 0f, 70.5f, InfoArt, InfoArt, InfoW, InfoH);
        _infoGem.preserveAspect = true;
        _infoGem.raycastTarget  = false;

        _infoName = InfoText(panel, "InfoName", UIWeight.Medium, 20f * k, UITheme.Thin.Ink, -10.5f, 28f);
        // 효과는 두 줄 넘게 올 수 있다 — 네 줄 높이를 주고, 넘치면 16까지만 줄인다.
        _infoDesc = InfoText(panel, "InfoDesc", UIWeight.Regular, 17f * k, UITheme.Thin.InkA(0.85f), -72.5f, 84f);
        _infoDesc.textWrappingMode = TextWrappingModes.Normal;
        _infoDesc.enableAutoSizing = true;
        _infoDesc.fontSizeMin      = MinFont * k;
        _infoDesc.fontSizeMax      = 17f * k;
        _infoFoot = InfoText(panel, "InfoFoot", UIWeight.Regular, MinFont * k, UITheme.Thin.InkA(0.6f), -133.5f, 22f);
    }

    /// <summary>
    /// [돌리기] 칸 안 오른쪽 — 원석 아이콘 + 비용 숫자(C §2 「비용 아이콘 + 숫자가 버튼 안」). 아이콘은 HUD 원석 칸과 같은 그림.
    /// 아이콘이 없으면 숫자 칸이 「원석 12」 글로 대신한다(<see cref="RefreshSpinButton"/>).
    /// </summary>
    private void EnsureSpinCost(float k)
    {
        var host = _spinBtnImg.transform;
        if (_spinIcon == null)
        {
            var f = host.Find("CostIcon");
            _spinIcon = f != null ? f.GetComponent<Image>() : null;
            if (_spinIcon == null) _spinIcon = ShopUIStyle.MakeImage(host, "CostIcon", Color.white);
        }
        if (_spinIcon.sprite == null) _spinIcon.sprite = OreIconSprite();   // 코드가 붙인 그림 — 구운 프리팹엔 없다
        _spinIcon.enabled        = _spinIcon.sprite != null;
        _spinIcon.preserveAspect = true;
        _spinIcon.raycastTarget  = false;
        PlaceProportional(_spinIcon.rectTransform, CostIconX, 0f, CostIcon, CostIcon, SpinW, SpinH);

        if (_spinCost == null)
        {
            var f = host.Find("CostNum");
            _spinCost = f != null ? f.GetComponent<TMP_Text>() : null;
            if (_spinCost == null)
                _spinCost = ShopUIStyle.MakeText(host, "CostNum", ButtonFont * k, FontStyles.Normal,
                                                 TextAlignmentOptions.MidlineLeft, UITheme.Thin.Ink);
        }
        UITheme.StyleThinText(_spinCost, UIWeight.Regular, ButtonFont * k, UITheme.Thin.Ink);
        _spinCost.alignment        = TextAlignmentOptions.MidlineLeft;
        _spinCost.textWrappingMode = TextWrappingModes.NoWrap;
        _spinCost.raycastTarget    = false;
        if (_spinIcon.enabled) PlaceProportional(_spinCost.rectTransform, CostNumX, 0f, 72f, SpinH, SpinW, SpinH);
        else                   PlaceProportional(_spinCost.rectTransform, CostNumX - 20f, 0f, 112f, SpinH, SpinW, SpinH);
    }

    /// <summary>원석 아이콘 — 정제소 스킨 칸이 비어 있어 재련소 스킨의 같은 그림(HUD 원석 칸과 동일)을 쓴다.</summary>
    private static Sprite OreIconSprite()
    {
        var own = UISkin.Refinery != null ? UISkin.Refinery.oreIcon : null;
        if (own != null) return own;
        return UISkin.Crucible != null ? UISkin.Crucible.OreIcon : null;
    }

    /// <summary>평판 + 1px 선(10-11 v2) — 베벨 아트를 걷고 짙은 판 한 장에 네 변 1px. 선 색은 상태마다 <see cref="SetLine"/>로 칠한다.</summary>
    private static Image[] StyleFlatButton(Image img)
    {
        img.sprite = null;
        img.type   = Image.Type.Simple;
        img.color  = ButtonFill;
        var box = img.transform.Find("Line") as RectTransform;
        if (box == null)
            box = UIThinBox.Create(img.rectTransform, "Line", Vector2.zero, Vector2.one, 0f, Color.white, Color.clear, Color.clear);
        var lines = box.GetComponentsInChildren<Image>(true);
        for (int i = 0; i < lines.Length; i++) lines[i].raycastTarget = false;
        return lines;
    }

    private static void SetLine(Image[] lines, Color c)
    {
        if (lines == null) return;
        for (int i = 0; i < lines.Length; i++) if (lines[i] != null) lines[i].color = c;
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

    /// <summary>좌측 판 글 한 줄 — 이름으로 찾고 없으면 만든다. 톤 v2(Noto · 가짜 굵게 없음 · 부드러운 그림자).</summary>
    private static TMP_Text InfoText(RectTransform parent, string name, UIWeight weight, float size, Color color,
                                     float cy, float height)
    {
        var found = parent.Find(name);
        var t = found != null ? found.GetComponent<TMP_Text>() : null;
        if (t == null) t = ShopUIStyle.MakeText(parent, name, size, FontStyles.Normal, TextAlignmentOptions.Top, color);
        UITheme.StyleThinText(t, weight, size, color, shadow: true);
        t.alignment = TextAlignmentOptions.Top;
        PlaceProportional(t.rectTransform, 0f, cy, InfoW - 28f, height, InfoW, InfoH);
        t.raycastTarget = false;
        return t;
    }

    /// <summary>
    /// 좌측 판 — 무엇이 나오는 곳인지(「나올 룬」). 나온 룬(얼굴 · 이름 · 효과 · 칸 수 · 속성)은 v3부터 전체 화면 공개가 보여 준다
    /// (<see cref="RuneEffectText"/> · <see cref="RuneFootText"/> — 예전 이 판의 「나온 룬」 글과 같은 말).
    /// </summary>
    private void RefreshInfo()
    {
        if (_infoName == null) return;

        _infoCaption.text = "나올 룬";
        _infoGem.sprite   = RuneArt.GetArt(ItemRarity.Rare);
        _infoGem.enabled  = _infoGem.sprite != null;
        _infoName.text    = _svc != null && _svc.TwoCards ? "무작위 룬 두 장 중 하나" : "무작위 룬 하나";
        _infoName.color   = UITheme.Thin.Ink;
        // 등급 이름은 영문(Rare · Epic · Legend — 10-10 용어집, 확률판과 같은 말)
        _infoDesc.text    = "Rare 이상 · 효과와 속성,\n모양은 룬마다 다르다";
        _infoFoot.text    = "";
    }

    /// <summary>나온 룬의 효과 글 — CSV 원문(카드 · 보관함과 같은 말), 효과마다 한 줄.</summary>
    private static string RuneEffectText(RuntimeItemData rune)
    {
        if (rune?.effects == null) return string.Empty;
        var sb = new System.Text.StringBuilder();
        foreach (var s in rune.effects)
        {
            if (s == null) continue;
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(!string.IsNullOrEmpty(s.description) ? s.description : EffectDescriptionFormatter.Describe(s).Label);
        }
        return sb.ToString();
    }

    /// <summary>나온 룬의 칸 수 · 속성 — 「3칸 · 불 존」.</summary>
    private static string RuneFootText(RuntimeItemData rune)
    {
        if (rune == null) return string.Empty;
        var e     = string.IsNullOrEmpty(rune.element) ? null : ElementDef.GetById(rune.element);
        int cells = RuneCardKit.CellCount(rune);
        string a  = cells > 0 ? $"{cells}칸" : "";
        string b  = e != null ? $"{e.Name} 존" : "";
        return a.Length > 0 && b.Length > 0 ? a + " · " + b : a + b;
    }

    private void Refresh()
    {
        if (_svc == null) return;
        var (rare, epic, leg) = _svc.CurrentOdds();
        // 해금 몫은 확률판 머리 오른쪽(무엇이 등급을 올렸나 — 룬 선택 막대와 같은 자리). 예전엔 결과 줄 끝에 덧붙였는데,
        // 그 줄은 이제 공개 결과(◆ · 등급 · 이름)가 쓴다.
        _oddsBar?.SetBoost(RefineryService.QualityUnlockEpic > 0f
            ? $"정제 등급 상승 +{RefineryService.QualityUnlockEpic * 100f:F0}%p" : string.Empty);
        _oddsBar?.SetOdds(rare, epic, leg, false);
        CurrencyCounter.Apply(_oreText, _svc.OreOwned, "원석 ");
        RefreshIdleStage(rare, epic, leg);
        RefreshSpinButton();
    }

    /// <summary>
    /// [정제하기] 한 칸 — 비용은 버튼 안 오른쪽 원석 아이콘 + 숫자. 모자라면 숫자만 빨강.
    /// 끝 상태의 [다시 정제 · 원석 N]은 전체 화면 쪽 버튼이다(<see cref="RefreshAgainButton"/>) — 같은 값으로 함께 칠한다.
    /// </summary>
    private void RefreshSpinButton()
    {
        if (_svc == null) return;
        bool afford  = _svc.CanAfford;
        bool ready   = afford && !_busy;
        RefreshAgainButton(afford, ready);
        RefreshSpinBig(afford, ready);
        if (_spinBtnImg == null) return;

        if (_spinLabel != null)
        {
            _spinLabel.text  = "정제하기";
            _spinLabel.color = ready ? UITheme.Thin.Gold : UITheme.Thin.InkA(0.45f);
        }
        if (_spinCost != null)
        {
            bool icon = _spinIcon != null && _spinIcon.enabled;
            _spinCost.text  = icon ? _svc.CurrentCost.ToString() : $"원석 {_svc.CurrentCost}";
            _spinCost.color = afford ? UITheme.Thin.Ink : UITheme.Thin.Danger;
        }
        if (_spinIcon != null) _spinIcon.color = new Color(1f, 1f, 1f, ready ? 1f : 0.5f);
        SetLine(_spinLine, ready ? UITheme.Thin.LineReady : UITheme.Thin.LineIdle);
        _spinBtnImg.color = ButtonFill;
        // 은은한 불은 지금 누를 것 하나에만(후광 한 화면 1곳) — 끝 상태는 전체 화면이 덮어 이 칸이 보이지 않는다.
        UIAffordGlow.Set(_spinBtnImg, ready);
    }

    // ── 돌리기 ──

    private void OnSpinClicked()
    {
        if (_busy || _svc == null) return;

        // 거절에는 반드시 사유가 붙는다 — 흐린 버튼만으로는 "고장난 버튼"으로 읽혔다.
        if (!_svc.CanAfford)
        {
            SetHint(_svc.MeltForNext > 0 ? "원석이 부족합니다 — 오른쪽 [녹이기]로 강화재료를 원석으로 바꿀 수 있다" : "원석이 부족합니다");
            ShopUIStyle.PlaySfx("shop_reject");
            return;
        }
        SpinAsync().Forget();
    }

    /// <summary>[재료 녹이기] — 이번 정제에 모자란 원석만큼 강화재료를 녹인다(<see cref="RefineryService.MeltForNext"/>). 보유 원석 숫자가 굴러 오른다.</summary>
    private void OnMeltClicked()
    {
        if (_busy || _svc == null) return;
        int melt = _svc.MeltForNext;
        if (melt <= 0 || !_svc.TryMelt(melt)) { ShopUIStyle.PlaySfx("shop_reject"); Refresh(); return; }
        ShopUIStyle.PlaySfx("shop_select");
        SetHint($"강화재료를 녹였다 — 원석 +{melt}");
        Refresh();
    }

    private async UniTaskVoid SpinAsync()
    {
        _busy = true;
        var outcome = _svc.Craft();
        // 실패(보관함 가득 등)면 원석을 쓰지 않았다 — 끝 상태였으면 그대로 둔다([룬판에 놓기]를 잃지 않게).
        if (!outcome.Success) { SetHint(outcome.FailReason); _busy = false; Refresh(); return; }

        // 끝 상태에서 [다시 정제] — 앞 룬은 이미 보관함에 있다(판에 놓지 않았을 뿐). 공개를 비우고 처음부터 다시 돈다.
        _pendingRune = null;
        ShowEndButtons(false);
        SetHint("");
        Refresh();
        // 쓴 원석은 누른 순간 빠져나간다 — 예전엔 결과 공개 뒤(약 1초 늦게) 줄어 무엇을 치렀는지가 흐렸다(10-01).
        CurrencyCounter.Apply(_oreText, _svc.OreOwned, "원석 ");

        // 「정제 품질」 할인 조건 집계. RefineryService는 런 참조가 없어 호출부에서 센다.
        GameRunBootstrapper.Instance?.Run?.ReportRefineUse();

        try { await PlayGachaAsync(outcome, destroyCancellationToken); }
        catch (OperationCanceledException) { return; }

        // 잠금(_busy)은 끝 상태가 연다 — 공개 중엔 [다시 정제] · ESC · 바깥 클릭이 막혀 있다(ESC 가드 10-11).
        Refresh();
        if (outcome.Alt != null) { await OfferPickAsync(outcome); return; }
        EnterEndState(outcome.Rune);
    }

    /// <summary>
    /// 끝 상태(희귀 3.0초 · 영웅 3.4초 · 전설 4.4초) — 입력이 열린다: [룬판에 놓기] · [다시 정제 · 원석 N](설계 v3 1-1).
    /// 룬은 이미 보관함에 들어가 있다(<see cref="RefineryService.Craft"/>) — 여기서 창을 닫아도(ESC) 룬은 잃지 않는다.
    /// </summary>
    private void EnterEndState(RuntimeItemData rune)
    {
        _pendingRune = rune;
        _busy = false;
        _inputOpenAt = Time.unscaledTime;
        ShowEndButtons(rune != null);
        Refresh();
    }

    /// <summary>[룬판에 놓기] — 결과가 판 쪽으로 빨려 든 뒤 정제소를 닫고 그 룬을 판에 올린다.</summary>
    private void OnPlaceClicked()
    {
        if (_busy || _pendingRune == null) return;
        _busy = true;   // 넘기는 동안 다시 눌리거나 닫히지 않게(ESC 가드와 같은 이유)
        var rune = _pendingRune;
        _pendingRune = null;
        ShowEndButtons(false);
        ShopUIStyle.PlaySfx(SoundEvent.RunePlace);   // 결과가 판 쪽으로 빨려 듦 — 판에 놓이는 같은 뜻(10-12 R5 → G2)
        Refresh();
        HandOffToGridAsync(rune).Forget();
    }

    /// <summary>안내 · 거절 사유 한 줄 — 창 아래 줄과 전체 화면 아래 줄(끝 상태에서 [다시 정제]를 눌렀을 때 보이는 쪽)에 같이 쓴다.</summary>
    private void SetHint(string text)
    {
        if (_hint  != null) _hint.text  = text;
        if (_wHint != null) _wHint.text = text;
        if (_iHint != null) _iHint.text = text;
    }

    private static async UniTaskVoid FadeInAsync(CanvasGroup cg, CancellationToken ct)
    {
        try { await UIMotion.Tween(UIMotion.Swap, k => cg.alpha = k, ct); }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// 「정제 두 장」(기억의 제단) — 두 장을 함께 공개했다. 잠깐 보여 준 뒤 정제소를 닫고 두 장을 룬 선택 화면에 올린다
    /// (방 보상과 같은 화면 · 같은 배치 흐름). 넘기면 쓴 원석을 돌려준다.
    /// </summary>
    private async UniTask OfferPickAsync(RefineryOutcome outcome)
    {
        try { await UniTask.Delay(700, ignoreTimeScale: true, cancellationToken: this.GetCancellationTokenOnDestroy()); }
        catch (OperationCanceledException) { return; }

        var inv  = GameRunBootstrapper.Instance?.Run?.ItemInventory;
        var svc  = _svc;
        var list = new List<(RuntimeItemData data, ItemSO so)> { (outcome.Rune, outcome.RuneSo), (outcome.Alt, outcome.AltSo) };
        base.ClosePopupUI();   // 정제소를 먼저 닫는다 — 아래는 이 창이 사라진 뒤에도 이어진다
        OfferPickDetachedAsync(list, inv, svc, outcome.Spent).Forget();
    }

    private static async UniTaskVoid OfferPickDetachedAsync(List<(RuntimeItemData data, ItemSO so)> candidates,
                                                            RunItemInventory inv, RefineryService svc, int spent)
    {
        var popup = Managers.UI != null ? await Managers.UI.ShowPopupUIAndGetAsync<UI_RuneSelectPopup>() : null;
        if (popup == null)
        {
            // 화면을 못 열면 좋은 쪽을 그대로 넣는다 — 원석만 빠지고 룬이 사라지면 안 된다.
            if (candidates.Count > 0 && candidates[0].data != null) inv?.AddToStaging(candidates[0].data);
            return;
        }
        var wait = popup.WaitForInteractionAsync(CancellationToken.None);
        popup.Setup(candidates, inv, refinerySpent: spent);   // 넘기기 글 = 쓴 원석 환불 · 방 확률 막대 숨김(10-18)
        await wait;
        if (popup.Skipped) svc?.Refund(spent);
    }

    /// <summary>[룬판에 놓기] 뒤 — 결과가 아래로 빨려 들어간 다음(어디로 가는지가 보이게) 정제소를 닫고 그 룬을 판에 올린다.</summary>
    private async UniTask HandOffToGridAsync(RuntimeItemData rune)
    {
        if (rune == null) return;
        try { await SlipToGridAsync(this.GetCancellationTokenOnDestroy()); }
        catch (OperationCanceledException) { return; }

        OpenGridForRune(rune);
    }

    /// <summary>
    /// 공개된 룬이 판 쪽(아래)으로 빨려 들어간다 — 룬판이 열리기 직전의 손짓. 그 뒤 창이 닫히므로 되돌리지 않는다
    /// (다음 공개는 <see cref="LayoutCards"/>가 자리를 다시 잡는다).
    /// </summary>
    private async UniTask SlipToGridAsync(CancellationToken ct)
    {
        var icon = _wMain?.Icon;
        if (icon == null || !icon.gameObject.activeInHierarchy) return;
        var rt  = icon.rectTransform;
        var rim = _wMain.Rim != null ? _wMain.Rim.rectTransform : null;
        Vector2 from = rt.anchoredPosition;
        float t = 0f;
        while (t < SlipDur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / SlipDur);
            var   p = from + new Vector2(0f, -320f * k * k);
            float s = Mathf.Lerp(1f, 0.4f, k);
            rt.anchoredPosition = p;
            rt.localScale       = new Vector3(s, s, 1f);
            if (rim != null) { rim.anchoredPosition = p; rim.localScale = rt.localScale; }
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
    }

    private static int RarityTier(ItemRarity r) => r switch
    {
        ItemRarity.Legendary => 3,
        ItemRarity.Epic      => 2,
        ItemRarity.Rare      => 1,
        _                    => 0,
    };

    // ── Helpers ──

    private static string Pct(float f) => Mathf.RoundToInt(f * 100f) + "%";
    private static string HexOf(ItemRarity r) => ColorUtility.ToHtmlStringRGB(ShopUIStyle.RarityGlow(r));

    private static void AddClick(GameObject go, Action onClick)
    {
        var btn = go.GetComponent<Button>() ?? go.AddComponent<Button>();
        ShopUIStyle.ApplyButtonColors(btn);
        btn.onClick.AddListener(() => onClick?.Invoke());
    }
}
