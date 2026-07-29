using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 마우스 호버 시 설명 말풍선을 띄우는 범용 트리거. 대상 오브젝트에 붙이고 <see cref="Set"/>로 내용을 준다.
///
/// 기존 <see cref="SkillTooltipTrigger"/>는 <b>미리 만들어 둔 패널을 주입받아야</b> 동작해서
/// 코드로 생성되는 위젯(HUD 재화 pill 등)엔 쓸 수 없다. 이쪽은 표시면을 스스로 만든다.
/// 표시면은 캔버스당 1개를 공유해 재사용한다(호버는 한 번에 하나뿐).
///
/// 부모 계층에 그래픽이 있으면 이벤트가 올라오므로, 자식 Image를 가진 컨테이너에 붙여도 동작한다.
/// </summary>
[DisallowMultipleComponent]
public sealed class UITooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private const float MaxWidth   = 320f;
    private const float PadX       = 14f;
    private const float PadY       = 10f;
    private const float GapFromTarget = 10f;

    private static readonly Color PanelFill   = new(0.06f, 0.06f, 0.09f, 0.96f);
    private static readonly Color PanelBorder = new(0.72f, 0.62f, 0.38f, 1f);
    private static readonly Color BodyColor   = new(0.78f, 0.80f, 0.86f, 1f);

    private static readonly Vector3[] _corners = new Vector3[4];

    // 캔버스별 공유 표시면 (호버는 동시에 하나라 1개면 충분)
    private static GameObject _surface;
    private static RectTransform _surfaceRT;
    private static TMP_Text _titleText;
    private static TMP_Text _bodyText;
    private static Image _borderImg;

    private string _title;
    private string _body;
    private Color  _accent = PanelBorder;

    /// <summary>툴팁 내용 설정. accent = 제목 색·테두리 색(재화별 구분용).</summary>
    public void Set(string title, string body, Color accent)
    {
        _title  = title;
        _body   = body;
        _accent = accent;
    }

    /// <summary>대상에 트리거를 붙이고 내용을 설정한다. 이미 있으면 내용만 갱신.</summary>
    public static UITooltipTrigger Attach(GameObject target, string title, string body, Color accent)
    {
        if (target == null) return null;
        if (!target.TryGetComponent<UITooltipTrigger>(out var t))
            t = target.AddComponent<UITooltipTrigger>();
        t.Set(title, body, accent);
        return t;
    }

    private void OnDisable() => Hide();

    private void OnDestroy() => Hide();

    // ── Event Handlers ──────────────────────────────────────────

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (string.IsNullOrEmpty(_title) && string.IsNullOrEmpty(_body)) return;

        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;

        EnsureSurface(canvas);
        if (_surface == null) return;

        if (_titleText != null) { _titleText.text = _title ?? ""; _titleText.color = _accent; }
        if (_bodyText  != null) _bodyText.text = _body ?? "";
        if (_borderImg != null) _borderImg.color = _accent;

        // 표시면은 공유물이라 매번 이 트리거의 캔버스로 옮겨 붙인다(캔버스가 여럿일 수 있음).
        if (_surfaceRT.parent != canvas.transform)
            _surfaceRT.SetParent(canvas.transform, false);
        _surfaceRT.SetAsLastSibling();

        _surface.SetActive(true);
        PositionUnder((RectTransform)transform);
    }

    public void OnPointerExit(PointerEventData eventData) => Hide();

    // ── Private Methods ─────────────────────────────────────────

    private static void Hide()
    {
        if (_surface != null && _surface.activeSelf) _surface.SetActive(false);
    }

    /// <summary>대상 아래에 붙이고 화면 밖으로 나가지 않게 보정한다.</summary>
    private static void PositionUnder(RectTransform target)
    {
        if (target == null) return;

        LayoutRebuilder.ForceRebuildLayoutImmediate(_surfaceRT);

        target.GetWorldCorners(_corners);                 // 0:BL 1:TL 2:TR 3:BR
        float cx = (_corners[0].x + _corners[2].x) * 0.5f;
        float by = _corners[0].y;
        _surfaceRT.position = new Vector3(cx, by - GapFromTarget, 0f);

        // 화면 클램프 — Overlay 캔버스는 월드코너가 곧 스크린 픽셀.
        _surfaceRT.GetWorldCorners(_corners);
        float minX = _corners[0].x, maxX = _corners[2].x;
        float minY = _corners[0].y, maxY = _corners[1].y;

        float dx = 0f, dy = 0f;
        if (maxX > Screen.width)       dx  = Screen.width - maxX;
        if (minX + dx < 0f)            dx += -(minX + dx);
        if (minY + dy < 0f)            dy  = -minY;
        if (maxY + dy > Screen.height) dy += Screen.height - (maxY + dy);

        if (dx != 0f || dy != 0f)
            _surfaceRT.position += new Vector3(dx, dy, 0f);
    }

    private static void EnsureSurface(Canvas canvas)
    {
        if (_surface != null) return;

        _surface = new GameObject("@UITooltip", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup),
                                  typeof(ContentSizeFitter));
        _surfaceRT = (RectTransform)_surface.transform;
        _surfaceRT.SetParent(canvas.transform, false);
        _surfaceRT.pivot     = new Vector2(0.5f, 1f);   // 대상 아래로 늘어남
        _surfaceRT.anchorMin = _surfaceRT.anchorMax = new Vector2(0.5f, 0.5f);

        _borderImg = _surface.GetComponent<Image>();
        _borderImg.color = PanelBorder;
        _borderImg.raycastTarget = false;

        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        var frt = (RectTransform)fill.transform;
        frt.SetParent(_surfaceRT, false);
        frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
        frt.offsetMin = new Vector2(2f, 2f); frt.offsetMax = new Vector2(-2f, -2f);
        var fillImg = fill.GetComponent<Image>();
        fillImg.color = PanelFill;
        fillImg.raycastTarget = false;
        frt.SetAsFirstSibling();

        var vlg = _surface.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset((int)PadX, (int)PadX, (int)PadY, (int)PadY);
        vlg.spacing = 4f;
        vlg.childControlWidth = true;  vlg.childControlHeight = true;
        vlg.childForceExpandWidth = false; vlg.childForceExpandHeight = false;

        var fitter = _surface.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        _titleText = MakeLine("Title", 17f, FontStyles.Bold, PanelBorder);
        _bodyText  = MakeLine("Body",  14f, FontStyles.Normal, BodyColor);

        _surface.SetActive(false);
    }

    private static TMP_Text MakeLine(string name, float size, FontStyles style, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(_surfaceRT, false);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.fontSize      = size;
        tmp.fontStyle     = style;
        tmp.color         = color;
        tmp.raycastTarget = false;
        tmp.textWrappingMode = TextWrappingModes.Normal;

        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = MaxWidth;
        return tmp;
    }
}
