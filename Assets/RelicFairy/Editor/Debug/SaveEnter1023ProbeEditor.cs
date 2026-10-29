#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using Object = UnityEngine.Object;

/// <summary>
/// 10-23 사용자 「세이브 팝업에서 게임을 진입할 때 뒷배경이 날아가면서 잠깐 유니티 기본 배경이 유출된다」 — 진입 순간을 프레임마다 찍는다.
/// 세이브는 건드리지 않는다: 슬롯 고르기 · 초기화 · 씬 로드 없이 <b>화면 쪽 순서만</b> 똑같이 돈다
/// (창 닫기 → 음악 줄이기 → 새 게임 = 시작 영상 · 이어하기 = 로딩 막).
/// 프레임마다 창 알파 · 로비 배경 영상 · 시작 영상 판 · 검은 막 · 로딩 막 알파 · 화면 왼쪽 위 색을 적는다.
/// 결과 Temp/save_enter.txt · Temp/ui_shots/SAVEENTER_{new|load}_NN.jpg · 로그 「[SaveEnter] 끝」.
/// </summary>
public static class SaveEnter1023ProbeEditor
{
    private const string Tag     = "[SaveEnter]";
    private const string OutTxt  = "Temp/save_enter.txt";
    private const string ShotDir = "Temp/ui_shots";
    private const int    Frames  = 36;
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    [MenuItem("RelicFairy/Debug/10-23 세이브 창 진입 프레임 촬영 (플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중에만"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("10-23 세이브 창 → 게임 진입 프레임\n");
        Directory.CreateDirectory(ShotDir);
        try
        {
            if (FindActive<UI_Lobby>() == null)
            {
                AppBootstrapper.Instance?.RequestLoad(Define.Scene.Lobby);
                for (int i = 0; i < 400 && FindActive<UI_Lobby>() == null; i++) await Wait(0.1f);
                await Wait(4f);
            }
            var lobby = FindActive<UI_Lobby>();
            var panel = lobby != null ? typeof(UI_Lobby).GetField("saveSlotPanel", Any)?.GetValue(lobby) as UI_SaveSlotPanel : null;
            if (panel == null) { sb.AppendLine("로비 · 세이브 창 없음"); return; }

            var cam = Camera.main;
            sb.AppendLine($"카메라 {(cam != null ? cam.name : "없음")} · 지우기 {(cam != null ? cam.clearFlags.ToString() : "-")} · 배경색 {(cam != null ? cam.backgroundColor.ToString() : "-")}");
            var back = new StringBuilder();
            for (int i = 0; i < Mathf.Min(3, lobby.transform.childCount); i++) back.Append(lobby.transform.GetChild(i).name).Append(' ');
            sb.AppendLine($"로비 맨 뒤 차례: {back}");

            foreach (var mode in new[] { "new", "load" })
            {
                typeof(UI_Lobby).GetMethod("OnClickStartRun", Any)?.Invoke(lobby, null);
                await Wait(1.5f);
                sb.AppendLine($"\n── {mode} ──");
                await CaptureAsync(sb, mode, lobby, panel);
                await Wait(1.5f);
            }
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText(OutTxt, sb.ToString());
            Debug.Log($"{Tag} 끝\n" + sb);
        }
    }

    private static async UniTask CaptureAsync(StringBuilder sb, string mode, UI_Lobby lobby, UI_SaveSlotPanel panel)
    {
        var runner = Object.FindFirstObjectByType<Managers>();
        var vp     = UIRootBootstrapper.Instance != null ? UIRootBootstrapper.Instance.GetComponentInChildren<GameStartVideoPlayer>(true) : null;
        var intro  = lobby.GetComponent<LobbyIntroPlayer>();
        var bg     = lobby.transform.Find("BG")?.GetComponent<RawImage>();
        var loading = UI_SceneLoading.Instance;
        var cts    = new CancellationTokenSource();
        var shots  = new List<Texture2D>(Frames);

        // OnSlotStartClicked와 같은 순서(슬롯 · 세이브는 건드리지 않음) — 창은 닫지 않고 덮인 채로 떠난다(LeaveCovered)
        typeof(UI_SaveSlotPanel).GetMethod("LeaveCovered", Any)?.Invoke(panel, null);
        Managers.Sound.FadeOutBgmAsync().Forget();
        if (mode == "new") { if (vp != null) vp.PlayAsync(cts.Token).Forget(); }
        else if (loading != null) loading.ShowAsync().Forget();

        float t0 = Time.realtimeSinceStartup;
        for (int f = 0; f < Frames; f++)
        {
            await UniTask.WaitForEndOfFrame(runner);
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            shots.Add(tex);
            var c = tex.GetPixel(tex.width / 10, tex.height * 9 / 10);
            var pcg = panel.GetComponent<CanvasGroup>();
            var vpRaw = vp != null ? vp.GetComponentInChildren<RawImage>(true) : null;
            var vpPlayer = vp != null ? vp.GetComponentInChildren<VideoPlayer>(true) : null;
            var cover = vp != null ? typeof(GameStartVideoPlayer).GetField("_coverOverlay", Any)?.GetValue(vp) as Image : null;
            var lcg = loading != null ? typeof(UI_SceneLoading).GetField("canvasGroup", Any)?.GetValue(loading) as CanvasGroup : null;
            sb.AppendLine($"  {f:00} {(Time.realtimeSinceStartup - t0) * 1000f,5:0}ms · 창 {(panel.gameObject.activeInHierarchy ? (pcg != null ? pcg.alpha.ToString("0.00") + (pcg.interactable ? "" : "(막힘)") : "켜짐") : "꺼짐")}" +
                          $" · 로비 {lobby.gameObject.activeInHierarchy} · 로비 배경 {(bg != null ? $"{bg.enabled}/{bg.color.a:0.00}/영상 {(intro != null && intro.IsPlaying)}" : "-")}" +
                          $" · 시작 영상 판 {(vpRaw != null ? vpRaw.enabled.ToString() : "-")}/준비 {(vpPlayer != null && vpPlayer.isPrepared)}/재생 {(vpPlayer != null && vpPlayer.isPlaying)}/프레임 {(vpPlayer != null ? vpPlayer.frame : -9)} · 검은 막 {(cover != null && cover.gameObject.activeInHierarchy)}" +
                          $" · 로딩 {(lcg != null ? lcg.alpha.ToString("0.00") : "-")} · 왼쪽 위 색 {c.r:0.00},{c.g:0.00},{c.b:0.00}");
        }

        cts.Cancel();
        if (vp != null)
        {
            vp.GetComponentInChildren<VideoPlayer>(true)?.Stop();
            vp.HideNow();
        }
        if (mode == "load" && loading != null) await loading.HideAsync();
        panel.Close();   // 실측 뒤 로비로 — 다음 경우를 위해 닫는다(실제로는 로비가 사라진다)
        for (int i = 0; i < shots.Count; i++)
        {
            File.WriteAllBytes(Path.Combine(ShotDir, $"SAVEENTER_{mode}_{i:00}.jpg"), shots[i].EncodeToJPG(70));
            Object.Destroy(shots[i]);
        }
        cts.Dispose();
    }

    private static T FindActive<T>() where T : Component
    {
        foreach (var x in Object.FindObjectsByType<T>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (x != null && x.gameObject.activeInHierarchy) return x;
        return null;
    }

    private static UniTask Wait(float s) => UniTask.Delay((int)(s * 1000), DelayType.Realtime);
}
#endif
