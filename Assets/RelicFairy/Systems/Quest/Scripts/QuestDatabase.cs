using System.Collections.Generic;
using System.Linq;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 퀘스트·업적 에셋을 모으는 <b>어드레서블 라벨</b>. 생성기(라벨 부착)와 부트(라벨 로드)가
/// 같은 문자열을 봐야 하므로 한곳에 둔다 — 양쪽에 문자열을 흩어 놓으면 오타 하나로 목록이 빈다.
/// </summary>
public static class QuestLabels
{
    public const string Quest       = "Quest";
    public const string Achievement = "Achievement";
}

/// <summary>
/// ⚠️ <b>런타임에서는 더 이상 쓰지 않는다.</b> 부트는 <see cref="QuestLabels"/> 라벨로 폴더째 모은다.
///
/// 이 에셋의 손 목록은 CSV로 항목을 늘린 뒤 재연결을 빼먹으면 조용히 낡아, 업적 21개 중 3개만
/// 등록되는 사고를 냈다. 에디터 도구(Rebuild Database)와의 호환을 위해 타입만 남긴다.
/// </summary>
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
