#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-29 F6 클리어 시간 칭호 실측 — 단계 경계(해방 30 · 40 · 50분 · 악몽 35 · 45 · 55분)와 비석 기록 줄.
/// 세이브는 건드리지 않는다(「이번 기록」만 리플렉션으로 넣고 창을 연다 — 가장 좋은 칭호 남기기는 런 클리어에서만).
/// 결과 Temp/fix1029_title.txt · Temp/ui_shots/F29T_*.png · 로그 「[Fix29T] 끝」.
/// </summary>
public static class Fix1029RankTitleProbeEditor
{
    private const string Tag = "[Fix29T]";
    private const string ShotDir = "Temp/ui_shots";

    [MenuItem("RelicFairy/Debug/10-29 F6 클리어 시간 칭호 — 경계 · 비석 기록 줄 (플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중에"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("10-29 F6 칭호\n");
        int pass = 0, total = 0;
        void Check(string what, bool ok, string detail)
        {
            total++; if (ok) pass++;
            sb.AppendLine($"CHECK {what}: {ok}  — {detail}");
        }

        // ① 경계 — 분 단위 정확히 그 분까지 위 단계
        (StoryEra era, int min, int sec, int want)[] cases =
        {
            (StoryEra.Liberated, 29, 59, 0), (StoryEra.Liberated, 30, 0, 0), (StoryEra.Liberated, 30, 1, 1),
            (StoryEra.Liberated, 40, 1, 2), (StoryEra.Liberated, 50, 1, 3),
            (StoryEra.NightmareMode, 35, 0, 0), (StoryEra.NightmareMode, 45, 1, 2), (StoryEra.NightmareMode, 70, 0, 3),
        };
        foreach (var c in cases)
        {
            int centis = (c.min * 60 + c.sec) * 100;
            int tier = RankingFlow.TitleTier(c.era, centis);
            Check($"① {c.era} {c.min}:{c.sec:00} → 단계 {c.want}", tier == c.want,
                  $"단계 {tier} 「{RankingFlow.TitleName(c.era, tier)}」 · 다음 {RankingFlow.NextTierMinutes(c.era, tier)}분");
        }

        // ② 비석 기록 줄 — 해방 38:12.40(단계 1) · 악몽 33:05.00(단계 0)
        Directory.CreateDirectory(ShotDir);
        await ShowAndShoot(StoryEra.Liberated, 229240, "F29T_lib", "날랜 해방자", "다음 칭호 30분 안", sb, Check);
        await ShowAndShoot(StoryEra.NightmareMode, 198500, "F29T_nm", "번개 같은 악몽 사냥꾼", null, sb, Check);

        SetLast(StoryEra.Sealed, 0);
        sb.AppendLine($"통과 {pass}/{total}");
        File.WriteAllText("Temp/fix1029_title.txt", sb.ToString());
        Debug.Log($"{Tag} 끝 — {pass}/{total}");
    }

    private static async UniTask ShowAndShoot(StoryEra era, int centis, string shot, string wantTitle, string wantNext,
                                              StringBuilder sb, System.Action<string, bool, string> check)
    {
        SetLast(era, centis);
        RankingBoardOverlay.ShowAsync(era).Forget();
        await UniTask.Delay(1200, DelayType.Realtime);
        var go = GameObject.Find("@RankingBoard");
        string line = "";
        if (go != null)
            foreach (var t in go.GetComponentsInChildren<TMPro.TMP_Text>(true))
                if (t.text.Contains("이번 기록")) { line = t.text; break; }
        bool ok = line.Contains(wantTitle) && (wantNext == null ? !line.Contains("다음 칭호") : line.Contains(wantNext));
        check($"② {era} 비석 기록 줄", ok, line);
        ScreenCapture.CaptureScreenshot($"{ShotDir}/{shot}.png");
        await UniTask.Delay(400, DelayType.Realtime);
        if (go != null) Object.Destroy(go);   // 창은 파괴로 닫힌다(취소 → finally에서 시간 · 입력 되돌림)
        await UniTask.Delay(500, DelayType.Realtime);
    }

    private static void SetLast(StoryEra era, int centis)
    {
        const BindingFlags any = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        typeof(RankingFlow).GetProperty(nameof(RankingFlow.LastEra), any)?.SetValue(null, era);
        typeof(RankingFlow).GetProperty(nameof(RankingFlow.LastCentis), any)?.SetValue(null, centis);
    }
}
#endif
