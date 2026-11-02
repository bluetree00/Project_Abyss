#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 11-02 Q11 L4 실측 — 언어를 영어로 바꾸고 대사(DialogueDataManager)의 모든 시퀀스를 GetLines로 읽어 한글이 남은 줄을 센다.
/// 서버 차트에 text_en 열이 없어도 동봉 CSV 영어가 채워지는지 · 한국어로 되돌리면 한글인지 · 견본 몇 줄. 언어는 원래대로 되돌린다.
/// 결과: 로그 「[Q11En] 끝」.
/// </summary>
public static class Q11DialogueEnProbeEditor
{
    private static readonly string[] Samples = { "StartRoom", "Lich_Intro", "Intro_LichCorrupt", "Seal_Exile", "DK_Hint_Pyramid" };

    [MenuItem("RelicFairy/Debug/11-02 Q11 대사 영어 전수 (Play)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[Q11En] 플레이 모드에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var dd = Managers.DialogueData;
        if (dd == null) { Debug.LogWarning("[Q11En] DialogueData 없음"); return; }
        if (!dd.IsInitialized) await dd.InitializeAsync();   // 처음 쓰일 때 읽는다 — 서버 차트가 받아져 있으면 CDN 길(로그로 어느 길인지)

        var was = Loc.Current;
        var sb  = new StringBuilder("[Q11En] 끝").AppendLine();
        try
        {
            Loc.SetLanguage(Loc.Language.English);
            var seqs = typeof(DialogueDataManager).GetField("_sequences", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(dd) as IDictionary;
            int total = 0, hangul = 0, seqCount = 0;
            if (seqs != null)
            {
                foreach (DictionaryEntry kv in seqs)
                {
                    seqCount++;
                    var lines = dd.GetLines((string)kv.Key);
                    if (lines == null) continue;
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (lines[i] == null || string.IsNullOrWhiteSpace(lines[i].text)) continue;
                        total++;
                        if (!HasHangul(lines[i].text)) continue;
                        hangul++;
                        if (hangul <= 12) sb.AppendLine($"  한글 남음 {kv.Key}[{i}] {lines[i].text}");
                    }
                }
            }
            sb.AppendLine($"  영어 — 시퀀스 {seqCount} · 줄 {total} · 한글 남은 줄 {hangul}");
            foreach (var key in Samples)
            {
                var lines = dd.GetLines(key);
                if (lines == null) { sb.AppendLine($"  {key} 없음"); continue; }
                for (int i = 0; i < lines.Length && i < 3; i++) sb.AppendLine($"  EN {key}[{i}] {lines[i].text}");
            }

            Loc.SetLanguage(Loc.Language.Korean);
            var ko = dd.GetLines("StartRoom");
            sb.AppendLine($"  한국어로 되돌림 — StartRoom[0] {(ko != null && ko.Length > 0 ? ko[0].text : "(없음)")}");
        }
        finally
        {
            Loc.SetLanguage(was);
        }
        Debug.Log(sb.ToString());
    }

    private static bool HasHangul(string s)
    {
        foreach (char c in s) if (c >= '가' && c <= '힣') return true;
        return false;
    }
}
#endif
