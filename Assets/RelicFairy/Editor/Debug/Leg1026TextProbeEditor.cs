#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 10-26 사용자 「전설 룬은 텍스트가 길어서 넘치는 문제」 — 지금 불러온 아이템 표(CDN · 폴백)의 전설 룬 전부(10-24 도구는 첫 3장만 봤다)를
/// ① 룬 선택 창 카드(3장씩) ② 룬판 오른쪽 정보판에 띄워 글 칸마다 넘침(높이 · 잘림)을 잰다. 세이브 · 판 · 보관함은 안 건드린다(사본 카드).
/// 결과 Temp/leg1026.txt · Temp/ui_shots/LEG26_*.png · 로그 「[Leg26] 끝」.
/// </summary>
public static class Leg1026TextProbeEditor
{
    private const string Tag = "[Leg26]";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem("RelicFairy/Debug/10-26 전설 룬 글 넘침 — 룬 선택 카드 · 룬판 정보판 전부 (런 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run == null) { Debug.LogWarning($"{Tag} 런 안에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("10-26 전설 룬 글 넘침\n");
        int pass = 0, total = 0;
        var inv = GameRunBootstrapper.Instance.Run.ItemInventory;
        UI_GridPanel panel = null;
        try
        {
            var legends = new List<(RuntimeItemData d, ItemSO so)>();
            foreach (var kv in Managers.ItemData.GetAllItems())
            {
                var d = RuntimeItemData.FromServer(kv.Value);
                if (d == null || d.rarity != ItemRarity.Legendary) continue;
                d.EnsureElement();
                legends.Add((d, ItemSORegistry.Find(d.itemId)));
            }
            sb.AppendLine($"전설 룬 {legends.Count}개");

            // ① 룬 선택 창 — 3장씩
            for (int i = 0; i < legends.Count; i += 3)
            {
                var batch = legends.Skip(i).Take(3).ToList();
                var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_RuneSelectPopup>();
                popup.Setup(batch, inv);
                await UniTask.Delay(600, DelayType.Realtime);
                typeof(UI_RuneSelectPopup).GetMethod("SkipReveal", Inst)?.Invoke(popup, null);
                await UniTask.Delay(900, DelayType.Realtime);
                var bad = Overflows(popup.transform);
                sb.AppendLine($"\n■ 룬 선택 카드 {i / 3 + 1} — {string.Join(" · ", batch.Select(b => b.d.displayName))}");
                foreach (var line in bad) sb.AppendLine("  넘침 " + line);
                total++; if (bad.Count == 0) pass++;
                sb.AppendLine($"CHECK 룬 선택 카드 {i / 3 + 1} 글이 칸 안: {bad.Count == 0}");
                if (bad.Count > 0 || i == 0) await Shot($"LEG26_select_{i / 3 + 1}");
                Managers.UI.CloseAllPopupUI();
                await UniTask.Delay(300, DelayType.Realtime);
            }

            // ② 룬판 정보판 — 고른 룬 상세
            if (UI_GridPanel.Instance == null) Managers.UI?.ShowOverlayUI<UI_GridPanel>();
            panel = UI_GridPanel.Instance;
            panel.ShowWithNewItem(null);
            await UniTask.Delay(1200, DelayType.Realtime);
            var info = typeof(UI_GridPanel).GetField("_itemInfoPanel", Inst)?.GetValue(panel) as ItemInfoPanel;
            if (info == null) sb.AppendLine("정보판 없음");
            else
            {
                int shots = 0;
                foreach (var (d, _) in legends)
                {
                    info.ShowItem(d, isNew: false);
                    await UniTask.Delay(250, DelayType.Realtime);
                    var bad = Overflows(info.transform);
                    total++; if (bad.Count == 0) pass++;
                    sb.AppendLine($"CHECK 정보판 「{d.displayName}」 글이 칸 안: {bad.Count == 0}");
                    foreach (var line in bad) sb.AppendLine("  넘침 " + line);
                    if (bad.Count > 0 && shots < 3) { await Shot($"LEG26_info_{++shots}"); }
                }
                if (shots == 0) await Shot("LEG26_info_ok");
            }
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            Managers.UI?.CloseAllPopupUI();
            if (panel != null && panel.IsOpen) typeof(UI_GridPanel).GetMethod("ClosePanel", Inst)?.Invoke(panel, null);
            sb.AppendLine($"CHECK 합계 {pass}/{total}");
            File.WriteAllText("Temp/leg1026.txt", sb.ToString());
            Debug.Log($"{Tag} 끝\n" + sb);
        }
    }

    /// <summary>켜진 글 칸 중 넘친 것 — 높이가 칸을 넘음 · 잘림(말줄임) · TMP 넘침 표시.</summary>
    private static List<string> Overflows(Transform root)
    {
        var bad = new List<string>();
        Canvas.ForceUpdateCanvases();
        foreach (var t in root.GetComponentsInChildren<TMP_Text>())
        {
            if (!t.isActiveAndEnabled || string.IsNullOrWhiteSpace(t.text)) continue;
            t.ForceMeshUpdate();
            var r = t.rectTransform.rect;
            bool wraps = t.textWrappingMode != TextWrappingModes.NoWrap;
            bool tall  = wraps && t.preferredHeight > r.height + 2f;
            bool wide  = !wraps && t.preferredWidth > r.width + 2f && t.overflowMode != TextOverflowModes.Overflow;
            if (!(t.isTextOverflowing || t.isTextTruncated || tall || wide)) continue;
            string plain = t.GetParsedText();
            bad.Add($"{t.transform.parent?.name}/{t.name} 칸 {r.width:0}×{r.height:0} · 필요 {t.preferredWidth:0}×{t.preferredHeight:0} · 크기 {t.fontSize:0.#} · 「{plain.Substring(0, Mathf.Min(28, plain.Length))}…」");
        }
        return bad;
    }

    private static UniTask Shot(string name)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, 0 }) : UniTask.CompletedTask;
    }
}
#endif
