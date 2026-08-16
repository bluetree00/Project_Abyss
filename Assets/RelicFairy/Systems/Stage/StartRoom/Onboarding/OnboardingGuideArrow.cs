using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 온보딩 목표 지시 화살표. 대상이 화면 안이면 **3D 월드 화살표**(대상 위 부유 + 바운스 + 빌보드),
/// 화면 밖이면 **화면 가장자리 UI 인디케이터**로 방향을 가리킨다. (Director가 SetTarget 호출)
///
/// worldArrow / screenIndicator 미할당 시 **TMP 글리프(▼/➤)로 절차 생성**(수동 제작 불필요).
/// </summary>
public sealed class OnboardingGuideArrow : MonoBehaviour
{
    private const float ScreenEdgePadding = 64f;

    [Header("폰트 (미지정 시 TMP 기본)")]
    [SerializeField] private TMP_FontAsset font;

    [Header("3D 월드 화살표 (대상 위) — 미할당 시 자동 생성")]
    [SerializeField] private Transform worldArrow;
    [SerializeField] private float worldHeight = 2.6f;
    [SerializeField] private float bounceAmplitude = 0.25f;
    [SerializeField] private float bounceSpeed = 3f;

    [Header("화면 가장자리 인디케이터 — 미할당 시 자동 생성")]
    [SerializeField] private RectTransform screenIndicator;

    private Camera _cam;
    private Transform _target;

    public bool HasTarget => _target != null;

    private void Awake()
    {
        _cam = Camera.main;
        if (worldArrow == null) worldArrow = CreateWorldArrow();
        if (screenIndicator == null) screenIndicator = CreateScreenIndicator();
        SetWorldArrowActive(false);
        SetScreenIndicatorActive(false);
    }

    private void LateUpdate()
    {
        if (_target == null) return;
        if (_cam == null) { _cam = Camera.main; if (_cam == null) return; }

        Vector3 targetPos = _target.position;
        Vector3 sp = _cam.WorldToScreenPoint(targetPos);
        bool inFront = sp.z > 0f;
        bool onScreen = inFront && sp.x >= 0f && sp.x <= Screen.width && sp.y >= 0f && sp.y <= Screen.height;

        if (onScreen)
        {
            ShowWorldArrow(targetPos);
            SetScreenIndicatorActive(false);
        }
        else
        {
            SetWorldArrowActive(false);
            ShowScreenIndicator(sp, inFront);
        }
    }

    // ── Public API ────────────────────────────────────────────

    /// <summary>화살표가 가리킬 대상 지정(null이면 숨김).</summary>
    public void SetTarget(Transform target)
    {
        _target = target;
        if (target == null) Clear();
    }

    public void Clear()
    {
        _target = null;
        SetWorldArrowActive(false);
        SetScreenIndicatorActive(false);
    }

    // ── 표시 ──────────────────────────────────────────────────

    private void ShowWorldArrow(Vector3 targetPos)
    {
        if (worldArrow == null) return;
        SetWorldArrowActive(true);

        float bounce = Mathf.Sin(Time.unscaledTime * bounceSpeed) * bounceAmplitude;
        worldArrow.position = targetPos + Vector3.up * (worldHeight + bounce);
        if (_cam != null) worldArrow.rotation = _cam.transform.rotation;   // 빌보드(▼ 글리프가 아래를 가리킴)
    }

    private void ShowScreenIndicator(Vector3 screenPoint, bool inFront)
    {
        if (screenIndicator == null) return;
        SetScreenIndicatorActive(true);

        Vector2 center = new Vector2(Screen.width, Screen.height) * 0.5f;
        Vector2 dir = (Vector2)screenPoint - center;
        if (!inFront) dir = -dir;          // 뒤쪽이면 반전
        if (dir.sqrMagnitude < 0.001f) dir = Vector2.up;
        dir.Normalize();

        float maxX = Screen.width * 0.5f - ScreenEdgePadding;
        float maxY = Screen.height * 0.5f - ScreenEdgePadding;
        float scale = Mathf.Min(
            maxX / Mathf.Max(Mathf.Abs(dir.x), 0.0001f),
            maxY / Mathf.Max(Mathf.Abs(dir.y), 0.0001f));

        screenIndicator.position = center + dir * scale;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        screenIndicator.rotation = Quaternion.Euler(0f, 0f, angle);   // ➤(+X 기준)가 방향을 가리킴
    }

    // ── 절차 생성 ─────────────────────────────────────────────

    private Transform CreateWorldArrow()
    {
        var go = new GameObject("GuideArrow3D");
        go.transform.SetParent(transform, false);
        var tmp = go.AddComponent<TextMeshPro>();
        ApplyFont(tmp);
        tmp.text = "▼";
        tmp.fontSize = 10f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(1f, 0.85f, 0.2f);
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        return go.transform;
    }

    private RectTransform CreateScreenIndicator()
    {
        var canvasGo = new GameObject("GuideArrowCanvas", typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = UISortingOrder.HudIndicator;

        // 프로젝트 캔버스 기준 준수 — 1920×1080, Scale With Screen Size, Match 0.5
        // 화살표 배치는 RectTransform.position(=화면 픽셀)으로 계산하므로 스케일러와 무관하다.
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;

        var go = new GameObject("GuideArrowUI", typeof(RectTransform));
        go.transform.SetParent(canvasGo.transform, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        ApplyFontUI(tmp);
        tmp.text = "▶";
        tmp.fontSize = 56f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(1f, 0.85f, 0.2f);
        tmp.raycastTarget = false;
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(64f, 64f);
        return rt;
    }

    private void ApplyFont(TextMeshPro tmp)
    {
        var f = font != null ? font : TMP_Settings.defaultFontAsset;
        if (f != null) tmp.font = f;
    }

    private void ApplyFontUI(TextMeshProUGUI tmp)
    {
        var f = font != null ? font : TMP_Settings.defaultFontAsset;
        if (f != null) tmp.font = f;
    }

    private void SetWorldArrowActive(bool on)
    {
        if (worldArrow != null && worldArrow.gameObject.activeSelf != on)
            worldArrow.gameObject.SetActive(on);
    }

    private void SetScreenIndicatorActive(bool on)
    {
        if (screenIndicator != null && screenIndicator.gameObject.activeSelf != on)
            screenIndicator.gameObject.SetActive(on);
    }
}
