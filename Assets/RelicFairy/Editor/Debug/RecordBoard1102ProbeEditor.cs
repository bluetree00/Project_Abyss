#if UNITY_EDITOR
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 11-02 Q14 보스전 · 챕터 전투 기록 실측(테스트 허브 런 중) — 결과 Temp/record_board1102.txt · 사진 Temp/ui_shots/RB1102.png · 로그 「[RB1102] 끝」.
/// 보스방에 든 것으로 두고 전투 시계를 52.4초 더해 처치 → 개인 최고(신기록) → 더 느린 두 번째는 신기록 아님 → 알림 줄 사진.
/// 뒤끝 표(RANK_BOSS_n)가 이미 있으면 실제 랭킹을 더럽히지 않게 건너뛴다. 끝나면 메타 records를 처음 문자열로 되돌려 저장 · 방 종류도 처음대로.
/// </summary>
public static class RecordBoard1102ProbeEditor
{
    [MenuItem("RelicFairy/Debug/11-02 보스 · 챕터 기록 실측 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[RB1102] 플레이 중에만"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("11-02 보스 · 챕터 기록\n");
        Directory.CreateDirectory("Temp/ui_shots");
        var run = GameRunBootstrapper.Instance?.Run;
        var data = BackendGameData.Instance?.Data;
        string records0 = data?.records;
        RoomPlanKind kind0 = run != null ? run.CurrentRoomKind : RoomPlanKind.Normal;
        bool danger0 = run != null && run.CurrentRoomDanger;
        string focus0 = run?.CurrentRoomRuneFocus;
        try
        {
            if (run == null || data == null) { sb.AppendLine($"런 {run != null} · 메타 {data != null}"); return; }
            int ch = (int)run.CurrentChapter;
            var era = run.StartEra;
            string table = RecordBoard.TableOf(true, ch, era);
            string uuid = await RankingService.ResolveTableAsync(table);
            if (!string.IsNullOrEmpty(uuid)) { sb.AppendLine($"SKIP 뒤끝 표 {table}가 있다 — 실제 랭킹을 더럽히지 않게 건너뜀"); return; }
            sb.AppendLine($"시기 {era} · 뒤끝 표 {table ?? "(봉인기 — 랭킹 없음)"} 없음 → 기기 기록만(콘솔에서 만들면 올라간다)");

            int before = RecordBoard.PersonalBest(true, ch, era);
            int expect = before == 0 || 5240 < before ? 5240 : before;   // 이미 더 빠른 기록이 있으면 그대로여야
            run.SetCurrentRoomKind(RoomPlanKind.Boss, countSpecialVisit: false);
            run.RunStats.AddCombatTime(52.4f);
            RecordBoard.Capture(run);
            int best = RecordBoard.PersonalBest(true, ch, era);
            sb.AppendLine($"{(data.records.Contains("bestBoss" + ch + "_") ? "PASS" : "FAIL")} 시기별 칸 — {data.records}");
            sb.AppendLine($"{(best == expect ? "PASS" : "FAIL")} 처치 52.4초(전 최고 {RankingService.Format(before)}) → 내 최고 {RankingService.Format(best)} · 챕터 전투 최고 {RankingService.Format(RecordBoard.PersonalBest(false, ch, era))}");
            await RecordBoard.ShowAsync();
            await UniTask.Delay(400, ignoreTimeScale: true);
            ScreenCapture.CaptureScreenshot("Temp/ui_shots/RB1102.png");
            await UniTask.Delay(300, ignoreTimeScale: true);

            run.SetCurrentRoomKind(RoomPlanKind.Boss, countSpecialVisit: false);
            run.RunStats.AddCombatTime(70f);
            RecordBoard.Capture(run);
            int best2 = RecordBoard.PersonalBest(true, ch, era);
            sb.AppendLine($"{(best2 == best ? "PASS" : "FAIL")} 두 번째 70초(더 느림) → 내 최고 그대로 {RankingService.Format(best2)}");
            bool min = data.SetRecordMin("probeMin", 300) && !data.SetRecordMin("probeMin", 400) && data.SetRecordMin("probeMin", 200) && data.GetRecord("probeMin") == 200;
            sb.AppendLine($"{(min ? "PASS" : "FAIL")} SetRecordMin — 처음 · 더 큰 값 무시 · 더 작은 값");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            if (data != null && data.records != (records0 ?? ""))
            {
                data.records = records0 ?? "";   // 실측이 적은 기록을 걷고 원래대로 저장
                BackendGameData.Instance.SaveAsync().Forget();
            }
            run?.SetCurrentRoomKind(kind0, countSpecialVisit: false, danger: danger0, runeFocus: focus0);
            File.WriteAllText("Temp/record_board1102.txt", sb.ToString());
            Debug.Log("[RB1102] 끝 → Temp/record_board1102.txt");
        }
    }
}
#endif
