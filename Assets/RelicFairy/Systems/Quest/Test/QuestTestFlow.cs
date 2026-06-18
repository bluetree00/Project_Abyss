using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 퀘스트 시스템 테스트 플로우. BaseCamp에 배치.
/// 진입 시 첫 튜토리얼 퀘스트(유물 획득)를 등록 → afterQuest 체인으로 장비 선택 퀘스트로 이어짐.
/// 마지막 퀘스트 완료 시 신(God) 완료 대사를 재생한다.
///
/// 흐름: [유물 획득] → [장비 선택] → [완료 대사 + 토스트]
/// </summary>
public sealed class QuestTestFlow : MonoBehaviour
{
    [Header("Quest Codes")]
    [SerializeField] private string firstQuestCode = "tut_acquire_relic";
    [SerializeField] private string finalQuestCode = "tut_select_equip";

    [Header("완료 대사 (신)")]
    [SerializeField] private DialogueSequenceSO completionDialogue;

    private QuestManager _quest;
    private CancellationTokenSource _cts;

    private void OnEnable()
    {
        _cts = new CancellationTokenSource();

        _quest = Managers.Quest;
        if (_quest != null)
            _quest.onQuestCompleted += HandleQuestCompleted;
    }

    private void Start()
    {
        // QuestManager가 초기화됐다면 첫 퀘스트 등록 (DB 로드는 AppBootstrapper에서 선행)
        var registered = _quest?.RegisterQuest(firstQuestCode);
        if (registered == null)
            Debug.LogWarning($"[QuestTestFlow] '{firstQuestCode}' 등록 실패 — QuestDatabase 초기화 여부 확인.");
    }

    private void OnDisable()
    {
        if (_quest != null)
        {
            _quest.onQuestCompleted -= HandleQuestCompleted;
            _quest = null;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private void HandleQuestCompleted(Quest quest)
    {
        if (quest == null || quest.CodeName != finalQuestCode) return;
        PlayCompletionDialogueAsync(_cts.Token).Forget();
    }

    private async UniTaskVoid PlayCompletionDialogueAsync(CancellationToken ct)
    {
        if (completionDialogue == null || completionDialogue.Lines == null || completionDialogue.Lines.Length == 0)
            return;

        try
        {
            var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_DialoguePopup>();
            if (popup == null) return;
            await popup.ShowAsync(completionDialogue);
        }
        catch (System.OperationCanceledException) { /* 씬 종료 시 정상 */ }
    }
}
