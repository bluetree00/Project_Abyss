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
/// UX 연출: 등급색 글로우(실버/골드/루비) · 선택 시 스케일 팝 + 글로우 점등 · 호버 살짝 확대.
/// 등급 테두리 아트는 팝업이 SetSkin으로 주입(카드별 개별 배선 불필요).
/// </summary>
public class UI_AssembleCard : MonoBehaviour, IOwnsButtonScale, IPointerEnterHandler, IPointerExitHandler
{
    // ── Constants ────────────────────────────────────────
    private const float SelectScale = 1.06f;
    private const float HoverScale  = 1.03f;
    private const float PopScale    = 1.12f;
    private const float TweenTime   = 0.12f;
    private const float RevealTime  = 0.26f;   // 등장(드래프트/리롤) 연출 길이

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

    // 개편본 등급 테두리 — 조각 조립식(위·아래 장식바 2 + 모서리 4).
    // 완성 목업(서약 풀샷.png)에서 카드 몸통 223×106 위에 장식바 113×31, 모서리 34×53으로
    // 얹혀 있다. <b>고정 px로 두면 안 된다</b> — 판(Book)이 해상도에 따라 줄면 조각만 그대로
    // 남아 카드를 통째로 덮는다(1920 기준으로도 장식바가 30% 컸다).
    private const float GradeBarWFrac    = 0.507f;   // 113 / 223
    private const float GradeBarHFrac    = 0.292f;   //  31 / 106
    private const float GradeCornerWFrac = 0.152f;   //  34 / 223
    private const float GradeCornerHFrac = 0.500f;   //  53 / 106

    // 카드 바탕이 선택 여부로 <b>명도가 뒤집힌다</b>(밝은 양피지 ↔ 어두운 판).
    // 프리팹 authoring 색(흰 이름·옅은 보라 설명)은 어두운 구 카드 기준이라 양피지 위에서 글자가 날아간다.
    private static readonly Color InkOnParchment    = new(0.16f, 0.11f, 0.06f, 1f);
    private static readonly Color InkDimOnParchment = new(0.34f, 0.26f, 0.17f, 1f);
    private static readonly Color InkOnDark         = new(0.96f, 0.92f, 0.82f, 1f);
    private static readonly Color InkDimOnDark      = new(0.74f, 0.69f, 0.58f, 1f);

    // 프리팹 리롤 버튼은 형광 보라라 양피지 카드 위에서 혼자 튄다. 청동으로 눌러 담는다.
    private static readonly Color RerollFill = new(0.36f, 0.26f, 0.13f, 0.92f);
    private CovenantSkinSO _gradeSkin;
    private Image[]        _gradePieces;

    // 보유 서약과 통화가 물리는 카드 — 등급색과 구분되는 청록 글로우로 상시 은은하게 켠다.
    private static readonly Color SynergyGlow = new(0.35f, 0.90f, 0.80f);
    private const float SynergyGlowAlpha = 0.16f;

    private Color  _gradeColor = Color.white;
    private bool   _selected, _hover, _synergy;
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

        EnsureGradePieces();
        TintRerollButton();
        ApplyCardBg();
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

    /// <summary>조각 6개를 1회 생성한다. 카드 크기가 변해도 앵커로 따라간다.</summary>
    private void EnsureGradePieces()
    {
        if (_gradePieces != null) return;

        var root = (RectTransform)transform;
        _gradePieces = new Image[6];

        float bw = GradeBarWFrac * 0.5f;
        float bh = GradeBarHFrac * 0.5f;
        float cw = GradeCornerWFrac;
        float ch = GradeCornerHFrac;

        // 위·아래 가로 장식바 — 아트의 가로선이 세로 정중앙이라 <b>모서리 선에 걸터앉혀야</b> 한다.
        // 안쪽으로 반 칸 밀어 넣으면 다이아 장식이 통째로 카드 안으로 들어와 이름 위를 덮는다.
        _gradePieces[0] = MakePiece(root, "GradeBar_T", new Vector2(0.5f - bw, 1f - bh), new Vector2(0.5f + bw, 1f + bh), Vector2.one);
        _gradePieces[1] = MakePiece(root, "GradeBar_B", new Vector2(0.5f - bw,     -bh), new Vector2(0.5f + bw,      bh), new Vector2(1f, -1f));

        // 네 귀퉁이 — 좌상단 아트 하나를 축 반전해 나머지 셋으로 쓴다.
        // 세로로 카드 절반씩 차지해 위·아래 조각이 한가운데서 만나 테두리 한 줄이 된다(목업과 동일).
        _gradePieces[2] = MakePiece(root, "GradeCorner_TL", new Vector2(0f,      1f - ch), new Vector2(cw, 1f), new Vector2( 1f,  1f));
        _gradePieces[3] = MakePiece(root, "GradeCorner_TR", new Vector2(1f - cw, 1f - ch), new Vector2(1f, 1f), new Vector2(-1f,  1f));
        _gradePieces[4] = MakePiece(root, "GradeCorner_BL", new Vector2(0f,      0f), new Vector2(cw, ch), new Vector2( 1f, -1f));
        _gradePieces[5] = MakePiece(root, "GradeCorner_BR", new Vector2(1f - cw, 0f), new Vector2(1f, ch), new Vector2(-1f, -1f));
    }

    /// <summary>
    /// 조각 하나. 크기를 <b>앵커로</b> 잡아 카드가 커지든 줄든 비율이 유지된다
    /// (sizeDelta 고정이면 판이 줄어드는 초광폭·저해상도에서 조각만 남아 카드를 덮는다).
    /// 피벗은 한가운데 — localScale 반전이 제자리 거울상이 되어 조각이 카드 밖으로 튀지 않는다.
    /// </summary>
    private static Image MakePiece(RectTransform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 flip)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = aMin;
        rt.anchorMax = aMax;
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = Vector2.zero;
        rt.anchoredPosition = Vector2.zero;
        rt.localScale = new Vector3(flip.x, flip.y, 1f);   // 아트 1장을 반전해 재사용

        // 테두리는 장식이다 — 맨 뒤로 보내지 않으면 나중에 붙은 자식이라 이름·설명 위를 덮는다.
        rt.SetAsFirstSibling();

        var img = go.AddComponent<Image>();
        img.raycastTarget = false;   // 장식이 카드 클릭을 먹으면 선택이 안 된다
        return img;
    }

    /// <summary>현재 등급에 맞춰 조각 스프라이트를 갈아끼운다.</summary>
    private void ApplyGradePieces(CovenantTier tier)
    {
        if (_gradeSkin == null || _gradePieces == null) return;

        var bar    = _gradeSkin.GradeBar(tier);
        var corner = _gradeSkin.GradeCorner(tier);

        for (int i = 0; i < _gradePieces.Length; i++)
        {
            var img = _gradePieces[i];
            if (img == null) continue;
            var art = i < 2 ? bar : corner;
            img.sprite = art;
            img.enabled = art != null;
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
        if (_tierText) _tierText.color = darkBg ? _gradeColor  : DarkenForParchment(_gradeColor);
    }

    /// <summary>등급색을 양피지 위에서 읽히게 눌러 담는다(실버가 특히 배경에 날아간다).</summary>
    private static Color DarkenForParchment(Color c)
        => new(c.r * 0.40f, c.g * 0.34f, c.b * 0.28f, 1f);

    /// <param name="badge">
    /// 축·상태 통화 배지("생존 · 보호막"). 등급 라벨 뒤에 붙는다 —
    /// 카드 앞면에서 "이게 공격이냐 생존이냐"를 이름 해석 없이 알 수 있어야 방어축 보장이 눈에 보인다.
    /// 전용 슬롯을 새로 만들지 않고 기존 등급 라벨에 얹는다(프리팹 무수술).
    /// </param>
    public void Bind(string title, string sub, CovenantTier tier, Color tierColor, string badge = null)
    {
        // 이름·설명은 팔레트에서 오는 가변 길이 문자열이라 고정 박스를 넘기기 쉽다.
        // 카드 밖으로 흘러 옆 카드 위에 겹치지 않도록, 여기서 박스 안에 가둔다.
        if (_nameText) { _nameText.text = title; FitInBox(_nameText, wrap: false); }
        if (_subText)  { _subText.text  = sub;   FitInBox(_subText,  wrap: true);  }
        if (_tierText)
        {
            _tierText.text  = string.IsNullOrEmpty(badge)
                ? tier.DisplayName()
                : tier.DisplayName() + "  " + badge;
            _tierText.color = tierColor;   // 등급명도 등급색으로
            FitInBox(_tierText, wrap: false);
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

    public void SetSelected(bool on)
    {
        if (_selectedMark) _selectedMark.SetActive(on);
        bool wasSelected = _selected;
        _selected = on;
        ApplyCardBg();   // 선택 시 밝은 양피지 → 어두운 판
        RefreshVisual(instant: false);
        if (on && !wasSelected) PopAsync().Forget();   // 새로 선택된 카드만 팝
    }

    /// <summary>보유 서약과 통화가 물리는 카드 표시(청록 글로우). 선택/호버보다 약해 서로 가리지 않는다.</summary>
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

    /// <summary>박스 안에 가둔다 — 자동 크기는 authoring 값을 넘지 않고 줄이기만 한다.</summary>
    private static void FitInBox(TMP_Text t, bool wrap)
    {
        float authored = t.fontSize;
        t.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        t.overflowMode     = wrap ? TextOverflowModes.Truncate : TextOverflowModes.Ellipsis;
        t.enableAutoSizing = true;

        // 자동크기는 줄바꿈을 끈 상태에서 <b>가로만</b> 맞춘다 — 상자가 낮으면 글자가
        // 세로로 잘린다(서약 이름 상자 229×35에 필요 높이 39). 한 줄 선호높이는 글꼴의
        // 약 1.45배이므로, 줄바꿈이 없는 글에 한해 상자 높이에서 상한을 역산해 함께 묶는다.
        float cap = wrap ? authored
                         : Mathf.Min(authored, t.rectTransform.rect.height / 1.45f);
        t.fontSizeMax = Mathf.Max(9f, cap);
        t.fontSizeMin = Mathf.Max(9f, t.fontSizeMax * 0.6f);
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
    /// 평시 글로우 색. 선택/호버는 등급색, 아무것도 아닐 때 시너지 카드만 청록으로 켠다.
    /// 등장 연출(<see cref="RevealAsync"/>)의 착지점도 이 값을 쓴다 — 안 그러면 연출이 끝나는 순간
    /// 시너지 표시가 덮여 사라진다(연출은 비동기라 늦게 착지한다).
    /// </summary>
    private Color RestGlow()
    {
        if (_selected) return new Color(_gradeColor.r, _gradeColor.g, _gradeColor.b, 0.20f);
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
                transform.localScale = Vector3.Lerp(from, to, Mathf.Clamp01(t / TweenTime));
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            transform.localScale = to;
        }
        catch (System.OperationCanceledException) { }
    }

    /// <summary>선택 순간 살짝 튕기는 팝(over-shoot → 정착).</summary>
    private async UniTaskVoid PopAsync()
    {
        _tweenCts?.Cancel();
        _tweenCts = new CancellationTokenSource();
        var ct = _tweenCts.Token;
        try
        {
            transform.localScale = Vector3.one * PopScale;
            float t = 0f;
            while (t < TweenTime)
            {
                t += Time.unscaledDeltaTime;
                transform.localScale = Vector3.Lerp(Vector3.one * PopScale, Vector3.one * SelectScale, Mathf.Clamp01(t / TweenTime));
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            transform.localScale = Vector3.one * SelectScale;
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
