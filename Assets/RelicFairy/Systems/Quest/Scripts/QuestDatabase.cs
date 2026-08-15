using System.Collections.Generic;
using System.Linq;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[CreateAssetMenu(menuName = "Quest/QuestDatabase", fileName = "QuestDatabase")]
public class QuestDatabase : ScriptableObject
{
    [SerializeField] private List<Quest> quests;

    public IReadOnlyList<Quest> Quests => quests;

    public Quest FindQuestBy(string codeName)
        => quests.FirstOrDefault(x => x.CodeName == codeName);

#if UNITY_EDITOR
    // 이 DB는 <b>한 종류만</b> 담는다 — QuestDatabase는 퀘스트, AchievementDatabase는 업적.
    // QuestManager가 두 목록을 따로 받기 때문이다(Initialize(questDb, achievementDb)).
    //
    // ⚠ 아래 두 메뉴는 목록을 통째로 갈아치우므로 <b>엉뚱한 에셋에서 누르면 내용이 사라진다</b>.
    //   평소에는 메뉴 「RelicFairy/Gameplay/Quest/Rebuild Database」를 쓴다 —
    //   에셋 경로명으로 종류를 판별해 두 DB를 한 번에, 올바르게 채운다.

    [ContextMenu("FindQuests")]
    private void FindQuests() => FindQuestByType<Quest>();

    [ContextMenu("FindAchievements")]
    private void FindAchievements() => FindQuestByType<Achievement>();

    private void FindQuestByType<T>() where T : Quest
    {
        quests = new List<Quest>();
        string[] guids = AssetDatabase.FindAssets($"t:{typeof(T)}");
        foreach (var guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            var quest = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (quest != null && quest.GetType() == typeof(T))
                quests.Add(quest);
        }
        EditorUtility.SetDirty(this);
        AssetDatabase.SaveAssets();
    }
#endif
}
