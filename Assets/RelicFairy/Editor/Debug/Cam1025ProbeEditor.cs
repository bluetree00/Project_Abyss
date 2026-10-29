#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 10-25 「카메라를 늘리면 천장 쪽 오브젝트도 카메라와 플레이어 사이에 끼면 투명 처리」 진단 — 세이브는 건드리지 않는다.
/// 카메라 거리 · 높이를 기본 / 최대(1.3 · 1.4)로 두고, 카메라 → 플레이어 머리 선분에 걸리는 렌더러를 전부 적는다
/// (이름 · 레이어 · 콜라이더 유무 · 높이 · 페이더가 지금 흐리게 하는 중인지). 결과 Temp/cam1025.txt · Temp/ui_shots/CAM25_*.png · 로그 「[Cam25] 끝」.
/// </summary>
public static class Cam1025ProbeEditor
{
    private const string Tag = "[Cam25]";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem("RelicFairy/Debug/10-25 카메라 가림 진단 — 기본 · 최대 거리에서 가리는 것 목록 (런 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null) { Debug.LogWarning($"{Tag} 런 안에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("10-25 카메라 가림 진단\n");
        float d0 = CameraUserSettings.Distance, h0 = CameraUserSettings.Height;
        var invField = typeof(PlayerController).GetField("debugInvincible", Any);
        var player0  = GameRunBootstrapper.Instance.Run.Player;
        object inv0  = invField?.GetValue(player0);
        invField?.SetValue(player0, true);   // 전투방에서 재는 동안 죽지 않게(10-25 첫 진단은 최대 거리 중 죽어 끊겼다)
        try
        {
            var run = GameRunBootstrapper.Instance.Run;
            sb.AppendLine($"방 {run.CurrentRoomKind} · 챕터 {(int)run.CurrentChapter} · 씬 {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
            foreach (var (dist, height, tag) in new[] { (1f, 1f, "base"), (1.3f, 1.4f, "max") })
            {
                CameraUserSettings.SetDistance(dist); CameraUserSettings.SetHeight(height);
                await UniTask.Delay(1600, DelayType.Realtime);
                Dump(sb, tag);
                await Shot($"CAM25_{tag}");
            }
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            if (player0 != null && inv0 != null) invField.SetValue(player0, inv0);
            CameraUserSettings.SetDistance(d0); CameraUserSettings.SetHeight(h0);
            File.WriteAllText("Temp/cam1025.txt", sb.ToString());
            Debug.Log($"{Tag} 끝\n" + sb);
        }
    }

    private static void Dump(StringBuilder sb, string tag)
    {
        var cam = Camera.main;
        var player = GameRunBootstrapper.Instance.Run.Player;
        Vector3 head = player.transform.position + Vector3.up * 1.2f;
        Vector3 from = cam.transform.position;
        Vector3 d = head - from;
        float len = d.magnitude;
        var ray = new Ray(from, d / len);
        var fader = Object.FindFirstObjectByType<CameraOcclusionFader>();
        var fading = fader != null ? typeof(CameraOcclusionFader).GetField("_fading", Any)?.GetValue(fader) as System.Collections.IDictionary : null;
        int mask = fader != null ? ((LayerMask)typeof(CameraOcclusionFader).GetField("occlusionMask", Any).GetValue(fader)).value : -1;
        var overhead = fader != null ? typeof(CameraOcclusionFader).GetField("_overhead", Any)?.GetValue(fader) as List<Renderer> : null;
        var overheadSet = overhead != null ? new HashSet<Renderer>(overhead) : new HashSet<Renderer>();
        sb.AppendLine($"\n■ {tag} — 카메라 {from} · 머리 {head} · 거리 {len:0.0} m · 카메라 높이(플레이어 위) {from.y - player.transform.position.y:0.0} m · 페이더 마스크 {mask} · 떠 있는 것 후보 {(overhead != null ? overhead.Count.ToString() : "없음(옛 페이더)")}"
                    + $" · 천장 {(typeof(CameraOcclusionFader).GetField("_ceiling", Any)?.GetValue(fader) as List<Renderer>)?.Count} · 천장 아랫면 {typeof(CameraOcclusionFader).GetField("_ceilingMinY", Any)?.GetValue(fader)} · 걷힘 {typeof(CameraOcclusionFader).GetField("_ceilingHidden", Any)?.GetValue(fader)}");
        int n = 0;
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            if (r.transform.IsChildOf(player.transform) || r.GetComponentInParent<RelicFairy.Monster.MonsterBase>() != null) continue;
            if (!r.bounds.IntersectRay(ray, out float t) || t > len - 0.3f) continue;
            var col = r.GetComponentInParent<Collider>();
            bool faded = false;
            if (fading != null) foreach (var k in fading.Keys) if (k is Renderer kr && kr == r) faded = true;
            string path = r.transform.parent != null ? $"{r.transform.parent.name}/{r.name}" : r.name;
            var w2l = r.transform.worldToLocalMatrix;
            var la  = w2l.MultiplyVector(d);
            bool local = la.magnitude > 1e-4f && r.localBounds.IntersectRay(new Ray(w2l.MultiplyPoint3x4(from), la.normalized), out float lt) && lt <= la.magnitude;
            sb.AppendLine($"  {path} · 레이어 {LayerMask.LayerToName(r.gameObject.layer)}({r.gameObject.layer}) · 콜라이더 {(col != null ? LayerMask.LayerToName(col.gameObject.layer) : "없음")} · 높이 {r.bounds.min.y:0.0}~{r.bounds.max.y:0.0} · 거리 {t:0.0} · 흐림 {faded}"
                        + $" · 상자 {r.bounds.size.x:0.0}×{r.bounds.size.y:0.0}×{r.bounds.size.z:0.0} · 자리 {r.transform.position} · 자기 상자 맞음 {local} · 후보 {overheadSet.Contains(r)} · 걷힘 {r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly || r.forceRenderingOff}");
            if (++n >= 40) { sb.AppendLine("  …"); break; }
        }
        if (n == 0) sb.AppendLine("  (가리는 렌더러 없음)");
    }

    private static UniTask Shot(string name)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, 0 }) : UniTask.CompletedTask;
    }
}
#endif
