using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 켜고 끄는 화면의 공통 박자 — 의뢰서 §3 「모달 = 페이드 + 스케일(0.15초) · 과함 지양」(09-28 UI 톤 통일).
/// 설정 · ESC · 로비 · 세이브 슬롯 · 루프 선택처럼 <see cref="UI_Popup"/>을 거치지 않는 화면이
/// SetActive로 <b>순간 등장 · 순간 소멸</b>하던 것을 한 박자로 맞춘다.
///
/// <para>화면 루트에 붙인다. 막(루트)은 페이드만, 판(<see cref="Panel"/>)은 0.97 → 1로 살짝 커진다 —
/// 막까지 배율을 주면 화면 가장자리가 움츠러드는 게 보인다. 시간이 멈춘 동안에도 돈다(unscaled).</para>
/// </summary>
[DisallowMultipleComponent]
public sealed class UIFader : MonoBehaviour
{
    // ── Constants ────────────────────────────────────────────
    public const float OpenSec        = 0.16f;
    public const float CloseSec       = 0.12f;
    public const float PanelFromScale = 0.97f;
    public const float PanelToScale   = 0.98f;   // 닫힐 때

    // ── Private ──────────────────────────────────────────────
    private CanvasGroup             _cg;
    private RectTransform           _panel;
    private CancellationTokenSource _cts;
    private bool                    _hiding;

    // ── Properties ───────────────────────────────────────────
    /// <summary>켜져 있고 닫히는 중이 아니다.</summary>
    public bool Visible => gameObject.activeSelf && !_hiding;

    // ── Lifecycle ────────────────────────────────────────────
    private void Awake() => _cg = gameObject.GetOrAddComponent<CanvasGroup>();

    private void OnDestroy() => Cancel();

    // ── Public Methods ───────────────────────────────────────

    /// <summary>루트에 붙이고(이미 있으면 그대로) 판을 지정한다. 판은 없어도 된다(페이드만).</summary>
    public static UIFader On(GameObject root, RectTransform panel = null)
    {
        var f = root.GetOrAddComponent<UIFader>();
        if (panel != null) f._panel = panel;
        return f;
    }

    /// <summary>켜면서 페이드 인. 닫히는 중이었으면 지금 밝기에서 되돌아온다.</summary>
    public void Show()
    {
        bool wasOff = !gameObject.activeSelf;
        gameObject.SetActive(true);
        if (_cg == null) _cg = gameObject.GetOrAddComponent<CanvasGroup>();
        _hiding = false;
        _cg.blocksRaycasts = true;
        _cg.interactable   = true;
        if (wasOff) _cg.alpha = 0f;
        Run(_cg.alpha, 1f, OpenSec, PanelFromScale, 1f, null);
    }

    /// <summary>페이드 아웃 뒤 끈다. 도는 동안 클릭은 막는다. onHidden은 꺼진 뒤 부른다.</summary>
    public void Hide(Action onHidden = null)
    {
        if (!gameObject.activeSelf) { onHidden?.Invoke(); return; }
        if (_cg == null) _cg = gameObject.GetOrAddComponent<CanvasGroup>();
        _hiding = true;
        _cg.blocksRaycasts = false;
        _cg.interactable   = false;
        Run(_cg.alpha, 0f, CloseSec, 1f, PanelToScale, () =>
        {
            _hiding = false;
            gameObject.SetActive(false);
            onHidden?.Invoke();
        });
    }

    /// <summary>박자 없이 곧바로 끈다(씬 정리 등).</summary>
    public void HideImmediate()
    {
        Cancel();
        _hiding = false;
        if (_panel != null) _panel.localScale = Vector3.one;
        gameObject.SetActive(false);
    }

    // ── Private Methods ──────────────────────────────────────

    private void Run(float from, float to, float dur, float scaleFrom, float scaleTo, Action done)
    {
        Cancel();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        RunAsync(from, to, dur, scaleFrom, scaleTo, done, _cts.Token).Forget();
    }

    private async UniTaskVoid RunAsync(float from, float to, float dur, float scaleFrom, float scaleTo,
                                       Action done, CancellationToken ct)
    {
        try
        {
            // 이어 가는 경우(닫히다 다시 열림)는 남은 거리만큼만 걸린다.
            float span = Mathf.Abs(to - from);
            float len  = dur * Mathf.Max(0.25f, span);
            for (float t = 0f; t < 1f; )
            {
                t = Mathf.Min(1f, t + Time.unscaledDeltaTime / len);
                float e = 1f - (1f - t) * (1f - t) * (1f - t);   // 감속, 튕김 없음
                _cg.alpha = Mathf.Lerp(from, to, e);
                if (_panel != null) _panel.localScale = Vector3.one * Mathf.Lerp(scaleFrom, scaleTo, e);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            _cg.alpha = to;
            if (_panel != null) _panel.localScale = Vector3.one;
            done?.Invoke();
        }
        catch (OperationCanceledException) { }
    }

    private void Cancel()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}
