using UnityEngine;

/// <summary>
/// 가이드 퀘스트 시작점. BaseCamp에 배치 — 진입 시 첫 가이드 퀘스트를 등록한다.
/// 이후 진행(afterQuest 체인)은 QuestManager, 등장/완료 대사 연출은 QuestFeedbackPresenter가 담당한다.
/// 직접 씬 플레이 시 비동기 부트스트랩이 늦으므로 QuestManager.onInitialized로 등록을 건다.
/// </summary>
public sealed class QuestTestFlow : MonoBehaviour
{
    [Header("Quest Codes")]
    [SerializeField] private string firstQuestCode = "tut_acquire_relic";

    private QuestManager _quest;

    private void OnEnable()
    {
        _quest = Managers.Quest;
        if (_quest == null) return;

        if (_quest.IsInitialized)
            TryRegisterFirstQuest();
        else
            _quest.onInitialized += HandleQuestManagerInitialized;
    }

    private void OnDisable()
    {
        if (_quest != null)
        {
            _quest.onInitialized -= HandleQuestManagerInitialized;
            _quest = null;
        }
    }

    private void HandleQuestManagerInitialized()
    {
        if (_quest != null)
            _quest.onInitialized -= HandleQuestManagerInitialized;
        TryRegisterFirstQuest();
    }

    private void TryRegisterFirstQuest()
    {
        var registered = _quest.RegisterQuest(firstQuestCode);
        if (registered == null)
            Debug.LogWarning($"[QuestTestFlow] '{firstQuestCode}' 등록 실패 — 이미 진행/완료 중이거나 DB에 없음.");
    }
}
