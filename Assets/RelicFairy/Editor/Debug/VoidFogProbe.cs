#if UNITY_EDITOR
using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using OccaSoftware.Buto.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 챕터 밖 허공 안개 조율(에디터 전용) — 플레이 중 런 방에서 Buto 볼류메트릭 안개의 빛 세기 · 밀도 · 빛깔을
/// 임시 전역 볼륨(우선순위 50, 런타임 프로파일 — 에셋 무변경)으로 바꿔 가며 같은 구도로 찍는다.
/// 10-01 f5 전주기 시뮬: Ch2 베이지 · Ch3 흰색 · Ch4 연보라 허공이 화면 절반을 채워 바닥 · 몬스터 대비를 낮춘다.
/// 결과: Logs/void_fog/&lt;시각&gt;_&lt;방&gt;/&lt;번호&gt;_&lt;이름&gt;.png · 콘솔 [VoidFog]
/// </summary>
public static class VoidFogProbe
{
    private const float SettleSeconds = 1.6f;   // 안개 시간 누적(temporal)이 자리 잡는 시간

    private struct Step
    {
        public string Name;
        public float  Light;       // < 0 = 덮지 않음
        public float  Density;     // < 0 = 덮지 않음
        public float  Desaturate;  // 0 = 직사광 빛깔 그대로, 0~1 = 회색 쪽으로(빛깔 영향 1로 덮음)
    }

    private static readonly Step[] Steps =
    {
        new Step { Name = "base",            Light = -1f,   Density = -1f },
        new Step { Name = "off",             Light = -1f,   Density = 0f },
        new Step { Name = "L0.5",            Light = 0.5f,  Density = -1f },
        new Step { Name = "L0.4",            Light = 0.4f,  Density = -1f },
        new Step { Name = "L0.5_desat0.35",  Light = 0.5f,  Density = -1f, Desaturate = 0.35f },
        new Step { Name = "L0.4_desat0.35",  Light = 0.4f,  Density = -1f, Desaturate = 0.35f },
    };

    private static CancellationTokenSource s_cts;

    [MenuItem("RelicFairy/Debug/Void Fog Probe (Play)")]
    public static void Run()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[VoidFog] 플레이 모드에서만."); return; }
        s_cts?.Cancel();
        s_cts?.Dispose();
        s_cts = new CancellationTokenSource();
        RunAsync(s_cts.Token).Forget();
    }

    private static async UniTaskVoid RunAsync(CancellationToken ct)
    {
        var runner = UnityEngine.Object.FindFirstObjectByType<Managers>();
        if (runner == null) { Debug.LogWarning("[VoidFog] Managers 없음"); return; }

        string room = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "void_fog", $"{DateTime.Now:MMdd_HHmmss}_{room}");
        Directory.CreateDirectory(dir);

        Color sun = FindSunColor();
        var go = new GameObject("@VoidFogProbe");
        var volume = go.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 50f;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        var fog = profile.Add<ButoVolumetricFog>(false);
        volume.sharedProfile = profile;
        try
        {
            for (int i = 0; i < Steps.Length; i++)
            {
                var s = Steps[i];
                fog.lightIntensity.overrideState = s.Light >= 0f;
                if (s.Light >= 0f) fog.lightIntensity.value = s.Light;
                fog.fogDensity.overrideState = s.Density >= 0f;
                if (s.Density >= 0f) fog.fogDensity.value = s.Density;
                bool desat = s.Desaturate > 0f;
                fog.colorInfluence.overrideState = desat;
                fog.litColor.overrideState       = desat;
                if (desat)
                {
                    float lum = sun.r * 0.299f + sun.g * 0.587f + sun.b * 0.114f;
                    fog.colorInfluence.value = 1f;
                    fog.litColor.value       = Color.Lerp(sun, new Color(lum, lum, lum), s.Desaturate);
                }
                await UniTask.Delay(TimeSpan.FromSeconds(SettleSeconds), DelayType.Realtime, cancellationToken: ct);
                await UniTask.WaitForEndOfFrame(runner, ct);
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(dir, $"{i:00}_{s.Name}.png"), tex.EncodeToPNG());
                UnityEngine.Object.Destroy(tex);
            }
            Debug.Log($"[VoidFog] 완료 — 직사광 {sun} · {dir}");
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (go != null) UnityEngine.Object.Destroy(go);
            UnityEngine.Object.Destroy(profile);
        }
    }

    private static Color FindSunColor()
    {
        if (RenderSettings.sun != null) return RenderSettings.sun.color;
        foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional && l.isActiveAndEnabled) return l.color;
        return Color.white;
    }
}
#endif
