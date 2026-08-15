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
public class UI_AssembleCard : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
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
    private const float GradeBarW    = 150f;   // 골드.png 186×52 원본 비율
    private const float GradeBarH    = 42f;
    private const float GradeCornerW = 34f;    // 골드 테두리.png 53×88
    private const float GradeCornerH = 56f;
    private CovenantSkinSO _gradeSkin;
    private Image[]        _gradePieces;

    private Color  _gradeColor = Color.white;
    private bool   _selected, _hover;
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
        if (_tierFrame != null) _tierFrame.gameObject.SetActive(false);   // 구 액자와 겹치지 않게

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
        ApplyCardBg();
    }

    /// <summary>조각 6개를 1회 생성한다. 카드 크기가 변해도 앵커로 따라간다.</summary>
    private void EnsureGradePieces()
    {
        if (_gradePieces != null) return;

        var root = (RectTransform)transform;
        _gradePieces = new Image[6];

        // 위·아래 가로 장식바 — 카드 중앙 상/하단에 걸친다.
        _gradePieces[0] = MakePiece(root, "GradeBar_T", new Vector2(0.5f, 1f), new Vector2(GradeBarW, GradeBarH), Vector2.zero, Vector2.one);
        _gradePieces[1] = MakePiece(root, "GradeBar_B", new Vector2(0.5f, 0f), new Vector2(GradeBarW, GradeBarH), Vector2.zero, new Vector2(1f, -1f));

        // 네 귀퉁이 — 좌상단 아트 하나를 축 반전해 나머지 셋으로 쓴다.
        _gradePieces[2] = MakePiece(root, "GradeCorner_TL", new Vector2(0f, 1f), new Vector2(GradeCornerW, GradeCornerH), Vector2.zero, new Vector2( 1f,  1f));
        _gradePieces[3] = MakePiece(root, "GradeCorner_TR", new Vector2(1f, 1f), new Vector2(GradeCornerW, GradeCornerH), Vector2.zero, new Vector2(-1f,  1f));
        _gradePieces[4] = MakePiece(root, "GradeCorner_BL", new Vector2(0f, 0f), new Vector2(GradeCornerW, GradeCornerH), Vector2.zero, new Vector2( 1f, -1f));
        _gradePieces[5] = MakePiece(root, "GradeCorner_BR", new Vector2(1f, 0f), new Vector2(GradeCornerW, GradeCornerH), Vector2.zero, new Vector2(-1f, -1f));
    }

    private static Image MakePiece(RectTransform parent, string name, Vector2 anchor, Vector2 size, Vector2 offset, Vector2 flip)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor;

        // 피벗을 모서리에 두고 localScale로 뒤집으면 <b>피벗을 축으로</b> 반전돼 조각이 카드 밖으로 나간다
        // (우측 모서리들이 카드 오른쪽에 붕 떠 보이던 원인). 피벗을 한가운데로 두고
        // 위치를 안쪽으로 반 칸 밀면, 뒤집어도 제자리에서 거울상만 된다.
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = new Vector2(
            offset.x + (anchor.x == 0.5f ? 0f : (anchor.x < 0.5f ? size.x * 0.5f : -size.x * 0.5f)),
            offset.y + (anchor.y == 0.5f ? 0f : (anchor.y < 0.5f ? size.y * 0.5f : -size.y * 0.5f)));
        rt.localScale = new Vector3(flip.x, flip.y, 1f);   // 아트 1장을 반전해 재사용

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
    }

    public void Bind(string title, string sub, CovenantTier tier, Color tierColor)
    {
        // 이름·설명은 팔레트에서 오는 가변 길이 문자열이라 고정 박스를 넘기기 쉽다.
        // 카드 밖으로 흘러 옆 카드 위에 겹치지 않도록, 여기서 박스 안에 가둔다.
        if (_nameText) { _nameText.text = title; FitInBox(_nameText, wrap: false); }
        if (_subText)  { _subText.text  = sub;   FitInBox(_subText,  wrap: true);  }
        if (_tierText)
        {
            _tierText.text  = tier.DisplayName();
            _tierText.color = tierColor;   // 등급명도 등급색으로
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
        t.fontSizeMax      = authored;
        t.fontSizeMin      = Mathf.Max(9f, authored * 0.6f);
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
        if (_glow)
        {
            // 카드 전체를 채우는 단색 글로우라 너무 진하면 텍스트가 묻힌다 — 은은한 등급 틴트로.
            float a = _selected ? 0.20f : (_hover ? 0.10f : 0f);
            _glow.color = new Color(_gradeColor.r, _gradeColor.g, _gradeColor.b, a);
        }
        if (instant) { transform.localScale = Vector3.one * target; return; }
        TweenScaleAsync(target).Forget();
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
        float restGlow   = _selected ? 0.20f : 0f;

        try
        {
            float t = 0f;
            while (t < RevealTime)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / RevealTime);
                float ease = 1f - (1f - k) * (1f - k);   // ease-out
                transform.localScale = Vector3.one * Mathf.Lerp(startScale, restScale, ease);
                if (_glow)
                    _glow.color = new Color(_gradeColor.r, _gradeColor.g, _gradeColor.b, Mathf.Lerp(peakGlow, restGlow, ease));
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            transform.localScale = Vector3.one * restScale;
            if (_glow) _glow.color = new Color(_gradeColor.r, _gradeColor.g, _gradeColor.b, restGlow);
        }
        catch (System.OperationCanceledException) { }
    }
}
