using Cysharp.Threading.Tasks;
using System.Threading;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Button GameObject에 자동 부착되는 Hover/Press 스케일 피드백.
/// UI_Popup.Init()이 자식 Button을 순회하며 자동 추가한다.
/// Time.unscaledDeltaTime 기반 — 게임 일시정지 중에도 동작.
/// </summary>
[DisallowMultipleComponent]
public sealed class UIButtonFeedback : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler,  IPointerUpHandler
{
    // ── Constants ────────────────────────────────────────────
    private const float HoverScale    = 1.07f;
    private const float PressScale    = 0.91f;
    private const float HoverDuration = 0.08f;
    private const float PressDuration = 0.05f;
    private const float BackDuration  = 0.12f;

    // ── Private ──────────────────────────────────────────────
    private RectTransform _rt;
    private Vector3       _baseScale;
    private bool          _hovered;
    private CancellationTokenSource _cts;

    // ── Lifecycle ─────────────────────────────────────────────

    private void Awake()
    {
        _rt        = GetComponent<RectTransform>();
        _baseScale = _rt != null ? _rt.localScale : Vector3.one;
    }

    private void OnDisable()
    {
        CancelCts();
        if (_rt != null) _rt.localScale = _baseScale;
        _hovered = false;
    }

    private void OnDestroy() => CancelCts();

    // ── Pointer Events ────────────────────────────────────────

    public void OnPointerEnter(PointerEventData _)
    {
        _hovered = true;
        TweenTo(_baseScale * HoverScale, HoverDuration);
    }

    public void OnPointerExit(PointerEventData _)
    {
        _hovered = false;
        TweenTo(_baseScale, BackDuration);
    }

    public void OnPointerDown(PointerEventData _)  => TweenTo(_baseScale * PressScale, PressDuration);
    public void OnPointerUp(PointerEventData _)    => TweenTo(_hovered ? _baseScale * HoverScale : _baseScale, BackDuration);

    // ── Tween ─────────────────────────────────────────────────

    private void TweenTo(Vector3 target, float duration)
    {
        CancelCts();
        _cts = new CancellationTokenSource();
        ScaleAsync(_rt.localScale, target, duration, _cts.Token).Forget();
    }

    private async UniTaskVoid ScaleAsync(Vector3 from, Vector3 to, float duration, CancellationToken ct)
    {
        float t = 0f;
        while (t < 1f)
        {
            t = Mathf.Min(t + Time.unscaledDeltaTime / Mathf.Max(duration, 0.001f), 1f);
            if (_rt != null) _rt.localScale = Vector3.Lerp(from, to, EaseOut(t));
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
            if (ct.IsCancellationRequested) return;
        }
        if (_rt != null) _rt.localScale = to;
    }

    private void CancelCts()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private static float EaseOut(float t) => 1f - (1f - t) * (1f - t);
}
