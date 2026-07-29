using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 퀘스트/업적 완료 토스트의 View. 단일 토스트의 표시·페이드만 담당한다.
/// 큐잉/구독은 QuestNotifierPresenter가 소유한다 (Provider→Presenter→View).
/// Time.timeScale 영향을 받지 않도록 unscaled time 사용 (일시정지 중에도 표시).
/// </summary>
public sealed class QuestNotifierView : MonoBehaviour
{
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text labelText;

    [Header("Timing (sec, unscaled)")]
    [SerializeField] private float fadeInDuration = 0.25f;
    [SerializeField] private float holdDuration   = 2.0f;
    [SerializeField] private float fadeOutDuration = 0.4f;

    private void Awake()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup != null)
            canvasGroup.alpha = 0f;
    }

    /// <summary>토스트 1건을 페이드 인 → 유지 → 페이드 아웃. 취소 시 즉시 숨김.</summary>
    public async UniTask PlayAsync(Sprite icon, string title, string label, CancellationToken ct)
    {
        Apply(icon, title, label);

        try
        {
            await FadeAsync(0f, 1f, fadeInDuration, ct);
            await UniTask.Delay((int)(holdDuration * 1000f), DelayType.UnscaledDeltaTime, cancellationToken: ct);
            await FadeAsync(1f, 0f, fadeOutDuration, ct);
        }
        catch (System.OperationCanceledException)
        {
            if (canvasGroup != null) canvasGroup.alpha = 0f;
            throw;
        }
    }

    private void Apply(Sprite icon, string title, string label)
    {
        if (iconImage != null)
        {
            iconImage.sprite  = icon;
            iconImage.enabled = icon != null;
        }
        if (titleText != null) titleText.text = title;
        if (labelText != null) labelText.text = label;
    }

    private async UniTask FadeAsync(float from, float to, float duration, CancellationToken ct)
    {
        if (canvasGroup == null) return;

        float dur = Mathf.Max(0.0001f, duration);
        float t = 0f;
        while (t < 1f)
        {
            ct.ThrowIfCancellationRequested();
            t += Time.unscaledDeltaTime / dur;
            canvasGroup.alpha = Mathf.Lerp(from, to, t);
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        canvasGroup.alpha = to;
    }
}
