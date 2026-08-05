using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 화면 전환 유틸. 단순 알파 페이드(Out/In)와 방향성 와이프(Cover/Reveal)를 제공한다.
/// 와이프는 컬러 패널을 진행 방향으로 슬라이드시켜 '이동' 느낌을 준다(Congruence 원칙).
/// DontDestroyOnLoad 캔버스 1개를 재사용, timeScale 영향 없는 unscaled 시간으로 동작.
/// </summary>
public static class ScreenFade
{
    private const int SortingOrder = UISortingOrder.ScreenFade;

    private static readonly Vector2 FullMin = Vector2.zero;
    private static readonly Vector2 FullMax = Vector2.one;

    private static CanvasGroup _cg;
    private static Image       _img;

    // ── 알파 페이드 ──
    public static UniTask Out(float duration, CancellationToken ct = default) => FadeTo(1f, duration, ct);
    public static UniTask In(float duration, CancellationToken ct = default)  => FadeTo(0f, duration, ct);

    // ── 방향성 와이프 ──
    /// <summary>패널이 -dir 쪽 화면 밖에서 들어와 화면을 덮는다. color = 다음 방 종류색.</summary>
    public static UniTask CoverAsync(Vector2 dir, Color color, float duration, AnimationCurve ease, CancellationToken ct = default)
        => Slide(FullMin - dir, FullMax - dir, FullMin, FullMax, color, duration, ease, ct, blockAfter: true);

    /// <summary>덮인 상태에서 패널이 dir 방향으로 빠져나가 새 화면을 드러낸다.</summary>
    public static UniTask RevealAsync(Vector2 dir, float duration, AnimationCurve ease, CancellationToken ct = default)
        => Slide(FullMin, FullMax, FullMin + dir, FullMax + dir, null, duration, ease, ct, blockAfter: false);

    // ── 내부 ──

    private static void Ensure()
    {
        if (_cg != null) return;

        var go = new GameObject("@ScreenFade");
        Object.DontDestroyOnLoad(go);

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;

        _cg = go.AddComponent<CanvasGroup>();
        _cg.alpha          = 0f;
        _cg.blocksRaycasts = false;
        _cg.interactable   = false;

        var imgGO = new GameObject("Cover");
        imgGO.transform.SetParent(go.transform, false);
        _img = imgGO.AddComponent<Image>();
        _img.color         = Color.black;
        _img.raycastTarget = false;
        var rt = _img.rectTransform;
        rt.anchorMin = FullMin; rt.anchorMax = FullMax;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    private static async UniTask FadeTo(float target, float duration, CancellationToken ct)
    {
        Ensure();
        // 알파 페이드(Out/In)는 항상 검정 암전 — 직전 방향성 와이프(Slide)가 _img.color에 남긴
        // 방 종류색(예: 보스방 어두운 빨강)을 물려받아 빨갛게 페이드되는 누수를 차단.
        _img.color = Color.black;
        var rt = _img.rectTransform;
        rt.anchorMin = FullMin; rt.anchorMax = FullMax; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        _cg.blocksRaycasts = target > 0.5f;

        if (duration <= 0f) { _cg.alpha = target; if (target <= 0f) _cg.blocksRaycasts = false; return; }

        float start = _cg.alpha, t = 0f;
        while (t < duration)
        {
            ct.ThrowIfCancellationRequested();
            t += Time.unscaledDeltaTime;
            _cg.alpha = Mathf.Lerp(start, target, t / duration);
            await UniTask.Yield(PlayerLoopTiming.Update);
        }
        _cg.alpha = target;
        if (target <= 0f) _cg.blocksRaycasts = false;
    }

    private static async UniTask Slide(
        Vector2 fromMin, Vector2 fromMax, Vector2 toMin, Vector2 toMax,
        Color? color, float duration, AnimationCurve ease, CancellationToken ct, bool blockAfter)
    {
        Ensure();
        _cg.alpha = 1f; // 와이프는 알파 1, 위치(앵커)로만 가림
        if (color.HasValue) _img.color = color.Value;
        _cg.blocksRaycasts = true;
        var rt = _img.rectTransform;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

        if (duration <= 0f) { rt.anchorMin = toMin; rt.anchorMax = toMax; _cg.blocksRaycasts = blockAfter; return; }

        float t = 0f;
        while (t < duration)
        {
            ct.ThrowIfCancellationRequested();
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / duration);
            if (ease != null) k = ease.Evaluate(k);
            rt.anchorMin = Vector2.LerpUnclamped(fromMin, toMin, k);
            rt.anchorMax = Vector2.LerpUnclamped(fromMax, toMax, k);
            await UniTask.Yield(PlayerLoopTiming.Update);
        }
        rt.anchorMin = toMin; rt.anchorMax = toMax;
        _cg.blocksRaycasts = blockAfter;
    }
}
