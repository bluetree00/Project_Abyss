#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 10-29 보스 바 무적 표시 실측 — 보스 없이 보스 바만 띄워 무적 켜짐 · 꺼짐을 찍고 덮개 상태(켜짐 · 크기 · 색 · 부모 · 형제 순서)를 적는다.
/// (2a 화룡 실측 사진에서 무적 0.5초 뒤에도 채움 색이 평소와 같았다.)
/// 결과 Temp/fix1029_bossbar.txt · Temp/ui_shots/F29B_bar_*.png · 로그 「[Fix29B] 끝」.
/// </summary>
public static class Fix1029BossBarInvulProbeEditor
{
    private const string Tag = "[Fix29B]";
    private const string ShotDir = "Temp/ui_shots";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    [MenuItem("RelicFairy/Debug/10-29 보스 바 무적 표시 — 켜짐 · 꺼짐 촬영 (플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중에"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("10-29 보스 바 무적 표시\n");
        var presenter = Object.FindFirstObjectByType<HudPresenter>(FindObjectsInactive.Include);
        var view = presenter != null ? typeof(HudPresenter).GetField("view", Any)?.GetValue(presenter) : null;
        var panel = view?.GetType().GetProperty("BossPanel", Any)?.GetValue(view) as BossPanelView;
        if (panel == null) { Finish(sb.AppendLine("보스 바를 못 찾았다")); return; }

        presenter.SetMode(HUDIds.Mode.Boss);
        panel.gameObject.SetActive(true);
        panel.ResetPageFx();
        panel.Init(16000, "화룡");
        panel.SetHP(10748, 16000);
        await UniTask.Delay(600, DelayType.Realtime);
        Directory.CreateDirectory(ShotDir);
        ScreenCapture.CaptureScreenshot($"{ShotDir}/F29B_bar_off.png");
        await UniTask.Delay(300, DelayType.Realtime);

        panel.SetInvulnerable(true);
        await UniTask.Delay(700, DelayType.Realtime);
        Dump(sb, panel, "켜짐 0.7초");
        ScreenCapture.CaptureScreenshot($"{ShotDir}/F29B_bar_on.png");
        await UniTask.Delay(500, DelayType.Realtime);
        ScreenCapture.CaptureScreenshot($"{ShotDir}/F29B_bar_on2.png");
        await UniTask.Delay(300, DelayType.Realtime);

        panel.SetInvulnerable(false);
        await UniTask.Delay(400, DelayType.Realtime);
        Dump(sb, panel, "꺼짐");
        Finish(sb);
    }

    private static void Dump(StringBuilder sb, BossPanelView panel, string when)
    {
        var ov = typeof(BossPanelView).GetField("_invulnerableOverlay", Any)?.GetValue(panel) as Image;
        var name = typeof(BossPanelView).GetField("nameText", Any)?.GetValue(panel) as TMPro.TMP_Text;
        if (ov == null) { sb.AppendLine($"[{when}] 덮개 없음 · 이름 「{name?.text}」"); return; }
        var rt = (RectTransform)ov.transform;
        var parent = rt.parent as RectTransform;
        sb.AppendLine($"[{when}] 덮개 켜짐 {ov.enabled} · 활성 {ov.gameObject.activeInHierarchy} · 색 {ov.color} · 크기 {rt.rect.size} · 앵커 {rt.anchorMin}~{rt.anchorMax}");
        sb.AppendLine($"    부모 {parent?.name}({parent?.rect.size}) · 형제 {rt.GetSiblingIndex()}/{parent?.childCount} · 컬 {ov.canvasRenderer.cull} · 알파 {ov.canvasRenderer.GetAlpha()} · 이름 「{name?.text}」");
        if (parent != null)
            for (int i = 0; i < parent.childCount; i++)
            {
                var c = parent.GetChild(i);
                var img = c.GetComponent<Image>();
                sb.AppendLine($"      자식 {i} {c.name} · 켜짐 {(img != null ? img.enabled.ToString() : "-")} · 활성 {c.gameObject.activeSelf}");
            }
    }

    private static void Finish(StringBuilder sb)
    {
        File.WriteAllText("Temp/fix1029_bossbar.txt", sb.ToString());
        Debug.Log($"{Tag} 끝\n{sb}");
    }
}
#endif
