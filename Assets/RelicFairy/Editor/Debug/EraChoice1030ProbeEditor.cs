#if UNITY_EDITOR
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-30 사용자 「해방기 클리어 시 악몽 선택 팝업이 어떻게 되어 있는지」 — 던전 입구 두 장 창(NightmareGateChoice)과 기록의 문 창(RankingRoomChoice)을 그대로 띄워 찍는다.
/// 고르지 않고 창만 지운다(기록 · 내려감 없음). 결과 Temp/ui_shots/EC30_*.png · 로그 「[EC30] 끝」. 거점 플레이 중에만.
/// </summary>
public static class EraChoice1030ProbeEditor
{
    [MenuItem("RelicFairy/Debug/10-30 해방기 · 악몽 선택 창 촬영 (거점 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[EC30] 플레이 중에"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        System.IO.Directory.CreateDirectory("Temp/ui_shots");
        NightmareGateChoice.Show(_ => { }, () => { });
        await Shoot("EC30_gate");
        RankingRoomChoice.Show(_ => { }, () => { });
        await Shoot("EC30_record");
        Debug.Log("[EC30] 끝");
    }

    private static async UniTask Shoot(string name)
    {
        await UniTask.Delay(1200, DelayType.Realtime);
        ScreenCapture.CaptureScreenshot($"Temp/ui_shots/{name}.png");
        await UniTask.Delay(400, DelayType.Realtime);
        var go = GameObject.Find("@NightmareGateChoice");
        if (go != null) Object.Destroy(go);   // 파괴로 닫힌다(취소 → 시간 · 입력 되돌림)
        await UniTask.Delay(600, DelayType.Realtime);
    }
}
#endif
