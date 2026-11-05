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
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 11-05 사용자 「스탯 상세보기 팝업을 열 수 있게 잘 보이게 · 지금 적용된 모든 룬 기능 · 수치를 보기 좋게」 실측 — 런 안에서(플레이 중).
/// 같은 룬 둘 · 같은 계열 셋(각인 단계) · 스탯 룬 · 전설을 잠깐 판에 놓고(놓인 목록만 — 판 칸은 안 바뀐다) 룬판 → [스탯 상세] 단추 → 창을 연다.
/// 단추가 보이고 눌리나 · 창 수치 = 실제 스탯 · 놓인 룬 장수 · ×2 묶음 · 각인 배율 글 · 한 줄 칸 말줄임 0 · 글이 판 밖으로 안 나가나 · Tab은 창만 닫나 · [닫기].
/// 결과 Temp/statdetail1105.txt · Temp/ui_shots/SD1105_a.png(위) · SD1105_b.png(오른쪽 맨 아래) · 로그 「[SD1105] 끝」. 끝나면 놓은 룬을 걷는다.
/// </summary>
public static class StatDetail1105ProbeEditor
{
    private const string Tag = "[SD1105]";
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static int s_fail;
    private static StringBuilder s_sb;

    [MenuItem("RelicFairy/Debug/11-05 스탯 상세 창 실측 (런 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null) { Debug.LogWarning($"{Tag} 런 안에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        s_sb = new StringBuilder("11-05 스탯 상세 창\n");
        s_fail = 0;
        var run = GameRunBootstrapper.Instance.Run;
        var inv = run.ItemInventory;
        var staging = typeof(RunItemInventory).GetField("_stagingItems", Any)?.GetValue(inv) as List<RuntimeItemData>;
        var added = new List<RuntimeItemData>();
        // 속성 시너지 칸 — 시험 룬은 판 칸에 안 놓여 존 칸 수가 0이다 → 칸 수만 잠깐 채운다(끝나면 되돌린다)
        var zones = MerlinRuneBridge.Instance != null ? typeof(MerlinRuneBridge).GetField("_zoneOccupiedCounts", Any)?.GetValue(MerlinRuneBridge.Instance) as Dictionary<string, int> : null;
        var zoneBackup = zones != null ? new Dictionary<string, int>(zones) : null;
        try
        {
            // ── 0 룬 놓기 — 같은 룬 둘 · 같은 계열(연격) 셋 · 스탯 룬 · 전설 하나
            var all = Managers.ItemData.GetAllItems().Values.Select(RuntimeItemData.FromServer).Where(d => d != null && d.shapeId > 0).ToList();
            var combo = all.Where(d => d.effects.Any(e => BuildFamilyRules.FromSlot(e.effectType, e.trigger) == BuildFamily.Combo && BuildFamilyRules.IsScalable(e.effectType))).Take(2).ToList();
            var stat  = all.FirstOrDefault(d => d.effects.All(e => e.trigger == "Always"));
            var legend = all.FirstOrDefault(d => d.rarity == ItemRarity.Legendary);
            var picks = new List<string>();
            if (combo.Count == 2) { picks.Add(combo[0].itemId); picks.Add(combo[0].itemId); picks.Add(combo[1].itemId); }
            if (stat != null) picks.Add(stat.itemId);
            if (legend != null) picks.Add(legend.itemId);
            foreach (var id in picks)
            {
                var d = RuntimeItemData.FromServer(Managers.ItemData.GetAllItems()[id]);
                d.EnsureElement();
                staging?.Add(d);
                inv.PlaceItem(d);
                if (inv.PlacedItems.Contains(d)) added.Add(d);
            }
            await UniTask.DelayFrame(3);
            s_sb.AppendLine($"  놓은 룬 {added.Count}장 [{string.Join(", ", added.Select(a => a.displayName))}] · 판 전체 {inv.PlacedItems.Count}장");
            Check(added.Count == picks.Count && picks.Count >= 4, $"시험 룬 놓기 {added.Count}/{picks.Count}");
            var comboFam = combo.Count > 0 ? BuildFamily.Combo : BuildFamily.None;
            s_sb.AppendLine($"  연격 각인 {BuildImprint.Count(comboFam)} · {BuildImprint.Stage(comboFam)}단계 · 배율 ×{BuildImprint.EffectMultiplier(comboFam):0.0#}");

            // ── 1 룬판 → 단추
            var grid = UI_GridPanel.Instance;
            if (grid != null && !grid.IsOpen) grid.Open();
            await UniTask.Delay(700, DelayType.Realtime);
            Check(grid != null && grid.IsOpen, "룬판 열림");
            var info = Object.FindFirstObjectByType<CharacterInfoPanelView>();
            var btn = info != null ? info.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name.StartsWith("Btn_")) : null;
            Check(btn != null && btn.gameObject.activeInHierarchy, $"[스탯 상세] 단추 있음 「{(btn != null ? btn.name : "-")}」");
            if (btn == null) return;
            var brt = (RectTransform)btn.transform;
            var c = new Vector3[4]; brt.GetWorldCorners(c);
            var canvas = btn.GetComponentInParent<Canvas>().rootCanvas;
            float scale = canvas.transform.localScale.x;
            Vector2 size = new Vector2(c[2].x - c[0].x, c[2].y - c[0].y) / scale;
            bool onScreen = c[0].x >= 0f && c[0].y >= 0f && c[2].x <= Screen.width && c[2].y <= Screen.height;
            Check(onScreen && size.x >= 100f && size.y >= 30f, $"단추 화면 안 · 크기 {size.x:0}×{size.y:0}(기준 px)");
            var ped = new PointerEventData(EventSystem.current) { position = (c[0] + c[2]) * 0.5f };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(ped, hits);
            var top = hits.Count > 0 ? hits[0].gameObject : null;
            Check(top != null && top.transform.IsChildOf(btn.transform), $"단추 가운데 맨 위 = 단추(「{(top != null ? top.name : "-")}」)");
            await Shot("SD1105_0");

            if (zones != null)
            {
                zones[ElementDef.Order[0]] = 6;
                zones[ElementDef.Order[1]] = 3;
                foreach (var z in new[] { ElementDef.Order[0], ElementDef.Order[1] })
                    s_sb.AppendLine($"  존 {z} 칸 {zones[z]} · 단계 칸 [{string.Join(", ", (Managers.RuneData?.GetZoneSynergies(z) ?? new List<RuneSynergyEntry>()).Select(e => e.threshold))}]");
            }

            // ── 2 창 열기
            btn.onClick.Invoke();
            await UniTask.Delay(500, DelayType.Realtime);
            var overlay = GameObject.Find("@StatDetail");
            Check(StatDetailView.IsOpen && overlay != null, "창 열림");
            if (overlay == null) return;

            // ── 3 수치 = 실제
            var rep = StatDetailReport.Build(run);
            var s = run.Player.RuntimeStats;
            var atkRow = rep.Stats.SelectMany(g => g.Rows).FirstOrDefault(r => r.Label == Loc.T("공격력"));
            var hpRow  = rep.Stats.SelectMany(g => g.Rows).FirstOrDefault(r => r.Label == Loc.T("체력"));
            Check(atkRow.Value == s.AttackPower.ToString(), $"공격력 {atkRow.Value} = {s.AttackPower} · 룬 몫 「{atkRow.Note}」");
            Check(hpRow.Value == $"{s.Hp} / {s.MaxHp}", $"체력 {hpRow.Value} · 룬 몫 「{hpRow.Note}」");
            foreach (var g in rep.Stats) s_sb.AppendLine($"  [{g.Title}] " + string.Join("  |  ", g.Rows.Select(r => $"{r.Label} {r.Value}{(r.Note != null ? " (" + r.Note + ")" : "")}")));
            var runes = rep.Sections.FirstOrDefault(x => x.Title == Loc.T("놓인 룬"));
            int distinct = inv.PlacedItems.Select(p => p.itemId).Distinct().Count();
            Check(runes != null && runes.Items.Count == distinct, $"놓인 룬 칸 {runes?.Items.Count} = 서로 다른 룬 {distinct}");
            Check(runes != null && runes.Items.Any(i => i.Head.Contains("×2")), "같은 룬 둘 = 한 칸 ×2");
            bool scaled = runes != null && runes.Items.Any(i => i.Body != null && i.Body.Contains("각인 ×"));
            Check(BuildImprint.Stage(comboFam) == 0 || scaled, "각인 단계가 있으면 그 계열 룬 줄에 「각인 ×배율 → 실제 값」");
            var syn = rep.Sections.FirstOrDefault(x => x.Title == Loc.T("속성 시너지"));
            Check(zones == null || (syn != null && syn.Items.Count == 2 && syn.Items.Any(i => i.Body != null && i.Body.Contains("◆"))), $"속성 시너지 칸 {syn?.Items.Count} = 채운 존 2 · 켜진 단계 ◆");
            var allV = new List<BuffViewItem>(); run.EffectManager.CollectActiveBuffViews(allV);
            var condV = new List<BuffViewItem>(); run.EffectManager.CollectActiveBuffViews(condV, skipPassive: true);
            var now = rep.Sections.FirstOrDefault(x => x.Title == Loc.T("지금 켜진 조건부 효과"));
            Check(now != null && now.Items.Count == condV.Count, $"조건부 칸 {now?.Items.Count} = 조건부만 {condV.Count}(상시 포함 전체 {allV.Count})");
            foreach (var sec in rep.Sections)
            {
                s_sb.AppendLine($"  ◆ {sec.Title} {sec.Count} — 칸 {sec.Items.Count}");
                foreach (var it in sec.Items.Take(6)) s_sb.AppendLine($"     {Plain(it.Head)}{(it.Body != null ? " ⏎ " + Plain(it.Body).Replace("\n", " / ") : "")}");
            }

            // ── 4 글 — 한 줄 칸 말줄임 0 · 판 밖으로 안 나감
            var panel = (RectTransform)overlay.transform.Find("Panel");
            var pc = new Vector3[4]; panel.GetWorldCorners(pc);
            var texts = overlay.GetComponentsInChildren<TMP_Text>(true);
            foreach (var t in texts) t.ForceMeshUpdate();
            var cut = texts.Where(t => t.textWrappingMode == TextWrappingModes.NoWrap && t.isTextTruncated).Select(t => Plain(t.text)).ToList();
            Check(cut.Count == 0, $"한 줄 칸 말줄임 {cut.Count}" + (cut.Count > 0 ? " [" + string.Join(", ", cut.Take(5)) + "]" : ""));
            int outside = 0;
            foreach (var t in texts)
            {
                if (string.IsNullOrEmpty(t.text) || !t.gameObject.activeInHierarchy) continue;
                var b = t.textBounds;
                if (b.size.x <= 0f) continue;
                Vector3 l = t.rectTransform.TransformPoint(b.min), r = t.rectTransform.TransformPoint(b.max);
                if (l.x < pc[0].x - 1f || r.x > pc[2].x + 1f) outside++;
            }
            Check(outside == 0, $"판 밖으로 나간 글 {outside}");
            await Shot("SD1105_a");
            var scrolls = overlay.GetComponentsInChildren<ScrollRect>(true);
            s_sb.AppendLine($"  스크롤 {scrolls.Length} · 내용 높이 [{string.Join(", ", scrolls.Select(x => $"{x.content.sizeDelta.y:0}/{((RectTransform)x.transform).rect.height:0}"))}]");
            foreach (var sc in scrolls) sc.verticalNormalizedPosition = 0f;
            await UniTask.Delay(200, DelayType.Realtime);
            await Shot("SD1105_b");

            // ── 5 Tab = 창만 · [닫기]
            var toggle = typeof(PlayerController).GetMethod("TogglePuzzleGrid", Any);
            toggle?.Invoke(run.Player, null);
            await UniTask.Delay(300, DelayType.Realtime);
            Check(grid.IsOpen && StatDetailView.IsOpen, "창이 열린 동안 룬판 Tab은 비킨다(룬판 그대로)");
            var close = overlay.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == "Btn_닫기");
            close?.onClick.Invoke();
            await UniTask.Delay(500, DelayType.Realtime);
            Check(!StatDetailView.IsOpen && GameObject.Find("@StatDetail") == null && grid.IsOpen, "[닫기] — 창만 닫히고 룬판은 남는다");
        }
        catch (System.Exception e) { s_fail++; s_sb.AppendLine("예외: " + e); }
        finally
        {
            foreach (var d in added) { inv.UnplaceItem(d); staging?.Remove(d); }
            if (zones != null) { zones.Clear(); foreach (var kv in zoneBackup) zones[kv.Key] = kv.Value; }
            s_sb.AppendLine($"  정리 — 놓은 룬 {added.Count}장 걷음 · 판 {inv.PlacedItems.Count}장");
            s_sb.Insert(0, s_fail == 0 ? "PASS\n" : $"FAIL {s_fail}\n");
            File.WriteAllText("Temp/statdetail1105.txt", s_sb.ToString());
            Debug.Log($"{Tag} 끝\n{s_sb}");
        }
    }

    private static void Check(bool ok, string what)
    {
        if (!ok) s_fail++;
        s_sb.AppendLine($"{(ok ? "PASS" : "FAIL")} {what}");
    }

    private static string Plain(string s) => string.IsNullOrEmpty(s) ? s : System.Text.RegularExpressions.Regex.Replace(s, "<[^>]+>", "");

    private static async UniTask Shot(string name)
    {
        Directory.CreateDirectory("Temp/ui_shots");
        ScreenCapture.CaptureScreenshot($"Temp/ui_shots/{name}.png");
        await UniTask.Delay(400, DelayType.Realtime);
    }
}
#endif
