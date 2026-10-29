#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 10-24 전설 룬 개편 — 화면 확인(e2). 세이브 · 판은 건드리지 않는다(카드는 사본).
/// ① 새 카드 글 미리보기: Temp/leg_desc.txt(한 줄 「id|이름|설명」)의 글을 전설 룬 사본에 입혀 룬 선택 창 3장으로 띄우고, 글이 칸을 넘치는지 잰다.
/// 결과 Temp/leg1024_cards.txt · Temp/ui_shots/LEG24_cards.png · 로그 「[Leg24] 끝」.
/// </summary>
public static class Leg1024UiProbeEditor
{
    private const string Tag = "[Leg24]";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem("RelicFairy/Debug/10-24 전설 룬 새 카드 글 미리보기 (Temp/leg_desc.txt · 런 중)")]
    private static void RunCards()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run == null) { Debug.LogWarning($"{Tag} 런 안에서"); return; }
        CardsAsync().Forget();
    }

    private static async UniTaskVoid CardsAsync()
    {
        var sb = new StringBuilder("10-24 전설 룬 새 카드 글\n");
        int pass = 0, total = 0;
        void Check(string what, bool ok) { total++; if (ok) pass++; sb.AppendLine($"CHECK {ok} — {what}"); }
        try
        {
            var picks = new List<(RuntimeItemData, ItemSO)>();
            var descs = new List<string>();
            foreach (var line in File.ReadAllLines("Temp/leg_desc.txt", Encoding.UTF8))
            {
                var parts = line.Split('|');
                if (parts.Length < 3 || !Managers.ItemData.GetAllItems().TryGetValue(parts[0].Trim(), out var entries)) continue;
                var d = RuntimeItemData.FromServer(entries);
                if (d == null) continue;
                d.displayName = parts[1].Trim();
                if (d.effects != null && d.effects.Count > 0) d.effects[0].description = parts[2].Trim();
                picks.Add((d, ItemSORegistry.Find(d.itemId)));
                descs.Add(parts[2].Trim());
                if (picks.Count == 3) break;
            }
            sb.AppendLine($"카드 {picks.Count}장");
            var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_RuneSelectPopup>();
            popup.Setup(picks, GameRunBootstrapper.Instance.Run.ItemInventory);
            await UniTask.Delay(7000, DelayType.Realtime);   // 내려앉고 뒤집힐 때까지(전설은 장마다 멈춘다 — 4.5초엔 셋째 장이 뒤집히는 중이었다)
            foreach (var t in popup.GetComponentsInChildren<TMP_Text>(true))
            {
                if (!t.gameObject.activeInHierarchy || string.IsNullOrEmpty(t.text)) continue;
                string plain = t.GetParsedText();
                bool mine = false;
                foreach (var d in descs) if (plain.Length > 20 && d.Contains(plain.Substring(0, 12))) mine = true;
                if (!mine) continue;
                t.ForceMeshUpdate();
                var r = t.rectTransform.rect;
                sb.AppendLine($"    「{plain.Substring(0, Mathf.Min(24, plain.Length))}…」 칸 {r.width:0}×{r.height:0} · 필요 높이 {t.preferredHeight:0} · 크기 {t.fontSize:0.#} · 넘침 {t.isTextOverflowing}");
                Check($"설명 글이 칸 안에 든다({plain.Substring(0, Mathf.Min(10, plain.Length))}…)", !t.isTextOverflowing && t.preferredHeight <= r.height + 1f);
            }
            await Shot("LEG24_cards");
            Managers.UI.CloseAllPopupUI();
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            sb.Insert(0, $"통과 {pass}/{total}\n");
            File.WriteAllText("Temp/leg1024_cards.txt", sb.ToString());
            Debug.Log($"{Tag} 끝\n" + sb);
        }
    }

    // ── ② 94 뒤 — 상세 카드 각성 줄 · 각성 띠 · HUD 자원 ──
    [MenuItem("RelicFairy/Debug/10-24 전설 화면 — 상세 카드 각성 줄 · 각성 띠 · HUD 자원 (장비 형 94 뒤, 런 중)")]
    private static void RunScreens()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run == null) { Debug.LogWarning($"{Tag} 런 안에서(장비 형 94 뒤)"); return; }
        ScreensAsync().Forget();
    }

    private static async UniTaskVoid ScreensAsync()
    {
        var sb = new StringBuilder("10-24 전설 화면\n");
        int pass = 0, total = 0;
        void Check(string what, bool ok) { total++; if (ok) pass++; sb.AppendLine($"CHECK {ok} — {what}"); }
        try
        {
            var inv = GameRunBootstrapper.Instance.Run.ItemInventory;
            RuntimeItemData item = null;
            foreach (var d in inv.PlacedItems) if (d != null && d.rarity == ItemRarity.Legendary) { item = d; break; }
            if (item == null) { sb.AppendLine("판에 전설 룬이 없다 — 장비 형 94를 먼저"); return; }
            string zone = item.element;
            if (UI_GridPanel.Instance == null) Managers.UI?.ShowOverlayUI<UI_GridPanel>();
            var panel = UI_GridPanel.Instance;
            var info = typeof(UI_GridPanel).GetField("_itemInfoPanel", Any)?.GetValue(panel) as ItemInfoPanel;
            info?.ShowItem(item, false);
            await UniTask.Delay(900, DelayType.Realtime);
            string Line() { var t = info != null ? FindDeep(info.transform, "AwakenLine")?.GetComponent<TMP_Text>() : null; return t != null ? t.GetParsedText() : null; }
            string l1 = Line();
            sb.AppendLine($"    {item.displayName}({zone}) · 단계 {LegendaryTuning.Tier(zone)} · 칸 {LegendaryAwaken.Cells(zone)}/{LegendaryAwaken.AwakenCells(zone)} · 줄 「{l1}」");
            Check("상세 카드에 각성 진척 줄(「n/4 · 각성까지 m칸」)", l1 != null && l1.Contains("/4") && l1.Contains("각성까지"));
            await Shot("LEG24_board_tier");

            // 존 4단계로 → 카드 줄 「각성」 · 룬판을 닫으면 각성 띠(띠는 룬판 아래라 닫힌 뒤에 뜬다)
            var set = typeof(WeaponFormProbeEditor).GetMethod("SetZoneTier", Any);
            set?.Invoke(null, new object[] { zone, 4, true });
            await UniTask.Delay(900, DelayType.Realtime);
            info?.ShowItem(item, false);
            await UniTask.Delay(700, DelayType.Realtime);
            string l2 = Line();
            sb.AppendLine($"    존 4단계 → 각성 {LegendaryTuning.Awakened(zone)} · 카드 줄 「{l2}」");
            Check("각성 뒤 카드 줄 「각성 — … 존 4단계」", l2 != null && l2.StartsWith("각성"));
            await Shot("LEG24_board_awake");
            var banner = Object.FindFirstObjectByType<ZoneTitleBanner>();
            var bt = banner != null ? typeof(ZoneTitleBanner).GetField("_title", Any)?.GetValue(banner) as TMP_Text : null;
            var bd = banner != null ? typeof(ZoneTitleBanner).GetField("_desc", Any)?.GetValue(banner) as TMP_Text : null;
            var bg = banner != null ? typeof(ZoneTitleBanner).GetField("_group", Any)?.GetValue(banner) as CanvasGroup : null;
            sb.AppendLine($"    룬판 열린 동안 띠 밝기 {(bg != null ? bg.alpha : -1f):0.00}(0이어야 — 닫힌 뒤로 미룸)");
            Check("룬판이 열린 동안엔 띠를 미룬다", bg == null || bg.alpha < 0.05f || (bt != null && !bt.text.Contains("각성")));
            Managers.UI.CloseAllPopupUI();
            panel.Close();
            await UniTask.Delay(1200, DelayType.Realtime);
            sb.AppendLine($"    룬판 닫은 뒤 띠 「{bt?.text}」 · 「{bd?.text}」 · 밝기 {(bg != null ? bg.alpha : -1f):0.00}");
            Check("룬판을 닫으면 각성 띠 「… — 각성」 + 둘째 줄", bt != null && bt.text.Contains("각성") && bd != null && !string.IsNullOrEmpty(bd.text));
            await Shot("LEG24_awaken_banner");
            await UniTask.Delay(2500, DelayType.Realtime);

            // HUD 자원 — 화면 길만 본다(쌓이는 셈은 c1 실측 95). 값을 넣고 버프 줄에 오르는지 찍은 뒤 지운다
            LegendaryHud.Set("heat", 12, 20);
            LegendaryHud.Set("dark", 80, 285);
            await UniTask.Delay(1500, DelayType.Realtime);
            var src = new List<BuffViewItem>();
            var effects = GameRunBootstrapper.Instance.Run.EffectManager;
            new ItemBuffViewSource(effects).Contribute(src);
            string labels = string.Join(" · ", src.ConvertAll(b => b.Label));
            sb.AppendLine($"    버프 줄 수집 「{labels}」");
            Check("HUD 버프 줄에 열기 · 어둠", labels.Contains("열기 12/20") && labels.Contains("어둠 80/285"));
            await Shot("LEG24_hud");
            LegendaryHud.Clear("heat"); LegendaryHud.Clear("dark");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            sb.Insert(0, $"통과 {pass}/{total}\n");
            File.WriteAllText("Temp/leg1024_screens.txt", sb.ToString());
            Debug.Log($"{Tag} 끝\n" + sb);
        }
    }

    private static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        for (int i = 0; i < t.childCount; i++) { var hit = FindDeep(t.GetChild(i), name); if (hit != null) return hit; }
        return null;
    }

    private static UniTask Shot(string name)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, 0 }) : UniTask.CompletedTask;
    }
}
#endif
