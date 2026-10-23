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
using UnityEngine.AI;

/// <summary>
/// 10-19 B2(10-18 ⑱ 남은 것) — 망령 황소(Ch3) 도약 · 돌진의 원 · 줄 판정과 그림자 베기 궤적 모양을 위에서 본다.
/// 테스트 허브 게임 카메라는 벽에 가렸다 → 예고 가운데 위 20 m에서 똑바로 아래로(가까운 면 16.5 m — 천장 · 벽 위쪽은 잘린다).
/// · Shadow Look: 「9 BullTank Chapters」를 돌린 뒤 켜 두면, Ch3 황소가 있을 때 원 · 줄 예고가 뜰 때마다 맞기 직전 · 직후를 찍는다.
/// · Shadow Charge Look: 첫 전투방에 Ch3 황소만 부르고 플레이어를 막힘 없는 쪽 10 m 밖에 세워 돌진(5 m 이상 · 쿨 7초)을 끌어낸다
///   (Chapters 실행은 플레이어를 정해진 거리로 옮겨 40초 동안 돌진이 한 번도 안 나왔다).
/// 결과: Logs/boss_fight_sim/bull_shadow_*/ · 로그 「[BullShadowLook] 끝」.
/// </summary>
public static class BullShadowLookEditor
{
    private const string AutoRoot = "RelicFairy/Debug/런 구조 자동 실측/";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const float Lead = 0.75f;   // BullTankMonster.Chapter ShadowLead
    private const float Height = 20f, Near = 16.5f;
    private const float FarDist = 10f;
    private const int MaxEvents = 8;
    private static bool s_armed;
    private static bool s_chargeSeen;

    [MenuItem("RelicFairy/Boss/BullTank/10-19 Shadow Look (Play)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[BullShadowLook] 플레이 중에"); return; }
        WatchAsync(240f).Forget();
    }

    // 10-22 ⑧ 「황소 휘두르기의 이상한 폭발」 전후 — 「9 BullTank Chapters」의 Ch1 황소 휘두르기(부채꼴)를 위에서 맞는 순간 앞뒤로
    [MenuItem("RelicFairy/Boss/BullTank/10-22 Bull Swing Look (Play)")]
    private static void RunSwing()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[BullShadowLook] 플레이 중에"); return; }
        WatchAsync(240f, "BullTank_Ch1", cone: true).Forget();
    }

    [MenuItem("RelicFairy/Boss/BullTank/10-19 Shadow Charge Look (테스트 허브, Play)")]
    private static void BeginCharge()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[BullShadowLook] 플레이 중에"); return; }
        var launcher = UnityEngine.Object.FindFirstObjectByType<TestHubLauncher>();
        if (launcher == null) { Debug.LogWarning("[BullShadowLook] 테스트 허브에서"); return; }
        typeof(TestHubLauncher).GetField("relics", Inst)?.SetValue(launcher, new RelicClassSO[] { null });
        s_armed = true;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        EditorApplication.ExecuteMenuItem(AutoRoot + "시작 (테스트 허브에서, 플레이 중)");
    }

    private static void OnLog(string msg, string stack, LogType type)
    {
        if (!s_armed || !msg.StartsWith("[RunAuto]") || !msg.Contains("방 진입")) return;
        s_armed = false;
        Application.logMessageReceived -= OnLog;
        EditorApplication.delayCall += () =>
        {
            EditorApplication.ExecuteMenuItem(AutoRoot + "중지·기록");
            var p = GameRunBootstrapper.Instance?.Run?.Player;
            if (p == null) { Debug.LogWarning("[BullShadowLook] 런 플레이어가 없다"); return; }
            ChargeAsync(p).Forget();
        };
    }

    private static async UniTaskVoid ChargeAsync(PlayerController p)
    {
        try
        {
            typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);
            await UniTask.Delay(4000, DelayType.Realtime);   // 첫 전투방 부감 카메라
            foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
                if (m != null) m.gameObject.SetActive(false);
            Vector3 home = p.transform.position, fwd = p.transform.forward;
            fwd.y = 0f; fwd.Normalize();
            var bull = await Managers.ObjectPooler.SpawnAsync<MonsterBase>("BullTank/BullTank_Ch3", ObjectPoolerManager.PoolType.Monster, home + fwd * 4f, Quaternion.LookRotation(-fwd));
            if (bull == null) { Debug.LogWarning("[BullShadowLook] 황소 부르기 실패"); return; }
            s_chargeSeen = false;
            var watch = WatchAsync(75f);
            await UniTask.Delay(3000, DelayType.Realtime);   // 깨어나게(포효)
            float last = -99f, t0 = Time.realtimeSinceStartup;
            while (!s_chargeSeen && Time.realtimeSinceStartup - t0 < 65f && bull != null && !bull.IsDead)
            {
                if (Time.realtimeSinceStartup - last > 7f) { last = Time.realtimeSinceStartup; PlaceFar(p, bull.transform.position); }
                await UniTask.Delay(250, DelayType.Realtime);
            }
            await watch;
        }
        catch (Exception e) { Debug.LogException(e); }
    }

    /// <summary>황소에서 막힘 없는 쪽(8방향 · NavMesh 직선)으로 FarDist 밖에 플레이어를 세운다.</summary>
    private static void PlaceFar(PlayerController p, Vector3 from)
    {
        float best = 0f;
        Vector3 at = p.transform.position;
        for (int i = 0; i < 8; i++)
        {
            Vector3 d = Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward;
            float free = NavMesh.Raycast(from, from + d * FarDist, out var hit, NavMesh.AllAreas) ? Vector3.Distance(from, hit.position) : FarDist;
            if (free > best && NavMesh.SamplePosition(from + d * free * 0.95f, out var s, 1.5f, NavMesh.AllAreas)) { best = free; at = s.position; }
        }
        p.transform.position = at;
        if (p.TryGetComponent<Rigidbody>(out var rb)) { rb.position = at; rb.linearVelocity = Vector3.zero; }
        Debug.Log($"[BullShadowLook] 플레이어를 황소에서 {best:0.0} m 밖에");
    }

    private static async UniTask WatchAsync(float seconds, string key = "BullTank_Ch3", bool cone = false)
    {
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "boss_fight_sim", $"bull_shadow_{DateTime.Now:MMdd_HHmmss}");
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder("망령 황소(Ch3) 도약 · 돌진 — 위에서(예고 가운데 위 20 m)\n");
        var seen = new HashSet<int>();
        int events = 0;
        float start = Time.realtimeSinceStartup;
        try
        {
            while (events < MaxEvents && Time.realtimeSinceStartup - start < seconds && EditorApplication.isPlaying)
            {
                await UniTask.Yield(PlayerLoopTiming.PostLateUpdate);
                var all = UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None);
                bool found = all.Any(m => m != null && !m.IsDead && m.name.Contains(key));
                if (!found) continue;
                foreach (var m in all)   // 방 몬스터가 원 안에 몰려 이름표로 덮었다 — 숨긴다
                    if (m != null && !m.name.Contains(key) && m.gameObject.activeSelf) m.gameObject.SetActive(false);
                foreach (var t in UnityEngine.Object.FindObjectsByType<MonsterTelegraph>(FindObjectsSortMode.None))
                {
                    if (t == null || !seen.Add(t.GetInstanceID())) continue;
                    string shape = t.name.Replace("[MonsterTelegraph.", "").Replace("]", "");
                    var rs = t.GetComponentsInChildren<Renderer>();
                    if (rs.Length == 0) continue;
                    var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                    // 도약(원) · 돌진(줄 = Rect, 길다)만 — 휘두르기(부채꼴)는 10-18에 봤다
                    bool line = shape == "Rect" && Mathf.Max(b.extents.x, b.extents.z) >= 2f;
                    if (cone ? shape != "Cone" : shape != "Circle" && !line) continue;
                    if (line) s_chargeSeen = true;
                    int n = ++events;
                    sb.AppendLine($"{n} {shape} t={Time.time:0.0} 예고 가운데 {b.center} · 반폭 x {b.extents.x:0.00} z {b.extents.z:0.00}");
                    ShootLater(dir, n, shape, b, cone ? SwingShots : ShadowShots).Forget();
                }
            }
            await UniTask.Delay(1500, DelayType.Realtime);
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText(Path.Combine(dir, "look.txt"), sb.ToString(), Encoding.UTF8);
            Debug.Log($"[BullShadowLook] 끝 — {dir}");
        }
    }

    private static readonly float[] ShadowShots = { Lead - 0.05f, Lead + 0.05f, Lead + 0.2f };
    private static readonly float[] SwingShots  = { 0.6f, 0.9f, 1.1f };   // 황소 휘두르기 판정 0.85초(_swingHitTime) 앞 · 뒤

    private static async UniTaskVoid ShootLater(string dir, int n, string shape, Bounds b, float[] times)
    {
        float t0 = Time.time;
        foreach (float at in times)
        {
            while (Time.time - t0 < at) await UniTask.Yield(PlayerLoopTiming.PostLateUpdate);
            Shoot(Path.Combine(dir, $"{n}_{shape}_{at:0.00}.png"), b);
        }
    }

    /// <summary>예고 가운데 위에서 아래로 — 긴 쪽이 화면 가로가 되게 돌린다.</summary>
    private static void Shoot(string path, Bounds b)
    {
        var go = new GameObject("~ShadowLookCam");
        var cam = go.AddComponent<Camera>();
        if (Camera.main != null) cam.CopyFrom(Camera.main);
        cam.enabled = false;
        cam.orthographic = false;
        cam.fieldOfView = 50f;
        cam.nearClipPlane = Near;
        cam.farClipPlane = Height + 10f;
        float yaw = b.extents.z > b.extents.x ? 90f : 0f;
        go.transform.SetPositionAndRotation(new Vector3(b.center.x, b.min.y + Height, b.center.z), Quaternion.Euler(90f, yaw, 0f));
        int w = 1280, h = 720;
        var rtex = new RenderTexture(w, h, 24);
        cam.targetTexture = rtex;
        cam.Render();
        cam.targetTexture = null;
        var prev = RenderTexture.active;
        RenderTexture.active = rtex;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.Destroy(rtex);
        UnityEngine.Object.Destroy(tex);
        UnityEngine.Object.Destroy(go);
    }
}
#endif
