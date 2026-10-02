using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// [실측 도구 · 플레이 중 · 런 안에서] 빌드 컨셉 「발동 계열」 화면 — 룬 선택 카드의 계열 칩 · 룬판 머리띠 각인 줄 · HUD 버프 줄.
/// <para>기동 룬 셋을 판에 올린 셈 치고(각인 기동 3 · 1단계) 룬 선택 창에 「기동 하나 더(→ 4 = 2단계)」 · 「필살 첫 장」 · 「계열 없는 룬」을 띄워 찍는다.
/// CDN 업로드 전이면 새 룬 행을 메모리에만 넣고 끝나면 뺀다. 판 · 보관함 · 세이브는 건드리지 않는다(끝나면 되돌림).</para>
/// 결과: Temp/ui_shots/Build_*.png · Temp/build_family_ui_probe.txt
/// </summary>
public static class BuildFamilyUiProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const BindingFlags StaticNonPub = BindingFlags.Static | BindingFlags.NonPublic;

    private static readonly string[] Placed = { "item_t2_trig_dash", "item_t2_dash_fury", "item_t2_trig_perfect" };
    private static readonly string[] Offer  = { "item_t2_trig_dash_strike", "item_t2_trig_crit", "item_t2_heavy_weight" };

    [MenuItem("RelicFairy/Debug/발동 계열 실측/화면 — 룬 선택 칩 · 룬판 줄 · HUD (런 안에서)")]
    private static void Run()
    {
        if (!Application.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null)
        {
            Debug.LogWarning("[BuildFamilyUiProbe] 런 안에서 실행해야 한다.");
            return;
        }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb  = new StringBuilder();
        var run = GameRunBootstrapper.Instance.Run;
        var inv = run.ItemInventory;
        var placedList = (List<RuntimeItemData>)typeof(RunItemInventory).GetField("_placedItems", Inst).GetValue(inv);
        var injected = Inject();
        var added = new List<RuntimeItemData>();
        HideHubGui();
        try
        {
            string[] elems = { "FIRE", "ICE", "LIGHT" };
            for (int i = 0; i < Placed.Length; i++)
            {
                var d = RuntimeItemData.FromServer(Managers.ItemData.GetItem(Placed[i]));
                if (d == null) continue;
                d.element = elems[i];
                placedList.Add(d); added.Add(d);
            }
            run.EffectManager.Rebuild();
            sb.AppendLine($"각인 기동 {BuildImprint.Count(BuildFamily.Mobility)} · {BuildImprint.Stage(BuildFamily.Mobility)}단계");

            // ① 룬 선택 카드 — 계열 칩
            var cands = new List<(RuntimeItemData, ItemSO)>();
            foreach (var id in Offer)
            {
                var d = RuntimeItemData.FromServer(Managers.ItemData.GetItem(id));
                if (d == null) continue;
                d.element = "ELECTRIC";
                cands.Add((d, ItemSORegistry.Find(id)));
            }
            var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_RuneSelectPopup>();
            popup.Setup(cands, inv);
            await UniTask.Delay(900, ignoreTimeScale: true);
            await Shot("Build_RuneSelect");
            foreach (var tr in popup.GetComponentsInChildren<Transform>(true))
                if (tr.name.StartsWith("FamilyChip") && tr.GetComponentInChildren<TMPro.TMP_Text>(true) is { } chip)
                    sb.AppendLine($"칩 「{chip.text}」");
            Managers.UI.CloseAllPopupUI();
            await UniTask.Delay(400, ignoreTimeScale: true);

            // ② 룬판 — 「활성 효과」 머리띠 오른쪽 각인 줄
            if (UI_GridPanel.Instance == null) Managers.UI?.ShowOverlayUI<UI_GridPanel>();
            UI_GridPanel.Instance?.ShowWithNewItem(null);   // 정상 경로로 연다(판 · 캐릭터 정보가 채워진다)
            await UniTask.Delay(900, ignoreTimeScale: true);
            await Shot("Build_Grid");
            var strip = Object.FindFirstObjectByType<BuildImprintStrip>();
            var stripText = strip != null ? strip.transform.Find("Imprints")?.GetComponent<TMPro.TMP_Text>() : null;
            sb.AppendLine($"룬판 각인 줄 「{stripText?.text}」 · 보임 {stripText != null && stripText.gameObject.activeInHierarchy}");
            typeof(UI_GridPanel).GetMethod("ClosePanel", Inst)?.Invoke(UI_GridPanel.Instance, null);
            await UniTask.Delay(900, ignoreTimeScale: true);

            // ③ HUD 버프 줄
            await Shot("Build_Hud");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            foreach (var d in added) placedList.Remove(d);
            run.EffectManager.Rebuild();
            Remove(injected);
            RestoreHubGui();
            File.WriteAllText(Path.Combine("Temp", "build_family_ui_probe.txt"), sb.ToString());
            Debug.Log("[BuildFamilyUiProbe] 완료 — Temp/build_family_ui_probe.txt\n" + sb);
        }
    }

    private static async UniTask Shot(string name)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", StaticNonPub);
        if (m != null) await (UniTask)m.Invoke(null, new object[] { name, 0 });
    }

    private static void HideHubGui()    => typeof(UILayoutRuntimeProbeEditor).GetMethod("HideTestHubGui", StaticNonPub)?.Invoke(null, null);
    private static void RestoreHubGui() => typeof(UILayoutRuntimeProbeEditor).GetMethod("RestoreTestHubGui", StaticNonPub)?.Invoke(null, null);

    // ── 차트(메모리에만) ───────────────────────────────────
    private static List<string> Inject()
    {
        var added = new List<string>();
        var chart = Managers.ItemData;
        if (chart == null) return added;
        var map = (Dictionary<string, List<ItemEntry>>)typeof(ItemDataManager).GetField("_itemById", Inst).GetValue(chart);
        var json = Resources.Load<TextAsset>("ITEM_DATA");
        if (json == null) return added;
        foreach (var e in JsonUtility.FromJson<ItemEntryCollection>(json.text).items)
        {
            string id = string.IsNullOrEmpty(e.item_id) ? e.passive_id : e.item_id;
            if (id == null || !id.StartsWith("item_t2_")) continue;
            if (map.ContainsKey(id) && !added.Contains(id)) continue;
            if (!map.TryGetValue(id, out var list)) { list = new List<ItemEntry>(); map[id] = list; added.Add(id); }
            list.Add(e);
        }
        return added;
    }

    private static void Remove(List<string> added)
    {
        var chart = Managers.ItemData;
        if (chart == null || added.Count == 0) return;
        var map = (Dictionary<string, List<ItemEntry>>)typeof(ItemDataManager).GetField("_itemById", Inst).GetValue(chart);
        foreach (var id in added) map.Remove(id);
    }
}
