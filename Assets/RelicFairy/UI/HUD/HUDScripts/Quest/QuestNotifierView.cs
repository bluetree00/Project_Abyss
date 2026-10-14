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
    // 톤 v2(10-11 비교 리서치 A §3-4 P3) — 제목 28 · 줄 18(DNF) → 제목 Noto Medium 24 · 줄 Regular 17. 위치 · 판 크기는 그대로.
    // 금은 「준비됨 · 누를 것」 한 뜻이라 글자에서 뺐다(예전 제목 = 금) — 위계는 크기 · 굵기로.
    private const float TitleFontSize = UITheme.Thin.Title;   // 24
    private const float LabelFontSize = UITheme.Thin.Body;    // 17
    private static readonly Color LabelInk = UITheme.Thin.InkA(0.72f);

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

        // 판 = 자식 Background — 루트엔 Image가 없어 예전 StylePanel(루트)은 한 번도 걸리지 않았다(평평한 남색 판 그대로였다).
        // 톤 v2: 인디고 글래스 채움 + 1px 금 30% 테(평소 선 · 각진 모서리 그대로). 글꼴은 Apply가 띄울 때마다 바른다
        // (@UIRoot는 앱 부트 때 생겨 Awake엔 톤 v2 글꼴이 아직 없다).
        var bg = transform.Find("Background");
        if (bg != null && bg.TryGetComponent<Image>(out var plate))
        {
            plate.color = UITheme.Glass;
            if (bg.Find("ThinEdge") == null)
                UIThinBox.Create((RectTransform)bg, "ThinEdge", Vector2.zero, Vector2.one, 0f,
                                 UITheme.Thin.LineGold, Color.clear, Color.clear);
        }
    }

    /// <summary>토스트 1건을 페이드 인 → 유지 → 페이드 아웃. 취소 시 즉시 숨김.</summary>
    public async UniTask PlayAsync(Sprite icon, string title, string label, CancellationToken ct)
    {
        Apply(icon, title, label);
        Managers.Sound?.PlayUiEvent(SoundEvent.QuestDone);   // 목록에 체크하는 소리(10-12 Q1)

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
        // 어두운 판 위라 그림자는 두지 않는다(획만 두꺼워진다).
        UITheme.StyleThinText(titleText, UIWeight.Medium, TitleFontSize, UITheme.Thin.Ink);
        UITheme.StyleThinText(labelText, UIWeight.Regular, LabelFontSize, LabelInk);
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
