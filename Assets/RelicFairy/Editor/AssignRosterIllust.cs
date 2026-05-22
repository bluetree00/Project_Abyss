using UnityEditor;
using UnityEngine;

public static class AssignRosterIllust
{
    [MenuItem("Tools/Abyss/Assign Roster Illustrations")]
    public static void Execute()
    {
        Assign(
            "Assets/Abyss/Characters/Player/Knight/Data/Knight.asset",
            "Assets/Abyss/Characters/불타는 전장에서 싸우는 기사.png"
        );
        Assign(
            "Assets/Abyss/Characters/Player/Mage/Data/MageData.asset",
            "Assets/Abyss/Characters/마법의 에너지를 다루는 소녀.png"
        );
        Assign(
            "Assets/Abyss/Characters/Player/Berserker/Data/BerserkerData.asset",
            "Assets/Abyss/Characters/어둠 속의 전설적인 기사.png"
        );

        AssetDatabase.SaveAssets();
        Debug.Log("[AssignRosterIllust] 완료 — 3개 캐릭터 일러스트 등록");
    }

    private static void Assign(string dataPath, string spritePath)
    {
        var data   = AssetDatabase.LoadAssetAtPath<ScriptableObject>(dataPath);
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);

        if (data == null)   { Debug.LogError($"[AssignRosterIllust] SO 없음: {dataPath}"); return; }
        if (sprite == null) { Debug.LogError($"[AssignRosterIllust] 스프라이트 없음: {spritePath}"); return; }

        var so   = new SerializedObject(data);
        var prop = so.FindProperty("rosterIllust");
        if (prop == null) { Debug.LogError($"[AssignRosterIllust] rosterIllust 필드 없음: {dataPath}"); return; }

        prop.objectReferenceValue = sprite;
        so.ApplyModifiedPropertiesWithoutUndo();

        Debug.Log($"[AssignRosterIllust] {System.IO.Path.GetFileName(dataPath)} ← {System.IO.Path.GetFileName(spritePath)}");
    }
}
