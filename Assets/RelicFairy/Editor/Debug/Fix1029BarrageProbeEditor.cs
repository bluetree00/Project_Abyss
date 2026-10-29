#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 10-29 D 실측 — ① 룬 보상이 화면 밖이면 가장자리 배지 · 화면 안이면 숨김 ② 금화 · 정수 자석 거리 ③ 탄막의 회랑 탄 수 · 가만히 서면 맞는 횟수.
/// 「출구 보류 확인(보상 늦게 받기)」 자동 진행으로 보상이 놓인 방까지 간 뒤 멈추고 돈다. 결과 Temp/fix1029_d.txt · Temp/ui_shots/F29D_*.png · 로그 「[Fix29D] 끝」.
/// </summary>
public static class Fix1029BarrageProbeEditor
{
    private const string Tag = "[Fix29D]";
    private const string ShotDir = "Temp/ui_shots";
    private const string AutoRoot = "RelicFairy/Debug/런 구조 자동 실측/";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private static double s_until;

    [MenuItem("RelicFairy/Debug/10-29 D 자동 진행 → 보상 놓인 방에서 멈추고 실측 (테스트 허브, 플레이 중)")]
    private static void AutoToReward()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중에"); return; }
        EditorApplication.ExecuteMenuItem(AutoRoot + "시작 — 출구 보류 확인 (처치 위치 맵 밖 · 보상 늦게 받기)");
        s_until = EditorApplication.timeSinceStartup + 240.0;
        EditorApplication.update -= Watch;
        EditorApplication.update += Watch;
    }

    private static void Watch()
    {
        if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > s_until)
        {
            EditorApplication.update -= Watch;
            Debug.Log($"{Tag} 끝 — 보상 놓인 방에 닿지 못했다(시간 초과)");
            return;
        }
        if (Object.FindFirstObjectByType<ClearRewardTrigger>() == null) return;
        EditorApplication.update -= Watch;
        EditorApplication.ExecuteMenuItem(AutoRoot + "중지·기록");
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("10-29 D 룬 보상 배지 · 자석 거리 · 탄막\n");
        int pass = 0, total = 0;
        void Check(string what, bool ok, string detail = "") { total++; if (ok) pass++; sb.AppendLine($"CHECK {what}: {ok}{(detail.Length > 0 ? "  — " + detail : "")}"); }
        Directory.CreateDirectory(ShotDir);
        try
        {
            await Wait(1.5f);

            // ① 룬 보상 배지
            var trig = Object.FindFirstObjectByType<ClearRewardTrigger>();
            var hud  = ExitCompassHud.Instance;
            var entries = (typeof(ExitCompassHud).GetField("_entries", Any)?.GetValue(hud) as IList)?.Cast<object>().ToList();
            object e0 = entries != null && entries.Count > 0 ? entries[0] : null;
            bool offOnly = e0 != null && (bool)e0.GetType().GetField("OffScreenOnly").GetValue(e0);
            Check("①-1 보상이 서면 배지 하나 · 화면 밖 전용", trig != null && entries?.Count == 1 && offOnly, $"배지 {entries?.Count} · 화면 밖 전용 {offOnly}");
            if (trig != null && e0 != null)
            {
                var cam  = Camera.main;
                var rect = (RectTransform)e0.GetType().GetField("Rect").GetValue(e0);
                var arrow = (Component)e0.GetType().GetField("Arrow").GetValue(e0);
                bool OnScreen() { var sp = cam.WorldToScreenPoint(trig.transform.position); return sp.z > 0 && sp.x > 88 && sp.x < Screen.width - 88 && sp.y > 88 && sp.y < Screen.height - 88; }

                Vector3 home = trig.transform.position;
                Vector3 near = Managers.Player.PlayerTransform.position + cam.transform.right * 2f;
                trig.transform.position = new Vector3(near.x, home.y, near.z);
                await Wait(0.6f);
                Check("①-2 화면 안이면 배지 숨김(머리 위 아이콘이 가리킨다)", OnScreen() && !rect.gameObject.activeSelf, $"화면 안 {OnScreen()} · 배지 {rect.gameObject.activeSelf}");
                await ShotAsync("F29D_reward_onscreen");

                trig.transform.position = home + cam.transform.right * 45f;
                await Wait(0.8f);
                Check("①-3 화면 밖이면 가장자리 배지 + 화살표", !OnScreen() && rect.gameObject.activeSelf && arrow.gameObject.activeSelf,
                      $"화면 안 {OnScreen()} · 배지 {rect.gameObject.activeSelf} · 화살표 {arrow.gameObject.activeSelf} · 자리 {rect.position}");
                await ShotAsync("F29D_reward_offscreen");
                trig.transform.position = home;
            }

            // ② 자석 거리
            float gold = (float)typeof(GoldCoinPickup).GetField("MagnetRange", Any).GetValue(null);
            float ess  = (float)typeof(EssenceShardPickup).GetField("MagnetRange", Any).GetValue(null);
            Check("② 자석 거리 금화 4.5 · 정수 5", Mathf.Approximately(gold, 4.5f) && Mathf.Approximately(ess, 5f), $"금화 {gold} · 정수 {ess}");

            // ③ 탄막 — 다 피한 셈(탄 수 · 사진) → 가만히 서 있기(맞는 횟수)
            await BarrageAsync(sb, Check, MinigameDemo.Perfect, "perfect");
            await Wait(2f);
            await BarrageAsync(sb, Check, MinigameDemo.Idle, "idle");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            EventMinigame.DebugDemo = MinigameDemo.None;
            sb.AppendLine($"\n통과 {pass}/{total}");
            File.WriteAllText("Temp/fix1029_d.txt", sb.ToString());
            Debug.Log($"{Tag} 끝 — {pass}/{total}");
        }
    }

    // ── D2 — 진짜 탄막 방(반경 9 m)에서 ───────────────────────

    /// <summary>다음 문을 탄막 방으로 고정하고 자동 진행 — 탄막이 서면 자동 진행을 멈추고(무적도 끈다) 그 방에서 한 판, 같은 자리에서 한 판 더.</summary>
    [MenuItem("RelicFairy/Debug/10-29 D2 자동 진행 → 탄막 방에서 실측 (테스트 허브, 플레이 중)")]
    private static void AutoToBarrage()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중에"); return; }
        EventMinigameDebugMenu.ForceDoor("ch1_event_barrage_01");
        EditorApplication.ExecuteMenuItem(AutoRoot + "시작 (테스트 허브에서, 플레이 중)");
        s_until = EditorApplication.timeSinceStartup + 300.0;
        EditorApplication.update -= WatchBarrage;
        EditorApplication.update += WatchBarrage;
    }

    private static void WatchBarrage()
    {
        if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > s_until)
        {
            EditorApplication.update -= WatchBarrage;
            EventMinigameDebugMenu.ForceDoor("");
            Debug.Log($"{Tag} 끝 — 탄막 방에 닿지 못했다(시간 초과)");
            return;
        }
        if (!(EventMinigame.Active is BarrageCorridorGame game)) return;
        EditorApplication.update -= WatchBarrage;
        EditorApplication.ExecuteMenuItem(AutoRoot + "중지·기록");
        RoomRunAsync(game).Forget();
    }

    private static async UniTaskVoid RoomRunAsync(BarrageCorridorGame game)
    {
        var sb = new StringBuilder("10-29 D2 탄막의 회랑 — 진짜 방\n");
        int pass = 0, total = 0;
        void Check(string what, bool ok, string detail = "") { total++; if (ok) pass++; sb.AppendLine($"CHECK {what}: {ok}{(detail.Length > 0 ? "  — " + detail : "")}"); }
        Directory.CreateDirectory(ShotDir);
        try
        {
            EventMinigameDebugMenu.ForceDoor("");
            await BarrageAsync(sb, Check, MinigameDemo.Idle, "방 · 가만히", game);
            await Wait(3f);
            await BarrageAsync(sb, Check, MinigameDemo.Perfect, "같은 자리 · 다 피한 셈");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            EventMinigame.DebugDemo = MinigameDemo.None;
            sb.AppendLine($"\n통과 {pass}/{total}");
            File.WriteAllText("Temp/fix1029_d2.txt", sb.ToString());
            Debug.Log($"{Tag} 끝 D2 — {pass}/{total}");
        }
    }

    private static async UniTask BarrageAsync(StringBuilder sb, System.Action<string, bool, string> check, MinigameDemo demo, string label,
                                              BarrageCorridorGame game = null)
    {
        EventMinigame.DebugDemo = demo;
        var player = Managers.Player?.PlayerTransform != null ? Managers.Player.PlayerTransform.GetComponent<PlayerController>() : null;
        typeof(PlayerController).GetField("debugInvincible", Any)?.SetValue(player, false);   // 자동 진행이 켜 둔 무적 — 맞는 횟수를 재려면 끈다
        if (game == null)
        {
            EditorApplication.ExecuteMenuItem("RelicFairy/Minigame/Play Here/Barrage (Play)");
            await Wait(0.5f);
            game = EventMinigame.Active as BarrageCorridorGame;
        }
        if (game == null) { check($"③ {label} 탄막 시작", false, "놀이가 안 섰다"); return; }

        var hazards = typeof(BarrageCorridorGame).GetField("_hazards", Any);
        var pool    = typeof(BarrageCorridorGame).GetField("_bulletPool", Any);
        var hits    = typeof(BarrageCorridorGame).GetField("_hits", Any);
        var radiusF = typeof(EventMinigame).GetField("Radius", Any);
        var centerF = typeof(EventMinigame).GetField("Center", Any);
        int max = 0, samples = 0, hitCount = 0; long sum = 0;
        bool shot1 = false, shot2 = false, placed = false;
        float radius = 0f;
        while (EventMinigame.Active == game && game != null)
        {
            radius = (float)radiusF.GetValue(game);
            // 판이 정해지면 플레이어를 판 안(가운데에서 3 m)에 세운다 — 자동 진행은 방 입구에서 멈췄을 수 있다
            if (!placed && radius > 0f && player != null)
            {
                placed = true;
                var c   = (Vector3)centerF.GetValue(game);
                var pos = c + Vector3.right * Mathf.Min(3f, radius * 0.4f);
                player.transform.position = pos;
                if (player.TryGetComponent<Rigidbody>(out var rb)) { rb.position = pos; rb.linearVelocity = Vector3.zero; }
            }
            var list = hazards.GetValue(game) as IList;
            int bullets = 0;
            if (list != null) foreach (var h in list) if (h.GetType().Name == "BulletHazard") bullets++;
            if (bullets > 0) { samples++; sum += bullets; max = Mathf.Max(max, bullets); }
            hitCount = (int)hits.GetValue(game);
            if (!shot1 && bullets > 0 && game.Remaining < 13f && game.Remaining > 0f) { shot1 = true; await ShotAsync($"F29D_barrage_mid_{demo}"); }
            if (!shot2 && bullets > 0 && game.Remaining < 4f && game.Remaining > 0f)  { shot2 = true; await ShotAsync($"F29D_barrage_final_{demo}"); }
            await Wait(0.25f);
        }
        int made = (pool?.GetValue(game) as ICollection)?.Count ?? -1;
        sb.AppendLine($"③ {label}: 판 반경 {radius:0.0} m · 탄 동시 최대 {max} · 평균 {(samples > 0 ? sum / (float)samples : 0f):0.0} · 만든 공 {made}개(되쓰기) · 맞은 횟수 {hitCount}");
        if (demo == MinigameDemo.Perfect)
            check("③-1 압도적으로 많게(동시 40발 이상)", max >= 40, $"최대 {max}");
        else
            check("③-2 가만히 서 있으면 맞는다(피해야 하는 놀이)", hitCount >= 3, $"맞은 횟수 {hitCount}");
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
