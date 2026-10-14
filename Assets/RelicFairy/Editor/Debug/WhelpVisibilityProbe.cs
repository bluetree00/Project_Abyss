#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;

/// <summary>
/// [실측 도구 · 플레이 중] 화룡 새끼 용 — 공격 이펙트 · 공격 방식 · 크기 · 가시성(10-08 사용자 요청).
/// 0.2초마다 새끼 용 · 브레스 탄 · 바닥 예고를 읽어 월드 크기, 화면 크기(픽셀), 화면 안 여부, 카메라 거리를 기록하고
/// 처음 나온 순간(새끼 용 · 예고 · 탄 · 탄 비행 중)을 사진으로 남긴다. 결과: Logs/boss_fight_sim/whelp_probe_시각.txt + .png
/// 화룡 시뮬 「Tank Mid 7m - Watch Whelps」와 함께 쓴다(시뮬이 새끼 용을 바로 쓰러뜨리지 않는다).
/// </summary>
internal static class WhelpVisibilityProbe
{
    private const float SampleSeconds = 0.2f;
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic;

    private static readonly FieldInfo s_tele  = typeof(DragonMiniDragon).GetField("_telegraphing", Inst);
    private static readonly FieldInfo s_guide = typeof(DragonMiniDragon).GetField("_breathGuide", Inst);
    private static readonly FieldInfo s_tint  = typeof(DragonMiniDragon).GetField("_tint", Inst);
    private static readonly FieldInfo s_teleT = typeof(DragonMiniDragon).GetField("_telegraphTimer", Inst);
    private static readonly StringBuilder s_log  = new();
    private static readonly HashSet<string> s_shots = new();
    private static readonly Dictionary<int, float> s_shotBorn = new();
    private static readonly Vector3[] s_corners = new Vector3[8];

    private static bool   s_on;
    private static double s_next;
    private static float  s_t0;
    private static string s_stem;
    private static int    s_samples, s_whelpSamples, s_onScreen;
    private static float  s_minPx = float.MaxValue, s_maxPx, s_sumPx;
    private static int    s_shotCount, s_teleCount;

    [MenuItem("RelicFairy/Boss/Dragon/Whelp Probe Start (Play)")]
    private static void StartProbe()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[WhelpProbe] 플레이 중에만"); return; }
        s_log.Clear(); s_shots.Clear(); s_shotBorn.Clear();
        s_samples = s_whelpSamples = s_onScreen = s_shotCount = s_teleCount = 0;
        s_minPx = float.MaxValue; s_maxPx = s_sumPx = 0f;
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "boss_fight_sim");
        Directory.CreateDirectory(dir);
        s_stem = Path.Combine(dir, $"whelp_probe_{System.DateTime.Now:MMdd_HHmmss}");
        s_t0 = Time.time;
        s_on = true;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= OnPlayMode;
        EditorApplication.playModeStateChanged += OnPlayMode;
        Debug.Log($"[WhelpProbe] 시작 — {s_stem}");
    }

    [MenuItem("RelicFairy/Boss/Dragon/Whelp Probe Write (Play)")]
    private static void Write()
    {
        if (!s_on) return;
        s_on = false;
        EditorApplication.update -= Tick;
        var sb = new StringBuilder();
        float avg = s_whelpSamples > 0 ? s_sumPx / s_whelpSamples : 0f;
        sb.AppendLine($"새끼 용 측정 — 표본 {s_samples} · 새끼 용 보인 표본 {s_whelpSamples}(화면 안 {s_onScreen})");
        sb.AppendLine($"새끼 용 화면 키(px) 최소 {(s_whelpSamples > 0 ? s_minPx : 0f):0} · 평균 {avg:0} · 최대 {s_maxPx:0} · 예고 {s_teleCount}회 · 탄 {s_shotCount}발");
        sb.Append(s_log);
        File.WriteAllText(s_stem + ".txt", sb.ToString(), Encoding.UTF8);
        Debug.Log($"[WhelpProbe] {s_stem}.txt");
    }

    private static void OnPlayMode(PlayModeStateChange s)
    {
        if (s == PlayModeStateChange.ExitingPlayMode) Write();
    }

    private static void Tick()
    {
        if (!s_on || !EditorApplication.isPlaying || EditorApplication.timeSinceStartup < s_next) return;
        s_next = EditorApplication.timeSinceStartup + SampleSeconds;
        var cam = Camera.main;
        if (cam == null) return;
        s_samples++;
        float t = Time.time - s_t0;

        foreach (var w in Object.FindObjectsByType<DragonMiniDragon>(FindObjectsSortMode.None))
        {
            if (w == null || w.IsDead) continue;
            if (!TryBounds(w.gameObject, out var b)) continue;
            Rect r = ScreenRect(cam, b, out bool inView);
            s_whelpSamples++;
            if (inView) s_onScreen++;
            s_minPx = Mathf.Min(s_minPx, r.height); s_maxPx = Mathf.Max(s_maxPx, r.height); s_sumPx += r.height;
            float camDist = Vector3.Distance(cam.transform.position, b.center);
            Color tint = s_tint?.GetValue(w) is Color c ? c : Color.clear;
            string key = $"whelp_{ColorName(tint)}";
            if (s_shots.Add(key))
            {
                Line(t, $"새끼 용 첫 등장({ColorName(tint)}) · 몸 {b.size:F1} m · 화면 {r.width:0}×{r.height:0}px · 화면 안 {(inView ? "예" : "아니오")} · 카메라 {camDist:0.0} m");
                Shot(key);
            }
            bool tele = s_tele?.GetValue(w) is bool bt && bt;
            if (tele && s_guide?.GetValue(w) is GameObject g && g != null && s_shots.Add("tele_" + w.GetInstanceID() + "_" + Mathf.FloorToInt(t)))
            {
                s_teleCount++;
                Vector3 sc = g.transform.lossyScale;
                Rect gr = TryBounds(g, out var gb) ? ScreenRect(cam, gb, out _) : default;
                Line(t, $"예고({ColorName(tint)}) · 바닥 화살표 크기 {sc.x:0.0}×{sc.y:0.0} · 화면 {gr.width:0}×{gr.height:0}px · 새끼 용 화면 안 {(inView ? "예" : "아니오")}");
                if (s_shots.Add("tele_first")) Shot("telegraph");
            }
            // 예고가 반쯤 찼을 때(0.6초 중 0.3초 뒤) — 처음 찍은 사진은 막 깔려 거의 비어 있다
            if (tele && s_teleT?.GetValue(w) is float tt && tt >= 0.3f && s_shots.Add($"tele_mid_{ColorName(tint)}"))
                Shot($"telegraph_mid_{ColorName(tint)}");
        }

        foreach (var shot in Object.FindObjectsByType<DragonBreathShot>(FindObjectsSortMode.None))
        {
            if (shot == null) continue;
            int id = shot.GetInstanceID();
            if (!s_shotBorn.ContainsKey(id))
            {
                s_shotBorn[id] = t;
                s_shotCount++;
                var ps = shot.GetComponentInChildren<ParticleSystem>(true);
                var col = shot.GetComponent<Collider>();
                float colR = col is SphereCollider sc2 ? sc2.radius * shot.transform.lossyScale.x : -1f;
                Rect sr = TryBounds(shot.gameObject, out var sb) ? ScreenRect(cam, sb, out bool sv) : default;
                Line(t, $"탄 발사 · 이펙트 {shot.name.Replace("(Clone)", "")} · 보이는 크기 {sb.size:F2} m · 화면 {sr.width:0}×{sr.height:0}px · 판정 반경 {colR:0.00} m · 파티클 {(ps != null ? ps.main.startSize.constant.ToString("0.00") : "-")}");
                string kind = shot.name.Replace("(Clone)", "").Trim().Replace(' ', '_');
                if (s_shots.Add("shot_" + kind)) Shot("shot_" + kind);   // 탄 종류마다 첫 발
            }
            else if (t - s_shotBorn[id] > 0.3f && s_shots.Add("shot_flight"))
                Shot("shot_flight");
        }
    }

    private static bool TryBounds(GameObject go, out Bounds b)
    {
        b = default;
        bool any = false;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        return any;
    }

    private static Rect ScreenRect(Camera cam, Bounds b, out bool inView)
    {
        Vector3 c = b.center, e = b.extents;
        int i = 0;
        for (int x = -1; x <= 1; x += 2)
        for (int y = -1; y <= 1; y += 2)
        for (int z = -1; z <= 1; z += 2)
            s_corners[i++] = cam.WorldToScreenPoint(c + Vector3.Scale(e, new Vector3(x, y, z)));
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        bool front = false;
        foreach (var p in s_corners)
        {
            if (p.z > 0f) front = true;
            minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
            minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
        }
        Vector3 v = cam.WorldToViewportPoint(c);
        inView = front && v.z > 0f && v.x > 0f && v.x < 1f && v.y > 0f && v.y < 1f;
        return Rect.MinMaxRect(minX, minY, maxX, maxY);
    }

    private static string ColorName(Color c)
    {
        if (c.a <= 0f) return "?";
        if (c.b > c.r && c.g > 0.6f) return "얼음";
        if (c.b > c.g && c.r > 0.5f) return "번개";
        return "불";
    }

    private static void Line(float t, string s) => s_log.AppendLine($"[{t,6:0.0}] {s}");

    private static void Shot(string tag) => ScreenCapture.CaptureScreenshot($"{s_stem}_{tag}.png");
}
#endif
