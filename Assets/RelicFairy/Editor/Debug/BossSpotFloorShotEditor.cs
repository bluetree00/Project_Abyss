#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 11-02 Q24 사용자 「모드레드 연출 때 바닥이 깨진다 · 모드레드 자리 타일이 가까이 보면 깨진다」(사진 09 — 챕터 3 보스 등장 클로즈업).
/// 11-02 겹친 바닥 점검: 보스 자리 둘레에 낮은 제단(SM_ArchFloorCurve_01b0 · 위 0.463)과 계단 조각(SM_StairStoneKit_02a · 위 0.48)이 2 cm 차로 겹친다.
/// 보스방(보스 잠든 채)에서 임시 카메라로 보스 발치를 낮게 2 · 4 · 8 m에서 찍는다 — a 그대로 · b 둘레 계단 조각 끔 · c 제단 끔.
/// a가 얼룩지고 b가 깨끗하면 계단 조각이 원인. 결과: Temp/boss_spot_floor/*.png · spot.txt · 로그 「[BossSpotFloor] 끝」.
/// </summary>
public static class BossSpotFloorShotEditor
{
    [MenuItem("RelicFairy/Debug/11-02 보스 자리 바닥 클로즈업 — 계단 · 제단 끄고 비교 (보스방, Play)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[BossSpotFloor] 보스방 플레이 중에"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "boss_spot_floor");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder("보스 자리 바닥 클로즈업\n");
        // 보스는 입구를 지나야 나온다 — 없으면 입구를 밟고 나올 때까지(10초)
        MonsterBase FindBoss() => UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None).FirstOrDefault(m => m is IBoss);
        var boss = FindBoss();
        if (boss == null)
        {
            TestHubDebugMenu.StepIntoBossEntrance();
            await UniTask.Delay(300, DelayType.Realtime);
            TestHubDebugMenu.StepPastBossEntrance();
            float until = Time.realtimeSinceStartup + 10f;
            while ((boss = FindBoss()) == null && Time.realtimeSinceStartup < until) await UniTask.Delay(200, DelayType.Realtime);
            await UniTask.Delay(1500, DelayType.Realtime);   // 등장 자리에 선 채(연출 중)
        }
        var main = Camera.main;
        if (boss == null || main == null) { sb.AppendLine("보스 · 카메라 없음"); File.WriteAllText(Path.Combine(dir, "spot.txt"), sb.ToString(), Encoding.UTF8); Debug.Log($"[BossSpotFloor] 끝 — 보스 · 카메라 없음"); return; }
        Vector3 at = boss.transform.position;
        sb.AppendLine($"보스 {boss.name} {at}");

        var all = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToList();
        var stairs = all.Where(r => r.name.Contains("StairStoneKit") && Vector3.Distance(r.bounds.center, at) < 6f).ToList();
        var altar  = all.Where(r => r.name.Contains("ArchFloorCurve") && r.bounds.Contains(new Vector3(at.x, r.bounds.center.y, at.z))).ToList();
        var bossR  = boss.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToList();
        sb.AppendLine($"둘레 계단 조각 {stairs.Count} · 보스 발밑 제단 {altar.Count}({string.Join(", ", altar.Select(r => r.name))}) · 보스 몸 렌더러 {bossR.Count}(찍는 동안 끔)");

        var go = new GameObject("BossSpotFloorCam");
        var cam = go.AddComponent<Camera>();
        cam.CopyFrom(main);
        cam.enabled = false;
        var rt = new RenderTexture(1600, 900, 24);
        cam.targetTexture = rt;
        foreach (var r in bossR) r.enabled = false;
        try
        {
            Vector3 side = Vector3.ProjectOnPlane(main.transform.position - at, Vector3.up).normalized;
            if (side.sqrMagnitude < 0.01f) side = Vector3.back;
            foreach (float d in new[] { 2f, 4f, 8f })
            {
                go.transform.position = at + side * d + Vector3.up * (0.6f + d * 0.25f);
                go.transform.rotation = Quaternion.LookRotation(at + Vector3.up * 0.2f - go.transform.position);
                Shot(cam, rt, Path.Combine(dir, $"d{d:0}_a.png"));
                foreach (var r in stairs) r.enabled = false;
                Shot(cam, rt, Path.Combine(dir, $"d{d:0}_b_no_stairs.png"));
                foreach (var r in stairs) r.enabled = true;
                foreach (var r in altar) r.enabled = false;
                Shot(cam, rt, Path.Combine(dir, $"d{d:0}_c_no_altar.png"));
                foreach (var r in altar) r.enabled = true;
            }
            foreach (var r in stairs.Take(12)) sb.AppendLine($"  계단 {r.name} 위 {r.bounds.max.y:0.000} · 가운데 {r.bounds.center}");
            foreach (var r in altar) sb.AppendLine($"  제단 {r.name} 위 {r.bounds.max.y:0.000}");
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            foreach (var r in bossR) if (r != null) r.enabled = true;
            foreach (var r in stairs) if (r != null) r.enabled = true;
            foreach (var r in altar) if (r != null) r.enabled = true;
            cam.targetTexture = null;
            rt.Release();
            UnityEngine.Object.Destroy(go);
            File.WriteAllText(Path.Combine(dir, "spot.txt"), sb.ToString(), Encoding.UTF8);
            Debug.Log($"[BossSpotFloor] 끝 — {dir}");
        }
    }

    // Q24 2차 — 사진 09는 인트로(어둠으로 뒤집힌 뒤 보스 등장 「왕을 등진 검」)였다. 인트로 아레나 겹친 바닥 점검: 모르드레드(0.3, −0.58, 2.3) 오른쪽
    // 돌 잔해(SM_StoneDebris_02a)가 바닥과 4 ~ 8 mm 차로 겹친다. 테스트 허브에서 인트로를 열어(오프닝 넘김) 임시 카메라로 그 둘레를 낮게 — a 그대로 · b 잔해 끔.
    [MenuItem("RelicFairy/Debug/11-02 인트로 모르드레드 자리 바닥 — 잔해 켜고 · 끄고 (테스트 허브, Play)")]
    private static void IntroSpot()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[BossSpotFloor] 플레이 중에"); return; }
        IntroSpotAsync().Forget();
    }

    private static async UniTaskVoid IntroSpotAsync()
    {
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "intro_spot_floor");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder("인트로 모르드레드 자리 바닥" + Environment.NewLine);
        string key = "game_intro_done_v1_slot" + (RunProgressManager.Instance?.ActiveSlotIndex ?? 0);
        bool hadKey = ProgressPrefs.HasKey(key);
        int  wasVal = ProgressPrefs.GetInt(key, 0);
        GameObject go = null;
        RenderTexture rt = null;
        var debris = new List<Renderer>();
        try
        {
            AppBootstrapper.Instance?.RequestLoad(Define.Scene.Game_Intro);
            float t0 = Time.realtimeSinceStartup;
            while (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "Game_Intro" && Time.realtimeSinceStartup - t0 < 60f) await UniTask.Delay(200, DelayType.Realtime);
            float lastSkip = 0f;
            while (Time.realtimeSinceStartup - t0 < 120f)
            {
                var pl = IntroBootstrapper.Instance?.Player;
                bool locked = pl != null && (bool)(typeof(PlayerController).GetField("_inputDisabledExternally", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(pl) ?? false);
                if (pl != null && !locked && Time.realtimeSinceStartup - t0 > 8f) break;
                if (Time.realtimeSinceStartup - lastSkip > 1.6f) { lastSkip = Time.realtimeSinceStartup; TestHubDebugMenu.AdvanceDialogue(); }
                await UniTask.Delay(200, DelayType.Realtime);
            }
            await IntroArenaLoader.ReadyAsync();
            var main = Camera.main;
            if (main == null) { sb.AppendLine("카메라 없음"); return; }
            Vector3 spot = new Vector3(0.3f, -0.56f, 2.3f);   // 모르드레드 액터 발치(11-02 인트로 실측)
            debris = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
                .Where(r => r.enabled && r.gameObject.activeInHierarchy && r.name.Contains("StoneDebris") && Vector3.Distance(r.bounds.center, spot) < 9f).ToList();
            sb.AppendLine($"모르드레드 자리 둘레 9 m 돌 잔해 렌더러 {debris.Count}");
            foreach (var r in debris.Take(10)) sb.AppendLine($"  {r.name} 위 {r.bounds.max.y:0.000} · 아래 {r.bounds.min.y:0.000} · 가운데 {r.bounds.center}");
            go = new GameObject("IntroSpotCam");
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(main);
            cam.enabled = false;
            rt = new RenderTexture(1600, 900, 24);
            cam.targetTexture = rt;
            Vector3 look = new Vector3(3.5f, -0.56f, 1.5f);   // 오른쪽 잔해 쪽
            foreach (var (d, tag) in new[] { (2.5f, "near"), (5f, "mid"), (9f, "far") })
            {
                go.transform.position = look + new Vector3(-0.6f, 0.9f + d * 0.3f, -d);
                go.transform.rotation = Quaternion.LookRotation(look - go.transform.position);
                Shot(cam, rt, Path.Combine(dir, $"{tag}_a.png"));
                foreach (var r in debris) r.enabled = false;
                Shot(cam, rt, Path.Combine(dir, $"{tag}_b_no_debris.png"));
                foreach (var r in debris) r.enabled = true;
            }
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            foreach (var r in debris) if (r != null) r.enabled = true;
            if (rt != null) rt.Release();
            if (go != null) UnityEngine.Object.Destroy(go);
            if (hadKey) ProgressPrefs.SetInt(key, wasVal); else ProgressPrefs.DeleteKey(key);
            ProgressPrefs.Save();
            File.WriteAllText(Path.Combine(dir, "spot.txt"), sb.ToString(), Encoding.UTF8);
            Debug.Log($"[BossSpotFloor] 인트로 끝 — {dir}");
        }
    }

    // Q24 3차 — 사진 09는 인트로가 아니라 챕터 3 보스방 첫 등장(자막 DeathKnight_Encounter_First · 칭호 「왕을 등진 검」 · 등 뒤 낮은 클로즈업).
    // 그 장면을 플레이 화면 그대로 0.35초마다 찍고, 클로즈업 중 세 번 보스 발치 5 m 안 바닥 가까운 소품(바닥 · 계단 · 제단 · 보스 몸 빼고)을 끄고 한 장 더.
    // 첫 관람 기록은 잠깐 지웠다가 되돌린다. 결과: Temp/boss_entrance_ch3/*.png · spot.txt · 로그 「[BossSpotFloor] 등장 끝」.
    [MenuItem("RelicFairy/Debug/11-02 Q24 챕터 3 보스 등장 클로즈업 — 둘레 소품 끄고 비교 (보스방, Play)")]
    private static void Ch3Entrance()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[BossSpotFloor] 보스방 플레이 중에"); return; }
        Ch3EntranceAsync().Forget();
    }

    private static async UniTaskVoid Ch3EntranceAsync()
    {
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "boss_entrance_ch3");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder("챕터 3 보스 등장 클로즈업" + Environment.NewLine);
        var data = BackendGameData.Instance?.Data;
        string seenKey = StoryProgress.Rec.SeenPrefix + "entr_" + StoryProgress.Era + "_" + StoryProgress.DeathKnight;
        int seenWas = data?.GetRecord(seenKey) ?? 0;
        var props = new List<Renderer>();
        try
        {
            if (seenWas > 0) data.AddRecord(seenKey, -seenWas);   // 첫 관람으로(사진 09 = 첫 등장 자막)
            MonsterBase FindBoss() => UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None).FirstOrDefault(m => m is IBoss);
            if (FindBoss() == null)
            {
                TestHubDebugMenu.StepIntoBossEntrance();
                await UniTask.Delay(300, DelayType.Realtime);
                TestHubDebugMenu.StepPastBossEntrance();
            }
            MonsterBase boss = null;
            float until = Time.realtimeSinceStartup + 10f;
            while ((boss = FindBoss()) == null && Time.realtimeSinceStartup < until) await UniTask.Delay(100, DelayType.Realtime);
            if (boss == null) { sb.AppendLine("보스 없음"); return; }

            float t0 = Time.realtimeSinceStartup, nextAb = 2.5f;
            int n = 0;
            while (Time.realtimeSinceStartup - t0 < 10f)
            {
                float t = Time.realtimeSinceStartup - t0;
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"c{n++:00}_{t:0.0}s.png"));
                await UniTask.Delay(150, DelayType.Realtime);
                if (t >= nextAb && nextAb <= 6.5f)
                {
                    nextAb += 2f;
                    Vector3 feet = boss.transform.position;
                    var near = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
                        .Where(r => r.enabled && r.gameObject.activeInHierarchy && !r.transform.IsChildOf(boss.transform)
                                 && r.bounds.min.y < feet.y + 0.6f && r.bounds.max.y > feet.y - 0.6f
                                 && Vector2.Distance(new Vector2(r.bounds.center.x, r.bounds.center.z), new Vector2(feet.x, feet.z)) < 5f)
                        .ToList();
                    props = near.Where(r => !(r.name.Contains("Floor") || r.name.Contains("StairStoneKit") || r.name.Contains("Ground") || r.name.Contains("Carpet"))).ToList();
                    sb.AppendLine($"[{t:0.0}s] 보스 발 {feet} · 둘레 5 m 바닥 높이 렌더러 {near.Count} · 끄는 소품 {props.Count}");
                    foreach (var r in near.Take(30))
                        sb.AppendLine($"  {(props.Contains(r) ? "소품" : "바닥")} {r.name} · 재질 {(r.sharedMaterial != null ? r.sharedMaterial.name : "-")} · 위 {r.bounds.max.y - feet.y:+0.000;-0.000} · 아래 {r.bounds.min.y - feet.y:+0.000;-0.000} · 가운데 {r.bounds.center}");
                    foreach (var r in props) r.enabled = false;
                    await UniTask.NextFrame();
                    ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"c{n++:00}_{t:0.0}s_no_props.png"));
                    await UniTask.Delay(150, DelayType.Realtime);
                    foreach (var r in props) if (r != null) r.enabled = true;
                }
                await UniTask.Delay(200, DelayType.Realtime);
            }
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            foreach (var r in props) if (r != null) r.enabled = true;
            if (data != null) data.AddRecord(seenKey, seenWas - data.GetRecord(seenKey));   // 첫 관람 기록 되돌림
            File.WriteAllText(Path.Combine(dir, "spot.txt"), sb.ToString(), Encoding.UTF8);
            Debug.Log($"[BossSpotFloor] 등장 끝 — {dir}");
        }
    }

    private static void Shot(Camera cam, RenderTexture rt, string path)
    {
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.Destroy(tex);
    }
}
#endif
