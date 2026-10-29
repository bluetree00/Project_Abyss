#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 10-29 이벤트 방 개편 실측 — 문 고정(제물 제단 · 보물고 · 신탁) 뒤 그 방 안에서 돌린다.
/// 제물: 낼 수 있는 칸만 서나 · 물약 1개만 빠지나 · 유물 기억 창 · 창 뒤 출구. 보물고: 상자 모델 5개 · 조작이 풀린 뒤 시간 · 상자마다 보상.
/// 신탁: 문 셋(강화재료 · 금화 · 정수) · 고른 것만. 결과 Temp/fix1029_c.txt · Temp/ui_shots/F29E_*.png · 로그 「[Fix29E] 끝」.
/// </summary>
public static class Fix1029EventProbeEditor
{
    private const string Tag = "[Fix29E]";
    private const string ShotDir = "Temp/ui_shots";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem("RelicFairy/Debug/10-29 C 이벤트 방 문 고정/제물 제단 (ch1)")] private static void ForceSacrifice() => EventMinigameDebugMenu.ForceDoor("ch1_event_sacrifice_01");
    [MenuItem("RelicFairy/Debug/10-29 C 이벤트 방 문 고정/보물고 (ch1)")]   private static void ForceVault()     => EventMinigameDebugMenu.ForceDoor("ch1_event_vault_01");
    [MenuItem("RelicFairy/Debug/10-29 C 이벤트 방 문 고정/신탁 (ch1)")]     private static void ForceOracle()    => EventMinigameDebugMenu.ForceDoor("ch1_event_oracle_01");
    [MenuItem("RelicFairy/Debug/10-29 C 이벤트 방 문 고정/끔")]             private static void ForceOff()       => EventMinigameDebugMenu.ForceDoor("");

    private const string AutoRoot = "RelicFairy/Debug/런 구조 자동 실측/";
    private static double s_autoUntil;

    /// <summary>런 구조 자동 진행으로 방을 지나다가 상호작용 이벤트 방에 들어서면 곧바로 자동 진행을 멈추고(자동 해결 2초 전) 실측을 돈다.</summary>
    [MenuItem("RelicFairy/Debug/10-29 C 자동 진행 → 이벤트 방에서 멈추고 실측 (테스트 허브, 플레이 중)")]
    private static void AutoToEvent()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중에"); return; }
        EditorApplication.ExecuteMenuItem(AutoRoot + "시작 (테스트 허브에서, 플레이 중)");
        s_autoUntil = EditorApplication.timeSinceStartup + 240.0;
        EditorApplication.update -= WatchForEvent;
        EditorApplication.update += WatchForEvent;
    }

    private static void WatchForEvent()
    {
        if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > s_autoUntil)
        {
            EditorApplication.update -= WatchForEvent;
            Debug.Log($"{Tag} 끝 — 이벤트 방에 닿지 못했다(시간 초과)");
            return;
        }
        if (Object.FindFirstObjectByType<WorldInteractionChallenge>() == null) return;
        EditorApplication.update -= WatchForEvent;
        EditorApplication.ExecuteMenuItem(AutoRoot + "중지·기록");
        RunAsync().Forget();
    }

    [MenuItem("RelicFairy/Debug/10-29 C 이벤트 방 실측 (그 방 안 · 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null) { Debug.LogWarning($"{Tag} 런 안에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("10-29 C 이벤트 방\n");
        int pass = 0, total = 0;
        void Check(string what, bool ok, string detail = "") { total++; if (ok) pass++; sb.AppendLine($"CHECK {what}: {ok}{(detail.Length > 0 ? "  — " + detail : "")}"); }
        Directory.CreateDirectory(ShotDir);
        var run = GameRunBootstrapper.Instance.Run;
        try
        {
            var ch = Object.FindObjectsByType<WorldInteractionChallenge>(FindObjectsSortMode.None).FirstOrDefault();
            // 사람처럼 — 방 입장 연출이 끝나 출구 쪽이 해결 알림을 듣기 시작한 뒤에 누른다(그 전엔 게임도 F를 받지 않는다)
            var listen = typeof(WorldInteractionChallenge).GetField("OnResolved", Any);
            for (float t = 0f; ch != null && listen?.GetValue(ch) == null && t < 25f; t += 0.25f) await Wait(0.25f);
            sb.AppendLine($"해결 알림 구독: {listen?.GetValue(ch) != null}");
            await Wait(1.5f);
            sb.AppendLine($"방 pool_key 추정: {ch?.GetType().Name ?? "상호작용 없음"} · 물약 {run.PlayerState?.PotionCount} · 금화 {run.PlayerState?.TempGold}");
            await ShotAsync("F29E_room_" + (ch != null ? ch.GetType().Name : "none"));
            if (ch == null) { sb.AppendLine("상호작용 이벤트 방이 아님 — 끝"); return; }

            var markers = (typeof(WorldInteractionChallenge).GetField("_markers", Any)?.GetValue(ch) as IList)?.Cast<object>().ToList();
            string LabelOf(object m) => (m.GetType().GetField("Label")?.GetValue(m) as TMP_Text)?.text ?? "";
            object TagOf(object m) => m.GetType().GetField("Tag")?.GetValue(m);
            sb.AppendLine("칸: " + string.Join(" / ", markers.Select(m => $"{LabelOf(m)}({TagOf(m)})")));
            var activate = ch.GetType().GetMethod("OnActivate", Any);
            int gold0 = run.PlayerState?.TempGold ?? 0;
            int mat0 = run.FuelBank?.Get(FuelKind.EnhanceMaterial) ?? 0, pot0 = run.PlayerState?.PotionCount ?? 0;

            if (ch is SacrificeAltarChallenge)
            {
                bool hasPotion = markers.Any(m => (TagOf(m) as string) == "potion");
                bool hasGold   = markers.Any(m => (TagOf(m) as string) == "gold");
                Check("① 낼 수 있는 칸만", hasPotion == (pot0 > 0) && hasGold == SacrificeAltarChallenge.CanPayGold(run), $"물약 칸 {hasPotion} · 금화 칸 {hasGold}");
                int idx = markers.FindIndex(m => (TagOf(m) as string) == (hasPotion ? "potion" : "gold"));
                activate.Invoke(ch, new object[] { idx });
                await Wait(3.5f);
                var draft = Object.FindFirstObjectByType<UI_RelicPartDraftPopup>();
                Check("② 바치면 유물 기억 창", draft != null);
                Check("③ 물약은 1개만(또는 금화만) 빠짐", hasPotion ? run.PlayerState.PotionCount == pot0 - 1 : run.PlayerState.TempGold < gold0,
                      $"물약 {pot0} → {run.PlayerState.PotionCount} · 금화 {gold0} → {run.PlayerState.TempGold}");
                await ShotAsync("F29E_sacrifice_draft");
                bool exitBefore = ExitOpen();
                if (draft != null)
                {
                    var guide = typeof(UI_RelicPartDraftPopup).GetMethod("CloseGuide", Any);
                    guide?.Invoke(draft, null);
                    typeof(UI_RelicPartDraftPopup).GetField("_selected", Any)?.SetValue(draft, 0);
                    typeof(UI_RelicPartDraftPopup).GetMethod("OnConfirmClicked", Any)?.Invoke(draft, null);
                    await Wait(4f);
                }
                Check("④ 창을 고르기 전엔 출구 닫힘 · 고른 뒤 열림", !exitBefore && ExitOpen(), $"전 {exitBefore} · 뒤 {ExitOpen()}");
            }
            else if (ch is TreasureVaultChallenge)
            {
                int props = Object.FindObjectsByType<Animator>(FindObjectsSortMode.None).Count(a => a.name.Contains("Chest") && Vector3.Distance(a.transform.position, run.Player.transform.position) < 15f);
                Check("① 상자 모델 5개", props >= 5, $"{props}개");
                bool started = (bool)(typeof(TreasureVaultChallenge).GetField("_started", Any)?.GetValue(ch) ?? false);
                float left = (float)(typeof(TreasureVaultChallenge).GetField("_timeLeft", Any)?.GetValue(ch) ?? 0f);
                Check("② 화면이 걷힌 뒤 시간이 흐른다(15초에서 시작)", started && left > 11f && left <= 15f, $"시작 {started} · 남은 {left:0.0}초");
                for (int i = 0; i < markers.Count; i++) { activate.Invoke(ch, new object[] { i }); await Wait(0.3f); }
                await Wait(2.5f);
                int gold1 = run.PlayerState?.TempGold ?? 0, mat1 = run.FuelBank?.Get(FuelKind.EnhanceMaterial) ?? 0;
                Check("③ 상자마다 보상(금화 · 강화재료 · 정수 튐)", mat1 > mat0, $"금화 {gold0} → {gold1}(줍기 전) · 강화재료 {mat0} → {mat1}");
                await ShotAsync("F29E_vault_open");
                Check("④ 다 열면 출구 열림", ExitOpen());
            }
            else if (ch is OracleChoiceChallenge)
            {
                Check("① 문 셋", markers.Count == 3, string.Join(" / ", markers.Select(LabelOf)));
                activate.Invoke(ch, new object[] { 0 });
                await Wait(2.5f);
                int mat1 = run.FuelBank?.Get(FuelKind.EnhanceMaterial) ?? 0;
                Check("② 첫 문 = 강화재료가 들어온다", mat1 > mat0, $"{mat0} → {mat1}");
                await ShotAsync("F29E_oracle");
                Check("③ 출구 열림", ExitOpen());
            }
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            sb.AppendLine($"\n통과 {pass}/{total}");
            File.WriteAllText("Temp/fix1029_c.txt", sb.ToString());
            Debug.Log($"{Tag} 끝 — {pass}/{total}");
        }
    }

    /// <summary>지금 방 출구가 열렸나 — RunFlowController가 해결을 받아 출구를 연 상태.</summary>
    private static bool ExitOpen()
    {
        var flow = Object.FindFirstObjectByType<RunFlowController>();
        var f = typeof(RunFlowController).GetField("_currentRoomCleared", Any);
        return flow != null && f != null && (bool)f.GetValue(flow);
    }

    private static UniTask Wait(float seconds) => UniTask.Delay(System.TimeSpan.FromSeconds(seconds), DelayType.Realtime);

    private static async UniTask ShotAsync(string name)
    {
        var runner = Object.FindFirstObjectByType<Managers>();
        if (runner == null) return;
        await UniTask.WaitForEndOfFrame(runner);
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        if (tex == null) return;
        File.WriteAllBytes(Path.Combine(ShotDir, name + ".png"), tex.EncodeToPNG());
        Object.Destroy(tex);
    }
}
#endif
