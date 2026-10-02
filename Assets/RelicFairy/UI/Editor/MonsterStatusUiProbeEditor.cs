#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 10-02 몬스터 상태이상 표시 실측 — 테스트 허브 Ch1 첫 전투방에서 가장 가까운 몬스터 둘에게 상태를 걸고(화상 · 서리 · 감전 · 중독 · 기절 / 점화 · 빙결)
/// 머리 위 상태 줄을 확대해 찍는다. 몬스터가 죽지 않게 피해 없는 DoT로 건다.
/// 결과: Temp/monster_status_probe.txt · Temp/ui_shots/MStatus_*.png(전체 · 확대) · 로그 「[MStatusProbe] 끝」. 실측 뒤 플레이를 멈출 것.
/// </summary>
public static class MonsterStatusUiProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("RelicFairy/UI/10-02 몬스터 상태이상 표시 실측 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[MStatusProbe] 플레이 모드(테스트 허브)에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("몬스터 상태이상 표시 실측\n");
        try
        {
            var launcher = Object.FindFirstObjectByType<TestHubLauncher>();
            if (launcher == null) { sb.AppendLine("테스트 허브가 아니다"); return; }
            typeof(TestHubLauncher).GetField("_chapter", Inst)?.SetValue(launcher, 1);
            typeof(TestHubLauncher).GetField("_bossApproach", Inst)?.SetValue(launcher, false);
            if (!launcher.TryLaunch()) { sb.AppendLine("런 시작 실패"); return; }
            if (!await WaitAsync(() => GameRunBootstrapper.Instance?.Run?.Player != null, 60f)) { sb.AppendLine("런 대기 초과"); return; }
            await UniTask.Delay(4000, ignoreTimeScale: true);
            for (int i = 0; i < 12; i++) { TestHubDebugMenu.AdvanceDialogue(); await UniTask.Delay(400, ignoreTimeScale: true); }

            var run = GameRunBootstrapper.Instance.Run;
            foreach (var gate in Object.FindObjectsByType<StartRoomGate>(FindObjectsSortMode.None))
            {
                if ((int)(typeof(StartRoomGate).GetField("_fromZoneIndex", Inst)?.GetValue(gate) ?? 0) != -1) continue;
                typeof(StartRoomGate).GetMethod("HandleCovenantAssembled", Inst)?.Invoke(gate, null);
                await UniTask.Delay(9000, ignoreTimeScale: true);
                if (gate != null && gate.TryGetComponent<Collider>(out var col))
                {
                    var b = col.bounds; var pos = new Vector3(b.center.x, b.min.y + 0.3f, b.center.z);
                    run.Player.transform.position = pos;
                    if (run.Player.TryGetComponent<Rigidbody>(out var rb)) { rb.position = pos; rb.linearVelocity = Vector3.zero; }
                }
                break;
            }
            // 몬스터가 나올 때까지
            MonsterBase[] mons = null;
            await WaitAsync(() => { mons = Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None); return mons.Length >= 2; }, 25f);
            for (int i = 0; i < 6; i++) { TestHubDebugMenu.AdvanceDialogue(); await UniTask.Delay(300, ignoreTimeScale: true); }
            if (mons == null || mons.Length == 0) { sb.AppendLine("몬스터 없음"); return; }
            Array.Sort(mons, (a, b) => Dist(a, run.Player).CompareTo(Dist(b, run.Player)));

            // 플레이어를 무적 · 몬스터 앞으로(카메라에 크게 잡히게)
            TestHubDebugMenu.PlayerInvincible();
            var m0 = mons[0];
            var fwd = (run.Player.transform.position - m0.transform.position); fwd.y = 0f;
            var stand = m0.transform.position + (fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.back) * 3.5f;
            run.Player.transform.position = stand;
            if (run.Player.TryGetComponent<Rigidbody>(out var prb)) { prb.position = stand; prb.linearVelocity = Vector3.zero; }

            // 상태 걸기 — 피해 0.01 DoT(0이면 안 걸린다) · 슬로우 · CC(지속 20초)
            Apply(m0, s => { s.ApplyDot("burn", 0.01f, 1f, 20, null, 1f, null, null); s.ApplySlow("frost", 10f, 20f, 3); s.ApplySlow("static", 1f, 20f, 5);
                              s.ApplyDot("poison", 0.01f, 1f, 12, null, 1f, null, null); s.ApplyCc("stun", 20f); });
            if (mons.Length > 1)
            {
                Apply(mons[1], s => { s.ApplyDot("ignite", 0.01f, 1f, 20, null, 1f, null, null); s.ApplyCc("freeze", 20f); s.ApplySlow("shock", 1f, 20f, 2);
                                      s.ApplySlow("ice_zone_slow", 10f, 20f, 1); s.ApplyAttackSlow("poison_atk", 0.1f, 20f); s.ApplyDot("bleed", 0.01f, 1f, 20, null, 1f, null, null); });
                mons[1].ApplyDamageTakenAmp(0.1f, 20f, "vulnerable");
            }
            sb.AppendLine($"상태 걸음 — {m0.name}{(mons.Length > 1 ? " · " + mons[1].name : "")}");

            await UniTask.Delay(1800, ignoreTimeScale: true);
            await ShotAndCropAsync("MStatus_1", m0);
            if (mons.Length > 1) await ShotAndCropAsync("MStatus_1b", mons[1]);
            await UniTask.Delay(4000, ignoreTimeScale: true);   // 쓸림이 진행된 모습
            await ShotAndCropAsync("MStatus_2", m0);
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText("Temp/monster_status_probe.txt", sb.ToString());
            Debug.Log("[MStatusProbe] 끝");
        }
    }

    private static void Apply(MonsterBase m, Action<MonsterStatusReceiver> a)
    {
        if (m == null || m.Status == null) return;
        try { a(m.Status); } catch (Exception e) { Debug.LogWarning($"[MStatusProbe] 상태 걸기 예외: {e.Message}"); }
    }

    private static float Dist(MonsterBase m, PlayerController p) => (m.transform.position - p.transform.position).sqrMagnitude;

    /// <summary>전체 화면 + 몬스터 머리 위(체력바 자리) 360×200 확대.</summary>
    private static async UniTask ShotAndCropAsync(string name, MonsterBase m)
    {
        await UniTask.WaitForEndOfFrame();
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        Directory.CreateDirectory("Temp/ui_shots");
        File.WriteAllBytes($"Temp/ui_shots/{name}_full.png", tex.EncodeToPNG());
        var cam = Camera.main;
        if (cam != null && m != null)
        {
            var sp = cam.WorldToScreenPoint(m.transform.position + Vector3.up * 2.2f);
            int w = 360, h = 200;
            int x = Mathf.Clamp((int)sp.x - w / 2, 0, tex.width - w), y = Mathf.Clamp((int)sp.y - h / 2, 0, tex.height - h);
            var crop = new Texture2D(w, h, TextureFormat.RGBA32, false);
            crop.SetPixels(tex.GetPixels(x, y, w, h));
            crop.Apply();
            File.WriteAllBytes($"Temp/ui_shots/{name}_crop.png", crop.EncodeToPNG());
            Object.DestroyImmediate(crop);
        }
        Object.DestroyImmediate(tex);
    }

    private static async UniTask<bool> WaitAsync(Func<bool> cond, float seconds)
    {
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < seconds)
        {
            try { if (cond()) return true; } catch { }
            await UniTask.Delay(500, ignoreTimeScale: true);
        }
        return false;
    }
}
#endif
