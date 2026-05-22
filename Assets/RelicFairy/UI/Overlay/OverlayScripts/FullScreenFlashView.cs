//============================================================
// FullScreenFlashView.cs
// - 전체화면 번쩍임 연출 (블로그 ⑧ Flash — 보완 요소)
// - 히트 시 지정 color로 즉시 peak → 감쇠 (TransitionOverlay 패턴 복제)
// - Time.timeScale(Hit-Stop)과 독립적으로 동작 — unscaledDeltaTime 사용
//============================================================
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace RelicFairy.UI.Overlay
{
    public sealed class FullScreenFlashView : MonoBehaviour
    {
        // ── SerializeField ────────────────────────────────────────────
        [Header("Refs")]
        [SerializeField] private Image       image;
        [SerializeField] private CanvasGroup canvasGroup;

        [Header("Shape")]
        [SerializeField] private AnimationCurve decayCurve = new AnimationCurve(
            new Keyframe(0f,   0f),
            new Keyframe(0.1f, 1f),
            new Keyframe(1f,   0f));

        // ── Private ───────────────────────────────────────────────────
        private CancellationTokenSource _cts;

        // ── Lifecycle ─────────────────────────────────────────────────
        private void Awake()
        {
            if (image == null) image = GetComponent<Image>();
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();

            if (canvasGroup != null)
            {
                canvasGroup.alpha          = 0f;
                canvasGroup.blocksRaycasts = false;
                canvasGroup.interactable   = false;
            }
            if (image != null) image.raycastTarget = false;
        }

        private void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        // ── Public Methods ────────────────────────────────────────────
        /// <summary>전체화면을 color로 번쩍이고 duration 동안 감쇠.</summary>
        public void Flash(Color color, float duration, float peakAlpha = 0.3f)
        {
            if (image == null || canvasGroup == null) return;
            if (duration <= 0f) return;

            // color는 RGB만 반영, alpha는 CanvasGroup으로 제어
            var rgb = color; rgb.a = 1f;
            image.color = rgb;

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(
                this.GetCancellationTokenOnDestroy());

            PlayAsync(Mathf.Clamp01(peakAlpha), duration, _cts.Token).Forget();
        }

        // ── Private Methods ───────────────────────────────────────────
        private async UniTaskVoid PlayAsync(float peak, float duration, CancellationToken token)
        {
            try
            {
                float t = 0f;
                while (t < duration)
                {
                    token.ThrowIfCancellationRequested();

                    float n = Mathf.Clamp01(t / duration);
                    canvasGroup.alpha = decayCurve.Evaluate(n) * peak;

                    t += Time.unscaledDeltaTime;
                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                }

                canvasGroup.alpha = 0f;
            }
            catch (OperationCanceledException)
            {
                if (canvasGroup != null) canvasGroup.alpha = 0f;
            }
        }
    }
}
