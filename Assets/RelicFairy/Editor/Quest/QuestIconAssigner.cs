#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 업적 22 · 퀘스트 8의 <c>icon</c>(Artsystack 흰 아이콘 128 — 분석 문서 §4-1). 같은 계열은 상위 단계가 더 무거운 그림이다.
/// 아이콘은 코드 표가 정본이다 — 「Generate From CSV」가 에셋을 새로 만들면 비므로 생성기가 끝에 이걸 다시 부른다.
/// 업적 행(<see cref="AchievementRowView"/>) · 퀘스트 알림(QuestNotifierView)이 이 아이콘을 그린다.
/// </summary>
public static class QuestIconAssigner
{
    private const string Root = "Assets/RelicFairy/Data/Quest/Generated";

    private static readonly (string id, string icon)[] Table =
    {
        // 업적 22
        ("boss_first", "skull"),        ("boss_10", "crown_2"),
        ("kill_100_any", "sword_1"),    ("kill_1000", "sword_3"),
        ("elite_30", "archery"),        ("elite_100", "axe_2"),
        ("clear_50_rooms", "compass"),  ("clear_300_rooms", "map"),
        ("first_clear", "trophy"),      ("clear_5", "infinity"),
        ("depth_3_clear", "anchor"),    ("depth_5", "lantern"),
        ("depth_10", "star_and_moon"),  ("reach_ch2", "flag_1"),
        ("reach_ch3", "flag_3"),        ("reach_ch4", "library"),
        ("enhance_6", "tools_2"),       ("enhance_9", "hammer"),
        ("refine_30", "crystal_2"),     ("refine_100", "crystal_1"),
        ("shop_5", "store"),            ("shop_30", "money_bag"),
        // 퀘스트 8
        ("clear_room_3", "foot_print"),        ("collect_gold_1000", "coin"),
        ("hidden_elite_ambush", "danger"),     ("kill_monster_20", "dagger"),
        ("tut_acquire_relic", "chest_1"),      ("tut_covenant", "scroll"),
        ("tut_enter_gate", "gate"),            ("tut_select_equip", "shield_2"),
    };

    [MenuItem("RelicFairy/Gameplay/Quest/Assign Icons (Artsystack 30)")]
    private static void Menu() => Debug.Log($"[QuestIconAssigner] 아이콘 {Apply()}/{Table.Length}");

    /// <summary>표대로 아이콘을 건다. 이미 같은 아이콘이면 건드리지 않는다. 건 수(이미 같은 것 포함)를 돌려준다.</summary>
    public static int Apply()
    {
        int ok = 0;
        foreach (var (id, icon) in Table)
        {
            var quest = AssetDatabase.LoadAssetAtPath<Quest>($"{Root}/Achievements/{id}.asset")
                        ?? AssetDatabase.LoadAssetAtPath<Quest>($"{Root}/Quests/{id}.asset");
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{ArtsystackImportEditor.IconDir}/{icon}_128.png");
            if (quest == null || sprite == null)
            {
                Debug.LogWarning($"[QuestIconAssigner] {id} — {(quest == null ? "에셋 없음" : $"아이콘 없음 {icon}")}");
                continue;
            }
            var so = new SerializedObject(quest);
            var prop = so.FindProperty("icon");
            if (prop.objectReferenceValue != sprite)
            {
                prop.objectReferenceValue = sprite;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(quest);
            }
            ok++;
        }
        AssetDatabase.SaveAssets();
        return ok;
    }
}
#endif
