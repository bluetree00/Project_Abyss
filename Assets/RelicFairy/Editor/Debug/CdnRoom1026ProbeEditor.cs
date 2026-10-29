#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-26 상점방 통로(P11) CDN 업로드 확인 — 캐시를 비우고 뒤끝 CDN에서 CHAPTER_1~4_ROOM_POOL을 다시 받아 행마다 JSON 한 줄로 적는다
/// (바탕화면 「사용중 테이블」 원본과 상점 행 대조는 밖에서). 결과 Temp/cdn_CHAPTER_n_ROOM_POOL.txt · 로그 「[CdnRoom26] 끝」. 로그인 뒤(플레이 중)에만.
/// </summary>
public static class CdnRoom1026ProbeEditor
{
    private const string Tag = "[CdnRoom26]";

    [MenuItem("RelicFairy/Debug/10-26 CDN 방 풀 받아 적기 (CHAPTER_1~4_ROOM_POOL, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중에만(로그인 뒤)"); return; }
        var sum = new StringBuilder();
        ChartLoader.ClearCache();
        for (int n = 1; n <= 4; n++)
        {
            string chart = $"CHAPTER_{n}_ROOM_POOL";
            var sb = new StringBuilder();
            int rows = ChartLoader.Load(chart, row => sb.AppendLine(row.ToJson()));
            File.WriteAllText($"Temp/cdn_{chart}.txt", sb.ToString(), new UTF8Encoding(false));
            sum.AppendLine($"{chart}: {rows}행");
        }
        Debug.Log($"{Tag} 끝\n" + sum);
    }
}
#endif
