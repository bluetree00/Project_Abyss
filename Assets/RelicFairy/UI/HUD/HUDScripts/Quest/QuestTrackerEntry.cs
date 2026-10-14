using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

/// <summary>
/// 추적 위젯의 단일 퀘스트 행 View. 제목 + 진행도(+제한시간 카운트다운)를 표시하고,
/// 등장(페이드 인)·완료(금색 강조→페이드 아웃)·실패(적색 강조→페이드 아웃) 연출을 제공한다.
/// 진행도는 Quest 이벤트를, 남은 시간은 QuestTimerService를 조회해 갱신한다. unscaled time.
/// </summary>
public sealed class QuestTrackerEntry : MonoBehaviour
{
    // 톤 v2(10-11) — 프리팹 글꼴이 Noto 가변(Thin 마스터로 구워져 작은 글자가 끊긴다) 22 / 16이었다 →
    // 퀘스트 이름 Noto Medium 20 · 진행 Regular 17. 판 없이 3D 위에 뜨므로 부드러운 그림자. 색: 이름 잉크 · 완료 금(지금 일어남) · 실패 빨강.
    private const float TitleFontSize    = UITheme.Thin.Lead;   // 20
    private const float ProgressFontSize = UITheme.Thin.Body;   // 17
    private static readonly StringBuilder Sb = new StringBuilder(64);
    private static readonly Color TitleInk    = UITheme.Thin.Ink;
    private static readonly Color ProgressInk = UITheme.Thin.InkA(0.78f);
    private static readonly Color ClearColor  = UITheme.Thin.Gold;
    private static readonly Color FailColor   = UITheme.Thin.Danger;

    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text progressText;

    [Header("Timing (sec, unscaled)")]
    [SerializeField] private float fadeInDuration   = 0.25f;
    [SerializeField] private float holdDuration      = 0.5f;
    [SerializeField] private float fadeOutDuration   = 0.35f;

    private Quest _quest;
    private string _baseProgress = string.Empty;
    private int _lastShownSec = -1;

    public Quest BoundQuest => _quest;

    private void Awake()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
    }

    private void Update()
    {
        // 제한시간 퀘스트만 카운트다운 갱신(초 단위 변할 때만 → alloc 최소화)
        if (_quest == null || _quest.TimeLimit <= 0f) return;

        var svc = QuestTimerService.Instance;
        if (svc == null || !svc.TryGetRemaining(_quest, out float remaining)) return;

        int sec = Mathf.CeilToInt(remaining);
        if (sec == _lastShownSec) return;
        _lastShownSec = sec;
        ApplyProgressText();
    }

    private void OnDisable() => Unbind();

    public void Bind(Quest quest)
    {
        Unbind();

        _quest = quest;
        if (_quest == null) return;

        _quest.onTaskSuccessChanged += HandleTaskSuccessChanged;
        _quest.onNewTaskGroup       += HandleNewTaskGroup;

        _lastShownSec = -1;
        // 묶을 때마다 바른다 — 템플릿은 앱 부트 때 생겨 Awake엔 톤 v2 글꼴(Addressables)이 아직 없다.
        UITheme.StyleThinText(titleText, UIWeight.Medium, TitleFontSize, TitleInk, shadow: true);
        UITheme.StyleThinText(progressText, UIWeight.Regular, ProgressFontSize, ProgressInk, shadow: true);
        ResetVisual();
        Refresh();
    }

    public void Unbind()
    {
        if (_quest == null) return;

        _quest.onTaskSuccessChanged -= HandleTaskSuccessChanged;
        _quest.onNewTaskGroup       -= HandleNewTaskGroup;
        _quest = null;
    }

    /// <summary>알파 0 + 기본 색으로 초기화(페이드 인 전 상태).</summary>
    public void ResetVisual()
    {
        if (titleText != null) titleText.color = TitleInk;
        if (canvasGroup != null) canvasGroup.alpha = 0f;
    }

    /// <summary>애니메이션 없이 즉시 표시(초기 채우기용).</summary>
    public void ShowInstant()
    {
        if (canvasGroup != null) canvasGroup.alpha = 1f;
    }

    /// <summary>등장 페이드 인.</summary>
    public UniTask FadeInAsync(CancellationToken ct) => FadeAsync(0f, 1f, fadeInDuration, ct);

    /// <summary>완료 연출 — 제목 금색 강조 + "완료!" 유지 → 페이드 아웃.</summary>
    public UniTask PlayClearAsync(CancellationToken ct) => PlayOutcomeAsync(ClearColor, "완료!", ct);

    /// <summary>실패 연출 — 제목 적색 강조 + "실패!" 유지 → 페이드 아웃.</summary>
    public UniTask PlayFailAsync(CancellationToken ct) => PlayOutcomeAsync(FailColor, "실패!", ct);

    private async UniTask PlayOutcomeAsync(Color color, string label, CancellationToken ct)
    {
        if (titleText != null) titleText.color = color;
        if (progressText != null) progressText.text = label;

        await UniTask.Delay((int)(holdDuration * 1000f), DelayType.UnscaledDeltaTime, cancellationToken: ct);
        await FadeAsync(1f, 0f, fadeOutDuration, ct);
    }

    private void Refresh()
    {
        if (_quest == null) return;

        if (titleText != null)
            titleText.text = _quest.DisplayName;

        _baseProgress = BuildProgress(_quest);
        ApplyProgressText();
    }

    /// <summary>기본 진행도 + (제한시간이면) 남은 시간 표기.</summary>
    private void ApplyProgressText()
    {
        if (progressText == null) return;

        if (_quest != null && _quest.TimeLimit > 0f && _lastShownSec >= 0)
            progressText.text = _baseProgress + "  남은 " + _lastShownSec + "초";
        else
            progressText.text = _baseProgress;
    }

    private static string BuildProgress(Quest quest)
    {
        Sb.Clear();

        var tasks = quest.CurrentTaskGroup.Tasks;
        for (int i = 0; i < tasks.Count; i++)
        {
            var task = tasks[i];
            if (i > 0) Sb.Append('\n');

            string label = string.IsNullOrEmpty(task.Description) ? quest.Description : task.Description;
            Sb.Append(label);

            if (task.NeedSuccessToComplete > 1)
                Sb.Append("  (").Append(task.CurrentSuccess).Append('/').Append(task.NeedSuccessToComplete).Append(')');
        }

        return Sb.ToString();
    }

    private async UniTask FadeAsync(float from, float to, float duration, CancellationToken ct)
    {
        if (canvasGroup == null) return;

        float dur = Mathf.Max(0.0001f, duration);
        float t = 0f;
        canvasGroup.alpha = from;
        while (t < 1f)
        {
            ct.ThrowIfCancellationRequested();
            t += Time.unscaledDeltaTime / dur;
            canvasGroup.alpha = Mathf.Lerp(from, to, t);
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        canvasGroup.alpha = to;
    }

    private void HandleTaskSuccessChanged(Quest quest, Task task, int currentSuccess, int prevSuccess) => Refresh();
    private void HandleNewTaskGroup(Quest quest, TaskGroup current, TaskGroup prev) => Refresh();
}
