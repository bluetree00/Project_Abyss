#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 10-26 사용자 「천장류가 제대로 페이드 처리가 안 된다 — 특히 문 위에 있는 천장이랑 다음 챕터로 넘어가는 통로 천장」 실측.
/// A. 전투방 출구 문틀(기둥 · 상인방 · 화로) — 떠 있는 것 후보에 들었나 · 문 앞에 서서 카메라 최대일 때 시선에 걸린 조각이 흐려지나.
/// B. 챕터 계단 회랑 — 지금 방 팔레트로 높은 곳에 지어 들어가(계단을 오를 때와 같은 모음) 천장 목록에 천장만 들었나 ·
///    회랑 다섯 자리에서 카메라 최대일 때 카메라 → 머리 선분을 막는 안 걷힌 천장이 없나. 끝나면 회랑을 지우고 지금 방을 다시 모은다.
/// 결과 Temp/ceil1026.txt · Temp/ui_shots/CEIL26_*.png · 로그 「[Ceil26] 끝」.
/// </summary>
public static class Ceil1026ProbeEditor
{
    private const string Tag = "[Ceil26]";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem("RelicFairy/Debug/10-26 문틀 · 챕터 계단 회랑 천장 걷힘 실측 (런 중 · 전투방)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null) { Debug.LogWarning($"{Tag} 런 안에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("10-26 문틀 · 챕터 계단 회랑 천장 걷힘 실측\n");
        int pass = 0, total = 0;
        void Check(string what, bool ok, string detail = "") { total++; if (ok) pass++; sb.AppendLine($"CHECK {what}: {ok}{(detail.Length > 0 ? "  — " + detail : "")}"); }

        float d0 = CameraUserSettings.Distance, h0 = CameraUserSettings.Height;
        var player = GameRunBootstrapper.Instance.Run.Player;
        Vector3 home = player.transform.position;
        Transform corridor = null;
        var fader = Object.FindFirstObjectByType<CameraOcclusionFader>();
        try
        {
            typeof(PlayerController).GetField("debugInvincible", Any)?.SetValue(player, true);
            foreach (var mo in Object.FindObjectsByType<RelicFairy.Monster.MonsterBase>(FindObjectsSortMode.None))
                if (mo != null && !mo.IsDead) mo.gameObject.SetActive(false);
            if (fader == null) { sb.AppendLine("페이더 없음"); return; }

            // ── A. 출구 문틀 ──
            var flow = RunFlowController.Active;
            var frames = flow != null ? flow.GetComponentsInChildren<Transform>(true).Where(t => t.name == "GateFrame").ToList() : new List<Transform>();
            Check("A 출구 문틀이 섰다", frames.Count > 0, $"{frames.Count}개 · {string.Join(", ", frames.Select(f => f.parent != null ? f.parent.name : "?"))}");
            if (frames.Count > 0)
            {
                var cand = new HashSet<Renderer>(Overhead(fader));
                int frameR = 0, frameC = 0;
                foreach (var f in frames) foreach (var r in f.GetComponentsInChildren<MeshRenderer>()) { frameR++; if (cand.Contains(r)) frameC++; }
                Check("A 문틀 위쪽 조각(상인방 등)이 떠 있는 것 후보", frameC > 0, $"후보 {frameC} / 문틀 렌더러 {frameR}");

                var gate = (frames.FirstOrDefault(f => f.parent != null && f.parent.name.Contains("Gate")) ?? frames[0]).parent;
                Vector3 toIn = home - gate.position; toIn.y = 0f;
                Vector3 stand = new Vector3(gate.position.x, home.y, gate.position.z) + toIn.normalized * 2.2f;
                Teleport(player, Ground(stand));
                foreach (var (dist, height, tag) in new[] { (1f, 1f, "기본"), (1.3f, 1.4f, "최대") })
                {
                    CameraUserSettings.SetDistance(dist); CameraUserSettings.SetHeight(height);
                    await UniTask.Delay(1600, DelayType.Realtime);
                    var (hit, faded, names) = Blocking(frames.SelectMany(f => f.GetComponentsInChildren<MeshRenderer>()), player, fader);
                    sb.AppendLine($"  문 앞 · 카메라 {tag} — 시선에 걸린 문틀 조각 {hit} · 흐림 {faded} {names}");
                    if (tag == "최대") Check("A 카메라 최대 · 문 앞 — 시선에 걸린 문틀 조각이 모두 흐려짐", faded == hit, $"걸림 {hit} · 흐림 {faded}");
                    typeof(Cam1025ProbeEditor).GetMethod("Dump", Any)?.Invoke(null, new object[] { sb, "문 앞 " + tag });
                    await Shot(tag == "최대" ? "CEIL26_1_door_max" : "CEIL26_0_door_base");
                }
            }

            // ── C. 벽에 붙음 — 카메라 쪽 벽 바로 안쪽에 서서 둘레 판정을 끄고(0) · 켜고(기본) 흐린 벽 수 · 사진 ──
            {
                var cam0 = Camera.main.transform;
                Vector3 back = cam0.position - home; back.y = 0f; back.Normalize();   // 플레이어 → 카메라 쪽(땅 위)
                if (Physics.Raycast(home + Vector3.up * 1f, back, out var wallHit, 25f, LayerMask.GetMask("Wall"), QueryTriggerInteraction.Ignore))
                {
                    Teleport(player, Ground(wallHit.point - back * 0.9f));
                    var aroundF = typeof(CameraOcclusionFader).GetField("aroundRadius", Any);
                    float around0 = (float)aroundF.GetValue(fader);
                    var fadingF = typeof(CameraOcclusionFader).GetField("_fading", Any);
                    int Faded() => (fadingF.GetValue(fader) as IDictionary)?.Count ?? 0;
                    CameraUserSettings.SetDistance(1f); CameraUserSettings.SetHeight(1f);
                    aroundF.SetValue(fader, 0f);
                    await UniTask.Delay(1500, DelayType.Realtime);
                    int narrow = Faded();
                    await Shot("CEIL26_W0_wall_center_only");
                    aroundF.SetValue(fader, around0);
                    await UniTask.Delay(1500, DelayType.Realtime);
                    int wide = Faded();
                    await Shot("CEIL26_W1_wall_around");
                    Check("C 벽에 붙음 — 둘레 판정이 더 많은 벽을 흐림(가운데만 → 둘레)", wide > narrow, $"벽 {wallHit.collider.name} · 흐림 {narrow} → {wide} · 둘레 {around0} m");
                    Teleport(player, home);
                    await UniTask.Delay(600, DelayType.Realtime);
                }
                else sb.AppendLine("C 카메라 쪽 벽을 못 찾음(25 m) — 건너뜀");
            }

            // ── B. 챕터 계단 회랑 ──
            var palette = typeof(GameRunBootstrapper).GetField("_currentMapPalette", Any)?.GetValue(GameRunBootstrapper.Instance) as BlockPalette;
            if (palette == null) { sb.AppendLine("B 지금 방 팔레트 없음 — 건너뜀"); return; }
            corridor = MapBuilder.BuildStairCorridor(palette, palette, home + Vector3.up * 200f, 1f, new System.Random(3), out _, out _);
            await UniTask.Delay(500, DelayType.Realtime);
            CameraOcclusionFader.CollectArea(corridor.gameObject);   // 계단 꼭대기에서 회랑으로 들어설 때와 같은 모음
            var ceiling = (typeof(CameraOcclusionFader).GetField("_ceiling", Any).GetValue(fader) as List<Renderer>) ?? new List<Renderer>();
            int notCeil = ceiling.Count(r => r != null && !Block(r.transform, corridor).StartsWith("Stair_C_"));
            int stairCeil = corridor.GetComponentsInChildren<MeshRenderer>().Count(r => Block(r.transform, corridor).StartsWith("Stair_C_"));
            Check("B 회랑 천장 목록 = 천장 조각만(바닥 · 계단 앞면 빠짐)", ceiling.Count > 0 && notCeil == 0 && ceiling.Count == stairCeil,
                  $"목록 {ceiling.Count} · 천장 조각 {stairCeil} · 천장 아닌 것 {notCeil}");

            var spots = new[] { ("들어선 곳", new Vector3(0f, 0.1f, 1f)), ("첫 계단 중간", new Vector3(0f, 2.6f, 12f)), ("층계참", new Vector3(0f, 3.6f, 19f)),
                                ("둘째 계단 중간", new Vector3(10f, 5.4f, 19f)), ("끝 층계참", new Vector3(22f, 7.1f, 19f)) };
            CameraUserSettings.SetDistance(1.3f); CameraUserSettings.SetHeight(1.4f);
            int k = 0;
            foreach (var (label, local) in spots)
            {
                Teleport(player, corridor.TransformPoint(local));
                await UniTask.Delay(k == 0 ? 2800 : 1500, DelayType.Realtime);   // 첫 자리는 200 m 위로 옮겨 카메라가 따라붙을 틈
                bool hidden = (bool)typeof(CameraOcclusionFader).GetField("_ceilingHidden", Any).GetValue(fader);
                var cam = Camera.main.transform.position;
                Vector3 head = player.transform.position + Vector3.up * 1.2f;
                var ray = new Ray(cam, (head - cam).normalized);
                float len = (head - cam).magnitude;
                int blocking = ceiling.Count(r => r != null && r.enabled && !r.forceRenderingOff && r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly
                                                  && r.bounds.IntersectRay(ray, out float t) && t < len - 0.3f);
                Check($"B 카메라 최대 · {label} — 시선을 막는 안 걷힌 천장 없음", blocking == 0,
                      $"카메라 높이(발 위) {cam.y - player.transform.position.y:0.0} m · 천장 걷힘 {hidden} · 막는 천장 {blocking}");
                if (k == 0 || k == 3) await Shot($"CEIL26_{2 + k}_stair_{k}");
                k++;
            }
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            CameraUserSettings.SetDistance(d0); CameraUserSettings.SetHeight(h0);
            if (player != null) Teleport(player, home);
            if (corridor != null) Object.Destroy(corridor.gameObject);
            // 지금 방을 다시 모은다(회랑으로 바꿔 둔 목록을 되돌린다)
            var flow = RunFlowController.Active;
            var cur  = flow != null ? typeof(RunFlowController).GetField("_current", Any)?.GetValue(flow) : null;
            var room = cur?.GetType().GetField("roomGO")?.GetValue(cur) as GameObject;
            if (fader != null && room != null) typeof(CameraOcclusionFader).GetMethod("CollectOverhead", Any)?.Invoke(fader, new object[] { room });
            sb.AppendLine($"CHECK 합계 {pass}/{total}");
            File.WriteAllText("Temp/ceil1026.txt", sb.ToString());
            Debug.Log($"{Tag} 끝\n" + sb);
        }
    }

    private static List<Renderer> Overhead(CameraOcclusionFader f)
        => (typeof(CameraOcclusionFader).GetField("_overhead", Any).GetValue(f) as List<Renderer>) ?? new List<Renderer>();

    /// <summary>카메라 → 머리 선분에 걸린 렌더러 수 · 그중 페이더가 흐리는 수 · 이름.</summary>
    private static (int hit, int faded, string names) Blocking(IEnumerable<MeshRenderer> rs, PlayerController p, CameraOcclusionFader f)
    {
        var fading = typeof(CameraOcclusionFader).GetField("_fading", Any).GetValue(f) as IDictionary;
        var cam = Camera.main.transform.position;
        Vector3 head = p.transform.position + Vector3.up * 1.2f;
        var ray = new Ray(cam, (head - cam).normalized);
        float len = (head - cam).magnitude;
        int hit = 0, faded = 0;
        var names = new StringBuilder();
        foreach (var r in rs)
        {
            if (r == null || !r.enabled || !r.bounds.IntersectRay(ray, out float t) || t > len - 0.3f) continue;
            hit++;
            bool fd = fading != null && fading.Contains(r);
            if (fd) faded++;
            names.Append($"[{r.name} {r.bounds.min.y:0.0}~{r.bounds.max.y:0.0} 흐림 {fd}] ");
        }
        return (hit, faded, names.ToString());
    }

    private static string Block(Transform t, Transform root)
    {
        while (t != null && t.parent != null && t.parent != root) t = t.parent;
        return t != null ? t.name : string.Empty;
    }

    private static Vector3 Ground(Vector3 p)
        => Physics.Raycast(p + Vector3.up * 4f, Vector3.down, out var hit, 10f, LayerMask.GetMask("Ground"), QueryTriggerInteraction.Ignore)
           ? hit.point + Vector3.up * 0.1f : p;

    private static void Teleport(PlayerController p, Vector3 pos)
    {
        p.transform.position = pos;
        if (p.TryGetComponent<Rigidbody>(out var rb)) { rb.position = pos; rb.linearVelocity = Vector3.zero; }
    }

    private static UniTask Shot(string name)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, 0 }) : UniTask.CompletedTask;
    }
}
#endif
