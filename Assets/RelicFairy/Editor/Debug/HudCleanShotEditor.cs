#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-08 HUD 시안 바탕 촬영(런 중) — 지금 HUD 그대로 한 장, 화면 UI(오버레이 · 카메라 캔버스)를 끄고 한 장(명조형 HUD 시안을 얹을 깨끗한 장면).
/// 머리 위 이름표 같은 월드 캔버스는 장면의 일부라 남긴다. HudPresenter.SetVisible은 전투 칸을 안 끈다(1차 실측).
/// 결과: Temp/ui_shots/HUDREF_with.png · HUDREF_clean.png · 로그 「[HudClean] 끝」.
/// </summary>
public static class HudCleanShotEditor
{
    [MenuItem("RelicFairy/Debug/10-08 HUD 시안 바탕 촬영 (런 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) return;
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        string note = "";
        Directory.CreateDirectory("Temp/ui_shots");
        var hidden = new List<Canvas>();
        try
        {
            GameRunBootstrapper.Instance?.Run?.Player?.SetInvincible(30f);
            ScreenCapture.CaptureScreenshot("Temp/ui_shots/HUDREF_with.png");
            await UniTask.Delay(300, ignoreTimeScale: true);
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (c.isRootCanvas && c.enabled && c.renderMode != RenderMode.WorldSpace) { c.enabled = false; hidden.Add(c); }
            await UniTask.Delay(400, ignoreTimeScale: true);
            ScreenCapture.CaptureScreenshot("Temp/ui_shots/HUDREF_clean.png");
            await UniTask.Delay(300, ignoreTimeScale: true);
        }
        catch (System.Exception e) { note = "예외: " + e.Message; }
        finally
        {
            foreach (var c in hidden) if (c != null) c.enabled = true;
            Debug.Log("[HudClean] 끝 " + note);
        }
    }
}
#endif
