#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 10-02 서약 점검 — 대기방 문 소프트락 실측. 테스트 허브에서 Ch1 대기방에 들어가 서약 칸을 다 채운 뒤,
/// 「새겼다」 신호 없이도 문 열림 연출이 시작되는지 본다(예전엔 칸이 가득 차면 문이 영영 안 열렸다).
/// 결과: Temp/covenant_gate_probe.txt · 로그 「[CovGateProbe] 끝」. 실측 뒤 플레이를 멈출 것(런 저장은 테스트 런).
/// </summary>
public static class CovenantGateProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("RelicFairy/Debug/10-02 대기방 문 · 서약 가득 실측 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[CovGateProbe] 플레이 모드(로그인 뒤 테스트 허브)에서 실행"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("대기방 문 · 서약 가득 실측\n");
        try
        {
            var launcher = Object.FindFirstObjectByType<TestHubLauncher>();
            if (launcher == null) { sb.AppendLine("테스트 허브가 아니다"); return; }
            typeof(TestHubLauncher).GetField("_chapter", Inst)?.SetValue(launcher, 1);
            typeof(TestHubLauncher).GetField("_bossApproach", Inst)?.SetValue(launcher, false);
            if (!launcher.TryLaunch()) { sb.AppendLine("런 시작 실패"); return; }

            // 대기방 문 · 서약 핸들러 · 로드아웃이 준비될 때까지(최대 60초)
            StartRoomGate gate = null;
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 60f)
            {
                await UniTask.Delay(500, ignoreTimeScale: true);
                gate = FindStartGate();
                var run = GameRunBootstrapper.Instance?.Run;
                if (gate != null && run?.CovenantHandler != null && run.Player != null) break;
                gate = null;
            }
            if (gate == null) { sb.AppendLine("대기방 문을 못 찾음(60초)"); return; }
            await UniTask.Delay(1500, ignoreTimeScale: true);

            var handler = GameRunBootstrapper.Instance.Run.CovenantHandler;
            sb.AppendLine($"대기방 도착 — 서약 {handler.Covenants.Count}/{CovenantHandler.Capacity} · 문 연출 시작 {RevealStarted(gate)}");

            // 칸을 다 채운다(제단 팝업 없이 — 판의 첫 원인 × 첫 효과)
            for (int i = 0; i < 12 && handler.Covenants.Count < CovenantHandler.Capacity; i++)
            {
                CovenantAssembleService.DraftBoard(3, null, handler.Covenants.Count == 0, handler.Covenants, out var causes, out var effects);
                if (causes == null || effects == null || causes.Count == 0 || effects.Count == 0) continue;
                handler.TryAdd(AssembledCovenant.MakeId(causes[0].id, causes[0].tier, effects[0].id, effects[0].tier));
            }
            sb.AppendLine($"채운 뒤 — 서약 {handler.Covenants.Count}/{CovenantHandler.Capacity} · 맺을 수 없음 {WorldCovenantAltar.AssembleBlockedHere}");

            await UniTask.Delay(2500, ignoreTimeScale: true);
            bool done = (bool)(typeof(StartRoomGate).GetField("_covenantDone", Inst)?.GetValue(gate) ?? false);
            sb.AppendLine($"2.5초 뒤 — 문 열림 연출 시작 {RevealStarted(gate)} · 「새겼다」 신호 {done}(받지 않아야 정상)");
            sb.AppendLine(RevealStarted(gate) ? "판정: 통과 — 서약을 맺을 수 없는 대기방에서 문이 스스로 열린다"
                                              : "판정: 실패 — 문이 열리지 않는다(소프트락)");
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText("Temp/covenant_gate_probe.txt", sb.ToString());
            Debug.Log("[CovGateProbe] 끝 → Temp/covenant_gate_probe.txt");
        }
    }

    private static StartRoomGate FindStartGate()
    {
        foreach (var g in Object.FindObjectsByType<StartRoomGate>(FindObjectsSortMode.None))
            if ((int)(typeof(StartRoomGate).GetField("_fromZoneIndex", Inst)?.GetValue(g) ?? 0) == -1) return g;
        return null;
    }

    private static bool RevealStarted(StartRoomGate gate)
        => (bool)(typeof(StartRoomGate).GetField("_gateRevealStarted", Inst)?.GetValue(gate) ?? false);
}
#endif
