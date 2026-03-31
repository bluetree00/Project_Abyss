using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using System.Text;
using System.IO;

public static class DumpHUDLayout
{
    [MenuItem("Tools/Dump HUD Layout")]
    public static void Execute()
    {
        var root = PrefabUtility.LoadPrefabContents("Assets/Abyss/UI/RootUI/@UIRoot.prefab");
        var sb = new StringBuilder();

        // @HUD 아래만 덤프
        var hud = Deep(root.transform, "@HUD");
        if (hud != null)
            DumpTransform(hud, 0, sb);
        else
            sb.AppendLine("@HUD not found");

        PrefabUtility.UnloadPrefabContents(root);

        File.WriteAllText("hud_layout.txt", sb.ToString());
        Debug.Log("[DumpHUD] hud_layout.txt 작성 완료 (" + sb.Length + " chars)");
    }

    static void DumpTransform(Transform t, int depth, StringBuilder sb)
    {
        var rt = t.GetComponent<RectTransform>();
        string indent = new string(' ', depth * 2);
        if (rt != null)
        {
            var img = t.GetComponent<Image>();
            string sprite = img?.sprite != null ? img.sprite.name : "-";
            sb.AppendLine($"{indent}[{t.name}] pos={rt.anchoredPosition} size={rt.sizeDelta} " +
                          $"anchorMin={rt.anchorMin} anchorMax={rt.anchorMax} pivot={rt.pivot} | img={sprite}");
        }
        foreach (Transform child in t) DumpTransform(child, depth + 1, sb);
    }

    static Transform Deep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t) { var r = Deep(c, name); if (r != null) return r; }
        return null;
    }
}
