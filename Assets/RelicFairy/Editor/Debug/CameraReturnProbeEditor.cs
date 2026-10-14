#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-14 카메라 복귀 실측(84 알림 — ReturnToPlayerAsync가 보간하면 약 24 m 위로 솟았다가 끊겼다).
/// 런 중에 카메라를 플레이어 앞 6 m · 높이 2 m로 옮긴 뒤 1.5초 복귀 — 그동안 플레이어 위 최대 높이, 끝 자세와 FreeLook이 이어받은 자세의 차이(끊김)를 잰다.
/// 보스 등장 연출이 쓰는 길(MoveManualCameraAsync → ReturnToPlayerAsync)과 같다. 결과 Temp/cam_return_probe.txt · 로그 「[CamReturn] 끝」.
/// </summary>
public static class CameraReturnProbeEditor
{
    [MenuItem("RelicFairy/Debug/10-14 카메라 복귀 실측 — 솟음 · 끊김 (런 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[CamReturn] 플레이 모드(런)에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("카메라 복귀 실측\n");
        try
        {
            var cam    = GameCameraController.Instance;
            var player = Object.FindFirstObjectByType<PlayerController>();
            if (cam == null || player == null) { sb.AppendLine("카메라 · 플레이어 없음"); return; }
            var p   = player.transform;
            float h0 = cam.transform.position.y - p.position.y;
            var orig = (Vector3)(typeof(GameCameraController).GetField("_originalPosition", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(cam) ?? Vector3.zero);
            sb.AppendLine($"평소 카메라 높이(플레이어 기준) {h0:0.0} m · 예전 복귀 목표 높이(_originalPosition.y) {orig.y:0.0} m");

            Vector3 start = p.position + p.forward * 6f + Vector3.up * 2f;
            await cam.MoveManualCameraAsync(start, p.position + Vector3.up * 1.2f, 0.4f, CancellationToken.None);
            await UniTask.Delay(300, DelayType.Realtime);

            float maxH = float.MinValue;
            bool  done = false;
            var   ret  = cam.ReturnToPlayerAsync(p, 1.5f, CancellationToken.None).ContinueWith(() => done = true);
            while (!done)
            {
                maxH = Mathf.Max(maxH, cam.transform.position.y - p.position.y);
                await UniTask.Yield(PlayerLoopTiming.PostLateUpdate);
            }
            await ret;
            Vector3 endPos = cam.transform.position;
            for (int i = 0; i < 4; i++) await UniTask.Yield(PlayerLoopTiming.PostLateUpdate);   // FreeLook이 이어받은 뒤
            Vector3 cmPos = cam.transform.position;
            float snap = Vector3.Distance(endPos, cmPos);
            sb.AppendLine($"복귀 중 최대 높이 {maxH:0.0} m · 끝 → FreeLook 이어받음 차이(끊김) {snap:0.00} m");
            sb.AppendLine($"CHECK 솟지 않음(최대 높이 ≤ 평소 + 1 m): {maxH <= h0 + 1f}");
            sb.AppendLine($"CHECK 끊김 없음(차이 ≤ 0.5 m): {snap <= 0.5f}");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText("Temp/cam_return_probe.txt", sb.ToString());
            Debug.Log("[CamReturn] 끝\n" + sb);
        }
    }
}
#endif
