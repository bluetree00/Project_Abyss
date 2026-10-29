#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 10-24 사용자 「랜슬롯 유물의 텍스트 위치가 오브젝트랑 떨어져 있다」 — 거점(Build/BaseCamp, 플레이 중)의 유물 제단마다
/// 이름표 · [F] 안내의 월드 위치와 받침대 · 석상(가까운 큰 렌더러) 가운데를 재고, 제단 앞으로 옮겨 찍는다.
/// 이름표 = Temp/relabel_tag.txt. 결과 Temp/relabel23.txt · Temp/ui_shots/RELABEL_*.png · 로그 「[ReLabel] 끝」.
/// </summary>
public static class RelicLabel1023ProbeEditor
{
    private const string Tag = "[ReLabel]";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem("RelicFairy/Debug/10-24 유물 제단 이름표 위치 촬영 (거점, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중에만"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        string tag = File.Exists("Temp/relabel_tag.txt") ? File.ReadAllText("Temp/relabel_tag.txt").Trim() : "shot";
        var sb = new StringBuilder($"[{tag}] 유물 제단 이름표\n");
        try
        {
            await Wait(3f);
            var player = Object.FindFirstObjectByType<PlayerController>();
            foreach (var altar in Object.FindObjectsByType<RelicAltar>(FindObjectsSortMode.None))
            {
                var label = typeof(RelicAltar).GetField("_worldText", Any)?.GetValue(altar) as TextMeshPro;
                Vector3 a = altar.transform.position;
                var station = altar.transform.parent != null ? altar.transform.parent : altar.transform;
                Bounds vis = default; bool has = false;
                foreach (var r in station.GetComponentsInChildren<Renderer>())
                {
                    if (r is ParticleSystemRenderer || r.GetComponent<TextMeshPro>() != null || !r.enabled) continue;
                    if (!has) { vis = r.bounds; has = true; } else vis.Encapsulate(r.bounds);
                }
                // 석상 — 제단 둘레 6 m 안의 큰 메시(받침대 프리팹 밖일 수 있다)
                Renderer statue = null; float best = 0f;
                foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                {
                    Vector3 d = r.bounds.center - a; d.y = 0f;
                    if (d.magnitude > 6f || r.bounds.size.y < 2.5f) continue;
                    if (r.transform.IsChildOf(station)) continue;
                    float vol = r.bounds.size.x * r.bounds.size.y * r.bounds.size.z;
                    if (vol > best) { best = vol; statue = r; }
                }
                Vector3 lp = label != null ? label.transform.position : Vector3.zero;
                sb.AppendLine($"  {station.name} · 제단 {a} · 받침대 가운데 {(has ? vis.center : Vector3.zero)}(크기 {(has ? vis.size : Vector3.zero)})");
                sb.AppendLine($"    이름표 {lp} 「{label?.text}」 · 제단에서 수평 {Horiz(lp - a):0.00} m · 받침대 가운데에서 {Horiz(lp - vis.center):0.00} m");
                if (statue != null)
                    sb.AppendLine($"    가까운 큰 메시 {statue.name} 가운데 {statue.bounds.center} · 이름표에서 수평 {Horiz(lp - statue.bounds.center):0.00} m · 제단에서 {Horiz(statue.bounds.center - a):0.00} m");

                if (player != null && player.TryGetComponent<Rigidbody>(out var rb))
                {
                    Vector3 stand = a + Vector3.left * 5.5f; stand.y = player.transform.position.y;
                    rb.position = stand; player.transform.position = stand;
                    Physics.SyncTransforms();
                    Vector3 look = a - stand;   // 카메라가 제단을 보게
                    GameCameraController.Instance?.SetHeadingImmediate(Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg);
                    await Wait(1.6f);
                    var shot = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
                    if (shot != null) await (UniTask)shot.Invoke(null, new object[] { $"RELABEL_{tag}_{station.name}", 0 });
                }
            }

            // ── 유물을 고른 뒤 목표 길잡이(◆)가 유물 성소를 계속 가리키나(사용자 「유물을 먹고 지나쳐도 가이드 화살표가 사라지지 않는다」)
            var guide = Object.FindFirstObjectByType<BaseCampObjectiveGuide>();
            var maskM = typeof(BaseCampObjectiveGuide).GetMethod("VisibleMask", Any);
            string Mask()
            {
                if (guide == null || maskM == null) return "길잡이 없음";
                var args = new object[] { -1 };
                int mask = (int)maskM.Invoke(guide, args);
                return $"표식 비트 {System.Convert.ToString(mask, 2).PadLeft(6, '0')}(유물={(mask >> 2) & 1}) · 주 목표 {args[0]}";
            }
            var lo = AppBootstrapper.Instance?.Loadout;
            sb.AppendLine($"  고르기 전 — 로드아웃 유물 {(lo?.Relic != null ? lo.Relic.name : "없음")} · {Mask()}");
            var target = System.Array.Find(Object.FindObjectsByType<RelicAltar>(FindObjectsSortMode.None), x => x.RelicClass != null);
            if (target != null)
            {
                typeof(RelicAltar).GetMethod("Claim", Any)?.Invoke(target, null);
                for (int i = 0; i < 6; i++)
                {
                    await Wait(0.5f);
                    lo = AppBootstrapper.Instance?.Loadout;
                    sb.AppendLine($"  고른 뒤 {(i + 1) * 0.5f:0.0}초 — 로드아웃 유물 {(lo?.Relic != null ? lo.Relic.name : "없음")} · {Mask()}");
                }
                var shot2 = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
                if (shot2 != null) await (UniTask)shot2.Invoke(null, new object[] { $"RELABEL_{tag}_after_claim", 0 });
            }
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.AppendAllText("Temp/relabel23.txt", sb.ToString());
            Debug.Log($"{Tag} 끝\n" + sb);
        }
    }

    private static float Horiz(Vector3 d) { d.y = 0f; return d.magnitude; }
    private static UniTask Wait(float s) => UniTask.Delay((int)(s * 1000), DelayType.Realtime);
}
#endif
