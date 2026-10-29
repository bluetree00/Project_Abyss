#if UNITY_EDITOR
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-18 ⑩ 정제소 비용 · 길안내 화살촉 확인 — 테스트 허브 Ch1 런에서.
/// ① 정제소: 방(BeginRoomEvent)마다 비용이 처음(10)부터인지 — 같은 방에서 두 번 정제 뒤 값이 오르고, 다른 방에 들어오면 다시 10.
///    천장(PityCount)과 런 정제 수(CraftCount)는 이어지는지.
/// ② 길안내 화살촉(UIProceduralSprites.Pointer)을 PNG로 떠서 모양 확인(0° = 오른쪽).
/// 결과 Temp/fb1018_refarrow.txt · Temp/ui_shots/FB18_arrow_pointer.png · 로그 「[FB18refarrow] 끝」.
/// </summary>
public static class Fb1018RefArrowProbeEditor
{
    private const string Tag = "[FB18refarrow]";

    [MenuItem("RelicFairy/Debug/10-18 정제소 값 초기화 · 길안내 화살촉 확인 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중(테스트 허브)에만 동작한다."); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("10-18 정제소 값 초기화 · 길안내 화살촉\n");
        Directory.CreateDirectory("Temp/ui_shots");
        try
        {
            var launcher = Object.FindFirstObjectByType<TestHubLauncher>();
            if (launcher != null && GameRunBootstrapper.Instance?.Run?.Player == null)
            {
                launcher.SelectChapter(1);
                launcher.TryLaunch();
            }
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 60f && GameRunBootstrapper.Instance?.Run?.Player == null)
                await UniTask.Delay(500, DelayType.Realtime);
            var run = GameRunBootstrapper.Instance?.Run;
            if (run == null) { sb.AppendLine("런이 시작되지 않음"); return; }

            // ① 정제소 비용 — 방 두 개를 흉내 낸다(실제 방 컨트롤러가 들어올 때 부르는 BeginRoomEvent)
            var svc = run.Refinery;
            int mat0 = run.FuelBank.EnhanceMaterial, ore0 = run.FuelBank.RuneOre;
            run.FuelBank.RestoreRaw(mat0, 200);
            object roomA = new object(), roomB = new object();
            svc.BeginRoomEvent(roomA, RefineryEvent.None);
            int c0 = svc.CurrentCost;
            int crafts0 = svc.CraftCount;
            for (int i = 0; i < 3; i++) svc.Craft();
            int cAfter = svc.CurrentCost;
            int pity = svc.PityCount;
            svc.EndRoomEvent(roomA);
            svc.BeginRoomEvent(roomB, RefineryEvent.None);
            int cNewRoom = svc.CurrentCost;
            sb.AppendLine($"정제소: 방 A 첫 값 {c0} → 세 번 정제 뒤 {cAfter} · 방 B 들어온 뒤 {cNewRoom} · 런 정제 수 {crafts0} → {svc.CraftCount} · 천장 진척 {pity} → {svc.PityCount}");
            sb.AppendLine($"CHECK 방마다 처음 값: {c0 == RefineryService.BaseCost && cAfter > c0 && cNewRoom == RefineryService.BaseCost} · 런 정제 수 이어짐: {svc.CraftCount == crafts0 + 3} · 천장 이어짐: {svc.PityCount == pity}");
            svc.EndRoomEvent(roomB);
            run.FuelBank.RestoreRaw(mat0, ore0);

            // ② 길안내 화살촉 — 128px로 떠서 저장
            var spr = UIProceduralSprites.Pointer(128);
            var tex = spr != null ? spr.texture : null;
            if (tex != null)
            {
                File.WriteAllBytes("Temp/ui_shots/FB18_arrow_pointer.png", tex.EncodeToPNG());
                float right = 0f, left = 0f;
                for (int y = 0; y < tex.height; y++)
                {
                    right += tex.GetPixel(tex.width * 7 / 8, y).a;
                    left  += tex.GetPixel(tex.width / 8, y).a;
                }
                sb.AppendLine($"화살촉: 앞(오른쪽 7/8) 세로 채움 {right:0.0}px · 뒤(왼쪽 1/8) {left:0.0}px — 앞이 좁고 뒤가 넓다");
                sb.AppendLine($"CHECK 한쪽으로 쏠린 모양(앞 < 뒤 × 0.6): {right < left * 0.6f}");
            }
            else sb.AppendLine("화살촉 그림을 못 만들었다");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText("Temp/fb1018_refarrow.txt", sb.ToString());
            Debug.Log($"{Tag} 끝\n{sb}");
        }
    }
}
#endif
