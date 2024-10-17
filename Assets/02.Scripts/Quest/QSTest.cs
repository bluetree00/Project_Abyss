using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class QSTest : MonoBehaviour
{
    [SerializeField]
    private Quest quest;
    [SerializeField]
    private Category category;
    [SerializeField]
    private TaskTarget taskTarget;


    void Start()
    {
        var questSystem = QuestSystem.Instance;

        questSystem.onQuestRegistered += (quest) =>
        {
            print($"새로운 퀘스트 : {quest.CodeName} 등록");
            print($"활성화된 퀘스트 카운트 : {questSystem.ActiveQuests.Count}");
        };

        questSystem.onQuestCompleted += (quest) =>
        {
            print($"퀘스트 : {quest.CodeName} 완료");
            print($"완료된 퀘스트 카운트 : {questSystem.CompletedQuests.Count}");
        };

        var newQuest = questSystem.Register(quest);
        newQuest.onTaskSuccessChanged += (quest, task, currentSuccess, prevSuccess) =>
        {
            print($"퀘스트 :{quest.CodeName}, 임무 : {task.CodeName}, 현재 성공 수 : {currentSuccess}");
        };
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
            QuestSystem.Instance.ReceiveReport(category, taskTarget, 1);
    }
}
