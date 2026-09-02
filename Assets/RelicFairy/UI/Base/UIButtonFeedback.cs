using Cysharp.Threading.Tasks;
using System.Threading;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Button GameObject에 자동 부착되는 Hover/Press 스케일 피드백.
/// UI_Popup.Init()이 자식 Button을 순회하며 자동 추가한다.
/// Time.unscaledDeltaTime 기반 — 게임 일시정지 중에도 동작.
/// </summary>
/// <summary>
/// <b>자기 배율을 직접 관리하는</b> UI가 다는 표식. 이걸 단 오브젝트에는
/// <see cref="UI_Popup.Init"/>이 <see cref="UIButtonFeedback"/>을 자동으로 붙이지 않는다.
///
/// <para>둘이 같은 Transform의 localScale을 두고 다투면, 마우스를 뗄 때 피드백이
/// Awake 시점 크기(1.0)로 되돌려 <b>선택 강조가 지워진다</b>. 서약 조립 카드가 그랬다.</para>
/// </summary>
public interface IOwnsButtonScale { }

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
    private Button        _btn;
    private Vector3       _baseScale;
    private bool          _hovered;
    private CancellationTokenSource _cts;

    // ── Lifecycle ─────────────────────────────────────────────

    private void Awake()
    {
        _rt        = GetComponent<RectTransform>();
        _baseScale = _rt != null ? _rt.localScale : Vector3.one;
        _btn       = GetComponent<Button>();
    }

    private void OnDisable()
    {
        CancelCts();
        if (_rt != null) _rt.localScale = _baseScale;
        _hovered = false;
    }

    private void OnDestroy() => CancelCts();

    // ── Pointer Events ────────────────────────────────────────

    /// <summary>
    /// 비활성(interactable=false) 버튼은 <b>반응하지 않아야 한다</b>.
    /// 커지고 눌리는 연출만 나오고 아무 일도 안 일어나면 "눌리는 버튼"으로 읽혀
    /// 플레이어가 같은 자리를 계속 누르게 된다.
    /// </summary>
    private bool Live => _btn == null || _btn.interactable;

    public void OnPointerEnter(PointerEventData _)
    {
        if (!Live) return;
        _hovered = true;
        TweenTo(_baseScale * HoverScale, HoverDuration);
    }

    public void OnPointerExit(PointerEventData _)
    {
        _hovered = false;
        TweenTo(_baseScale, BackDuration);
    }

    // 나가기·떼기는 조건 없이 원래 크기로 돌린다 — 호버 중 비활성이 되어도 커진 채 굳지 않게.
    public void OnPointerDown(PointerEventData _)  { if (Live) TweenTo(_baseScale * PressScale, PressDuration); }
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
