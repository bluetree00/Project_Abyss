#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 빌드 시너지(10-14) 에셋 손질 — K4(D1) 활 · 석궁 R 스킬 짝 교환.
/// 활 R = 더 빨리쏘기(연사 · 다발) · 석궁 R = 궁수의 집중(차지 저격). 궁수의 집중의 발사 상태(QSkill_02)는 기본 클립이
/// QSkill_Iasen(기사 발도)이라 석궁 애니 세트에 덮어쓰기 칸을 더한다(석궁 한 발 클립). SerializedObject로만 고친다.
/// </summary>
public static class BuildSynergyDataEditor
{
    private const string Root = "RelicFairy/Debug/빌드 시너지/";

    private static readonly string[] Bows =
    {
        "Assets/RelicFairy/Weapon/Bow/Data/T1_Bow.asset", "Assets/RelicFairy/Weapon/Bow/Data/T2_Bow.asset", "Assets/RelicFairy/Weapon/Bow/Data/T3_Bow.asset",
    };
    private static readonly string[] Crossbows =
    {
        "Assets/RelicFairy/Weapon/Crossbow/Data/T1_Crossbow.asset", "Assets/RelicFairy/Weapon/Crossbow/Data/T2_Crossbow.asset", "Assets/RelicFairy/Weapon/Crossbow/Data/T3_Crossbow.asset",
    };
    private const string FocusShot  = "Assets/RelicFairy/Weapon/Bow/Data/Bow_QSkill.asset";            // 궁수의 집중
    private const string MultiShot  = "Assets/RelicFairy/Weapon/Crossbow/Data/Crossbow_QSkill.asset";  // 더 빨리쏘기
    private const string XbowAnim   = "Assets/RelicFairy/Weapon/Crossbow/Data/CrossbowBase/CrossbowAnimation.asset";

    [MenuItem(Root + "K4 활 · 석궁 R 스킬 짝 교환")]
    private static void SwapRangedR()
    {
        var focus = AssetDatabase.LoadAssetAtPath<Object>(FocusShot);
        var multi = AssetDatabase.LoadAssetAtPath<Object>(MultiShot);
        var log = new StringBuilder("[BuildSynergyData] K4 — ");
        if (focus == null || multi == null) { Debug.LogWarning(log.Append("스킬 에셋 없음").ToString()); return; }

        foreach (var p in Bows) SetR(p, multi, log);
        foreach (var p in Crossbows) SetR(p, focus, log);

        var anim = AssetDatabase.LoadAssetAtPath<Object>(XbowAnim);
        if (anim != null)
        {
            var so = new SerializedObject(anim);
            var maps = so.FindProperty("animGroups").GetArrayElementAtIndex(0).FindPropertyRelative("clipMappings");
            int q1 = -1; bool has = false;
            for (int i = 0; i < maps.arraySize; i++)
            {
                string n = maps.GetArrayElementAtIndex(i).FindPropertyRelative("baseClipName").stringValue;
                if (n == "QSkill_01") q1 = i;
                if (n == "QSkill_Iasen") has = true;
            }
            if (!has && q1 >= 0)
            {
                maps.InsertArrayElementAtIndex(q1);   // q1 복사본이 q1 + 1에 들어간다
                var e = maps.GetArrayElementAtIndex(q1 + 1);
                e.FindPropertyRelative("baseClipName").stringValue  = "QSkill_Iasen";
                e.FindPropertyRelative("addressableKey").stringValue = "Crossbow_GroundLight_01";
                e.FindPropertyRelative("comboIndex").intValue        = 1;
                so.ApplyModifiedPropertiesWithoutUndo();
                log.Append("석궁 애니 칸 QSkill_Iasen → Crossbow_GroundLight_01 더함 · ");
            }
            else log.Append($"석궁 애니 칸 그대로(있음 {has}) · ");
        }
        AssetDatabase.SaveAssets();
        Debug.Log(log.ToString());
    }

    private static void SetR(string path, Object skill, StringBuilder log)
    {
        var w = AssetDatabase.LoadAssetAtPath<Object>(path);
        if (w == null) { log.Append(System.IO.Path.GetFileNameWithoutExtension(path)).Append(" 없음 · "); return; }
        var so = new SerializedObject(w);
        var r = so.FindProperty("skillQ");   // R 칸(필드명 skillQ는 레거시 — Q는 유물)
        string before = r.objectReferenceValue != null ? r.objectReferenceValue.name : "null";
        r.objectReferenceValue = skill;
        so.ApplyModifiedPropertiesWithoutUndo();
        log.Append(w.name).Append(" R ").Append(before).Append(" → ").Append(skill.name).Append(" · ");
    }
}
#endif
