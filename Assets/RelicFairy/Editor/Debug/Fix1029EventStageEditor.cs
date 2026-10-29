#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

/// <summary>
/// 10-29 「이벤트 방의 이벤트 오브젝트 위치들이 적절한지」 — ① 제단 · 신탁 문 소품 Addressables 등록 ② 종류별 배치 실측.
/// 실측: 문 고정 → 자동 진행 → 그 이벤트 방에 들어서면 멈추고 무대 · 마커마다 벽(걸을 수 있는 면 가장자리)까지 거리 · 무대 가운데까지 거리를 적고
/// 게임 화면 · 위에서 본 사진을 남긴다. 결과 Temp/fix1029_g_*.txt · Temp/ui_shots/F29G_*.png · 로그 「[Fix29G] 끝」.
/// </summary>
public static class Fix1029EventStageEditor
{
    private const string Tag = "[Fix29G]";
    private const string ShotDir = "Temp/ui_shots";
    private const string AutoRoot = "RelicFairy/Debug/런 구조 자동 실측/";
    private const string Gothic = "Assets/RelicFairy/_Imported/Gothic_Interior/Environment/Asset/Prefabs/";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

    private static double s_until;
    private static string s_name;

    // ── ① 소품 등록 ─────────────────────────────────────────

    [MenuItem("RelicFairy/Stage/10-29 이벤트 소품 Addressables 등록 (Edit)")]
    private static void RegisterProps()
    {
        Register(Gothic + "SM_Altar_01a.prefab",     "EventProp/Altar");
        Register(Gothic + "SM_StoneArch_01a.prefab", "EventProp/OracleArch");
        AssetDatabase.SaveAssets();
    }

    private static void Register(string path, string address)
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        string guid = AssetDatabase.AssetPathToGUID(path);
        if (settings == null || string.IsNullOrEmpty(guid)) { Debug.LogWarning($"{Tag} 등록 실패 {path}"); return; }
        var e = settings.CreateOrMoveEntry(guid, settings.DefaultGroup, false, false);
        e.address = address;
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, e, true);
        Debug.Log($"{Tag} 등록 {address} ← {path}");
    }

    // ── ② 배치 실측 ─────────────────────────────────────────

    [MenuItem("RelicFairy/Debug/10-29 G 이벤트 방 배치 실측 (테스트 허브, 플레이 중)/제물 제단")] private static void Sacrifice() => Begin("sacrifice", "ch1_event_sacrifice_01");
    [MenuItem("RelicFairy/Debug/10-29 G 이벤트 방 배치 실측 (테스트 허브, 플레이 중)/신탁")]     private static void Oracle()    => Begin("oracle", "ch1_event_oracle_01");
    [MenuItem("RelicFairy/Debug/10-29 G 이벤트 방 배치 실측 (테스트 허브, 플레이 중)/보물 창고")] private static void Vault()     => Begin("vault", "ch1_event_vault_01");
    [MenuItem("RelicFairy/Debug/10-29 G 이벤트 방 배치 실측 (테스트 허브, 플레이 중)/욕심의 상자")] private static void Greed()   => Begin("greed", "ch1_event_greed_01");
    [MenuItem("RelicFairy/Debug/10-29 G 이벤트 방 배치 실측 (테스트 허브, 플레이 중)/룬 기억")]   private static void Memory()    => Begin("memory", "ch1_event_memory_01");

    private static void Begin(string name, string poolKey)
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중에"); return; }
        s_name = name;
        EventMinigameDebugMenu.ForceDoor(poolKey);
        EditorApplication.ExecuteMenuItem(AutoRoot + "시작 (테스트 허브에서, 플레이 중)");
        s_until = EditorApplication.timeSinceStartup + 240.0;
        EditorApplication.update -= Watch;
        EditorApplication.update += Watch;
    }

    private static void Watch()
    {
        if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > s_until)
        {
            EditorApplication.update -= Watch;
            EventMinigameDebugMenu.ForceDoor("");
            Debug.Log($"{Tag} 끝 {s_name} — 이벤트 방에 닿지 못했다(시간 초과)");
            return;
        }
        var ch = Object.FindFirstObjectByType<WorldInteractionChallenge>();
        if (ch == null && EventMinigame.Active == null) return;
        EditorApplication.update -= Watch;
        EditorApplication.ExecuteMenuItem(AutoRoot + "중지·기록");
        EventMinigameDebugMenu.ForceDoor("");
        MeasureAsync(ch, EventMinigame.Active).Forget();
    }

    private static async UniTaskVoid MeasureAsync(WorldInteractionChallenge ch, EventMinigame game)
    {
        var sb = new StringBuilder($"10-29 G 이벤트 방 배치 — {s_name}\n");
        Directory.CreateDirectory(ShotDir);
        try
        {
            await Wait(4f);   // 입장 연출 · 소품이 솟는 시간
            var player = Managers.Player?.PlayerTransform;
            Vector3 center;
            if (ch != null)
            {
                center = (Vector3)(typeof(WorldInteractionChallenge).GetField("_stage", Any)?.GetValue(ch) ?? ch.transform.position);
                float r = (float)(typeof(WorldInteractionChallenge).GetField("_stageRadius", Any)?.GetValue(ch) ?? 0f);
                sb.AppendLine($"무대 {center} · 반경 {r:0.0} m · 방 뿌리 {ch.transform.position} · 플레이어 {player?.position}");
                var markers = (typeof(WorldInteractionChallenge).GetField("_markers", Any)?.GetValue(ch) as IList)?.Cast<object>().ToList();
                foreach (var m in markers ?? new System.Collections.Generic.List<object>())
                {
                    var pos   = (Vector3)m.GetType().GetField("Pos").GetValue(m);
                    string lb = (m.GetType().GetField("Label").GetValue(m) as TMP_Text)?.text ?? "";
                    float edge = NavMesh.FindClosestEdge(pos, out var e, MinigameArena.Filter) ? e.distance : -1f;
                    bool onMesh = NavMesh.SamplePosition(pos, out var hit, 0.3f, MinigameArena.Filter);
                    sb.AppendLine($"  「{lb}」 {pos} · 무대까지 {Flat(pos - center):0.0} m · 벽까지 {edge:0.00} m · 걸을 수 있는 면 {onMesh}");
                }
                var props = ch.transform.Find("ChallengeProps");
                int models = props == null ? 0 : props.GetComponentsInChildren<MeshRenderer>(true).Count(x => x.GetComponent<TMP_Text>() == null);
                sb.AppendLine($"  세운 모델 렌더러 {models}개");
            }
            else
            {
                center = (Vector3)(typeof(EventMinigame).GetField("Center", Any)?.GetValue(game) ?? game.transform.position);
                float r = (float)(typeof(EventMinigame).GetField("Radius", Any)?.GetValue(game) ?? 0f);
                sb.AppendLine($"놀이 {game.GetType().Name} · 판 가운데 {center} · 반경 {r:0.0} m · 플레이어 {player?.position}");
            }
            await ShotAsync($"F29G_{s_name}_game");
            TopDown(center, 15f, $"F29G_{s_name}_top");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText($"Temp/fix1029_g_{s_name}.txt", sb.ToString());
            Debug.Log($"{Tag} 끝 {s_name}");
        }
    }

    // ── ③ 룬 획득 팝업 사진(10-29 사용자 「룬 획득 팝업을 켜보고 아쉬운 건 힉스필드로」) ──

    [MenuItem("RelicFairy/Debug/10-29 E 룬 획득 팝업 사진 — 보상 늦게 받기로 자동 진행 (테스트 허브, 플레이 중)")]
    private static void RunePopupShot()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중에"); return; }
        EditorApplication.ExecuteMenuItem(AutoRoot + "시작 — 출구 보류 확인 (처치 위치 맵 밖 · 보상 늦게 받기)");
        s_until = EditorApplication.timeSinceStartup + 240.0;
        EditorApplication.update -= WatchReward;
        EditorApplication.update += WatchReward;
    }

    private static void WatchReward()
    {
        if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > s_until)
        {
            EditorApplication.update -= WatchReward;
            Debug.Log($"{Tag} 끝 rune — 보상에 닿지 못했다");
            return;
        }
        var trig = Object.FindFirstObjectByType<ClearRewardTrigger>();
        if (trig == null) return;
        EditorApplication.update -= WatchReward;
        EditorApplication.ExecuteMenuItem(AutoRoot + "중지·기록");
        RunePopupAsync(trig).Forget();
    }

    private static async UniTaskVoid RunePopupAsync(ClearRewardTrigger trig)
    {
        Directory.CreateDirectory(ShotDir);
        await Wait(1.5f);
        typeof(ClearRewardTrigger).GetMethod("OpenRewardFlowAsync", Any)?.Invoke(trig, null);
        await Wait(4.5f);   // 등장 연출이 끝나도록
        await ShotAsync("F29E_rune_popup_full");
        Debug.Log($"{Tag} 끝 rune");
    }

    /// <summary>위에서 내려다본 사진 — 임시 카메라(정사영)로 따로 그린다(게임 카메라는 건드리지 않는다).</summary>
    private static void TopDown(Vector3 center, float half, string name)
    {
        var go = new GameObject("Fix29G_TopCam");
        try
        {
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true; cam.orthographicSize = half;
            cam.transform.SetPositionAndRotation(center + Vector3.up * 40f, Quaternion.Euler(90f, 0f, 0f));
            cam.nearClipPlane = 0.3f; cam.farClipPlane = 80f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
            var rt = new RenderTexture(1024, 1024, 24);
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(1024, 1024, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1024, 1024), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(ShotDir, name + ".png"), tex.EncodeToPNG());
            cam.targetTexture = null;
            Object.Destroy(tex); rt.Release(); Object.Destroy(rt);
        }
        finally { Object.Destroy(go); }
    }

    private static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }
    private static UniTask Wait(float s) => UniTask.Delay(System.TimeSpan.FromSeconds(s), DelayType.Realtime);

    private static async UniTask ShotAsync(string name)
    {
        var runner = Object.FindFirstObjectByType<Managers>();
        if (runner == null) return;
        await UniTask.WaitForEndOfFrame(runner);
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        if (tex == null) return;
        File.WriteAllBytes(Path.Combine(ShotDir, name + ".png"), tex.EncodeToPNG());
        Object.Destroy(tex);
    }
}
#endif
