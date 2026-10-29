#if UNITY_EDITOR
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-29 F3 확인 — ① 시기별 역할 비율 표(편집 모드 · 스폰 표 가중치 × MonsterRoleTable.EraWeight · 챕터 × 일반/위협) ·
/// ② 자동 실측 중 「이 방 정예」 한 줄이 뜨면 한 장(Temp/f3/room_elite.png). 결과 Temp/f3/roles.txt · 로그 「[F3Probe]」.
/// </summary>
public static class F3ProbeEditor
{
    private const string TablePath = "Assets/RelicFairy/Characters/Monster/Monster/Core/MonsterSpawnTable.asset";

    [MenuItem("RelicFairy/Debug/10-29 F3 시기별 역할 비율 표 (편집 모드)")]
    private static void Shares()
    {
        var table = AssetDatabase.LoadAssetAtPath<MonsterSpawnTableSO>(TablePath);
        if (table == null) { Debug.LogWarning("[F3Probe] 스폰 표 없음"); return; }
        var sb = new StringBuilder("시기별 역할 비율(가중치 몫 — 원거리 / 돌진)\n");
        foreach (var grade in new[] { MonsterGrade.Common, MonsterGrade.Rare })
            for (int ch = 1; ch <= 4; ch++)
            {
                sb.Append($"Ch{ch} {grade,-6}");
                foreach (var era in new[] { StoryEra.Sealed, StoryEra.Liberated, StoryEra.NightmareMode })
                {
                    float total = 0f, ranged = 0f, charger = 0f;
                    foreach (var e in table.entries)
                    {
                        if (e == null || !e.enabled || e.grade != grade || e.poolTags == null || System.Array.IndexOf(e.poolTags, ch) < 0) continue;
                        float w = Mathf.Max(0f, e.weight) * MonsterRoleTable.EraWeight(e, era);
                        total += w;
                        var role = MonsterRoleTable.Of(e.displayName);
                        if (role == MonsterRoleTable.Role.Ranged) ranged += w;
                        else if (role == MonsterRoleTable.Role.Charger) charger += w;
                    }
                    sb.Append(total > 0f ? $" · {era} {ranged / total:P0} / {charger / total:P0}" : $" · {era} -");
                }
                sb.AppendLine();
            }
        Directory.CreateDirectory("Temp/f3");
        File.WriteAllText("Temp/f3/roles.txt", sb.ToString(), Encoding.UTF8);
        Debug.Log("[F3Probe] 비율 끝\n" + sb);
    }

    [MenuItem("RelicFairy/Debug/10-29 F3 이 방 정예 사진 (자동 실측 중, Play)")]
    private static void NoticeShot()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[F3Probe] 플레이 중에"); return; }
        NoticeShotAsync().Forget();
    }

    private static async UniTaskVoid NoticeShotAsync()
    {
        Directory.CreateDirectory("Temp/f3");
        string seen = null;
        void OnLog(string msg, string st, LogType t) { if (seen == null && msg.StartsWith("[F3] 이 방 정예")) seen = msg; }
        Application.logMessageReceived += OnLog;
        try
        {
            for (float until = Time.realtimeSinceStartup + 360f; Time.realtimeSinceStartup < until && seen == null;)
                await UniTask.Delay(100, DelayType.Realtime);
            if (seen != null)
            {
                await UniTask.Delay(300, DelayType.Realtime);
                ScreenCapture.CaptureScreenshot("Temp/f3/room_elite.png");
                await UniTask.Delay(500, DelayType.Realtime);
            }
        }
        finally
        {
            Application.logMessageReceived -= OnLog;
            Debug.Log($"[F3Probe] 사진 끝 — {seen ?? "없음"}");
        }
    }
}
#endif
