#if UNITY_EDITOR
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 11-02 Q18 환전소 실측(테스트 허브 런 중) — 결과 Temp/exchange1102.txt · 사진 Temp/ui_shots/EX1102.png · 로그 「[EX1102] 끝」.
/// 골드 300 · 원석 6 · 강화재료 6을 얹고 환율마다 바꿔 본다(값 · 모자랄 때 · 「얻은 양」에 안 셈) → 창을 열어 사진 → [닫기]. 끝나면 처음 양으로 되돌린다.
/// </summary>
public static class Exchange1102ProbeEditor
{
    [MenuItem("RelicFairy/Debug/11-02 환전소 실측 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[EX1102] 플레이 중에만"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("11-02 환전소\n");
        Directory.CreateDirectory("Temp/ui_shots");
        var run = GameRunBootstrapper.Instance?.Run;
        int g0 = 0, o0 = 0, m0 = 0, gained = 0;
        System.Action<FuelKind, int> onGained = (_, n) => gained += n;
        try
        {
            if (run?.PlayerState == null || run.FuelBank == null) { sb.AppendLine("런 없음"); return; }
            g0 = run.PlayerState.TempGold; o0 = run.FuelBank.RuneOre; m0 = run.FuelBank.EnhanceMaterial;
            run.PlayerState.AddTempGold(300);
            run.FuelBank.Add(FuelKind.RuneOre, 6);
            run.FuelBank.Add(FuelKind.EnhanceMaterial, 6);
            run.FuelBank.OnGained += onGained;
            sb.AppendLine($"시작 — 골드 {G(run)} · 원석 {O(run)} · 강화재료 {M(run)}");

            var R = RunExchange.Rates;
            Check(sb, run, R[0], 5, -150, 5, 0);    // 골드 30 → 원석 1 ×5
            Check(sb, run, R[1], 1, -30, 0, 1);     // 골드 30 → 재료 1
            Check(sb, run, R[2], 2, 0, -4, 2);      // 원석 2 → 재료 1 ×2
            Check(sb, run, R[3], 3, 0, 3, -6);      // 재료 2 → 원석 1 ×3
            Check(sb, run, R[4], 1, ShopRoomController.OreToGold, -1, 0);     // 원석 1 → 골드 3(상점 환전과 같은 값)
            int before = RunExchange.MaxTimes(run, R[5]);
            int done = RunExchange.Trade(run, R[5], int.MaxValue);   // 재료 전부 → 골드
            sb.AppendLine($"{(done == before && M(run) == 0 ? "PASS" : "FAIL")} 강화재료 전부 → 골드 — {done}번 · 재료 {M(run)}");
            int none = RunExchange.Trade(run, R[5], 1);
            sb.AppendLine($"{(none == 0 ? "PASS" : "FAIL")} 모자랄 때 — 바꾼 횟수 {none}");
            sb.AppendLine($"{(gained == 0 ? "PASS" : "FAIL")} 「얻은 양」(결과창)에 안 셈 — OnGained 합 {gained}");

            var open = ExchangePopupView.OpenAsync(run, default);
            await UniTask.Delay(700, ignoreTimeScale: true);
            sb.AppendLine($"창 열림 {ExchangePopupView.IsOpen} · 시간 배율 {Time.timeScale:0.00}");
            ScreenCapture.CaptureScreenshot("Temp/ui_shots/EX1102.png");
            await UniTask.Delay(400, ignoreTimeScale: true);
            var go = GameObject.Find("@Exchange");
            Button closeBtn = null;
            if (go != null) foreach (var b in go.GetComponentsInChildren<Button>()) if (b.name == "Btn_닫기") closeBtn = b;
            closeBtn?.onClick.Invoke();
            await open;
            sb.AppendLine($"{(!ExchangePopupView.IsOpen && Time.timeScale > 0.5f ? "PASS" : "FAIL")} 닫기 — 열림 {ExchangePopupView.IsOpen} · 시간 배율 {Time.timeScale:0.00}");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            if (run?.FuelBank != null) run.FuelBank.OnGained -= onGained;
            if (run?.PlayerState != null && run.FuelBank != null)
            {
                int dg = G(run) - g0;
                if (dg > 0) run.PlayerState.TrySpendGold(dg); else if (dg < 0) run.PlayerState.AddTempGold(-dg);
                run.FuelBank.Exchange(FuelKind.RuneOre, o0 - O(run));
                run.FuelBank.Exchange(FuelKind.EnhanceMaterial, m0 - M(run));
                sb.AppendLine($"되돌림 — 골드 {G(run)}/{g0} · 원석 {O(run)}/{o0} · 강화재료 {M(run)}/{m0}");
            }
            File.WriteAllText("Temp/exchange1102.txt", sb.ToString());
            Debug.Log("[EX1102] 끝 → Temp/exchange1102.txt");
        }
    }

    private static void Check(StringBuilder sb, GameRunSession run, RunExchange.Rate r, int times, int dg, int dOre, int dMat)
    {
        int g = G(run), o = O(run), m = M(run);
        int done = RunExchange.Trade(run, r, times);
        bool ok = done == times && G(run) - g == dg && O(run) - o == dOre && M(run) - m == dMat;
        sb.AppendLine($"{(ok ? "PASS" : "FAIL")} {r.From} {r.Give} → {r.To} {r.Get} ×{times} — 골드 {G(run) - g:+0;-0;0} · 원석 {O(run) - o:+0;-0;0} · 재료 {M(run) - m:+0;-0;0}");
    }

    private static int G(GameRunSession run) => run.PlayerState.TempGold;
    private static int O(GameRunSession run) => run.FuelBank.RuneOre;
    private static int M(GameRunSession run) => run.FuelBank.EnhanceMaterial;
}
#endif
