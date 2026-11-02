#if UNITY_EDITOR
using System.IO;
using System.Text;
using BackEnd;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-29 뒤끝 콘솔 등록 검증(사용자 「테이블이랑 차트 업로드 다 끝나서 검증」) — 로그인 뒤(플레이 중)에만.
/// ① 리더보드 목록: 표 RANK_LIBERATION · RANK_NIGHTMARE마다 하나 · 집계 clearTime · 추가 항목 weapon · 정렬 값.
/// ② RankingService가 표 이름으로 uuid를 찾나 · 순위 읽기 성공.
/// ③ 게임 정보 표 쓰기: 내 줄이 없으면 시험 줄(99:59:59.99 · "verify")을 넣었다가 바로 지운다 — 리더보드 갱신(UpdateMyDataAndRefreshLeaderboard)은 부르지 않아 순위표엔 안 오른다.
/// ④ CDN: 캐시를 비우고 ITEM_DATA · CHAPTER_1~4_ROOM_POOL을 다시 받아 Temp/cdn_*.txt로 적는다(원본 CSV 대조는 밖에서).
/// 결과 Temp/backend1029_verify.txt · 로그 「[Verify1029] 끝」.
/// </summary>
public static class Backend1029VerifyProbeEditor
{
    private const string Tag = "[Verify1029]";
    private const int    TestCentis = 35999999;

    [MenuItem("RelicFairy/Debug/10-29 뒤끝 검증 — 랭킹 표 · 리더보드 · CDN 차트 (플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중에만(로그인 뒤)"); return; }
        RunAsync().Forget();
    }

    /// <summary>
    /// 스키마 미정의 표는 콘솔 리더보드 만들기 창이 「최근 100줄」에서 열을 읽는다 — 빈 표면 clearTime이 안 뜬다.
    /// 내 줄(99:59:59.99 · "seed")을 남겨 두고, 리더보드를 만든 뒤 「씨앗 줄 지우기」로 지운다(리더보드 갱신은 부르지 않아 순위표엔 안 오른다).
    /// </summary>
    [MenuItem("RelicFairy/Debug/10-29 랭킹 표 씨앗 줄 남기기 — 리더보드 만들기용 (플레이 중)")]
    private static void Seed() => SeedOrClear(true);

    [MenuItem("RelicFairy/Debug/10-29 랭킹 표 씨앗 줄 지우기 (플레이 중)")]
    private static void ClearSeed() => SeedOrClear(false);

    private static void SeedOrClear(bool seed)
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중에만(로그인 뒤)"); return; }
        var sb = new StringBuilder(seed ? "씨앗 줄 남기기\n" : "씨앗 줄 지우기\n");
        foreach (string table in new[] { RankingService.LiberationTable, RankingService.NightmareTable })
        {
            var my = Backend.GameData.GetMyData(table, new Where());
            var rows = my.IsSuccess() ? my.FlattenRows() : null;
            int have = rows != null ? rows.Count : -1;
            if (seed)
            {
                if (have != 0) { sb.AppendLine($"{table}: 건너뜀(내 줄 {have}개 · {my.GetStatusCode()})"); continue; }
                var param = new Param();
                param.Add("clearTime", TestCentis);
                param.Add("weapon", "seed");
                var ins = Backend.GameData.Insert(table, param);
                sb.AppendLine($"{table}: 넣기 {ins.GetStatusCode()} {ins.GetMessage()}");
            }
            else
            {
                for (int i = 0; i < have; i++)
                {
                    if (!rows[i].ContainsKey("weapon") || rows[i]["weapon"].ToString() != "seed") { sb.AppendLine($"{table}: 씨앗이 아닌 줄은 남김"); continue; }
                    var del = Backend.GameData.DeleteV2(table, rows[i]["inDate"].ToString(), Backend.UserInDate);
                    sb.AppendLine($"{table}: 지우기 {del.GetStatusCode()} {del.GetMessage()}");
                }
                if (have <= 0) sb.AppendLine($"{table}: 지울 줄 없음({have})");
            }
        }
        File.WriteAllText("Temp/backend1029_seed.txt", sb.ToString(), new UTF8Encoding(false));
        Debug.Log($"{Tag} 씨앗 끝\n{sb}");
    }

    /// <summary>
    /// 10-30 사용자 「랭킹 시스템도 잘 작동하는지」 — 게임이 쓰는 그대로 끝까지: RankingService.SubmitAsync(표 줄 + 리더보드 갱신) → LoadAsync → 비석 창 촬영.
    /// 시험 기록은 가장 느린 99:59:59.99 · 무기 「TEST」(늘 꼴찌 — 진짜 기록을 밀어내지 않는다). 리더보드 줄은 클라이언트가 못 지운다 — 콘솔 「현재 순위」에서 지운다.
    /// 결과 Temp/ranking_e2e_1030.txt · Temp/ui_shots/RK30_*.png · 로그 「[Verify1029] 끝까지 끝」.
    /// </summary>
    [MenuItem("RelicFairy/Debug/10-30 랭킹 끝까지 — 시험 기록 올리기 · 순위 읽기 · 비석 촬영 (플레이 중)")]
    private static void EndToEnd()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중에만(로그인 뒤)"); return; }
        EndToEndAsync().Forget();
    }

    private static async UniTaskVoid EndToEndAsync()
    {
        var sb = new StringBuilder("10-30 랭킹 끝까지\n");
        Directory.CreateDirectory("Temp/ui_shots");
        // 고치기 전 실측이 이 기기 계정 이름에 번호를 붙였다(「bg000727#4257」) — 스팀 이름으로 한 번 되돌린다
        string persona = Steamworks.SteamFriends.GetPersonaName();
        string before = Backend.UserNickName;
        if (!string.IsNullOrEmpty(persona) && before != null && before.StartsWith(persona + "#"))
        {
            var r = Backend.BMember.UpdateNickname(persona);
            sb.AppendLine($"이름 되돌림 「{before}」 → 「{persona}」 {r.GetStatusCode()}");
        }
        foreach (var era in new[] { StoryEra.Liberated, StoryEra.NightmareMode })
        {
            bool ok = await RankingService.SubmitAsync(era, TestCentis / 100f, "TEST");
            var (top, mine) = await RankingService.LoadAsync(era);
            sb.AppendLine($"{era}: 올리기 {ok} · 순위 {top.Count}줄 · 내 줄 {(mine.HasValue ? $"{mine.Value.Rank}위 「{mine.Value.Nickname}」 {RankingService.Format(mine.Value.Centis)} · {mine.Value.Weapon}" : "없음")}");
            foreach (var e in top) sb.AppendLine($"    {e.Rank}위 「{e.Nickname}」 {RankingService.Format(e.Centis)} · {e.Weapon}{(e.IsMine ? " (나)" : "")}");

            RankingBoardOverlay.ShowAsync(era).Forget();
            await UniTask.Delay(2500, DelayType.Realtime);
            ScreenCapture.CaptureScreenshot($"Temp/ui_shots/RK30_{era}.png");
            await UniTask.Delay(500, DelayType.Realtime);
            var go = GameObject.Find("@RankingBoard");
            if (go != null) Object.Destroy(go);
            await UniTask.Delay(600, DelayType.Realtime);
        }
        File.WriteAllText("Temp/ranking_e2e_1030.txt", sb.ToString(), new UTF8Encoding(false));
        Debug.Log($"{Tag} 끝까지 끝\n{sb}");
    }

    /// <summary>
    /// 10-30 사용자 「랭킹 확인용은 지우고」 — 내 표 줄(weapon 「TEST」)을 지우고 리더보드에서도 빠졌는지 읽어 본다.
    /// 결과 Temp/ranking_cleanup_1030.txt · 로그 「[Verify1029] 지우기 끝」.
    /// </summary>
    [MenuItem("RelicFairy/Debug/10-30 랭킹 시험 기록 지우기 (플레이 중)")]
    private static void CleanupTest()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중에만(로그인 뒤)"); return; }
        CleanupTestAsync().Forget();
    }

    private static async UniTaskVoid CleanupTestAsync()
    {
        var sb = new StringBuilder("10-30 랭킹 시험 기록 지우기\n");
        foreach (var era in new[] { StoryEra.Liberated, StoryEra.NightmareMode })
        {
            string table = era == StoryEra.NightmareMode ? RankingService.NightmareTable : RankingService.LiberationTable;
            var my = Backend.GameData.GetMyData(table, new Where());
            var rows = my.IsSuccess() ? my.FlattenRows() : null;
            for (int i = 0; rows != null && i < rows.Count; i++)
            {
                if (!rows[i].ContainsKey("weapon") || rows[i]["weapon"].ToString() != "TEST") { sb.AppendLine($"{table}: 시험이 아닌 줄은 남김"); continue; }
                var del = Backend.GameData.DeleteV2(table, rows[i]["inDate"].ToString(), Backend.UserInDate);
                sb.AppendLine($"{table}: 줄 지우기 {del.GetStatusCode()} {del.GetMessage()}");
            }
            await UniTask.Delay(1500, DelayType.Realtime);
            var (top, mine) = await RankingService.LoadAsync(era);
            sb.AppendLine($"{era}: 리더보드 {top.Count}줄 · 내 줄 {(mine.HasValue ? $"{mine.Value.Rank}위 {RankingService.Format(mine.Value.Centis)} · {mine.Value.Weapon}" : "없음")}");
        }
        File.WriteAllText("Temp/ranking_cleanup_1030.txt", sb.ToString(), new UTF8Encoding(false));
        Debug.Log($"{Tag} 지우기 끝\n{sb}");
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("10-29 뒤끝 검증\n");
        int pass = 0, total = 0;
        void Check(string what, bool ok, string detail)
        {
            total++; if (ok) pass++;
            sb.AppendLine($"CHECK {what}: {ok}  — {detail}");
        }

        // ① 리더보드 목록
        var boards = Backend.Leaderboard.User.GetLeaderboards();
        Check("① 리더보드 목록 읽기", boards.IsSuccess(), $"{boards.GetStatusCode()} {boards.GetMessage()}");
        if (boards.IsSuccess())
        {
            foreach (string table in new[] { RankingService.LiberationTable, RankingService.NightmareTable })
            {
                int count = 0;
                foreach (var it in boards.GetLeaderboardTableList())
                {
                    if (it.table != table) continue;
                    count++;
                    sb.AppendLine($"    {table}: 제목 「{it.title}」 · uuid {it.uuid} · 집계 {it.column} · 정렬 {it.order} · 추가 {it.extraDataColumn}({it.extraDataType}) · 초기화 {it.isReset} · 주기 {it.rankType}");
                    Check($"① {table} 집계 열 = clearTime", it.column == "clearTime", it.column);
                    Check($"① {table} 추가 항목 = weapon", it.extraDataColumn == "weapon", it.extraDataColumn);
                    Check($"① {table} 정렬 오름차순", it.order != null && it.order.ToLowerInvariant().StartsWith("asc"), it.order);
                }
                Check($"① {table} 리더보드 하나", count == 1, $"{count}개");
            }
        }

        // ② 서비스가 찾고 읽나
        foreach (var era in new[] { StoryEra.Liberated, StoryEra.NightmareMode })
        {
            bool found = await RankingService.ResolveAsync(era);
            Check($"② {era} uuid 찾음", found, found ? "찾음" : "못 찾음");
            if (!found) continue;
            var (top, mine) = await RankingService.LoadAsync(era);
            sb.AppendLine($"    {era} 순위 {top.Count}줄 · 내 줄 {(mine.HasValue ? RankingService.Format(mine.Value.Centis) : "없음")}");
        }

        // ③ 게임 정보 표 쓰기 — 시험 줄 넣고 지우기
        foreach (string table in new[] { RankingService.LiberationTable, RankingService.NightmareTable })
        {
            var my = Backend.GameData.GetMyData(table, new Where());
            Check($"③ {table} 내 줄 읽기", my.IsSuccess(), $"{my.GetStatusCode()} {my.GetMessage()}");
            if (!my.IsSuccess()) continue;
            var rows = my.FlattenRows();
            if (rows != null && rows.Count > 0) { sb.AppendLine($"    {table}: 내 줄이 이미 있어 쓰기 시험은 건너뜀({rows[0].ToJson()})"); continue; }
            var param = new Param();
            param.Add("clearTime", TestCentis);
            param.Add("weapon", "verify");
            var ins = Backend.GameData.Insert(table, param);
            Check($"③ {table} 시험 줄 넣기(열 이름 · 형식)", ins.IsSuccess(), $"{ins.GetStatusCode()} {ins.GetMessage()}");
            if (!ins.IsSuccess()) continue;
            var del = Backend.GameData.DeleteV2(table, ins.GetInDate(), Backend.UserInDate);
            Check($"③ {table} 시험 줄 지우기", del.IsSuccess(), $"{del.GetStatusCode()} {del.GetMessage()}");
        }

        // ④ CDN 차트
        ChartLoader.ClearCache();
        foreach (string chart in new[] { "ITEM_DATA", "CHAPTER_1_ROOM_POOL", "CHAPTER_2_ROOM_POOL", "CHAPTER_3_ROOM_POOL", "CHAPTER_4_ROOM_POOL" })
        {
            var rsb = new StringBuilder();
            int n = ChartLoader.Load(chart, row => rsb.AppendLine(row.ToJson()));
            File.WriteAllText($"Temp/cdn_{chart}.txt", rsb.ToString(), new UTF8Encoding(false));
            Check($"④ CDN {chart} 받음", n > 0, $"{n}행 → Temp/cdn_{chart}.txt");
        }

        sb.AppendLine($"통과 {pass}/{total}");
        File.WriteAllText("Temp/backend1029_verify.txt", sb.ToString(), new UTF8Encoding(false));
        Debug.Log($"{Tag} 끝 — {pass}/{total}\n{sb}");
    }
}
#endif
