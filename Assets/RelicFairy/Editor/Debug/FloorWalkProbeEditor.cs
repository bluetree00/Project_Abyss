#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 11-02 Q1 사용자 「인트로 입구 바닥이 플레이어 위치(거리)에 따라 바뀐다」(사진 01 · 02 — 멀리선 잔 체크무늬, 다가가면 큰 마름모 대리석).
/// 실제 걷는 길을 따라 플레이어(카메라)를 3 m씩 옮기며, 앞 30 m 안 바닥 같은 LODGroup마다 지금 카메라가 고르는 단계(또는 통째로 꺼짐)와
/// 그 단계의 재질을 적는다 → 걷는 동안 보이는 그림(재질)이 바뀌거나 꺼졌다 켜지는 바닥을 짚는다. 그 바닥 아래 겹친 다른 바닥 면도 적는다.
/// 「인트로」: 테스트 허브 플레이 중 — Game_Intro를 열어 오프닝을 넘기고 스폰 → 무형검까지. 「지금 씬」: 플레이어 자리 → 앞으로 40 m.
/// 결과: Temp/floor_walk_{씬}.txt · 사진 Temp/floor_walk/*.png · 로그 「[FloorWalk] 끝」.
/// </summary>
public static class FloorWalkProbeEditor
{
    private const BindingFlags Any = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const float Step = 3f, Ahead = 30f;
    private const float BuildLodBias = 1f;   // 빌드 기본 화질 Balanced = LOD 기준 1(에디터 High Fidelity = 2 — 10-16 함정: 에디터에선 안 보이고 빌드에서만 바뀐다)

    [MenuItem("RelicFairy/Debug/11-02 바닥 걸으며 LOD — 인트로 스폰 → 무형검 (테스트 허브, Play)")]
    private static void RunIntro()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[FloorWalk] 플레이 중에"); return; }
        RunAsync(true).Forget();
    }

    [MenuItem("RelicFairy/Debug/11-02 바닥 걸으며 LOD — 지금 자리 → 앞 40 m (Play)")]
    private static void RunHere()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[FloorWalk] 플레이 중에"); return; }
        RunAsync(false).Forget();
    }

    // Q24 사용자 「모드레드 연출 때 바닥이 깨진다 · 모드레드 자리 타일이 가까이 보면 깨진다」(사진 09 — 챕터 3 보스 등장 클로즈업 「서임은 아직 끝나지 않았다」).
    // 보스방에서 등장을 일으켜 0.4초마다 찍고, 끝나면 플레이어를 보스 자리 옆에 세워 겹친 바닥 면 점검(LodMismatch)을 그 둘레로 돌린다.
    [MenuItem("RelicFairy/Debug/11-02 보스 등장 사진 · 보스 자리 바닥 (보스방, Play)")]
    private static void BossEntrance()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[FloorWalk] 보스방 플레이 중에"); return; }
        BossEntranceAsync().Forget();
    }

    private static async UniTaskVoid BossEntranceAsync()
    {
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "boss_entrance_floor");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder("보스 등장 사진 · 보스 자리 바닥\n");
        try
        {
            var boss = UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None).FirstOrDefault(m => m is IBoss);
            if (boss == null) { sb.AppendLine("보스 없음"); return; }
            sb.AppendLine($"보스 {boss.name} {V(boss.transform.position)}");
            TestHubDebugMenu.StepIntoBossEntrance();
            await UniTask.Delay(300, DelayType.Realtime);
            TestHubDebugMenu.StepPastBossEntrance();
            float t0 = Time.realtimeSinceStartup;
            int n = 0;
            while (Time.realtimeSinceStartup - t0 < 12f)
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"e{n++:00}_t{Time.realtimeSinceStartup - t0:00.0}.png"));
                await UniTask.Delay(400, DelayType.Realtime);
            }
            var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
            if (player != null)
            {
                Vector3 p = boss.transform.position + Flat(player.transform.position - boss.transform.position) * 2.5f;
                if (player.TryGetComponent<Rigidbody>(out var rb)) { rb.position = p; rb.linearVelocity = Vector3.zero; }
                player.transform.position = p;
                await UniTask.Delay(500, DelayType.Realtime);
                EditorApplication.ExecuteMenuItem("RelicFairy/Debug/11-02 LOD 단계 어긋남 · 겹친 바닥 점검 (지금 씬, Play)");
                sb.AppendLine($"보스 자리 옆 {V(p)}에서 겹친 바닥 점검 → Temp/lod_mismatch_{SceneManager.GetActiveScene().name}.txt");
            }
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText(Path.Combine(dir, "entrance.txt"), sb.ToString(), Encoding.UTF8);
            Debug.Log($"[FloorWalk] 보스 등장 끝 — {dir}");
        }
    }

    // Q1 3차 — 입구 바닥이 가까이서 밝은 크림 · 멀리서 회색으로 바뀐다(11-02 걷기 사진 10 ~ 13). 오클루전 페이더(가린 벽을 반투명으로 바꿈)가
    // 그림자 · 빛을 바꾸는지: 같은 자리에서 페이더 그대로(a) · 페이더를 원래대로 돌리고 끈 것(b)을 찍는다.
    [MenuItem("RelicFairy/Debug/11-02 인트로 입구 바닥 — 오클루전 페이더 켜고 · 끄고 (테스트 허브, Play)")]
    private static void FaderAB()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[FloorWalk] 플레이 중에"); return; }
        FaderABAsync().Forget();
    }

    private static async UniTaskVoid FaderABAsync()
    {
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "floor_fader_ab");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder("입구 바닥 — 페이더 켜고(a) · 끄고(b)" + Environment.NewLine);
        string key = "game_intro_done_v1_slot" + (RunProgressManager.Instance?.ActiveSlotIndex ?? 0);
        bool hadKey = ProgressPrefs.HasKey(key);
        int  wasVal = ProgressPrefs.GetInt(key, 0);
        float biasWas = QualitySettings.lodBias;
        try
        {
            QualitySettings.lodBias = BuildLodBias;
            AppBootstrapper.Instance?.RequestLoad(Define.Scene.Game_Intro);
            if (!await WaitAsync(() => SceneManager.GetActiveScene().name == "Game_Intro", 60f)) { sb.AppendLine("Game_Intro 진입 실패"); return; }
            float t0 = Time.realtimeSinceStartup, lastSkip = 0f;
            PlayerController player = null;
            while (Time.realtimeSinceStartup - t0 < 150f)
            {
                player = IntroBootstrapper.Instance?.Player;
                if (player != null && !InputLocked(player) && Time.realtimeSinceStartup - t0 > 8f) break;
                if (Time.realtimeSinceStartup - lastSkip > 1.6f) { lastSkip = Time.realtimeSinceStartup; TestHubDebugMenu.AdvanceDialogue(); }
                await UniTask.Delay(200, DelayType.Realtime);
            }
            if (player == null) { sb.AppendLine("플레이어 없음"); return; }
            var fader = UnityEngine.Object.FindFirstObjectByType<CameraOcclusionFader>();
            sb.AppendLine($"페이더 {(fader != null ? fader.name : "없음")}");
            foreach (float z in new[] { -46.4f, -43.4f, -40.5f, -37.5f })
            {
                Vector3 p = new Vector3(0.1f, player.transform.position.y, z);
                if (player.TryGetComponent<Rigidbody>(out var rb)) { rb.position = p; rb.linearVelocity = Vector3.zero; }
                player.transform.position = p;
                player.transform.rotation = Quaternion.identity;
                if (fader != null) fader.enabled = true;
                await UniTask.Delay(900, DelayType.Realtime);
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"z{-z:00}_a_fader.png"));
                await UniTask.Delay(200, DelayType.Realtime);
                if (fader != null && typeof(CameraOcclusionFader).GetField("_fading", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(fader) is System.Collections.IDictionary fading)
                    foreach (var k in fading.Keys)
                        if (k is Renderer fr && fr != null)
                            sb.AppendLine($"    흐림 {PathOf(fr.transform)} · 층 {LayerMask.LayerToName(fr.gameObject.layer)} · 범위 {fr.bounds.min:F2}~{fr.bounds.max:F2} · 재질 {Mats(fr)}");
                if (fader != null)
                {
                    typeof(CameraOcclusionFader).GetMethod("RestoreAll", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(fader, null);
                    fader.enabled = false;
                }
                await UniTask.Delay(300, DelayType.Realtime);
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"z{-z:00}_b_nofader.png"));
                await UniTask.Delay(200, DelayType.Realtime);
                sb.AppendLine($"  z {z} 찍음");
            }
            if (fader != null) fader.enabled = true;
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            QualitySettings.lodBias = biasWas;
            if (hadKey) ProgressPrefs.SetInt(key, wasVal); else ProgressPrefs.DeleteKey(key);
            ProgressPrefs.Save();
            File.WriteAllText(Path.Combine(dir, "ab.txt"), sb.ToString(), Encoding.UTF8);
            Debug.Log($"[FloorWalk] 페이더 비교 끝 — {dir}");
        }
    }

    private static async UniTaskVoid RunAsync(bool intro)
    {
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "floor_walk");
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder($"바닥 걸으며 LOD — {(intro ? "인트로 스폰 → 무형검" : "지금 자리 → 앞 40 m")}\n");
        string key = "game_intro_done_v1_slot" + (RunProgressManager.Instance?.ActiveSlotIndex ?? 0);
        bool hadKey = ProgressPrefs.HasKey(key);
        int  wasVal = ProgressPrefs.GetInt(key, 0);
        string scene = SceneManager.GetActiveScene().name;
        float biasWas = QualitySettings.lodBias;
        try
        {
            QualitySettings.lodBias = BuildLodBias;   // 사진도 빌드 화질 단계로(에디터 2 → 빌드 1)
            PlayerController player;
            Vector3 from, to;
            if (intro)
            {
                AppBootstrapper.Instance?.RequestLoad(Define.Scene.Game_Intro);
                if (!await WaitAsync(() => SceneManager.GetActiveScene().name == "Game_Intro", 60f)) { sb.AppendLine("Game_Intro 진입 실패"); return; }
                scene = "Game_Intro";
                float t0 = Time.realtimeSinceStartup, lastSkip = 0f;
                player = null;
                while (Time.realtimeSinceStartup - t0 < 150f)
                {
                    player = IntroBootstrapper.Instance?.Player;
                    if (player != null && !InputLocked(player) && Time.realtimeSinceStartup - t0 > 8f) break;
                    if (Time.realtimeSinceStartup - lastSkip > 1.6f) { lastSkip = Time.realtimeSinceStartup; TestHubDebugMenu.AdvanceDialogue(); }
                    await UniTask.Delay(200, DelayType.Realtime);
                }
                if (player == null) { sb.AppendLine("플레이어 없음"); return; }
                var pickup = UnityEngine.Object.FindFirstObjectByType<IntroSwordPickup>();
                from = player.transform.position;
                to   = pickup != null ? pickup.transform.position + (from - pickup.transform.position).normalized * 4f : from + player.transform.forward * 40f;
                to.y = from.y;
            }
            else
            {
                player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
                if (player == null) { sb.AppendLine("플레이어 없음"); return; }
                from = player.transform.position;
                Vector3 f = Camera.main != null ? Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized : player.transform.forward;
                to = from + f * 40f;
            }
            sb.AppendLine($"씬 {scene} · {V(from)} → {V(to)} · LOD 기준 {BuildLodBias}(빌드 · 에디터는 {QualitySettings.lodBias})");

            // 걸음마다: 바닥 LODGroup → 상태(단계 · 재질 · 꺼짐)
            var history = new Dictionary<LODGroup, List<string>>();
            var overlaps = new Dictionary<string, string>();   // 같은 높이(±3 cm)로 겹친 바닥 면 — 거리에 따라 이기는 쪽이 바뀌는 z-파이팅
            var stepsAt = new List<string>();
            int n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, to) / Step));
            for (int i = 0; i <= n; i++)
            {
                Vector3 p = Vector3.Lerp(from, to, i / (float)n);
                if (player.TryGetComponent<Rigidbody>(out var rb)) { rb.position = p; rb.linearVelocity = Vector3.zero; }
                player.transform.position = p;
                player.transform.rotation = Quaternion.LookRotation(Flat(to - from));
                await UniTask.Delay(700, DelayType.Realtime);   // 카메라가 따라오게
                var cam = Camera.main;
                if (cam == null) continue;
                stepsAt.Add($"{i:00} 플레이어 {V(p)} · 카메라 {V(cam.transform.position)}");
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"{scene}_{i:00}.png"));
                CollectOverlaps(p, 8f, i, overlaps);
                Vector3 fwd = Flat(to - from);
                foreach (var g in UnityEngine.Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None))
                {
                    if (!g.enabled || !g.gameObject.activeInHierarchy) continue;
                    var lods = g.GetLODs();
                    var r0 = lods.Length > 0 ? lods[0].renderers.FirstOrDefault(r => r != null) : null;
                    if (r0 == null) continue;
                    var b = r0.bounds;
                    if (b.size.y > 1.2f || b.size.x * b.size.z < 4f || Mathf.Abs(b.center.y - p.y) > 2f) continue;   // 바닥 같은 것만
                    Vector3 d = b.center - p; d.y = 0f;
                    float along = Vector3.Dot(d, fwd);
                    if (along < -6f || along > Ahead || Mathf.Abs(Vector3.Cross(fwd, d).y) > 14f) continue;
                    if (!history.TryGetValue(g, out var h)) { h = new List<string>(); history[g] = h; while (h.Count < i) h.Add("-"); }
                    while (h.Count < i) h.Add("-");
                    h.Add(StateOf(g, lods, cam, BuildLodBias));
                }
            }

            sb.AppendLine("걸음:");
            foreach (var s in stepsAt) sb.AppendLine("  " + s);
            sb.AppendLine("\n★ 걷는 동안 그림이 바뀐 바닥(재질이 바뀌거나 꺼짐 ↔ 보임):");
            int changed = 0;
            foreach (var kv in history.OrderBy(kv => kv.Key.transform.position.z))
            {
                var seq = kv.Value.Where(s => s != "-").Select(s => s.Split('|')[1]).Distinct().ToList();
                if (seq.Count < 2) continue;
                changed++;
                sb.AppendLine($"  {PathOf(kv.Key.transform)} · 월드 {V(kv.Key.transform.position)} · {string.Join(" → ", Collapse(kv.Value))}");
                sb.AppendLine($"      아래 · 겹친 바닥 면: {Under(kv.Key)}");
            }
            sb.AppendLine($"바뀐 바닥 {changed} / 지켜본 바닥 {history.Count}");
            sb.AppendLine();
            sb.AppendLine($"★ 같은 높이(±3 cm)로 겹친 바닥 면(렌더러 · 겹친 넓이 1 m² 이상) {overlaps.Count}:");
            foreach (var o in overlaps.Values) sb.AppendLine("  " + o);
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            QualitySettings.lodBias = biasWas;
            if (intro) { if (hadKey) ProgressPrefs.SetInt(key, wasVal); else ProgressPrefs.DeleteKey(key); ProgressPrefs.Save(); }
            string file = Path.Combine(Directory.GetCurrentDirectory(), "Temp", $"floor_walk_{scene}.txt");
            File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
            Debug.Log($"[FloorWalk] 끝 — {file}");
        }
    }

    /// <summary>둘레 <paramref name="radius"/> m 바닥 같은 렌더러끼리 — 위 면 높이가 3 cm 안이고 겹친 넓이가 1 m² 이상인 쌍.</summary>
    private static void CollectOverlaps(Vector3 p, float radius, int step, Dictionary<string, string> outPairs)
    {
        var floors = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
            .Where(r => r.enabled && r.gameObject.activeInHierarchy && r.isVisible)
            .Where(r => { var b = r.bounds; return b.size.y < 1.2f && b.size.x * b.size.z >= 4f && Mathf.Abs(b.max.y - p.y) < 1.5f
                                                && Vector2.Distance(new Vector2(b.center.x, b.center.z), new Vector2(p.x, p.z)) < radius + Mathf.Max(b.extents.x, b.extents.z); })
            .ToList();
        for (int a = 0; a < floors.Count; a++)
        for (int c = a + 1; c < floors.Count; c++)
        {
            Bounds x = floors[a].bounds, y = floors[c].bounds;
            if (Mathf.Abs(x.max.y - y.max.y) > 0.03f) continue;
            float w = Mathf.Min(x.max.x, y.max.x) - Mathf.Max(x.min.x, y.min.x), d = Mathf.Min(x.max.z, y.max.z) - Mathf.Max(x.min.z, y.min.z);
            if (w <= 0f || d <= 0f || w * d < 1f) continue;
            string key = floors[a].GetInstanceID() < floors[c].GetInstanceID() ? $"{floors[a].GetInstanceID()}|{floors[c].GetInstanceID()}" : $"{floors[c].GetInstanceID()}|{floors[a].GetInstanceID()}";
            if (outPairs.ContainsKey(key)) continue;
            outPairs[key] = $"[{step:00}] {PathOf(floors[a].transform)}[{Mats(floors[a])}] 위 {x.max.y:0.000}  ↔  {PathOf(floors[c].transform)}[{Mats(floors[c])}] 위 {y.max.y:0.000} · 겹침 {w:0.0}×{d:0.0} m · 가운데 ({(Mathf.Max(x.min.x, y.min.x) + w / 2):0.0}, {(Mathf.Max(x.min.z, y.min.z) + d / 2):0.0})";
        }
    }

    private static string Mats(Renderer r) => string.Join("+", r.sharedMaterials.Select(m => m != null ? m.name : "-"));

    /// <summary>지금 카메라가 고르는 단계 — 「L1|재질」 또는 「꺼짐|꺼짐」. 화면 높이 = 크기 · ½ ÷ (거리 ÷ LOD 기준 × tan(화각 ½)).</summary>
    private static string StateOf(LODGroup g, LOD[] lods, Camera cam, float bias)
    {
        Vector3 s = g.transform.lossyScale;
        float size = g.size * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
        float dist = Vector3.Distance(g.transform.TransformPoint(g.localReferencePoint), cam.transform.position);
        float h = size * 0.5f / (dist / bias * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
        int lv = Array.FindIndex(lods, l => h >= l.screenRelativeTransitionHeight);
        if (lv < 0) return $"꺼짐({h:0.000})|꺼짐";
        var r = lods[lv].renderers.FirstOrDefault(x => x != null);
        string mat = r != null ? string.Join("+", r.sharedMaterials.Select(m => m != null ? m.name : "-")) : "빈 단계";
        return $"L{lv}({h:0.000})|{(r != null ? mat : "빈 단계")}";
    }

    private static IEnumerable<string> Collapse(List<string> seq)
    {
        string last = null;
        for (int i = 0; i < seq.Count; i++)
        {
            string key = seq[i] == "-" ? "-" : seq[i].Split('|')[1];
            if (key == last) continue;
            last = key;
            yield return $"[{i:00}] {seq[i].Replace('|', ' ')}";
        }
    }

    /// <summary>그 바닥 가운데 아래로 쏴 맞는 다른 렌더러 면(아래층 바닥) — 꺼지면 드러나는 것.</summary>
    private static string Under(LODGroup g)
    {
        var b = g.GetLODs()[0].renderers.First(r => r != null).bounds;
        var own = new HashSet<Renderer>(g.GetLODs().SelectMany(l => l.renderers).Where(r => r != null));
        var list = new List<string>();
        foreach (var r in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (own.Contains(r) || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            var rb = r.bounds;
            if (rb.size.y > 1.5f || !rb.Contains(new Vector3(b.center.x, rb.center.y, b.center.z))) continue;
            if (Mathf.Abs(rb.max.y - b.max.y) > 0.6f) continue;
            list.Add($"{r.name}[{string.Join("+", r.sharedMaterials.Select(m => m != null ? m.name : "-"))}] 위 {rb.max.y:0.00}(차 {rb.max.y - b.max.y:+0.00;-0.00})");
            if (list.Count >= 4) break;
        }
        return list.Count == 0 ? "없음" : string.Join(" · ", list);
    }

    private static bool InputLocked(PlayerController p)
        => p != null && (bool)(typeof(PlayerController).GetField("_inputDisabledExternally", Any)?.GetValue(p) ?? false);

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward; }
    private static string V(Vector3 v) => $"({v.x:0.0}, {v.y:0.00}, {v.z:0.0})";

    private static string PathOf(Transform t)
    {
        var parts = new List<string>();
        for (int i = 0; t != null && i < 4; i++, t = t.parent) parts.Add(t.name);
        parts.Reverse();
        return string.Join("/", parts);
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
