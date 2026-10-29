#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 10-23 사용자 「맵이 몬스터 위치를 대칭이 안 맞게 보여 준다」 — 전투방에서 미니맵 점과 화면 속 몬스터 위치를 맞대어 본다.
/// 카메라 방향(yaw) · 방 범위 · 몬스터마다 화면 좌우/위아래(카메라 기준) 대 미니맵 점 좌우/위아래 부호가 맞는지 센다.
/// 결과 Temp/minimap23.txt · Temp/ui_shots/MAP23_*.png · 로그 「[Map23] 끝」.
/// </summary>
public static class Minimap1023ProbeEditor
{
    private const string Tag = "[Map23]";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem("RelicFairy/Debug/10-23 미니맵 몬스터 위치 대조 (전투방, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중에만"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("10-23 미니맵 대조\n");
        try
        {
            string tag = File.Exists("Temp/map23_tag.txt") ? File.ReadAllText("Temp/map23_tag.txt").Trim() : "shot";
            var view = UIRootBootstrapper.Instance != null ? UIRootBootstrapper.Instance.GetMinimapView() : null;
            var cam = Camera.main;
            if (view == null || cam == null) { sb.AppendLine($"미니맵 {view != null} · 카메라 {cam != null}"); return; }
            var center = (Vector3)typeof(MinimapView).GetField("_roomCenter", Any).GetValue(view);
            var half   = (Vector2)typeof(MinimapView).GetField("_roomHalfSize", Any).GetValue(view);
            sb.AppendLine($"[{tag}] 카메라 yaw {cam.transform.eulerAngles.y:0} · 방 가운데 {center} · 반폭 {half}");
            var box = typeof(MinimapView).GetField("_markerContainer", Any).GetValue(view) as RectTransform;
            for (Transform tr = box; tr != null && tr != view.transform.parent; tr = tr.parent)
                sb.AppendLine($"  판 계층 {tr.name} · 회전 {tr.localEulerAngles} · 배율 {tr.localScale}");

            var gcc = GameCameraController.Instance;
            float heading0 = gcc != null ? gcc.CurrentHeading : 0f;
            foreach (float turn in new[] { 0f, 90f, 180f })
            {
            if (gcc != null) { gcc.SetHeadingImmediate(heading0 + turn); await UniTask.Delay(700, DelayType.Realtime); }
            sb.AppendLine($"  ── 카메라 +{turn:0}° (yaw {cam.transform.eulerAngles.y:0})");
            int match = 0, mirrorX = 0, mirrorY = 0, total = 0;
            Vector3 camRight = Vector3.ProjectOnPlane(cam.transform.right, Vector3.up).normalized;
            Vector3 camFwd   = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            foreach (var mk in Object.FindObjectsByType<MinimapMarker>(FindObjectsSortMode.None))
            {
                var t = mk.TrackedTransform;
                if (t == null) continue;
                var rt = (RectTransform)mk.transform;
                Vector3 d = t.position - center;
                float sx = Vector3.Dot(d, camRight), sy = Vector3.Dot(d, camFwd);   // 화면 기준 좌우 · 앞뒤(위아래)
                Vector2 m = rt.anchoredPosition;
                if (Mathf.Abs(sx) < 1f || Mathf.Abs(sy) < 1f) continue;          // 가운데 가까운 건 판정에서 뺀다
                total++;
                bool okX = Mathf.Sign(sx) == Mathf.Sign(m.x), okY = Mathf.Sign(sy) == Mathf.Sign(m.y);
                if (okX && okY) match++;
                if (!okX) mirrorX++;
                if (!okY) mirrorY++;
                sb.AppendLine($"  {t.name,-24} 화면 좌우 {sx,6:0.0} 앞뒤 {sy,6:0.0} · 점 {m.x,6:0.0},{m.y,6:0.0} · {(okX && okY ? "맞음" : (!okX && !okY ? "둘 다 뒤집힘" : !okX ? "좌우 뒤집힘" : "위아래 뒤집힘"))}");
            }
            sb.AppendLine($"[{tag} +{turn:0}°] 맞음 {match}/{total} · 좌우 뒤집힘 {mirrorX} · 위아래 뒤집힘 {mirrorY}");
            var hub = Object.FindFirstObjectByType<TestHubLauncher>();
            bool was = hub != null && hub.enabled;
            if (hub != null) hub.enabled = false;
            var shot = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
            if (shot != null) await (UniTask)shot.Invoke(null, new object[] { $"MAP23_{tag}_{turn:0}", 0 });
            if (hub != null) hub.enabled = was;
            }
            if (gcc != null) gcc.SetHeadingImmediate(heading0);
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.AppendAllText("Temp/minimap23.txt", sb.ToString());
            Debug.Log($"{Tag} 끝\n" + sb);
        }
    }
}
#endif
