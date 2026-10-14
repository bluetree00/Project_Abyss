#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-08 룬판(UI_GridPanel) 지금 모습 촬영 — 보관함에 룬 3개를 넣고 마지막 것을 「새 룬」으로 열어 한 장 찍는다(개선 설계의 기준 사진).
/// 넣은 룬은 찍은 뒤 보관함에서 다시 뺀다. 결과: Temp/ui_shots/Grid_now.png · 로그 「[GridShot] 끝」.
/// </summary>
public static class GridPanelShotEditor
{
    [MenuItem("RelicFairy/Debug/10-08 룬판 지금 모습 촬영 (런 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null) return;
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb  = new StringBuilder("룬판 촬영\n");
        var inv = GameRunBootstrapper.Instance.Run.ItemInventory;
        var added = new List<RuntimeItemData>();
        Directory.CreateDirectory("Temp/ui_shots");
        try
        {
            string[] elems = { "FIRE", "ICE", "LIGHT" };
            foreach (var entry in Managers.ItemData.GetAllItems().Values.Take(40).Where((_, i) => i % 13 == 5).Take(3))
            {
                var d = RuntimeItemData.FromServer(entry);
                if (d == null) continue;
                d.element = elems[added.Count % elems.Length];
                if (inv.AddToStaging(d)) added.Add(d);
            }
            sb.AppendLine($"  보관함에 넣음 {added.Count}");
            if (UI_GridPanel.Instance == null) Managers.UI?.ShowOverlayUI<UI_GridPanel>();
            UI_GridPanel.Instance?.ShowWithNewItem(added.Count > 0 ? added[^1] : null);
            await UniTask.Delay(1400, ignoreTimeScale: true);
            ScreenCapture.CaptureScreenshot("Temp/ui_shots/Grid_now.png");
            await UniTask.Delay(300, ignoreTimeScale: true);
            typeof(UI_GridPanel).GetMethod("ClosePanel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                                ?.Invoke(UI_GridPanel.Instance, null);
            await UniTask.Delay(600, ignoreTimeScale: true);
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            foreach (var d in added) inv.DiscardFromStaging(d);
            File.WriteAllText("Temp/grid_shot.txt", sb.ToString());
            Debug.Log("[GridShot] 끝\n" + sb);
        }
    }
}
#endif
