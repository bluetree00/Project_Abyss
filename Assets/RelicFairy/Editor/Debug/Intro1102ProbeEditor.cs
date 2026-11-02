#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 11-02 인트로 실측 — Q3 「무형검 근처에 가면 바로 시작 · 검 곁으로 옮겨 등 뒤 구도」 · Q4 「보스 공격을 다 피하면 안 끝난다 → 패턴에 맞고 바로 죽게」.
/// 테스트 허브 플레이 중: Game_Intro를 열고 오프닝을 넘긴 뒤 플레이어를 검 범위 안 비스듬한 자리에 세운다(F · 사건 호출 없음 — 다가옴만으로 시작하는지).
/// 옮겨진 자리 · 바라보는 방향 · 쇼트마다 사진. 전투가 열리면 플레이어를 무적(debugInvincible — 모든 공격을 피한 것과 같다)으로 두고
/// 쓰러지기까지 · 그때 기사 상태를 적는다. 인트로 완료 기록(PlayerPrefs)은 되돌린다.
/// 결과: Temp/intro1102/probe.txt · 사진 · 로그 「[Intro1102] 끝」.
/// </summary>
public static class Intro1102ProbeEditor
{
    private const BindingFlags Any = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem("RelicFairy/Debug/11-02 인트로 무형검 다가옴 · 피해도 끝남 (테스트 허브, Play)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[Intro1102] 플레이 모드(테스트 허브)에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "intro1102");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder("11-02 인트로 실측\n");
        string key = "game_intro_done_v1_slot" + (RunProgressManager.Instance?.ActiveSlotIndex ?? 0);
        bool hadKey = PlayerPrefs.HasKey(key);
        int  wasVal = PlayerPrefs.GetInt(key, 0);
        try
        {
            AppBootstrapper.Instance?.RequestLoad(Define.Scene.Game_Intro);
            if (!await WaitAsync(() => SceneManager.GetActiveScene().name == "Game_Intro", 60f)) { sb.AppendLine("Game_Intro 진입 실패"); return; }

            // ① 오프닝 — 대사를 넘기며 조작이 돌아올 때까지
            float t0 = Time.realtimeSinceStartup, lastSkip = 0f;
            PlayerController player = null;
            while (Time.realtimeSinceStartup - t0 < 150f)
            {
                float t = Time.realtimeSinceStartup - t0;
                player = IntroBootstrapper.Instance?.Player;
                if (player != null && !InputLocked(player) && t > 8f) break;
                if (t - lastSkip > 1.6f) { lastSkip = t; TestHubDebugMenu.AdvanceDialogue(); }
                await UniTask.Delay(200, DelayType.Realtime);
            }
            if (player == null || InputLocked(player)) { sb.AppendLine("오프닝 뒤 조작이 안 돌아옴"); return; }
            sb.AppendLine($"오프닝 끝 {Time.realtimeSinceStartup - t0:0.0}초");

            // ② 검 범위 안 비스듬한 자리 — 사건은 부르지 않는다
            var pickup   = UnityEngine.Object.FindFirstObjectByType<IntroSwordPickup>();
            var director = UnityEngine.Object.FindFirstObjectByType<IntroMordredDirector>();
            if (pickup == null || director == null) { sb.AppendLine("검 · 연출 없음"); return; }
            var swordR   = typeof(IntroMordredDirector).GetField("swordRenderer", Any)?.GetValue(director) as Renderer;
            var mordredT = typeof(IntroMordredDirector).GetField("mordredTransform", Any)?.GetValue(director) as Transform;
            Vector3 sword = swordR != null ? swordR.transform.position : pickup.transform.position;
            sb.AppendLine($"검 {V(sword)} · 픽업 {V(pickup.transform.position)} · 모르드레드 {(mordredT != null ? V(mordredT.position) : "-")}");
            var to = pickup.transform.position + new Vector3(2.2f, 0f, -1.0f);
            to.y = player.transform.position.y;
            if (player.TryGetComponent<Rigidbody>(out var rb)) { rb.position = to; rb.linearVelocity = Vector3.zero; }
            player.transform.position = to;
            player.transform.rotation = Quaternion.Euler(0f, 250f, 0f);
            sb.AppendLine($"비스듬히 세움 {V(to)} · 방향 250°");
            await Shot(dir, "a0_before");
            EditorApplication.ExecuteMenuItem("RelicFairy/Debug/11-02 LOD 단계 어긋남 · 겹친 바닥 점검 (지금 씬, Play)");   // Q1 입구 · Q24 모르드레드 자리 바닥

            var started = typeof(IntroMordredDirector).GetField("_started", Any);
            float s0 = Time.realtimeSinceStartup;
            if (!await WaitAsync(() => started?.GetValue(director) is true, 4f)) { sb.AppendLine("★ 다가옴으로 시작 안 함(4초)"); return; }
            sb.AppendLine($"다가옴으로 시작 {Time.realtimeSinceStartup - s0:0.00}초 뒤");

            // ③ 옮겨진 자리 — 첫 쇼트가 나오면 잰다
            await WaitAsync(() => IntroMordredDirector.CurrentShot != null, 6f);
            Vector3 p = player.transform.position;
            Vector3 toM = mordredT != null ? mordredT.position - p : Vector3.forward; toM.y = 0f;
            float faceErr = Vector3.Angle(Flat(player.transform.forward), toM.normalized);
            Vector3 fs = sword - p; fs.y = 0f;
            sb.AppendLine($"옮겨진 자리 {V(p)} · 검까지 수평 {fs.magnitude:0.00} m · 모르드레드 쪽과 방향 차 {faceErr:0}° · 발밑 {FloorUnder(p)}");

            // ④ 쇼트마다 사진 — 조작이 돌아오면(전투) 끝
            string last = null; int n = 0, entranceShots = 0;
            float lastEntranceShot = 0f;
            float c0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - c0 < 120f)
            {
                string shot = IntroMordredDirector.CurrentShot;
                if (shot != null && shot != last)
                {
                    last = shot;
                    await UniTask.Delay(350, DelayType.Realtime);
                    await Shot(dir, $"s{n++:00}_{shot.Replace(' ', '_')}");
                    sb.AppendLine($"  [{Time.realtimeSinceStartup - c0,5:0.0}] {shot} · 플레이어 {V(player.transform.position)}");
                }
                if (shot == null && n > 0 && !InputLocked(player)) break;
                // Q24 — 컷신 뒤 보스 등장(어둠 · 「왕을 등진 검」 — 사용자 사진 09 장면)은 쇼트 이름이 없다: 0.4초마다 찍는다
                if (shot == null && n > 0 && Time.realtimeSinceStartup - lastEntranceShot > 0.4f)
                {
                    lastEntranceShot = Time.realtimeSinceStartup;
                    await Shot(dir, $"e{entranceShots++:00}_entrance");
                }
                await UniTask.Delay(100, DelayType.Realtime);
            }
            sb.AppendLine($"컷신 끝(조작 인계) {Time.realtimeSinceStartup - c0:0.0}초");

            // ⑤ 전투 — 무적(모든 공격을 피한 것과 같다)으로 두고 쓰러지기까지
            var dk = UnityEngine.Object.FindFirstObjectByType<DeathKnightBossMonster>();
            typeof(PlayerController).GetField("debugInvincible", Any)?.SetValue(player, true);
            var deadField = typeof(PlayerController).GetField("_dead", Any);
            float b0 = Time.realtimeSinceStartup, finisherAt = -1f;
            string lastState = "";
            bool died = false;
            while (Time.realtimeSinceStartup - b0 < 90f)
            {
                float t = Time.realtimeSinceStartup - b0;
                if (player == null || deadField?.GetValue(player) is true)
                {
                    sb.AppendLine($"[{t,5:0.0}] ★ 쓰러짐 — 기사 상태 {(dk != null ? StateOf(dk) : "-")} · 마무리 시작 뒤 {(finisherAt >= 0f ? (t - finisherAt).ToString("0.0") + "초" : "-")}");
                    await Shot(dir, "z_death");
                    died = true;
                    break;
                }
                string st = dk != null ? StateOf(dk) : "-";
                if (st != lastState)
                {
                    sb.AppendLine($"[{t,5:0.0}] 기사 {st} · 체력 {player.RuntimeStats.Hp}");
                    if (st.Contains("Pyramid") && finisherAt < 0f) { finisherAt = t; await Shot(dir, "y_finisher"); }
                    lastState = st;
                }
                await UniTask.Delay(100, DelayType.Realtime);
            }
            if (!died) sb.AppendLine("★ 90초 안에 쓰러지지 않음");
            await UniTask.Delay(3000, DelayType.Realtime);
            sb.AppendLine($"3초 뒤 씬 {SceneManager.GetActiveScene().name}");
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            if (hadKey) PlayerPrefs.SetInt(key, wasVal); else PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
            sb.AppendLine($"인트로 완료 기록 {key} 되돌림 → {(hadKey ? wasVal.ToString() : "없음")}");
            File.WriteAllText(Path.Combine(dir, "probe.txt"), sb.ToString(), Encoding.UTF8);
            Debug.Log("[Intro1102] 끝\n" + sb);
        }
    }

    private static bool InputLocked(PlayerController p)
        => p != null && (bool)(typeof(PlayerController).GetField("_inputDisabledExternally", Any)?.GetValue(p) ?? false);

    private static string StateOf(MonsterBase boss)
    {
        var fsm = typeof(MonsterBase).GetField("_fsm", Any)?.GetValue(boss);
        return (fsm?.GetType().GetProperty("CurrentType", Any)?.GetValue(fsm) as Type)?.Name ?? "-";
    }

    private static string FloorUnder(Vector3 p)
    {
        foreach (var h in Physics.RaycastAll(p + Vector3.up * 1.5f, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore))
            if (h.collider.GetComponentInParent<PlayerController>() == null)
                return $"{h.collider.name} {p.y - h.point.y:+0.00;-0.00}";
        return "바닥 없음";
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward; }
    private static string V(Vector3 v) => $"({v.x:0.0}, {v.y:0.00}, {v.z:0.0})";

    private static async UniTask Shot(string dir, string name)
    {
        ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
        await UniTask.Delay(150, DelayType.Realtime);
    }

    private static async UniTask<bool> WaitAsync(Func<bool> cond, float seconds)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < until)
        {
            if (cond()) return true;
            await UniTask.Delay(100, DelayType.Realtime);
        }
        return cond();
    }
}
#endif
