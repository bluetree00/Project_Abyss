#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 11-02 사용자 「랭킹을 갱신했을 때 인게임에 어떻게 나오는지 보여줘」 — 화면 사진(테스트 허브 런 중) · 결과 Temp/ui_shots/RS1102_*.png · 로그 「[RS1102] 끝」.
/// ① 보스 처치 기록 알림: 게임이 쓰는 RecordBoard.Line을 그대로 불러 세 줄(지금 — 표 없음 신기록 · 표가 생긴 뒤 신기록 + 1 · 2등 · 신기록 아님 + 내 최고)을 띄우고 사진.
/// ② 해방기 랭킹 비석: 그대로 연 화면(리더보드 없음 → 준비 중) 사진 → 같은 창 위에 <b>예시 줄</b>(서버에 올리지 않음)을 그려 넣은 사진.
/// 한국어 · 영어 두 번. 메타 기록 · 언어는 처음대로 되돌린다(저장 안 함 — 메모리 안 값만 잠깐).
/// </summary>
public static class RankShow1102ProbeEditor
{
    private const BindingFlags Stat = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem("RelicFairy/Debug/11-02 랭킹 갱신 화면 보기 (테스트 허브 런, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[RS1102] 플레이 중에만"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        Directory.CreateDirectory("Temp/ui_shots");
        var lang0 = Loc.Current;
        var data = BackendGameData.Instance?.Data;
        string records0 = data?.records;
        var log = new StringBuilder();
        try
        {
            foreach (var lang in new[] { Loc.Language.Korean, Loc.Language.English })
            {
                Loc.SetLanguage(lang);
                string tag = lang == Loc.Language.Korean ? "ko" : "en";
                await Notices(tag, data, log);
                await Board(tag, log);
            }
        }
        catch (System.Exception e) { log.AppendLine("예외: " + e); }
        finally
        {
            if (data != null) data.records = records0;
            Loc.SetLanguage(lang0);
            File.WriteAllText("Temp/rank_show1102.txt", log.ToString());
            Debug.Log("[RS1102] 끝");
        }
    }

    // ① 보스 처치 기록 알림 — RecordBoard.Line 그대로
    private static async UniTask Notices(string tag, UserGameData data, StringBuilder log)
    {
        var line = typeof(RecordBoard).GetMethod("Line", Stat);
        int ch = 2;
        string bossKey = "bestBoss" + ch, roomsKey = "bestRooms" + ch;
        data?.SetRecordMin(roomsKey, 10002);   // 「내 최고 1:40.02」를 보이려고 메모리 안에만 잠깐(끝나면 되돌림)
        var none = new List<RankingService.Entry>();
        var top  = new List<RankingService.Entry>
        {
            new(1, Ex("Percival"), 3100, Loc.T("칼리번") + " · Liberated", false),
            new(2, Ex("Gareth"),   3520, Loc.T("클라렌트") + " · Liberated", false),
        };
        string a = (string)line.Invoke(null, new object[] { Loc.T("보스전"), 5241, true, bossKey, none });
        string b = (string)line.Invoke(null, new object[] { Loc.T("보스전"), 5241, true, bossKey, top });
        string c = (string)line.Invoke(null, new object[] { Loc.F("챕터 {0} 전투", ch), 10810, false, roomsKey, top });
        ItemEffectVfxHelper.ShowNotice(a);
        await UniTask.Delay(350, ignoreTimeScale: true);
        ItemEffectVfxHelper.ShowNotice(b);
        await UniTask.Delay(350, ignoreTimeScale: true);
        ItemEffectVfxHelper.ShowNotice(c);
        await UniTask.Delay(900, ignoreTimeScale: true);
        ScreenCapture.CaptureScreenshot($"Temp/ui_shots/RS1102_{tag}_notice.png");
        await UniTask.Delay(400, ignoreTimeScale: true);
        log.AppendLine($"[{tag}] {a}\n[{tag}] {b}\n[{tag}] {c}");
        await UniTask.Delay(4500, ignoreTimeScale: true);   // 알림이 걷히게
    }

    private static string Ex(string name) => (Loc.Current == Loc.Language.Korean ? "(예시) " : "(Example) ") + name;

    // ② 해방기 랭킹 비석 — 그대로 · 예시 줄
    private static async UniTask Board(string tag, StringBuilder log)
    {
        var open = RankingBoardOverlay.ShowAsync(StoryEra.Liberated);
        await UniTask.Delay(2500, ignoreTimeScale: true);
        ScreenCapture.CaptureScreenshot($"Temp/ui_shots/RS1102_{tag}_board_now.png");
        await UniTask.Delay(400, ignoreTimeScale: true);

        var go = GameObject.Find("@RankingBoard");
        var board = go != null ? go.transform.Find("Board") as RectTransform : null;
        if (board != null)
        {
            var t = typeof(RankingBoardOverlay);
            bool art = t.GetField("s_tablet", Stat)?.GetValue(null) != null;
            float bw = art ? 1040f : 900f, bh = art ? 748f : 700f, rowH = art ? 36f : 40f, pad = art ? 138f : 46f;
            float top = bh * 0.5f - pad - 90f;
            foreach (var txt in board.GetComponentsInChildren<TMP_Text>())
                if (LocTextGuard.HasHangul(txt.text) && txt.text.Contains("준비 중")) txt.text = Loc.Current == Loc.Language.Korean ? "(예시 — 서버에 리더보드가 생기면 이렇게 보인다)" : "(Example — this is how it looks once the leaderboard exists)";
            // 실제 줄(서버에서 읽은 것)은 가리고 예시 줄만 — 머리 줄 아래에 있는 글 · 메달을 끈다
            foreach (RectTransform child in board)
                if (child.anchoredPosition.y < top - rowH * 0.5f && child.anchoredPosition.y > -bh * 0.5f + 80f) child.gameObject.SetActive(false);
            var row = t.GetMethod("Row", Stat);
            var sample = new (string name, int centis, string weapon, bool mine)[]
            {
                (Ex("Percival"), 228512, Loc.T("칼리번"), false), (Ex("Gareth"), 241030, Loc.T("클라렌트"), false),
                (Ex(Loc.Current == Loc.Language.Korean ? "나" : "Me"), 249944, Loc.T("무형검"), true), (Ex("Bedivere"), 262007, Loc.T("칼리번"), false),
                (Ex("Tristan"), 275871, Loc.T("칼리번"), false),
            };
            for (int i = 0; i < sample.Length; i++)
            {
                var s = sample[i];
                row.Invoke(null, new object[] { board, bw, rowH, top - rowH * (i + 1), $"{i + 1}", s.name, RankingService.Format(s.centis),
                                                s.weapon, s.mine ? UITheme.Gold : UITheme.Ink, 20f, i + 1 <= 3 ? i + 1 : 0 });
            }
            await UniTask.Delay(600, ignoreTimeScale: true);
            ScreenCapture.CaptureScreenshot($"Temp/ui_shots/RS1102_{tag}_board_example.png");
            await UniTask.Delay(400, ignoreTimeScale: true);
        }
        else log.AppendLine($"[{tag}] 비석 창 못 찾음");

        var veil = go != null ? go.transform.Find("Veil") : null;
        if (veil != null && veil.TryGetComponent<Button>(out var b)) b.onClick.Invoke();
        await open;
        await UniTask.Delay(500, ignoreTimeScale: true);
    }
}
#endif
