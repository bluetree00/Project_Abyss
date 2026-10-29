#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-24 CDN 업로드 확인 — 캐시를 비우고 뒤끝 CDN에서 표를 다시 받아 행마다 JSON 한 줄로 적는다(바탕화면 원본과 대조는 밖에서).
/// 결과 Temp/cdn_{차트}.txt · 로그 「[Cdn24] 끝」. 로그인 뒤(플레이 중)에만.
/// </summary>
public static class CdnCheck1024ProbeEditor
{
    private const string Tag = "[Cdn24]";
    private static readonly string[] Charts = { "RELIC_STAT_DATA", "RELIC_PARTS_DATA", "MERLIN_RUNE_SYNERGY_DATA" };

    [MenuItem("RelicFairy/Debug/10-24 CDN 표 받아 적기 (RELIC_STAT · RELIC_PARTS · 룬 시너지, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중에만(로그인 뒤)"); return; }
        var sum = new StringBuilder();
        ChartLoader.ClearCache();
        foreach (var chart in Charts)
        {
            var sb = new StringBuilder();
            int n = ChartLoader.Load(chart, row => sb.AppendLine(row.ToJson()));
            File.WriteAllText($"Temp/cdn_{chart}.txt", sb.ToString(), new UTF8Encoding(false));
            sum.AppendLine($"{chart}: {n}행");
        }
        Debug.Log($"{Tag} 끝\n" + sum);
    }
}
#endif
