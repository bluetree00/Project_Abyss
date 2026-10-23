#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-16 인트로 보스 실측 — 사용자 「보스전을 시작할 때 보스가 바닥보다 위에 떠 있다」 · 「인트로 보스는 가장 강력한 광역 패턴으로 끝내자」.
/// 「10-10 인트로 연출 실측」이 전투까지 몰고 가는 동안 함께 돌린다: 기사가 나타나면 0.25초마다 높이 · 발밑 바닥(콜라이더) · 몸 아래(스킨드 메시) ·
/// 상태를 적고 몇 장 찍는다. 플레이어가 쓰러지면 그 순간 기사 상태(= 끝낸 공격)를 적는다.
/// 인트로 사망이 켜는 완료 기록(PlayerPrefs game_intro_done_v1_slotN)은 실측 전 값으로 되돌린다.
/// 결과: Logs/boss_fight_sim/intro_boss_*/watch.txt · 사진 · 로그 「[IntroBoss] 끝」.
/// </summary>
public static class IntroBossProbeEditor
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const float Step      = 0.25f;
    private const float WatchSecs = 45f;
    private static readonly float[] ShotsAt = { 0.5f, 1.5f, 3f, 5f, 7f, 9f, 11f };
    private static readonly string[] LodWatch = { "ArchFloorCurve_01b", "Ambo", "Candleabra" };   // 10-18 ① 「LOD 고친 게 문제」 — 10-16에 빈 단계를 지운 것들

    private static float s_lodBias = -1f;   // 0보다 크면 관찰 동안 이 LOD 기준으로(빌드 기본 화질 Balanced = 1 흉내 · 끝나면 되돌림)

    [MenuItem("RelicFairy/Debug/10-16 Intro Boss Watch (Play)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[IntroBoss] 플레이 모드에서"); return; }
        s_lodBias = -1f;
        RunAsync().Forget();
    }

    // 10-16 빌드에서만 기사가 떴다 — 빌드 기본 화질(Balanced · LOD 기준 1)로 같은 관찰(에디터 High Fidelity = 2에선 안 보였다)
    [MenuItem("RelicFairy/Debug/10-16 Intro Boss Watch - Build LOD (Play)")]
    private static void RunBuildLod()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[IntroBoss] 플레이 모드에서"); return; }
        s_lodBias = 1f;
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        string key = "game_intro_done_v1_slot" + (RunProgressManager.Instance?.ActiveSlotIndex ?? 0);
        bool hadKey = PlayerPrefs.HasKey(key);
        int  wasVal = PlayerPrefs.GetInt(key, 0);
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "boss_fight_sim", $"intro_boss_{DateTime.Now:MMdd_HHmmss}");
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder("인트로 보스 — 시각(기사 첫 발견 기준) · 높이 = 기사 피벗 − 발밑 바닥 · 몸 아래 = 스킨드 메시 바닥 − 발밑 바닥 · 상태\n");
        float lodWas = QualitySettings.lodBias;
        if (s_lodBias > 0f) { QualitySettings.lodBias = s_lodBias; sb.AppendLine($"LOD 기준 {lodWas} → {s_lodBias}(빌드 흉내)"); }
        try
        {
            DeathKnightBossMonster dk = null;
            float until = Time.realtimeSinceStartup + 240f;
            while ((dk = UnityEngine.Object.FindFirstObjectByType<DeathKnightBossMonster>()) == null || !dk.isActiveAndEnabled)
            {
                if (Time.realtimeSinceStartup > until) { sb.AppendLine("기사가 240초 안에 나오지 않았다"); return; }
                await UniTask.Delay(200, DelayType.Realtime);
            }
            var deadField = typeof(PlayerController).GetField("_dead", Any);
            float t0 = Time.realtimeSinceStartup, deadAt = -1f;
            int shot = 0;
            string lastState = "";
            while (dk != null && Time.realtimeSinceStartup - t0 < WatchSecs)
            {
                float t = Time.realtimeSinceStartup - t0;
                var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
                string state = StateOf(dk);
                sb.AppendLine($"[{t,5:0.00}] {Floor(dk)} · 상태 {state}{(state != lastState ? " ←" : "")}");
                lastState = state;
                if (shot < ShotsAt.Length && t >= ShotsAt[shot])
                {
                    ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"boss_{shot:00}_t{t:0.0}.png"));
                    sb.AppendLine($"[{t,5:0.00}] 사진 {shot:00} · LOD{LodLevels()}");
                    shot++;
                }
                if (deadAt < 0f && player != null && deadField?.GetValue(player) is true)
                {
                    deadAt = t;
                    sb.AppendLine($"[{t,5:0.00}] ★ 플레이어 쓰러짐 — 그때 기사 상태 {state}");
                    ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"death_t{t:0.0}.png"));
                }
                if (deadAt >= 0f && t - deadAt > 3f) break;   // 슬로모 · 암전 뒤 완료 기록까지 기다린 뒤 끝
                await UniTask.Delay(TimeSpan.FromSeconds(Step), DelayType.Realtime);
            }
            if (deadAt < 0f) sb.AppendLine($"{WatchSecs}초 안에 플레이어가 쓰러지지 않았다");
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            QualitySettings.lodBias = lodWas;
            if (hadKey) PlayerPrefs.SetInt(key, wasVal); else PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
            sb.AppendLine($"인트로 완료 기록 {key} 되돌림 → {(hadKey ? wasVal.ToString() : "없음")}");
            File.WriteAllText(Path.Combine(dir, "watch.txt"), sb.ToString(), Encoding.UTF8);
            Debug.Log($"[IntroBoss] 끝 — {dir}");
        }
    }

    private static string StateOf(MonsterBase boss)
    {
        var fsm = typeof(MonsterBase).GetField("_fsm", Any)?.GetValue(boss);
        return (fsm?.GetType().GetProperty("CurrentType", Any)?.GetValue(fsm) as Type)?.Name ?? "-";
    }

    /// <summary>지켜보는 LODGroup이 지금 몇 번째 단계를 그리는지 — 메인 카메라 · 지금 LOD 기준으로 화면 높이를 계산(10-18 ①).</summary>
    private static string LodLevels()
    {
        var cam = Camera.main;
        if (cam == null) return " 카메라 없음";
        var sb = new StringBuilder();
        foreach (var g in UnityEngine.Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None))
        {
            if (!g.enabled || !g.gameObject.activeInHierarchy || Array.FindIndex(LodWatch, k => g.name.Contains(k)) < 0) continue;
            Vector3 s = g.transform.lossyScale;
            float size = g.size * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
            float dist = Vector3.Distance(g.transform.TransformPoint(g.localReferencePoint), cam.transform.position);
            float h = size * 0.5f / (dist / QualitySettings.lodBias * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
            var lods = g.GetLODs();
            int lv = Array.FindIndex(lods, l => h >= l.screenRelativeTransitionHeight);
            sb.Append($" {g.name}:{(lv < 0 ? "꺼짐" : "L" + lv)}/{lods.Length}({h:0.000} · {dist:0}m)");
        }
        return sb.ToString();
    }

    /// <summary>기사 피벗 · 몸 바닥을 발밑 콜라이더(자기 몸 · 트리거 제외) 윗면과 견준다.</summary>
    private static string Floor(MonsterBase b)
    {
        Vector3 p = b.transform.position;
        float top = float.NaN; string name = "없음"; float best = float.MaxValue;
        foreach (var h in Physics.RaycastAll(p + Vector3.up * 3f, Vector3.down, 12f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.collider.transform.IsChildOf(b.transform) || h.distance >= best) continue;
            best = h.distance; top = h.point.y; name = $"{h.collider.name}({LayerMask.LayerToName(h.collider.gameObject.layer)})";
        }
        float low = float.MaxValue;
        foreach (var r in b.GetComponentsInChildren<SkinnedMeshRenderer>())
            if (r.enabled) low = Mathf.Min(low, r.bounds.min.y);
        if (float.IsNaN(top)) return $"y {p.y:0.00} · 발밑 바닥 없음 · 몸 바닥 {low:0.00}";
        return $"높이 {p.y - top:+0.00;-0.00} · 몸 아래 {(low < float.MaxValue ? (low - top).ToString("+0.00;-0.00") : "?")} · 바닥 {top:0.00} {name}";
    }
}
#endif
