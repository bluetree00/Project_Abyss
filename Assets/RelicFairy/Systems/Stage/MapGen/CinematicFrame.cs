using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 시네마틱 레터박스 프레임 UI. 외부 연구·문서 근거로 구성:
///  - Fagerholt &amp; Lorentzon (2009) "Beyond the HUD": 연출 중 non-diegetic HUD 억제, 프레임 강조
///  - "Framing the Drama: Cinematic Letterboxing in Games": 레터박스 바 = 연출 전환 신호 + 시선 집중
///  - Unreal Cinematic Viewport: 목표 종횡비(기본 2.39:1)로 바 높이 산출, 삼분할/세이프영역 구도
/// 레터박스 바(상/하) + 미세 Dim + 삼분할 하단선 타이틀(선택)을 슬라이드 인/아웃으로 표시한다.
/// ScreenFade와 동일하게 DontDestroyOnLoad 캔버스 1개를 재사용, timeScale 영향 없는 unscaled 시간으로 동작.
/// </summary>
public static class CinematicFrame
{
    private const int   SortingOrder        = 9000;   // ScreenFade(10000) 와이프보다 아래
    private const float DefaultAspect       = 2.39f;  // 시네마스코프
    private const float TitleLineFromBottom = 0.28f;  // 삼분할 하단선 부근
    private const float SafeAreaMargin      = 0.10f;  // 좌우 세이프영역(타이틀 폭)

    private static Canvas          _canvas;
    private static RectTransform   _topBar;
    private static RectTransform   _bottomBar;
    private static CanvasGroup     _dim;
    private static TextMeshProUGUI _title;

    private static float _shown;     // 0=숨김 ~ 1=완전 표시
    private static float _barFrac;   // 현재 목표 바 높이 비율(각 바, 0~0.5)
    private static float _dimTarget; // 목표 Dim 알파

    /// <summary>레터박스 프레임을 슬라이드 인. title이 있으면 삼분할 하단선에 표시한다.</summary>
    public static async UniTask ShowAsync(
        float duration, AnimationCurve ease, CancellationToken ct = default,
        float aspect = DefaultAspect, float dimAlpha = 0.25f, string title = null)
    {
        Ensure();
        _canvas.enabled = true;
        _barFrac   = BarFraction(aspect);
        _dimTarget = Mathf.Clamp01(dimAlpha);

        if (_title != null)
        {
            bool hasTitle = !string.IsNullOrEmpty(title);
            _title.gameObject.SetActive(hasTitle);
            if (hasTitle) _title.text = title;
        }

        await AnimateTo(1f, duration, ease, ct);
    }

    /// <summary>레터박스 프레임을 슬라이드 아웃 후 캔버스를 끈다.</summary>
    public static async UniTask HideAsync(float duration, AnimationCurve ease, CancellationToken ct = default)
    {
        if (_canvas == null) return;
        await AnimateTo(0f, duration, ease, ct);
        _canvas.enabled = false;
    }

    // ── 내부 ──────────────────────────────────────────────────────────────

    /// <summary>목표 종횡비에서 각 레터박스 바의 높이 비율을 산출(화면이 더 넓으면 0).</summary>
    private static float BarFraction(float aspect)
    {
        if (aspect <= 0f) return 0f;
        float screen = (float)Screen.width / Mathf.Max(1, Screen.height);
        return Mathf.Clamp01(1f - screen / aspect) * 0.5f;
    }

    private static void Ensure()
    {
        if (_canvas != null) return;

        var go = new GameObject("@CinematicFrame");
        UnityEngine.Object.DontDestroyOnLoad(go);

        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = SortingOrder;

        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;

        // Dim (전체 화면, 바·타이틀보다 뒤)
        var dimGO = new GameObject("Dim");
        dimGO.transform.SetParent(go.transform, false);
        var dimImg = dimGO.AddComponent<Image>();
        dimImg.color         = Color.black;
        dimImg.raycastTarget = false;
        Stretch(dimImg.rectTransform, Vector2.zero, Vector2.one);
        _dim = dimGO.AddComponent<CanvasGroup>();
        _dim.alpha          = 0f;
        _dim.blocksRaycasts = false;
        _dim.interactable   = false;

        _topBar    = CreateBar("TopBar", go.transform);
        _bottomBar = CreateBar("BottomBar", go.transform);

        var titleGO = new GameObject("Title");
        titleGO.transform.SetParent(go.transform, false);
        _title = titleGO.AddComponent<TextMeshProUGUI>();
        _title.alignment    = TextAlignmentOptions.Center;
        _title.fontSize     = 54f;
        _title.color        = Color.white;
        _title.raycastTarget = false;
        var trt = _title.rectTransform;
        trt.anchorMin = new Vector2(SafeAreaMargin, TitleLineFromBottom);
        trt.anchorMax = new Vector2(1f - SafeAreaMargin, TitleLineFromBottom);
        trt.pivot     = new Vector2(0.5f, 0.5f);
        trt.offsetMin = Vector2.zero;
        trt.offsetMax = Vector2.zero;
        titleGO.SetActive(false);

        ApplyShown(0f);
        _canvas.enabled = false;
    }

    private static RectTransform CreateBar(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color         = Color.black;
        img.raycastTarget = false;
        return img.rectTransform;
    }

    private static void Stretch(RectTransform rt, Vector2 min, Vector2 max)
    {
        rt.anchorMin = min; rt.anchorMax = max;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    private static async UniTask AnimateTo(float target, float duration, AnimationCurve ease, CancellationToken ct)
    {
        if (duration <= 0f) { ApplyShown(target); return; }

        float start = _shown, t = 0f;
        while (t < duration)
        {
            ct.ThrowIfCancellationRequested();
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / duration);
            if (ease != null) k = ease.Evaluate(k);
            ApplyShown(Mathf.LerpUnclamped(start, target, k));
            await UniTask.Yield(PlayerLoopTiming.Update);
        }
        ApplyShown(target);
    }

    /// <summary>현재 표시 비율(shown)을 바 높이·Dim·타이틀 알파에 일괄 반영한다.</summary>
    private static void ApplyShown(float shown)
    {
        _shown = Mathf.Clamp01(shown);
        float h = _barFrac * _shown;

        // 상단 바: 위쪽 가장자리에서 아래로 자란다
        _topBar.anchorMin = new Vector2(0f, 1f - h);
        _topBar.anchorMax = new Vector2(1f, 1f);
        _topBar.offsetMin = Vector2.zero; _topBar.offsetMax = Vector2.zero;

        // 하단 바: 아래쪽 가장자리에서 위로 자란다
        _bottomBar.anchorMin = new Vector2(0f, 0f);
        _bottomBar.anchorMax = new Vector2(1f, h);
        _bottomBar.offsetMin = Vector2.zero; _bottomBar.offsetMax = Vector2.zero;

        if (_dim != null) _dim.alpha = _dimTarget * _shown;

        if (_title != null && _title.gameObject.activeSelf)
        {
            var c = _title.color; c.a = _shown; _title.color = c;
        }
    }
}
