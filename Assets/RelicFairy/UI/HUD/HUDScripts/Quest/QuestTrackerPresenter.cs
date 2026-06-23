using UnityEngine;

/// <summary>
/// 진행 중 퀘스트 추적 위젯 Presenter.
/// 전역 QuestManager(Managers.Quest)의 등록/완료/실패/취소 이벤트를 OnEnable에서 자가 구독해
/// QuestTrackerView에 증분 반영한다 (등록=페이드 인, 완료=금색 연출, 실패=적색 연출, 취소=즉시 제거).
/// 완료/등록 연출은 View가 op 큐로 순차 처리한다.
/// </summary>
public sealed class QuestTrackerPresenter : MonoBehaviour
{
    [SerializeField] private QuestTrackerView view;

    private QuestManager _quest;

    private void Awake()
    {
        if (view == null)
            view = GetComponentInChildren<QuestTrackerView>(true);
    }

    private void OnEnable()
    {
        _quest = Managers.Quest;
        if (_quest != null)
        {
            _quest.onQuestRegistered += HandleRegistered;
            _quest.onQuestCompleted  += HandleCompleted;
            _quest.onQuestFailed     += HandleFailed;
            _quest.onQuestCanceled   += HandleCanceled;
        }

        view?.SetQuestsInstant(_quest?.ActiveQuests);
    }

    private void OnDisable()
    {
        if (_quest != null)
        {
            _quest.onQuestRegistered -= HandleRegistered;
            _quest.onQuestCompleted  -= HandleCompleted;
            _quest.onQuestFailed     -= HandleFailed;
            _quest.onQuestCanceled   -= HandleCanceled;
            _quest = null;
        }

        view?.Clear();
    }

    private void HandleRegistered(Quest quest) => view?.AddQuest(quest);
    private void HandleCompleted(Quest quest)  => view?.RemoveQuest(quest, QuestRemoveKind.Clear);
    private void HandleFailed(Quest quest)     => view?.RemoveQuest(quest, QuestRemoveKind.Fail);
    private void HandleCanceled(Quest quest)   => view?.RemoveQuest(quest, QuestRemoveKind.Instant);
}
