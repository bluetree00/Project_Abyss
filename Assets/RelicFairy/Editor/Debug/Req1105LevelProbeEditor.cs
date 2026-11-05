#if UNITY_EDITOR
using System;
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
/// 11-05 사용자 버그 목록 레벨 몫(#2 · #9 · #11 · #14) 실측. 결과: Temp/req1105_level/*.txt · 사진 · 로그 「[Req1105] 끝 …」.
/// </summary>
public static class Req1105LevelProbeEditor
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private static string Dir => Path.Combine(Directory.GetCurrentDirectory(), "Temp", "req1105_level");

    // ── #11 서약 제단 — 대기방 표시가 있는 제단만 「이미 씀」에 숨는가 ─────────────────
    [MenuItem("RelicFairy/Debug/11-05 #11 서약 제단 — 성소 · 대기방 표시 (Play, 런 중)")]
    private static void Altar() { if (EditorApplication.isPlaying) AltarAsync().Forget(); }

    private static async UniTaskVoid AltarAsync()
    {
        var sb = new StringBuilder("11-05 #11 서약 제단" + Environment.NewLine);
        var run = GameRunBootstrapper.Instance?.Run;
        var p = run?.Player;
        if (run == null || p == null) { Debug.Log("[Req1105] 끝 #11 — 런 없음"); return; }
        bool was = run.WaitingAltarUsed;
        GameObject a = null, b = null;
        try
        {
            run.WaitingAltarUsed = true;
            a = WorldCovenantAltar.SpawnAt(p.transform.position + p.transform.forward * 6f).gameObject;                      // 성소(던전) 제단
            b = WorldCovenantAltar.SpawnAt(p.transform.position + p.transform.forward * 9f, waitingRoom: true).gameObject;   // 대기방 제단
            await UniTask.DelayFrame(3);
            sb.AppendLine($"  {(a.activeSelf ? "PASS" : "FAIL")} 성소 제단 — 「이미 씀」이어도 보인다(켜짐 {a.activeSelf})");
            sb.AppendLine($"  {(!b.activeSelf ? "PASS" : "FAIL")} 대기방 제단 — 「이미 씀」이면 숨는다(켜짐 {b.activeSelf})");
        }
        finally
        {
            if (a != null) UnityEngine.Object.Destroy(a);
            if (b != null) UnityEngine.Object.Destroy(b);
            run.WaitingAltarUsed = was;
            Write("altar.txt", sb);
        }
    }

    // ── #14 봉인 문 — 문 안쪽 → 바깥쪽 길찾기 경로가 끊기는가 ───────────────────────
    [MenuItem("RelicFairy/Debug/11-05 #14 봉인 문 길찾기 (Play, 전투방 봉인 중)")]
    private static void Gates()
    {
        if (!EditorApplication.isPlaying) return;
        var sb = new StringBuilder("11-05 #14 봉인 문 길찾기" + Environment.NewLine);
        var obstacles = UnityEngine.Object.FindObjectsByType<NavMeshObstacle>(FindObjectsSortMode.None).Where(o => o.name == "Marker").ToList();
        sb.AppendLine($"  문 판(Marker) 길찾기 막음 {obstacles.Count}개 · 켜짐 {obstacles.Count(o => o.enabled)}");
        int pass = 0, n = 0;
        foreach (var o in obstacles.Where(o => o.enabled))
        {
            Vector3 c = o.transform.position;
            Vector3 f = o.transform.forward; f.y = 0f; f.Normalize();
            foreach (float s in new[] { 1f, -1f })
            {
                Vector3 from = c + f * (3f * s), to = c - f * (3f * s);
                if (!NavMesh.SamplePosition(from, out var hf, 2f, NavMesh.AllAreas) || !NavMesh.SamplePosition(to, out var ht, 2f, NavMesh.AllAreas)) continue;
                var path = new NavMeshPath();
                NavMesh.CalculatePath(hf.position, ht.position, NavMesh.AllAreas, path);
                n++;
                bool cut = path.status != NavMeshPathStatus.PathComplete;
                if (cut) pass++;
                sb.AppendLine($"  {(cut ? "PASS" : "FAIL")} {o.transform.parent?.name} 쪽{s:+0;-0} — 경로 {path.status}");
                break;
            }
        }
        sb.AppendLine($"  끊긴 문 {pass}/{n}");
        Write("gates.txt", sb);
    }

    // ── #9 숲의 수호자 원형 띠 — 2페이지로 보내고 8방향 판정 + 위 사진 ──────────────
    [MenuItem("RelicFairy/Debug/11-05 #9 숲 원형 가시 띠 (Play, 숲 보스방 · 해방기)")]
    private static void Ring() { if (EditorApplication.isPlaying) RingAsync().Forget(); }

    private static async UniTaskVoid RingAsync()
    {
        var sb = new StringBuilder("11-05 #9 숲 원형 가시 띠" + Environment.NewLine);
        try
        {
            MonsterBase FindBoss() => UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None).FirstOrDefault(m => m is IBoss);
            if (FindBoss() == null) { TestHubDebugMenu.StepIntoBossEntrance(); await UniTask.Delay(300, DelayType.Realtime); TestHubDebugMenu.StepPastBossEntrance(); }
            MonsterBase boss = null;
            float until = Time.realtimeSinceStartup + 15f;
            while ((boss = FindBoss()) == null && Time.realtimeSinceStartup < until) await UniTask.Delay(200, DelayType.Realtime);
            if (boss == null) { sb.AppendLine("보스 없음"); return; }
            await UniTask.Delay(8000, DelayType.Realtime);   // 등장 연출
            var player = GameRunBootstrapper.Instance?.Run?.Player;
            if (player != null)
            {
                player.SetInvincible(90f);
                Vector3 front = boss.transform.position + boss.transform.forward * 5f;
                if (player.TryGetComponent<Rigidbody>(out var rb)) rb.position = front;
                player.transform.position = front;
            }
            BossPageDebugMenu.JumpToPage2();
            BossStageHazard ring = null;
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 40f)
            {
                ring = UnityEngine.Object.FindObjectsByType<BossStageHazard>(FindObjectsSortMode.None).FirstOrDefault(h => h.name.Contains("Ring"));
                if (ring != null) break;
                TestHubDebugMenu.KillBoss();
                await UniTask.Delay(1500, DelayType.Realtime);
            }
            if (ring == null) { sb.AppendLine("원형 띠 안 생김(40초)"); return; }
            await UniTask.Delay(6000, DelayType.Realtime);   // 띠가 다 자라도록
            float outer = (float)typeof(BossStageHazard).GetField("_ringOuter", Any).GetValue(ring);
            float band  = (float)typeof(BossStageHazard).GetField("_band", Any).GetValue(ring);
            var   cen   = (Vector3)typeof(BossStageHazard).GetField("_ringCenter", Any).GetValue(ring);
            sb.AppendLine($"  원형 띠 — 가운데 {cen} · 바깥 반경 {outer:0.0} · 폭 {band:0.0}");
            int ok = 0;
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f, r = outer - band * 0.5f;
                bool inBand = ring.Contains(cen + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
                if (inBand) ok++;
                sb.AppendLine($"  {(inBand ? "PASS" : "FAIL")} {i * 45,3}° 띠 가운데 판정 {inBand}");
            }
            bool center = ring.Contains(cen);
            sb.AppendLine($"  {(!center ? "PASS" : "FAIL")} 아레나 가운데는 띠 밖 {(!center)}");
            sb.AppendLine($"  8방향 {ok}/8");
            TopShot(cen, outer + 3f, "ring_top.png");
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally { Write("ring.txt", sb); }
    }

    // ── #2 계단 자리 — 계단 기준점을 플레이어 발밑에 두고 옮기기를 직접 부른다 ─────────
    [MenuItem("RelicFairy/Debug/11-05 #2 계단 자리에서 옮기기 (Play, 보스방)")]
    private static void Stair()
    {
        if (!EditorApplication.isPlaying) return;
        var sb = new StringBuilder("11-05 #2 계단 자리" + Environment.NewLine);
        var p = GameRunBootstrapper.Instance?.Run?.Player;
        if (p == null) { Debug.Log("[Req1105] 끝 #2 — 플레이어 없음"); return; }
        var go = new GameObject("@StairProbe");
        try
        {
            var s = go.AddComponent<ChapterStairway>();
            var entry = new GameObject("@StairEntry").transform;
            entry.SetParent(go.transform, false);
            Vector3 fwd = p.transform.forward; fwd.y = 0f;
            entry.SetPositionAndRotation(p.transform.position + fwd.normalized * 3f, Quaternion.LookRotation(fwd.normalized, Vector3.up));   // 플레이어 = 계단 기준 z −3(디딤판 자리)
            typeof(ChapterStairway).GetField("_entry", Any).SetValue(s, entry);
            Vector3 before = p.transform.position;
            typeof(ChapterStairway).GetMethod("EvictPlayerFromFootprint", Any).Invoke(s, null);
            Vector3 after = p.transform.position;
            Vector3 l = entry.InverseTransformPoint(after);
            bool outside = Mathf.Abs(l.x) > 1.8f || l.z < -7.5f || l.z > 1.5f;
            bool onNav = NavMesh.SamplePosition(after, out var hit, 0.6f, NavMesh.AllAreas);
            sb.AppendLine($"  옮기기 전 {before} → 뒤 {after} · 계단 기준 {l}");
            sb.AppendLine($"  {(outside ? "PASS" : "FAIL")} 계단 자리 밖(가로 ±1.8 · 세로 −7.5~+1.5)");
            sb.AppendLine($"  {(onNav ? "PASS" : "FAIL")} 디딜 수 있는 바닥(길찾기 판 0.6 m 안)");
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally { UnityEngine.Object.Destroy(go); Write("stair.txt", sb); }
    }

    // ── 공통 ─────────────────────────────────────────────────
    private static void Write(string file, StringBuilder sb)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(Path.Combine(Dir, file), sb.ToString(), Encoding.UTF8);
        int fail = sb.ToString().Split('\n').Count(x => x.Contains("FAIL"));
        Debug.Log($"[Req1105] 끝 {file} — FAIL {fail}");
    }

    private static void TopShot(Vector3 center, float half, string file)
    {
        var main = Camera.main;
        if (main == null) return;
        var go = new GameObject("Req1105Cam");
        try
        {
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(main);
            cam.enabled = false;
            cam.orthographic = true;
            cam.orthographicSize = half;
            cam.nearClipPlane = 0.3f; cam.farClipPlane = 200f;
            go.transform.SetPositionAndRotation(center + Vector3.up * 40f, Quaternion.LookRotation(Vector3.down, Vector3.forward));
            var rt = new RenderTexture(1200, 1200, 24);
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            Directory.CreateDirectory(Dir);
            File.WriteAllBytes(Path.Combine(Dir, file), tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
            cam.targetTexture = null;
            rt.Release();
        }
        finally { UnityEngine.Object.Destroy(go); }
    }
}
#endif
