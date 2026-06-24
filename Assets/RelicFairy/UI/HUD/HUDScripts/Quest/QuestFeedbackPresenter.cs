using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 퀘스트 등장(onQuestRegistered)/완료(onQuestCompleted) 시 퀘스트에 지정된 피드백을 재생한다.
/// 현재 Dialogue 타입만 처리(DIALOGUE_DATA 시퀀스 → UI_DialoguePopup). Text/None은 완료 토스트(QuestNotifier)가 커버.
/// 전역 QuestManager(Managers.Quest)를 OnEnable에서 자가 구독한다. @UIRoot에 배치.
/// 동시 다발 대사는 큐로 직렬화한다.
/// </summary>
public sealed class QuestFeedbackPresenter : MonoBehaviour
{
    private readonly Queue<string> _queue = new Queue<string>();
    private QuestManager _quest;
    private CancellationTokenSource _cts;
    private bool _isProcessing;

    private void OnEnable()
    {
        _cts = new CancellationTokenSource();

        _quest = Managers.Quest;
        if (_quest == null) return;

        _quest.onQuestRegistered       += HandleRegistered;
        _quest.onQuestCompleted        += HandleCompleted;
        _quest.onAchievementRegistered += HandleRegistered;
        _quest.onAchievementCompleted  += HandleCompleted;
    }

    private void OnDisable()
    {
        if (_quest != null)
        {
            _quest.onQuestRegistered       -= HandleRegistered;
            _quest.onQuestCompleted        -= HandleCompleted;
            _quest.onAchievementRegistered -= HandleRegistered;
            _quest.onAchievementCompleted  -= HandleCompleted;
            _quest = null;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _queue.Clear();
        _isProcessing = false;
    }

    private void HandleRegistered(Quest quest)
    {
        if (quest != null && quest.AcceptFeedbackType == QuestFeedbackType.Dialogue)
            Enqueue(quest.AcceptFeedbackId);
    }

    private void HandleCompleted(Quest quest)
    {
        if (quest != null && quest.CompleteFeedbackType == QuestFeedbackType.Dialogue)
            Enqueue(quest.CompleteFeedbackId);
    }

    private void Enqueue(string sequenceId)
    {
        if (string.IsNullOrEmpty(sequenceId)) return;

        _queue.Enqueue(sequenceId);
        if (!_isProcessing)
            ProcessQueueAsync(_cts.Token).Forget();
    }

    private async UniTaskVoid ProcessQueueAsync(CancellationToken ct)
    {
        _isProcessing = true;
        try
        {
            var dlg = Managers.DialogueData;
            if (dlg != null && !dlg.IsInitialized)
                await dlg.InitializeAsync();

            while (_queue.Count > 0)
            {
                var id = _queue.Dequeue();
                var lines = dlg?.GetLines(id);
                if (lines == null || lines.Length == 0) continue;

                // 장비/서약 선택 UI가 열려있으면 닫힐 때까지 대기 후 완료 대사.
                await Managers.UI.WaitUntilNoBlockingPopupAsync();

                var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_DialoguePopup>();
                if (popup == null) continue;
                await popup.ShowAsync(lines);
            }
        }
        catch (System.OperationCanceledException) { /* 비활성/파괴 시 정상 */ }
        finally
        {
            _isProcessing = false;
        }
    }
}
