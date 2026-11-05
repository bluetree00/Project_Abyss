#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using BackEnd;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 11-03 사용자 「(리더보드) 전부 완료했는데 에디터에서 테스트 체크」 — 로그인 뒤(플레이 중)에만. 게임이 쓰는 표 18개(해방기 · 악몽 클리어 + 챕터 기록 16)마다:
/// ① 리더보드 하나 · 집계 clearTime · 추가 항목 weapon · 오름차순 · 초기화 주기.
/// ② RankingService가 표 이름으로 찾고 판을 읽나(전체 인원 · 상위 줄 — 시험 줄이 남았나).
/// ③ 표 쓰기: 내 줄이 없으면 시험 줄(clearTime 정수 · weapon 글)을 넣었다가 바로 지운다 — 리더보드 갱신은 부르지 않아 순위표엔 안 오른다.
/// 결과 Temp/leaderboards1103_verify.txt · 로그 「[LB1103] 끝」.
/// </summary>
public static class Leaderboards1103VerifyProbeEditor
{
    private const string Tag = "[LB1103]";
    private const int    TestCentis = 35999999;

    [MenuItem("RelicFairy/Debug/11-03 뒤끝 리더보드 18개 검증 (플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중에만(로그인 뒤)"); return; }
        RunAsync().Forget();
    }

    private static List<string> Tables()
    {
        var list = new List<string> { RankingService.LiberationTable, RankingService.NightmareTable };
        foreach (var era in new[] { StoryEra.Liberated, StoryEra.NightmareMode })
            foreach (bool boss in new[] { true, false })
                for (int ch = 1; ch <= 4; ch++) list.Add(RecordBoard.TableOf(boss, ch, era));
        return list;
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("11-03 뒤끝 리더보드 18개 검증\n");
        int pass = 0, total = 0;
        void Check(string what, bool ok, string detail)
        {
            total++; if (ok) pass++;
            sb.AppendLine($"{(ok ? "PASS" : "FAIL")} {what}  — {detail}");
        }

        try
        {
            var boards = Backend.Leaderboard.User.GetLeaderboards();
            Check("리더보드 목록 읽기", boards.IsSuccess(), $"{boards.GetStatusCode()} {boards.GetMessage()}");
            if (!boards.IsSuccess()) return;
            var list = boards.GetLeaderboardTableList();
            sb.AppendLine($"    뒤끝 리더보드 전체 {list.Count}개");

            foreach (string table in Tables())
            {
                sb.AppendLine($"\n## {table}");
                // ① 설정
                int count = 0;
                foreach (var it in list)
                {
                    if (it.table != table) continue;
                    count++;
                    sb.AppendLine($"    제목 「{it.title}」 · 집계 {it.column} · 정렬 {it.order} · 추가 {it.extraDataColumn}({it.extraDataType}) · 초기화 {it.isReset} · 주기 {it.rankType}");
                    Check("집계 열 = clearTime", it.column == "clearTime", it.column);
                    Check("추가 항목 = weapon", it.extraDataColumn == "weapon", it.extraDataColumn);
                    Check("정렬 오름차순", it.order != null && it.order.ToLowerInvariant().StartsWith("asc"), it.order);
                }
                Check("리더보드 하나", count == 1, $"{count}개");
                if (count == 0) continue;

                // ② 게임이 찾고 읽나
                var board = await RankingService.LoadBoardAsync(table);
                Check("게임이 찾고 읽음", board.Ready, $"전체 {board.Total}명 · 상위 {board.Top.Count}줄");
                foreach (var e in board.Top)
                    sb.AppendLine($"    줄 {e.Rank}위 {e.Nickname} {RankingService.Format(e.Centis)} {e.Weapon}{(e.IsMine ? " (나)" : "")}");

                // ③ 표 쓰기 — 시험 줄 넣고 지우기(순위표엔 안 오른다)
                var my = Backend.GameData.GetMyData(table, new Where());
                Check("내 줄 읽기", my.IsSuccess(), $"{my.GetStatusCode()} {my.GetMessage()}");
                if (!my.IsSuccess()) continue;
                var rows = my.FlattenRows();
                if (rows != null && rows.Count > 0) { sb.AppendLine($"    내 줄이 이미 있음(쓰기 시험 건너뜀): {rows[0].ToJson()}"); continue; }
                var param = new Param();
                param.Add("clearTime", TestCentis);
                param.Add("weapon", "verify");
                var ins = Backend.GameData.Insert(table, param);
                Check("시험 줄 넣기(열 이름 · 형식)", ins.IsSuccess(), $"{ins.GetStatusCode()} {ins.GetMessage()}");
                if (!ins.IsSuccess()) continue;
                var del = Backend.GameData.DeleteV2(table, ins.GetInDate(), Backend.UserInDate);
                Check("시험 줄 지우기", del.IsSuccess(), $"{del.GetStatusCode()} {del.GetMessage()}");
            }
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            sb.AppendLine($"\n통과 {pass}/{total}");
            File.WriteAllText("Temp/leaderboards1103_verify.txt", sb.ToString(), new UTF8Encoding(false));
            Debug.Log($"{Tag} 끝 — {pass}/{total}");
        }
    }
}
#endif
