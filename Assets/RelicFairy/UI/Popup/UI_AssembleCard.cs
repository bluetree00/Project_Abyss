using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// 조립 서약 드래프트 카드(원인/효과 공용). 카드는 요약만 표시(이름·태그·티어) —
/// 상세 수치는 팝업 중앙 완성 미리보기/호버에서 노출한다. ↻ 리롤 버튼과 ?? 봉인 오버레이 지원.
///
/// UX 연출: 등급색 글로우(실버/골드/루비) · 선택 시 감속 확대(튕김 없음) · 호버 살짝 확대.
/// 등급 테두리 아트는 팝업이 SetSkin으로 주입(카드별 개별 배선 불필요).
///
/// 10-11 서약서 개편(설계서 1장 · 리서치 B §4-4): 글자는 톤 v2(이름 Noto 500 · 설명 Noto 400 · 꼬리표 Noto 500, 가짜 굵게 0),
/// 양피지 위 잉크는 4.5:1 이상, 등급 테는 조각을 작게 + 선을 얇게(<see cref="UISpriteMaster.Thinned"/>).
/// </summary>
public class UI_AssembleCard : MonoBehaviour, IOwnsButtonScale, IPointerEnterHandler, IPointerExitHandler
{
    // ── Constants ────────────────────────────────────────
    private const float SelectScale = 1.06f;
    private const float HoverScale  = 1.03f;
    private const float TweenTime   = 0.12f;
    private const float RevealTime  = 0.26f;   // 등장(드래프트/리롤) 연출 길이

    // 글자 단계(1080 기준 px) — 이름 22 · 설명 17(최소 15) · 꼬리표 15. 설명은 두 줄까지.
    private const float NameSize = 22f;
    private const float SubSize  = 17f;
    private const float SubMin   = 16f;   // 10-14 글자 하한 16(예전 15)
    private const float TagSize  = 16f;
    private const float NameMin  = 16f;   // 이름 자동 축소 하한 — 예전엔 상한 × 0.6(13px)까지 줄었다

    /// <summary>양피지 위 수치 — 짙은 적갈 5.3:1(리서치 B §4-4 P1, 예전 #8A3418 3.3:1). 팝업 두루마리도 같은 색을 쓴다.</summary>
    internal const string NumOnParchment = "#5E1A0A";

    // ── Static ───────────────────────────────────────────
    // 줄 자리(카드 rect 세로 비율). 몸통은 rect 아래 23px · 위 6px 안쪽이고(<see cref="CovenantSkinSO.CardBodyInset"/>),
    // 위·아래 장식바가 몸통 안으로 약 9px 들어온다 — 글은 그 사이(약 32~117px)에 앉는다.
    // 이름 상자 34px = Noto 22의 한 줄(줄높이 1.448 × 2% 여유) — 낮으면 FitInBox가 상한을 깎아 22가 안 나온다.
    private static readonly Vector2 NameRowY = new(0.632f, 0.890f);
    private static readonly Vector2 SubRowY  = new(0.240f, 0.628f);   // 51px = 17 두 줄

    private static string s_mediumTag;   // 수치용 굵기 글꼴 태그(<font="…">) — 한 번 등록하고 재사용

    // ── [SerializeField] ─────────────────────────────────
    [SerializeField] private Button     _selectButton;
    [SerializeField] private Button     _rerollButton;
    [SerializeField] private TMP_Text   _nameText;
    [SerializeField] private TMP_Text   _subText;
    [SerializeField] private TMP_Text   _tierText;
    [SerializeField] private Image      _tierFrame;
    [SerializeField] private GameObject _selectedMark;
    [SerializeField] private GameObject _sealedOverlay;

    [Header("연출 (선택)")]
    [SerializeField] private Image _cardBg;   // 테두리 바탕@2x (카드 검은 바탕)
    [SerializeField] private Image _glow;     // 등급색 글로우(선택/호버 시 점등). 없으면 스케일만.

    // ── Private ──────────────────────────────────────────
    // 등급 테두리 아트 — 팝업(UI_CovenantAssemble)이 SetSkin으로 주입.
    private Sprite _silverFrame, _goldFrame, _rubyFrame;
    private string _subRaw;     // 색을 입히기 전 설명(바탕이 바뀌면 다시 입힌다)
    private bool   _darkBg;

    // 개편본 등급 테두리 — 조각 조립식(위·아래 장식바 2 + 모서리 4).
    // 완성 목업(서약 풀샷.png)에서 카드 <b>몸통</b> 223×106 위에 장식바 113×31, 모서리 34×53으로
    // 얹혀 있다(바탕 아트 1:1 위에 조각 0.6배). <b>고정 px로 두면 안 된다</b> — 판(Book)이 해상도에 따라
    // 줄면 조각만 그대로 남아 카드를 통째로 덮는다.
    //
    // 높이만 몸통 비율로 잡고 <b>폭은 아트 비율로</b> 따라가게 한다. 등급마다 조각 비율이 다르다
    // (모서리 철 0.640 · 골드 0.602 · 루비 0.591) — 폭까지 비율로 고정하면 루비 모서리가 6.8% 늘어났다.
    //
    // 10-11: 카드 6장 × 조각 6개의 고대비 금속이 결과 두루마리보다 먼저 눈을 끌었다(리서치 B §4-3).
    // 장식은 남기되 작게 — 보석 바 30 → 18px, 모서리 52 → 34px(위·아래 모서리가 가운데서 만나지 않고 귀퉁이만 감싼다).
    private const float GradeBarHFrac    = 0.17f;   //  18 / 104
    private const float GradeCornerHFrac = 0.33f;   //  34 / 104

    // 통화 배지와 이름이 한 줄을 나눠 쓸 때 사이 간격.
    private const float BadgeGap = 8f;

    // 카드 바탕이 선택 여부로 <b>명도가 뒤집힌다</b>(밝은 양피지 ↔ 어두운 판).
    // 프리팹 authoring 색(흰 이름·옅은 보라 설명)은 어두운 구 카드 기준이라 양피지 위에서 글자가 날아간다.
    // 10-11: 양피지 바탕(#C1A887) 기준 4.5:1 이상 — 이름 #2A1C0E · 설명 #3F2E1C(5.7:1, 예전 #57422B 4.2:1) · 꼬리표 #4A3820.
    // 반전(어두운 판) 위는 톤 v2 잉크(설계서 1장 2).
    private static readonly Color InkOnParchment    = new(0.165f, 0.110f, 0.055f, 1f);
    private static readonly Color InkDimOnParchment = new(0.247f, 0.180f, 0.110f, 1f);
    private static readonly Color TagOnParchment    = new(0.290f, 0.220f, 0.125f, 1f);
    private static readonly Color InkOnDark         = UITheme.Thin.Ink;
    private static readonly Color InkDimOnDark      = UITheme.Thin.InkA(0.86f);
    private static readonly Color TagOnDark         = UITheme.Thin.InkA(0.72f);

    // 프리팹 리롤 버튼은 형광 보라라 양피지 카드 위에서 혼자 튄다. 청동으로 눌러 담는다.
    private static readonly Color RerollFill = new(0.36f, 0.26f, 0.13f, 0.92f);
    private CovenantSkinSO      _gradeSkin;
    private Image[]             _gradePieces;
    private AspectRatioFitter[] _gradeFitters;

    // 보유 서약과 통화가 물리는 카드 — 상시 은은하게 물들인다.
    // 10-11: 청록은 이 화면에서만 쓰는 7번째 강조색이었다(리서치 B §4-4 P3 「7 → 4」). 수치색(적갈)으로 옅게 —
    // 「시너지」라는 글자는 두루마리의 시너지 줄이 말한다.
    private static readonly Color SynergyGlow = new(0.369f, 0.102f, 0.039f);   // #5E1A0A
    private const float SynergyGlowAlpha = 0.10f;

    private Color  _gradeColor = Color.white;
    private bool   _selected, _hover, _synergy;
    private bool   _thinType;   // 톤 v2 글자로 갈았나(카드마다 한 번)
    private CancellationTokenSource _tweenCts;

    private bool HasFrameSkin => _silverFrame != null || _goldFrame != null || _rubyFrame != null;

    // ── Properties ───────────────────────────────────────
    public Button SelectButton => _selectButton;
    public Button RerollButton => _rerollButton;

    // ── Lifecycle ────────────────────────────────────────
    private void OnDisable()
    {
        _tweenCts?.Cancel();
        transform.localScale = Vector3.one;
    }

    // ── Public Methods ───────────────────────────────────

    /// <summary>팝업이 공용 등급 테두리 아트를 카드에 주입. Bind 전에 1회 호출.</summary>
    public void SetSkin(Sprite silver, Sprite gold, Sprite ruby, Sprite cardBg)
    {
        _silverFrame = silver;
        _goldFrame   = gold;
        _rubyFrame   = ruby;
        if (cardBg != null && _cardBg != null)
        {
            _cardBg.sprite = cardBg;
            _cardBg.type   = Image.Type.Sliced;
            _cardBg.color  = Color.white;
        }
    }

    /// <summary>
    /// 개편본 스킨 주입. 등급 테두리가 <b>조각 조립식</b>(위·아래 가로 장식바 + 네 귀퉁이 모서리)이라
    /// 한 장짜리 <see cref="_tierFrame"/>으로는 표현할 수 없어, 조각 6개를 카드에 깔고 등급마다 갈아끼운다.
    /// 카드 바탕은 선택 여부로 밝은 양피지 ↔ 어두운 판이 교체된다.
    /// </summary>
    public void SetGradeSkin(CovenantSkinSO skin)
    {
        if (skin == null || !skin.HasGradeFrames) return;

        _gradeSkin = skin;

        // 구 액자와 겹치지 않게 끈다. ⚠️ GameObject를 끄면 안 된다 —
        // 등급명·통화 배지를 그리는 TierText가 이 액자의 <b>자식</b>이라, 같이 꺼져 카드에서 사라진다.
        if (_tierFrame != null) _tierFrame.enabled = false;

        // 선택 표시는 프리팹에서 구 카드 크기에 맞춰 authoring된 사각형이라, 개편 카드보다 크고
        // 위치도 어긋나 회색 판이 카드 밖으로 삐져나온다. 카드에 딱 맞게 늘려 붙인다.
        if (_selectedMark != null &&
            _selectedMark.TryGetComponent<RectTransform>(out var markRt))
        {
            markRt.anchorMin = Vector2.zero;
            markRt.anchorMax = Vector2.one;
            markRt.offsetMin = Vector2.zero;
            markRt.offsetMax = Vector2.zero;
            markRt.anchoredPosition = Vector2.zero;
        }

        // 바탕을 먼저 개편본으로 바꾼다 — 몸통 여백은 바탕 스프라이트의 PPU로 환산하므로 구 아트가 꽂힌 채 재면 틀린다.
        ApplyCardBg();

        // 글로우는 카드 rect 전체(그림자 띠 포함)를 덮던 단색 판이라, 켜지면 그림자 자리까지 네모가 떠 보였다.
        if (_glow != null) InsetToBody(_glow.rectTransform);

        EnsureGradePieces();
        TintRerollButton();
        EnsureThinType();
    }

    /// <summary>
    /// 리롤 버튼을 카드 <b>바깥쪽 옆</b>에 붙인다(원인 열=왼쪽, 효과 열=오른쪽). 세로는 몸통 가운데.
    ///
    /// <para>예전엔 우측 상단에 얹혀 등급 테두리의 오른쪽 위 모서리를 통째로 가렸고,
    /// 그 줄은 이제 통화 배지가 쓴다. 카드 옆은 인쇄 테두리까지 60px 남짓 비어 있다.</para>
    ///
    /// <para>10-11: 40×24는 누르기 어려웠다 → 56×32(리서치 B §4-4 P3). 넓어진 만큼 카드와의 틈을 6 → 4px로 줄여
    /// 원인 열 왼쪽 바깥이 양피지 안쪽 장식선을 넘지 않게 한다.</para>
    /// </summary>
    public void DockReroll(bool outerLeft)
    {
        if (_rerollButton == null) return;
        var rt = (RectTransform)_rerollButton.transform;
        Vector4 inset = BodyInset();
        const float gap = 4f;

        rt.anchorMin = rt.anchorMax = new Vector2(outerLeft ? 0f : 1f, 0.5f);
        rt.pivot     = new Vector2(outerLeft ? 1f : 0f, 0.5f);
        rt.sizeDelta = new Vector2(56f, 32f);
        // 몸통 가운데 = rect 가운데에서 (아래 여백 − 위 여백)/2만큼 위.
        float y = (inset.y - inset.w) * 0.5f;
        rt.anchoredPosition = outerLeft
            ? new Vector2(inset.x - gap, y)
            : new Vector2(-inset.z + gap, y);
    }

    /// <summary>리롤 버튼을 양피지 톤에 맞춘다. 개편 스킨이 붙은 카드에서만 돈다.</summary>
    private void TintRerollButton()
    {
        if (_rerollButton == null) return;

        if (_rerollButton.TryGetComponent<Image>(out var img))
        {
            img.sprite = null;
            img.color  = RerollFill;
        }
        foreach (var t in _rerollButton.GetComponentsInChildren<TMP_Text>(true))
            t.color = InkOnDark;
    }

    /// <summary>
    /// 톤 v2 글자(10-11) — 프리팹은 DNF Bold + 가짜 굵게로 저작돼 있다. 이름 Noto 500 · 설명 Noto 400 · 꼬리표 Noto 500으로 갈고,
    /// 이름 22가 들어가게 줄 자리를 다시 잡는다. 크기는 <see cref="FitInBox"/>가 읽는 「저작 값」이 되므로 Bind보다 먼저 돈다.
    /// </summary>
    private void EnsureThinType()
    {
        if (_thinType) return;
        _thinType = true;

        UITheme.StyleThinText(_nameText, UIWeight.Medium,  NameSize, InkOnParchment);
        UITheme.StyleThinText(_subText,  UIWeight.Regular, SubSize,  InkDimOnParchment);
        UITheme.StyleThinText(_tierText, UIWeight.Medium,  TagSize,  TagOnParchment);
        SetRowY(_nameText, NameRowY);
        SetRowY(_tierText, NameRowY);   // 꼬리표는 이름과 한 줄(오른쪽) — 아래 줄로 내리면 설명이 한 줄밖에 못 든다
        SetRowY(_subText,  SubRowY);
    }

    private static void SetRowY(TMP_Text t, Vector2 y)
    {
        if (t == null) return;
        var rt = t.rectTransform;
        rt.anchorMin = new Vector2(rt.anchorMin.x, y.x);
        rt.anchorMax = new Vector2(rt.anchorMax.x, y.y);
        rt.offsetMin = new Vector2(rt.offsetMin.x, 0f);
        rt.offsetMax = new Vector2(rt.offsetMax.x, 0f);
    }

    /// <summary>
    /// 조각 6개를 1회 생성한다. 조각은 카드 rect가 아니라 <b>몸통</b>(그림자 뺀 불투명 판)을 두른 틀에 붙는다 —
    /// 카드 크기가 변해도 틀이 앵커로 따라간다.
    /// </summary>
    private void EnsureGradePieces()
    {
        if (_gradePieces != null) return;

        var frame = (RectTransform)new GameObject("GradeFrame", typeof(RectTransform)).transform;
        frame.SetParent(transform, false);
        InsetToBody(frame);
        // 글로우(몸통 틴트) 바로 위, 글자 아래 — 틴트가 테두리를 덮지 않고, 테두리가 글자를 덮지 않는다.
        bool glowIsChild = _glow != null && _glow.transform.parent == transform;
        frame.SetSiblingIndex(glowIsChild ? _glow.transform.GetSiblingIndex() + 1 : 0);

        _gradePieces  = new Image[6];
        _gradeFitters = new AspectRatioFitter[6];

        float bh = GradeBarHFrac * 0.5f;
        float ch = GradeCornerHFrac;

        // 위·아래 가로 장식바 — 아트의 가로선이 세로 정중앙이라 <b>몸통 모서리 선에 걸터앉혀야</b> 한다.
        // 안쪽으로 반 칸 밀어 넣으면 다이아 장식이 통째로 카드 안으로 들어와 이름 위를 덮는다.
        MakePiece(frame, 0, "GradeBar_T", new Vector2(0.5f, 1f - bh), new Vector2(0.5f, 1f + bh), new Vector2(0.5f, 0.5f), new Vector2(1f,  1f));
        MakePiece(frame, 1, "GradeBar_B", new Vector2(0.5f,     -bh), new Vector2(0.5f,      bh), new Vector2(0.5f, 0.5f), new Vector2(1f, -1f));

        // 네 귀퉁이 — 좌상단 아트 하나를 축 반전해 나머지 셋으로 쓴다. 아트의 왼쪽 끝이 L자의 바깥선이라
        // 피벗을 왼쪽(x=0)에 두면 반전해도 바깥선이 몸통 가장자리에 붙는다(오른쪽은 x=1에서 안쪽으로 자란다).
        // 세로로 몸통 절반씩 차지해 위·아래 조각이 한가운데서 만나 테두리 한 줄이 된다(목업과 동일).
        MakePiece(frame, 2, "GradeCorner_TL", new Vector2(0f, 1f - ch), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2( 1f,  1f));
        MakePiece(frame, 3, "GradeCorner_TR", new Vector2(1f, 1f - ch), new Vector2(1f, 1f), new Vector2(0f, 0.5f), new Vector2(-1f,  1f));
        MakePiece(frame, 4, "GradeCorner_BL", new Vector2(0f, 0f),      new Vector2(0f, ch), new Vector2(0f, 0.5f), new Vector2( 1f, -1f));
        MakePiece(frame, 5, "GradeCorner_BR", new Vector2(1f, 0f),      new Vector2(1f, ch), new Vector2(0f, 0.5f), new Vector2(-1f, -1f));
    }

    /// <summary>
    /// 조각 하나. <b>높이는 앵커</b>(몸통 비율), <b>폭은 아트 비율</b>(<see cref="AspectRatioFitter"/>)이 정한다 —
    /// 등급마다 조각 비율이 달라 폭까지 비율로 고정하면 등급별로 늘어나는 정도가 달라진다.
    /// localScale 반전은 피벗을 축으로 한 거울상이다.
    /// </summary>
    private void MakePiece(RectTransform parent, int index, string name, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 flip)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = aMin;
        rt.anchorMax = aMax;
        rt.pivot     = pivot;
        rt.sizeDelta = Vector2.zero;
        rt.anchoredPosition = Vector2.zero;
        rt.localScale = new Vector3(flip.x, flip.y, 1f);   // 아트 1장을 반전해 재사용

        var img = go.AddComponent<Image>();
        img.raycastTarget = false;   // 장식이 카드 클릭을 먹으면 선택이 안 된다

        var fit = go.AddComponent<AspectRatioFitter>();
        fit.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;

        _gradePieces[index]  = img;
        _gradeFitters[index] = fit;
    }

    /// <summary>
    /// 몸통 여백(아트 px)을 캔버스 단위로. 9-slice 가장자리는 <c>테두리 ÷ (pixelsPerUnit × 배수)</c>로 그려지므로
    /// 가장자리 안에 든 그림자 띠도 같은 비율로 화면에 앉는다.
    /// </summary>
    private Vector4 BodyInset()
    {
        if (_gradeSkin == null || _cardBg == null) return Vector4.zero;
        float ppu = _cardBg.pixelsPerUnit * _cardBg.pixelsPerUnitMultiplier;
        return ppu > 0f ? _gradeSkin.CardBodyInset / ppu : _gradeSkin.CardBodyInset;
    }

    /// <summary>rect를 카드 몸통(그림자 뺀 판)에 맞춘다.</summary>
    private void InsetToBody(RectTransform rt)
    {
        Vector4 inset = BodyInset();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(inset.x, inset.y);
        rt.offsetMax = new Vector2(-inset.z, -inset.w);
    }

    /// <summary>
    /// 현재 등급에 맞춰 조각 스프라이트를 갈아끼운다. 금속 선은 알파를 1px 깎은 판으로 —
    /// 장식은 남기되 선만 얇게(10-08 결정 · 리서치 B §4-4 P2 「선 약 6px → 2px」). 등급 금속 색이 곧 등급이라 색은 그대로 둔다.
    /// </summary>
    private void ApplyGradePieces(CovenantTier tier)
    {
        if (_gradeSkin == null || _gradePieces == null) return;

        var bar    = UISpriteMaster.Thinned(_gradeSkin.GradeBar(tier),    keepColor: true);
        var corner = UISpriteMaster.Thinned(_gradeSkin.GradeCorner(tier), keepColor: true);

        for (int i = 0; i < _gradePieces.Length; i++)
        {
            var img = _gradePieces[i];
            if (img == null) continue;
            var art = i < 2 ? bar : corner;
            img.sprite = art;
            img.enabled = art != null;
            if (art != null && _gradeFitters[i] != null)
                _gradeFitters[i].aspectRatio = art.rect.width / Mathf.Max(1f, art.rect.height);
        }
    }

    /// <summary>선택 여부에 따라 카드 바탕을 밝은 양피지 ↔ 어두운 판으로 교체.</summary>
    private void ApplyCardBg()
    {
        if (_gradeSkin == null || _cardBg == null) return;

        var art = _selected ? _gradeSkin.cardSelected : _gradeSkin.cardIdle;
        if (art == null) return;

        _cardBg.sprite = art;
        _cardBg.type   = Image.Type.Sliced;
        _cardBg.color  = Color.white;

        ApplyInkColors(_selected);
    }

    /// <summary>
    /// 바탕 명도에 맞춰 글자색을 뒤집는다. 개편 스킨이 붙은 카드에서만 돈다 —
    /// 아트가 없으면 바탕이 여전히 어두운 구 카드라 authoring 색이 맞다.
    /// </summary>
    private void ApplyInkColors(bool darkBg)
    {
        if (_nameText) _nameText.color = darkBg ? InkOnDark    : InkOnParchment;
        if (_subText)  _subText.color  = darkBg ? InkDimOnDark : InkDimOnParchment;
        if (_tierText) _tierText.color = darkBg ? TagOnDark    : TagOnParchment;
        _darkBg = darkBg;
        RenderSub();
    }

    /// <summary>설명 = 어절 줄바꿈 + 수치 색(바탕 명도에 맞춰 금 / 적갈) · 수치만 Noto 500.</summary>
    private void RenderSub()
    {
        if (_subText == null) return;
        _subText.text = InkNumbers(_subRaw ?? string.Empty, _darkBg ? UIKeywordInk.OnDark : NumOnParchment);
    }

    /// <summary>
    /// 어절 줄바꿈 + 수치 색 + <b>수치만 Noto 500</b>(설계서 1장 「숫자만 500」). <see cref="UIKeywordInk"/>는 수치를 &lt;b&gt;로 감싸는데
    /// 이 글꼴에는 굵은 판이 없어 가짜 굵게가 된다(v2-0 금지) — 굵기 글꼴 태그로 바꿔 낀다.
    /// 500 글꼴이 아직 안 올라왔으면 색만 입힌다. 서약서 팝업 두루마리도 이것을 쓴다.
    /// </summary>
    internal static string InkNumbers(string text, string hex)
    {
        string s = UIKeywordInk.Words(text, hex);
        string open = MediumFontTag();
        return open != null
            ? s.Replace("<b>", open).Replace("</b>", "</font>")
            : s.Replace("<b>", string.Empty).Replace("</b>", string.Empty);
    }

    /// <summary>
    /// Noto 500 글꼴로 바꾸는 태그. TMP는 &lt;font="이름"&gt;을 먼저 <see cref="MaterialReferenceManager"/>에서 찾고 없으면 Resources를 뒤진다 —
    /// 이 글꼴은 Addressables로 올라오므로 한 번 등록해 둔다(이름 해시가 태그 값 해시와 같은 규칙이다).
    /// </summary>
    private static string MediumFontTag()
    {
        if (s_mediumTag != null) return s_mediumTag;
        var f = UISkin.ThemeFonts != null ? UISkin.ThemeFonts.Medium : null;
        if (f == null) return null;
        try { MaterialReferenceManager.AddFontAsset(f); }
        catch (System.ArgumentException) { }   // 같은 재질이 다른 경로로 먼저 등록됨 — 글꼴 등록은 이미 끝났다
        s_mediumTag = $"<font=\"{f.name}\">";
        return s_mediumTag;
    }

    /// <param name="badge">
    /// 축·상태 통화 배지("생존 · 보호막"). 등급 라벨 뒤에 붙는다 —
    /// 카드 앞면에서 "이게 공격이냐 생존이냐"를 이름 해석 없이 알 수 있어야 방어축 보장이 눈에 보인다.
    /// 전용 슬롯을 새로 만들지 않고 기존 등급 라벨에 얹는다(프리팹 무수술).
    /// </param>
    public void Bind(string title, string sub, CovenantTier tier, Color tierColor, string badge = null)
    {
        // 이름·설명은 팔레트에서 오는 가변 길이 문자열이라 고정 박스를 넘기기 쉽다.
        // 카드 밖으로 흘러 옆 카드 위에 겹치지 않도록, 여기서 박스 안에 가둔다.
        _subRaw = sub;
        if (_subText)  { RenderSub(); FitInBox(_subText, wrap: true, Authored(_subText, SubSize), _thinType ? SubMin : 0f); }

        // 등급은 테두리(실버·골드·루비 조각)가 말하므로 글자로 다시 적지 않는다 — 그 자리를 통화 배지가 쓴다.
        // 배지는 이름과 한 줄을 나눠 쓴다(이름 왼쪽 · 배지 오른쪽). 이름 상자 오른쪽을 배지 폭만큼 비워
        // 긴 이름(「피의 보호막」)이 배지 위로 겹치지 않게 한다.
        if (_tierText)
        {
            _tierText.text  = badge ?? string.Empty;
            _tierText.color = tierColor;
            FitInBox(_tierText, wrap: false, Authored(_tierText, TagSize));
        }
        if (_nameText)
        {
            float reserve = 0f;
            if (_tierText && !string.IsNullOrEmpty(badge))
                reserve = _tierText.GetPreferredValues(badge, 0f, 0f).x + BadgeGap;
            var nrt = _nameText.rectTransform;
            nrt.offsetMax = new Vector2(-reserve, nrt.offsetMax.y);
            _nameText.text = title;
            FitInBox(_nameText, wrap: false, Authored(_nameText, NameSize), _thinType ? NameMin : 0f);
        }

        _gradeColor = tierColor;

        if (_tierFrame)
        {
            // 스킨 지정 시: 등급별 테두리 아트로 스왑(색 백지). 미지정 시: 기존 색상 방식.
            if (HasFrameSkin)
            {
                var s = FrameFor(tier);
                if (s != null) { _tierFrame.sprite = s; _tierFrame.type = Image.Type.Sliced; _tierFrame.color = Color.white; }
            }
            else
            {
                _tierFrame.color = tierColor;
            }
        }

        ApplyGradePieces(tier);   // 개편본: 조각 조립 테두리를 이 등급으로 갈아끼운다

        if (_glow) _glow.color = new Color(tierColor.r, tierColor.g, tierColor.b, 0f);   // 평시 꺼둠
        SetSealed(false);
        _selected = false;
        _synergy  = false;   // 리롤로 다른 효과가 오면 시너지 판정도 새로 받아야 한다
        ApplyCardBg();
        RefreshVisual(instant: true);
        RevealAsync(tier).Forget();   // 등급별 등장 연출
    }

    /// <summary>설명 줄만 갈아 끼운다(등장 연출 없이) — 효과 카드의 수치는 고른 원인(계수)에 따라 바뀐다.</summary>
    public void SetSub(string sub)
    {
        if (_subRaw == sub) return;
        _subRaw = sub;
        RenderSub();
    }

    public void SetSelected(bool on)
    {
        if (_selectedMark) _selectedMark.SetActive(on);
        _selected = on;
        ApplyCardBg();   // 선택 시 밝은 양피지 → 어두운 판
        // 10-11: 1.12배로 튀었다 내려앉던 팝을 걷었다(톤 v2 「튕김 없음」) — 바탕 반전 + 감속 확대가 선택을 말한다.
        RefreshVisual(instant: false);
    }

    /// <summary>보유 서약과 통화가 물리는 카드 표시(옅은 적갈 물들임). 선택/호버보다 약해 서로 가리지 않는다.</summary>
    public void SetSynergy(bool on)
    {
        if (_synergy == on) return;
        _synergy = on;
        RefreshVisual(instant: false);
    }

    public void SetSealed(bool on) { if (_sealedOverlay) _sealedOverlay.SetActive(on); }

    public void SetInteractable(bool on)
    {
        if (_selectButton) _selectButton.interactable = on;
        if (_rerollButton) _rerollButton.interactable = on;
    }

    // ── Event Handlers (호버 연출) ────────────────────────
    public void OnPointerEnter(PointerEventData e) { _hover = true;  RefreshVisual(false); }
    public void OnPointerExit(PointerEventData e)  { _hover = false; RefreshVisual(false); }

    // ── Private Methods ──────────────────────────────────

    /// <summary>
    /// 박스 안에 가둔다 — 자동 크기는 authoring 값을 넘지 않고 줄이기만 한다.
    /// 10-11: 저작 값을 인자로 받는다 — 자동 크기가 켜진 글은 <c>fontSize</c>가 직전에 줄어든 값을 돌려줘
    /// 리롤할 때마다 상한이 조금씩 내려앉을 수 있다. <paramref name="min"/>이 있으면 그 아래로는 줄이지 않는다(설명 15).
    /// </summary>
    private static void FitInBox(TMP_Text t, bool wrap, float authored, float min = 0f)
    {
        t.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        // 두 줄을 넘는 설명은 최소 크기(15)에서 멈추므로 잘린다 — 잘린 줄은 말줄임으로 알린다.
        t.overflowMode     = TextOverflowModes.Ellipsis;
        t.enableAutoSizing = true;

        // 자동크기는 줄바꿈을 끈 상태에서 <b>가로만</b> 맞춘다 — 상자가 낮으면 글자가
        // 세로로 잘린다(서약 이름 상자 229×35에 필요 높이 39). 줄바꿈이 없는 글에 한해
        // 상자 높이에서 상한을 역산해 함께 묶는다.
        //
        // 나누는 값은 <b>글꼴에서 읽는다</b>. 예전엔 1.45로 어림했는데 DNFForgedBlade의 실제
        // 줄높이는 1.286이라, 이름·등급이 상자에 들어가고도 11% 더 작게 그려졌다
        // (이름 25.5px ← 28.7px 가능). 글꼴을 바꾸면 이 값도 따라와야 해서 상수로 두지 않는다.
        float cap = wrap ? authored
                         : Mathf.Min(authored, t.rectTransform.rect.height / LineHeightRatio(t));
        t.fontSizeMax = Mathf.Max(9f, cap);
        t.fontSizeMin = min > 0f ? Mathf.Min(min, t.fontSizeMax) : Mathf.Max(9f, t.fontSizeMax * 0.6f);
    }

    /// <summary>톤 v2 글자로 갈았으면 그 단계 값, 아니면 프리팹 저작 값.</summary>
    private float Authored(TMP_Text t, float thin) => _thinType ? thin : t.fontSize;

    /// <summary>
    /// 글꼴 한 줄이 차지하는 높이 배수. TMP는 <c>faceInfo.lineHeight / faceInfo.pointSize</c>로 줄을 넘긴다.
    /// 2%의 여유를 얹는다 — 자동 크기가 상자와 정확히 같아지면 반올림 한 번에 아랫줄이 잘린다.
    /// </summary>
    private static float LineHeightRatio(TMP_Text t)
    {
        var face = t.font != null ? t.font.faceInfo : default;
        float r = face.pointSize > 0f ? face.lineHeight / face.pointSize : 1.286f;
        return Mathf.Max(1f, r) * 1.02f;
    }

    private Sprite FrameFor(CovenantTier tier) => tier switch
    {
        CovenantTier.Gold => _goldFrame,
        CovenantTier.Ruby => _rubyFrame,
        _                 => _silverFrame,
    };

    /// <summary>선택/호버 상태에 맞춰 글로우·스케일을 갱신.</summary>
    private void RefreshVisual(bool instant)
    {
        float target = _selected ? SelectScale : (_hover ? HoverScale : 1f);
        if (_glow) _glow.color = RestGlow();
        if (instant) { transform.localScale = Vector3.one * target; return; }
        TweenScaleAsync(target).Forget();
    }

    /// <summary>
    /// 평시 글로우 색. 선택/호버는 등급색, 아무것도 아닐 때 시너지 카드만 옅은 적갈로 켠다.
    /// 등장 연출(<see cref="RevealAsync"/>)의 착지점도 이 값을 쓴다 — 안 그러면 연출이 끝나는 순간
    /// 시너지 표시가 덮여 사라진다(연출은 비동기라 늦게 착지한다).
    /// </summary>
    private Color RestGlow()
    {
        // 개편 스킨에선 선택 = 어두운 판으로 바뀌는 것 자체가 표시다. 그 위에 등급색 20%를 덮으면
        // 실버(거의 흰색)가 판을 회색으로 씻어 글자 대비가 무너졌다(목업은 판을 어둡게 둔다).
        if (_selected) return new Color(_gradeColor.r, _gradeColor.g, _gradeColor.b, _gradeSkin != null ? 0f : 0.20f);
        if (_hover)    return new Color(_gradeColor.r, _gradeColor.g, _gradeColor.b, 0.10f);
        if (_synergy)  return new Color(SynergyGlow.r, SynergyGlow.g, SynergyGlow.b, SynergyGlowAlpha);
        return new Color(_gradeColor.r, _gradeColor.g, _gradeColor.b, 0f);
    }

    private async UniTaskVoid TweenScaleAsync(float target)
    {
        _tweenCts?.Cancel();
        _tweenCts = new CancellationTokenSource();
        var ct = _tweenCts.Token;
        Vector3 from = transform.localScale;
        Vector3 to   = Vector3.one * target;
        float t = 0f;
        try
        {
            while (t < TweenTime)
            {
                t += Time.unscaledDeltaTime;
                transform.localScale = Vector3.Lerp(from, to, UIMotion.EaseOut(t / TweenTime));   // 감속(톤 v2)
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            transform.localScale = to;
        }
        catch (System.OperationCanceledException) { }
    }

    /// <summary>드래프트/리롤로 카드가 나타날 때 등급별 등장 연출(높은 등급일수록 팝·글로우 강함).</summary>
    private async UniTaskVoid RevealAsync(CovenantTier tier)
    {
        if (!isActiveAndEnabled) return;
        _tweenCts?.Cancel();
        _tweenCts = new CancellationTokenSource();
        var ct = _tweenCts.Token;

        // 등급별 등장 강도 — 루비가 가장 화려하게.
        float startScale = tier switch { CovenantTier.Ruby => 1.20f, CovenantTier.Gold => 1.14f, _ => 1.09f };
        float peakGlow   = tier switch { CovenantTier.Ruby => 0.85f, CovenantTier.Gold => 0.55f, _ => 0.32f };
        float restScale  = _selected ? SelectScale : 1f;
        var   peakColor  = new Color(_gradeColor.r, _gradeColor.g, _gradeColor.b, peakGlow);

        try
        {
            float t = 0f;
            while (t < RevealTime)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / RevealTime);
                float ease = 1f - (1f - k) * (1f - k);   // ease-out
                transform.localScale = Vector3.one * Mathf.Lerp(startScale, restScale, ease);
                if (_glow) _glow.color = Color.Lerp(peakColor, RestGlow(), ease);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            transform.localScale = Vector3.one * restScale;
            if (_glow) _glow.color = RestGlow();
        }
        catch (System.OperationCanceledException) { }
    }
}
