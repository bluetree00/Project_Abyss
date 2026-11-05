#if UNITY_EDITOR
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 11-02 사용자 「해방기 · 악몽 기록은 리더보드를 했을 텐데」 — 뒤끝에 리더보드가 있는 표를 이름으로 찾아 적는다(읽기만 · 올리지 않음).
/// 결과 Temp/leaderboards1102.txt · 로그 「[LB1102] 끝」. 플레이 중(로그인 뒤).
/// </summary>
public static class Leaderboards1102ProbeEditor
{
    private static readonly string[] Tables = BuildTables();   // 11-03 챕터 기록 시기별 — 2 + 16(게임이 쓰는 이름 그대로)

    private static string[] BuildTables()
    {
        var list = new System.Collections.Generic.List<string> { RankingService.LiberationTable, RankingService.NightmareTable };
        foreach (var era in new[] { StoryEra.Liberated, StoryEra.NightmareMode })
            foreach (bool boss in new[] { true, false })
                for (int ch = 1; ch <= 4; ch++) list.Add(RecordBoard.TableOf(boss, ch, era));
        return list.ToArray();
    }

    [MenuItem("RelicFairy/Debug/11-02 뒤끝 리더보드 목록 (플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[LB1102] 플레이 중에만"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("11-02 뒤끝 리더보드(표 이름 → 있음/없음 · 상위 줄 수)\n");
        try
        {
            foreach (var t in Tables)
            {
                string uuid = await RankingService.ResolveTableAsync(t);
                if (string.IsNullOrEmpty(uuid)) { sb.AppendLine($"없음  {t}"); continue; }
                var top = await RankingService.LoadTopAsync(t, 10);
                sb.Append($"있음  {t}  · 줄 {top.Count}");
                foreach (var e in top) sb.Append($"  [{e.Rank} {e.Nickname} {RankingService.Format(e.Centis)} {e.Weapon}]");
                sb.AppendLine();
            }
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText("Temp/leaderboards1102.txt", sb.ToString());
            Debug.Log("[LB1102] 끝");
        }
    }
}
#endif
