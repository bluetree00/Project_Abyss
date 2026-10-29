#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.UI;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-23 사용자 「모르드레드 대사 중에 이상하게 깨지는 말이 있다」 — 인트로가 보스 자막(UI_BossBark)으로 띄우는 줄을 같은 인자로 하나씩 띄워 찍는다.
/// 줄마다 글꼴 · 없는 글자(폴백 포함) · 보이는 글(태그 뺀) · 색 태그 · 줄 수 · 자동 축소 크기를 적는다.
/// 결과 Temp/intro_bark.txt · Temp/ui_shots/IBARK_*.png · 로그 「[IntroBark] 끝」.
/// </summary>
public static class IntroBark1023ProbeEditor
{
    private const string Tag = "[IntroBark]";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly (string seq, int count)[] Seqs =
    {
        ("Intro_Mordred", 3), ("Intro_LichCorrupt", 7), ("Mordred_Corrupted", 2), ("DK_Intro_FinisherCall", 1), ("DK_Intro_Defeat", 1),
    };

    [MenuItem("RelicFairy/Debug/10-23 인트로 모르드레드 자막 촬영 (플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중에만"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("10-23 인트로 자막(모르드레드 · 리치)\n");
        var hub = Object.FindFirstObjectByType<TestHubLauncher>();
        bool hubWas = hub != null && hub.enabled;
        if (hub != null) hub.enabled = false;   // 허브 OnGUI 판이 사진을 가린다
        try
        {
            var dlg = Managers.DialogueData;
            if (dlg != null && !dlg.IsInitialized) await dlg.InitializeAsync();
            var bark = UI_BossBark.Instance;
            if (bark == null) { sb.AppendLine("UI_BossBark 없음"); return; }
            var label = typeof(UI_BossBark).GetField("_label", Any)?.GetValue(bark) as TMP_Text;

            foreach (var (seq, count) in Seqs)
            {
                var lines = dlg?.GetLines(seq);
                for (int i = 0; i < count; i++)
                {
                    if (lines == null || i >= lines.Length || lines[i] == null) { sb.AppendLine($"{seq}[{i}] 없음"); continue; }
                    var l = lines[i];
                    UI_BossBark.ShowLatest(l.text, BossBarkType.Bark, l.speaker);
                    await Wait(1.1f);
                    string name = $"IBARK_{(File.Exists("Temp/ibark_tag.txt") ? File.ReadAllText("Temp/ibark_tag.txt").Trim() + "_" : "")}{seq}_{i}";
                    await Shot(name);
                    sb.AppendLine($"{seq}[{i}] {l.speaker} 「{l.text}」");
                    if (label != null)
                    {
                        label.ForceMeshUpdate();
                        var font = label.font;
                        var missing = new StringBuilder();
                        string parsed = label.GetParsedText();
                        foreach (char c in parsed)
                            if (!char.IsWhiteSpace(c) && font != null && !font.HasCharacter(c, true, true)) missing.Append(c).Append($"(U+{(int)c:X4}) ");
                        sb.AppendLine($"    글꼴 {font?.name} · 크기 {label.fontSize:0.#}(최대 {label.fontSizeMax:0.#}) · 줄 {label.textInfo.lineCount} · 굵게 {(label.fontStyle & FontStyles.Bold) != 0} · 없는 글자 [{missing}]");
                        sb.AppendLine($"    보이는 글 「{parsed}」");
                    }
                }
            }
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            if (hub != null) hub.enabled = hubWas;
            File.WriteAllText("Temp/intro_bark.txt", sb.ToString());
            Debug.Log($"{Tag} 끝\n" + sb);
        }
    }

    private static UniTask Wait(float s) => UniTask.Delay((int)(s * 1000), DelayType.Realtime);

    private static UniTask Shot(string name)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, 0 }) : UniTask.CompletedTask;
    }
}
#endif
