#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 11-02 Q1 사용자 「길 자체와 입구가 대칭이 안 맞는다」(사진 1~3 인트로 입구 · 사진 4 챕터 3 보스 길).
/// 플레이어 자리에서 아레나 쪽으로 1 m마다 가슴 높이에서 좌 · 우로 레이 → 벽 · 기둥까지 거리 · 그 칸의 가운데가 걷는 선에서 얼마나 비켰나.
/// 바닥 레이로 그 칸 바닥 조각 이름도. 위에서 내려다본 사진 한 장(걷는 쪽 = 화면 위).
/// 결과: Temp/q1_symmetry/{tag}.txt · {tag}_top.png · 로그 「[Q1Sym] 끝」.
/// </summary>
public static class Q1SymmetryProbeEditor
{
    [MenuItem("RelicFairy/Debug/11-02 Q1 길 · 입구 대칭 — 인트로 (테스트 허브, Play)")]
    private static void Intro()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[Q1Sym] 플레이 중에"); return; }
        IntroAsync().Forget();
    }

    [MenuItem("RelicFairy/Debug/11-02 Q1 길 · 입구 대칭 — 지금 보스방 (보스방 입구, Play)")]
    private static void BossRoom()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[Q1Sym] 플레이 중에"); return; }
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        var spawner = UnityEngine.Object.FindFirstObjectByType<BossSpawner>();
        if (player == null || spawner == null) { Debug.LogWarning("[Q1Sym] 끝 — 플레이어 · 보스 자리 없음"); return; }
        Measure("boss_room", player.transform.position, spawner.transform.position);
    }

    [MenuItem("RelicFairy/Debug/11-02 Q1 길 · 입구 대칭 — 지금 방 플레이어 앞 40 m (Play)")]
    private static void RoomAhead()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[Q1Sym] 플레이 중에"); return; }
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        if (player == null) { Debug.LogWarning("[Q1Sym] 끝 — 플레이어 없음"); return; }
        Vector3 f = player.transform.forward; f.y = 0f;
        Measure("room_ahead", player.transform.position, player.transform.position + f.normalized * 40f);
    }

    [MenuItem("RelicFairy/Debug/11-02 Q1 길 · 입구 대칭 — 챕터 3 보스 앞 길 (보스방, Play)")]
    private static void Ch3Road()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[Q1Sym] 플레이 중에"); return; }
        var road = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                                     .FirstOrDefault(t => t.name == "Road_Boss_Ch3(Clone)");
        if (road == null) { Debug.LogWarning("[Q1Sym] 끝 — 보스 앞 길 없음"); return; }
        // 길 회랑(바닥 가운데 z −55 · 벽 ±4.25)을 앞에서 끝까지 — 길 프리팹 기준
        Measure("ch3_road", road.TransformPoint(new Vector3(0f, 0f, -88f)), road.TransformPoint(new Vector3(0f, 0f, -20f)));
    }

    [MenuItem("RelicFairy/Debug/11-02 Q1 길 · 입구 대칭 — 챕터 3 보스 앞 길 프리팹 (편집 모드)")]
    private static void Ch3RoadPrefab()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[Q1Sym] 편집 모드에서"); return; }
        const string path = "Assets/RelicFairy/Systems/Stage/MapGen/Prefabs/Road_Boss_Ch3.prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            Physics.SyncTransforms();
            var ps = root.scene.GetPhysicsScene();
            var sb = new StringBuilder("Q1 대칭 — Road_Boss_Ch3 프리팹(편집 모드 · 미리보기 물리) · 길 기준 z −88 → −20" + Environment.NewLine);
            sb.AppendLine("  z | 왼 | 오른 | 가운데 비킴(+오른) | 왼쪽 맞음 | 오른쪽 맞음");
            for (float z = -88f; z <= -20f; z += 2f)
            {
                Vector3 o = root.transform.TransformPoint(new Vector3(0f, 1.2f, z));
                Vector3 r = root.transform.right;
                bool hl = ps.Raycast(o, -r, out var L, 30f, ~0, QueryTriggerInteraction.Ignore);
                bool hr = ps.Raycast(o,  r, out var R, 30f, ~0, QueryTriggerInteraction.Ignore);
                float dl = hl ? L.distance : 30f, dr = hr ? R.distance : 30f;
                sb.AppendLine($"  {z,5:0} | {dl,5:0.00} | {dr,5:0.00} | {(dr - dl) * 0.5f,6:+0.00;-0.00} | {(hl ? L.collider.name : "-")} | {(hr ? R.collider.name : "-")}");
            }
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "q1_symmetry");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "ch3_road_prefab.txt"), sb.ToString(), Encoding.UTF8);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        Debug.Log("[Q1Sym] 끝 — ch3_road_prefab");
    }

    private static async UniTaskVoid IntroAsync()
    {
        string key = "game_intro_done_v1_slot" + (RunProgressManager.Instance?.ActiveSlotIndex ?? 0);
        bool hadKey = PlayerPrefs.HasKey(key);
        int  wasVal = PlayerPrefs.GetInt(key, 0);
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
            var player = IntroBootstrapper.Instance?.Player;
            if (player == null) { Debug.LogWarning("[Q1Sym] 끝 — 인트로 플레이어 없음"); return; }
            Measure("intro", player.transform.position, new Vector3(0.3f, -0.56f, 2.3f));   // 모르드레드 액터 발치(11-02 인트로 실측)
        }
        finally
        {
            if (hadKey) PlayerPrefs.SetInt(key, wasVal); else PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
        }
    }

    private static void Measure(string tag, Vector3 from, Vector3 to)
    {
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "q1_symmetry");
        Directory.CreateDirectory(dir);
        Vector3 f = to - from; f.y = 0f;
        float total = f.magnitude;
        f = f.sqrMagnitude > 0.01f ? f.normalized : Vector3.forward;
        Vector3 r = Vector3.Cross(Vector3.up, f);
        var sb = new StringBuilder($"Q1 대칭 — {tag} · 출발 {from} → 목표 {to} · 거리 {total:0.0} m · 걷는 쪽 {f}" + Environment.NewLine);
        sb.AppendLine("  거리 | 왼 | 오른 | 폭 | 가운데 비킴(+오른) | 왼쪽 맞음 | 오른쪽 맞음 | 바닥");
        int steps = Mathf.Min(60, Mathf.CeilToInt(total));
        for (int i = 0; i <= steps; i++)
        {
            Vector3 o = from + f * i + Vector3.up * 1.2f;
            bool hl = Physics.Raycast(o, -r, out var L, 30f, ~0, QueryTriggerInteraction.Ignore);
            bool hr = Physics.Raycast(o,  r, out var R, 30f, ~0, QueryTriggerInteraction.Ignore);
            float dl = hl ? L.distance : 30f, dr = hr ? R.distance : 30f;
            string floor = Physics.Raycast(o + Vector3.up * 2f, Vector3.down, out var D, 10f, ~0, QueryTriggerInteraction.Ignore) ? $"{D.collider.name}@{D.point.y:0.00}" : "-";
            sb.AppendLine($"  {i,3} | {dl,5:0.00} | {dr,5:0.00} | {dl + dr,5:0.00} | {(dr - dl) * 0.5f,6:+0.00;-0.00} | {(hl ? L.collider.name : "-")} | {(hr ? R.collider.name : "-")} | {floor}");
        }

        // 위에서 내려다본 사진 — 걷는 쪽이 화면 위
        var main = Camera.main;
        if (main != null)
        {
            var go = new GameObject("Q1SymCam");
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.CopyFrom(main);
                cam.enabled = false;
                cam.orthographic = true;
                cam.orthographicSize = Mathf.Max(8f, total * 0.55f);
                cam.nearClipPlane = 0.3f;
                cam.farClipPlane = 200f;
                Vector3 mid = (from + to) * 0.5f;
                go.transform.position = new Vector3(mid.x, Mathf.Max(from.y, to.y) + 40f, mid.z);
                go.transform.rotation = Quaternion.LookRotation(Vector3.down, f);
                var rt = new RenderTexture(1600, 1600, 24);
                cam.targetTexture = rt;
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(Path.Combine(dir, tag + "_top.png"), tex.EncodeToPNG());
                UnityEngine.Object.Destroy(tex);
                cam.targetTexture = null;
                rt.Release();
                sb.AppendLine($"  위 사진 — 가운데 {mid} · 반폭 {cam.orthographicSize:0.0} m(화면 가운데 세로선 = 출발 → 목표 선)");
            }
            finally { UnityEngine.Object.Destroy(go); }
        }
        File.WriteAllText(Path.Combine(dir, tag + ".txt"), sb.ToString(), Encoding.UTF8);
        Debug.Log($"[Q1Sym] 끝 — {tag} · {dir}");
    }
}
#endif
