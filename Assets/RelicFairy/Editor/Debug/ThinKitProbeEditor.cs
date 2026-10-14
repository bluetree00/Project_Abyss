#if UNITY_EDITOR
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 10-07 톤 v2 부품 실측(플레이 중, 아무 씬) — 임시 캔버스에 테 4개(Idle · Active · Ready 네모 · Ready 마름모)와
/// 글자 세 굵기를 띄우고, Idle 네모를 Ready로 올려 켜짐 연출을 0.06 · 0.14 · 0.30 · 1.2초에 찍는다. 끝나면 캔버스를 지운다.
/// 결과: Temp/ui_shots/ThinKit_*.png · 로그 「[ThinKit] 끝」.
/// </summary>
public static class ThinKitProbeEditor
{
    [MenuItem("RelicFairy/Debug/10-07 톤 v2 부품 실측 (플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) return;
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("톤 v2 부품 실측\n");
        Directory.CreateDirectory("Temp/ui_shots");
        var go = new GameObject("@ThinKitProbe");
        try
        {
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = UISortingOrder.RunBoundary + 5;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            var bg = ShopUIStyle.MakeImage(go.transform, "Bg", UITheme.Thin.Backdrop);
            bg.rectTransform.anchorMin = Vector2.zero; bg.rectTransform.anchorMax = Vector2.one;
            bg.rectTransform.offsetMin = bg.rectTransform.offsetMax = Vector2.zero;

            UIThinFrame Slot(float x, ThinShape shape, float size, ThinFrameState s)
            {
                var host = ShopUIStyle.MakeImage(go.transform, "Host", UITheme.Thin.GlassFill).rectTransform;
                host.anchorMin = host.anchorMax = new Vector2(0.5f, 0.5f);
                host.anchoredPosition = new Vector2(x, 120f);
                host.sizeDelta = new Vector2(size, size);
                var f = UIThinFrame.Create(host, shape, new Vector2(size, size));
                f.SetState(s, animate: false);
                return f;
            }
            var idle = Slot(-420f, ThinShape.Square, 112f, ThinFrameState.Idle);
            Slot(-220f, ThinShape.Square, 112f, ThinFrameState.Active);
            Slot(-20f, ThinShape.Square, 112f, ThinFrameState.Ready);
            Slot(220f, ThinShape.Diamond, 148f, ThinFrameState.Ready);

            float y = -60f;
            foreach (var (w, size, label) in new[] { (UIWeight.Light, UITheme.Thin.Display, "47:12"),
                                                      (UIWeight.Regular, UITheme.Thin.Lead, "공격력 +14 · 피격받지 않은 상태 4초 유지 시 치명타 확률 +12%"),
                                                      (UIWeight.Medium, UITheme.Thin.Small, "보관함 0/6 · 배치 0") })
            {
                var t = ShopUIStyle.MakeText(go.transform, "Type", size, FontStyles.Normal, TextAlignmentOptions.Center, UITheme.Thin.Ink);
                UITheme.StyleThinText(t, w, size, UITheme.Thin.Ink);
                t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                t.rectTransform.anchoredPosition = new Vector2(0f, y);
                t.rectTransform.sizeDelta = new Vector2(1400f, size * 1.4f);
                t.text = label;
                sb.AppendLine($"  {w} 글꼴 = {t.font?.name}");
                y -= size * 1.5f;
            }

            await UniTask.Delay(400, ignoreTimeScale: true);
            ScreenCapture.CaptureScreenshot("Temp/ui_shots/ThinKit_0_rest.png");
            await UniTask.Delay(200, ignoreTimeScale: true);
            float t0 = Time.unscaledTime;
            idle.SetState(ThinFrameState.Ready, animate: true);
            int k = 1;
            foreach (float at in new[] { 0.06f, 0.14f, 0.30f, 1.2f })
            {
                float wait = t0 + at - Time.unscaledTime;
                if (wait > 0f) await UniTask.Delay(System.TimeSpan.FromSeconds(wait), ignoreTimeScale: true);
                ScreenCapture.CaptureScreenshot($"Temp/ui_shots/ThinKit_{k++}_{at:0.00}.png");
            }
            await UniTask.Delay(300, ignoreTimeScale: true);
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            if (go != null) Object.Destroy(go);
            File.WriteAllText("Temp/thinkit_probe.txt", sb.ToString());
            Debug.Log("[ThinKit] 끝\n" + sb);
        }
    }
}
#endif
