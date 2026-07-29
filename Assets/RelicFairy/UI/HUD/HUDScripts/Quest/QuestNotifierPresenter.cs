using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 퀘스트/업적 완료 알림 토스트 Presenter.
/// 전역 QuestManager(Managers.Quest)의 완료 이벤트를 OnEnable에서 자가 구독하고,
/// 동시 다발 완료를 큐로 직렬화해 QuestNotifierView로 순차 표시한다.
/// </summary>
public sealed class QuestNotifierPresenter : MonoBehaviour
{
    private readonly struct ToastData
    {
        public readonly Sprite Icon;
        public readonly string Title;
        public readonly string Label;
        public ToastData(Sprite icon, string title, string label)
        {
            Icon = icon; Title = title; Label = label;
        }
    }

    [SerializeField] private QuestNotifierView view;

    private readonly Queue<ToastData> _queue = new Queue<ToastData>();
    private QuestManager _quest;
    private CancellationTokenSource _cts;
    private bool _isProcessing;

    private void Awake()
    {
        if (view == null)
            view = GetComponentInChildren<QuestNotifierView>(true);
    }

    private void OnEnable()
    {
        _cts = new CancellationTokenSource();

        _quest = Managers.Quest;
        if (_quest != null)
        {
            _quest.onQuestCompleted       += HandleQuestCompleted;
            _quest.onAchievementCompleted += HandleAchievementCompleted;
        }
    }

    private void OnDisable()
    {
        if (_quest != null)
        {
            _quest.onQuestCompleted       -= HandleQuestCompleted;
            _quest.onAchievementCompleted -= HandleAchievementCompleted;
            _quest = null;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        _queue.Clear();
        _isProcessing = false;
    }

    private void HandleQuestCompleted(Quest quest)
        => Enqueue(quest, "퀘스트 완료");

    private void HandleAchievementCompleted(Quest achievement)
        => Enqueue(achievement, "업적 달성");

    private void Enqueue(Quest quest, string label)
    {
        if (quest == null || view == null) return;

        _queue.Enqueue(new ToastData(quest.Icon, quest.DisplayName, label));
        if (!_isProcessing)
            ProcessQueueAsync(_cts.Token).Forget();
    }

    private async UniTaskVoid ProcessQueueAsync(CancellationToken ct)
    {
        _isProcessing = true;
        try
        {
            while (_queue.Count > 0)
            {
                var toast = _queue.Dequeue();
                await view.PlayAsync(toast.Icon, toast.Title, toast.Label, ct);
            }
        }
        catch (System.OperationCanceledException) { /* 비활성/파괴 시 정상 종료 */ }
        finally
        {
            _isProcessing = false;
        }
    }
}
