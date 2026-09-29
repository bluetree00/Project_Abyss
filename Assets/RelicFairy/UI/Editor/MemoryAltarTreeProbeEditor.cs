using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 기억의 제단 트리(09-29 개편) 검증 도구.
/// <para>① 그래프 · 배치 검증(에디터) — 부모 존재 · 같은 갈래 · 순환 없음 · 가운데에서 닿음 · 자물쇠는 둘뿐 · 값이 깊이를 따라 오름 · 노드 겹침 0.</para>
/// <para>② 화면 실측(플레이 중) — 새 계정 · 중간 · 전부 세 상태로 창을 열어 여는 연출 · 해금 연출(보통 · 열쇠)을 프레임으로 찍는다.
/// <b>세이브를 건드리지 않는다</b> — 정수 · 해금 · 기록을 메모리에서만 바꾸고 끝나면 원래 값으로 되돌린다(저장 호출 0).</para>
/// 결과: Temp/altar_tree_check.txt · Temp/altar_tree_probe.txt · Temp/ui_shots/Altar_*.png
/// </summary>
public static class MemoryAltarTreeProbeEditor
{
    private const BindingFlags NonPub = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags StaticNonPub = BindingFlags.Static | BindingFlags.NonPublic;

    // ── ① 그래프 · 배치 ─────────────────────────────────────

    [MenuItem("RelicFairy/Altar/트리 검증 (그래프 · 배치)")]
    private static void CheckGraph()
    {
        var sb = new StringBuilder();
        int bad = 0;
        var all = MemoryAltarCatalog.All;
        var ids = new HashSet<string>();
        foreach (var n in all) if (!ids.Add(n.Id)) { bad++; sb.AppendLine($"✗ id 중복: {n.Id}"); }

        int locks = 0;
        foreach (var n in all)
        {
            if (n.ConditionRequired) locks++;
            foreach (var p in n.Parents)
            {
                var parent = MemoryAltarCatalog.Get(p);
                if (parent == null) { bad++; sb.AppendLine($"✗ 없는 부모: {n.Id} ← {p}"); continue; }
                if (parent.Branch != n.Branch) { bad++; sb.AppendLine($"✗ 갈래를 넘는 선: {n.Id}({n.Branch}) ← {p}({parent.Branch})"); }
                if (!n.ConditionRequired && n.BaseCost < parent.BaseCost)
                { bad++; sb.AppendLine($"✗ 값이 부모보다 싸다: {n.DisplayName} {n.BaseCost} < {parent.DisplayName} {parent.BaseCost}"); }
            }
            // 순환 — 부모를 따라 올라가다 자기를 만나면 순환
            var seen = new HashSet<string>();
            var stack = new Stack<string>(n.Parents);
            while (stack.Count > 0)
            {
                var id = stack.Pop();
                if (id == n.Id) { bad++; sb.AppendLine($"✗ 순환: {n.Id}"); break; }
                if (!seen.Add(id)) continue;
                var p = MemoryAltarCatalog.Get(id);
                if (p != null) foreach (var pp in p.Parents) stack.Push(pp);
            }
        }
        if (locks != 2) { bad++; sb.AppendLine($"✗ 자물쇠 노드가 {locks}개(정본: 챕터 4 · 심연 입장 둘뿐)"); }

        // 배치 — 노드 원끼리 겹침(간격 8px 미만)
        var place = MemoryAltarLayout.Compute(all);
        float minGap = float.MaxValue; string pair = "";
        var list = new List<KeyValuePair<string, AltarNodePlacement>>(place);
        for (int i = 0; i < list.Count; i++)
        for (int j = i + 1; j < list.Count; j++)
        {
            var a = list[i].Value; var b = list[j].Value;
            float gap = Vector2.Distance(a.Position, b.Position) - (a.Diameter + b.Diameter) * 0.5f;
            if (gap < minGap) { minGap = gap; pair = $"{list[i].Key} ↔ {list[j].Key}"; }
            if (gap < 8f) { bad++; sb.AppendLine($"✗ 노드가 붙었다({gap:0}px): {list[i].Key} ↔ {list[j].Key}"); }
        }

        int maxDepth = 0;
        foreach (var p in place.Values) maxDepth = Mathf.Max(maxDepth, p.Depth);
        sb.Insert(0, $"기억의 제단 트리 검증 — 노드 {all.Count} · 갈래 5 · 최대 깊이 {maxDepth} · 자물쇠 {locks} · 가장 가까운 두 노드 {minGap:0}px({pair})\n" +
                     (bad == 0 ? "결과: 이상 없음\n" : $"결과: 문제 {bad}건\n"));
        Directory.CreateDirectory("Temp");
        File.WriteAllText("Temp/altar_tree_check.txt", sb.ToString());
        Debug.Log($"[AltarTreeProbe] 그래프 · 배치 검증 — 문제 {bad}건 → Temp/altar_tree_check.txt");
    }

    // ── ② 화면 실측 ─────────────────────────────────────────

    [MenuItem("RelicFairy/Altar/트리 화면 실측 (플레이 중)")]
    private static void ProbeScreen()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[AltarTreeProbe] 플레이 모드에서 실행해야 한다."); return; }
        ProbeAsync().Forget();
    }

    private static async UniTaskVoid ProbeAsync()
    {
        var data = BackendGameData.Instance?.Data;
        if (data == null) { Debug.LogWarning("[AltarTreeProbe] 계정 데이터가 없다(로그인 뒤 실행)."); return; }

        string keepUnlocked = data.unlockedIds, keepRecords = data.records;
        int    keepEssence  = data.abyssEssence;
        var sb = new StringBuilder();
        Invoke("HideTestHubGui");
        try
        {
            // A — 새 계정: 아무것도 없고 정수 800
            data.unlockedIds = ""; data.abyssEssence = 800;
            ResetFirstOpen();
            var panel = await OpenAsync();
            await Shots("Altar_A_Open", new[] { 60, 220, 420, 900 });
            DumpLabels(sb, "A 새 계정", panel);
            Managers.UI.CloseAllPopupUI();
            await UniTask.Delay(300, ignoreTimeScale: true);

            // B — 중간: 갈래마다 앞쪽 몇 칸 + 정수 8000 · 첫 완주 기록(영웅 룬 할인)
            data.unlockedIds = string.Join(",", new[]
            {
                MemoryAltarCatalog.RuneChoice4, MemoryAltarCatalog.RuneStorage1,
                MemoryAltarCatalog.CovenantParts1,
                MemoryAltarCatalog.SigilMerchant, MemoryAltarCatalog.SigilSmith, MemoryAltarCatalog.PartsDraft4,
                MemoryAltarCatalog.PartPierce, MemoryAltarCatalog.WeaponCrossbow, MemoryAltarCatalog.PartPower,
                MemoryAltarCatalog.Revive, MemoryAltarCatalog.MaxHpUp,
            });
            data.abyssEssence = 8000;
            panel = await OpenAsync();
            await Shots("Altar_B_Open", new[] { 60, 300 });
            DumpLabels(sb, "B 중간", panel);

            // B-1 보통 노드 해금(정제 두 장) · B-2 열쇠 해금(영웅 룬) — 저장 없이 연출만
            await UnlockAndShoot(panel, MemoryAltarCatalog.RefinePick, "Altar_B_UnlockNormal", sb);
            await UnlockAndShoot(panel, MemoryAltarCatalog.RuneEpic,   "Altar_B_UnlockKeystone", sb);
            await Shots("Altar_B_Idle", new[] { 1500 });
            Managers.UI.CloseAllPopupUI();
            await UniTask.Delay(300, ignoreTimeScale: true);

            // C — 전부 연 계정
            var allIds = new List<string>();
            foreach (var n in MemoryAltarCatalog.All) allIds.Add(n.Id);
            data.unlockedIds = string.Join(",", allIds);
            data.abyssEssence = 12000;
            panel = await OpenAsync();
            await Shots("Altar_C_Full", new[] { 400 });
            DumpLabels(sb, "C 전부", panel);
            Managers.UI.CloseAllPopupUI();
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            // 세이브를 건드리지 않는다 — 메모리 값만 되돌린다(이 실측은 저장을 부르지 않는다).
            data.unlockedIds = keepUnlocked; data.records = keepRecords; data.abyssEssence = keepEssence;
            Invoke("RestoreTestHubGui");
            File.WriteAllText("Temp/altar_tree_probe.txt", sb.ToString());
            Debug.Log("[AltarTreeProbe] 화면 실측 끝 → Temp/altar_tree_probe.txt · Temp/ui_shots/Altar_*.png (세이브 무변경)");
        }
    }

    private static async UniTask<UI_AwakeningPanel> OpenAsync()
    {
        var panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_AwakeningPanel>();
        // 받아갈 업적이 있으면 업적 탭으로 열린다 — 해금 탭으로 돌린다
        typeof(UI_AwakeningPanel).GetMethod("SetMode", NonPub)?.Invoke(panel, new object[] { false });
        return panel;
    }

    private static async UniTask UnlockAndShoot(UI_AwakeningPanel panel, string nodeId, string shot, StringBuilder sb)
    {
        var node = MemoryAltarCatalog.Get(nodeId);
        var presenter = typeof(UI_AwakeningPanel).GetField("_presenter", NonPub)?.GetValue(panel) as AltarTreePresenter;
        var tree = typeof(UI_AwakeningPanel).GetField("_tree", NonPub)?.GetValue(panel) as AltarTreeView;
        if (node == null || presenter == null || tree == null) { sb.AppendLine($"해금 실측 불가: {nodeId}"); return; }

        var before = new Dictionary<string, AltarNodeVisual>();
        foreach (var kv in presenter.Build()) before[kv.Key] = kv.Value.Visual;
        var st = MemoryAltarService.GetState(node);
        bool ok = MemoryAltarService.TryUnlock(node);   // 메모리만 — 저장은 부르지 않는다
        sb.AppendLine($"해금 {node.DisplayName}: {(ok ? "성공" : "실패")} (값 {st.Cost} · 정수 {MemoryAltarService.Essence})");
        if (!ok) return;
        typeof(UI_AwakeningPanel).GetMethod("Refresh", NonPub)?.Invoke(panel, null);
        tree.Focus(node, select: false);
        var play = tree.PlayUnlockAsync(node, before, CancellationToken.None);
        await Shots(shot, new[] { 90, 250, 420, 620, 850 });
        await play;
    }

    private static void DumpLabels(StringBuilder sb, string label, UI_AwakeningPanel panel)
    {
        var tree = typeof(UI_AwakeningPanel).GetField("_tree", NonPub)?.GetValue(panel) as AltarTreeView;
        if (tree == null) { sb.AppendLine($"{label}: 트리 없음"); return; }
        var rects = new List<(string, Rect)>();
        foreach (var t in tree.GetComponentsInChildren<TMP_Text>(false))
        {
            if (t.transform.parent == null || t.transform.parent.name != "Labels" || string.IsNullOrEmpty(t.text)) continue;
            var c = new Vector3[4]; t.rectTransform.GetWorldCorners(c);
            // 글자 실제 폭 — 칸이 아니라 그려진 글자 기준
            var b = t.textBounds;
            var center = t.rectTransform.TransformPoint(b.center);
            var ext = Vector3.Scale(b.extents, t.rectTransform.lossyScale);
            rects.Add((t.text, new Rect(center.x - ext.x, center.y - ext.y, ext.x * 2f, ext.y * 2f)));
        }
        int overlaps = 0;
        for (int i = 0; i < rects.Count; i++)
        for (int j = i + 1; j < rects.Count; j++)
            if (rects[i].Item2.Overlaps(rects[j].Item2)) { overlaps++; sb.AppendLine($"  {label} 이름표 겹침: {rects[i].Item1} ↔ {rects[j].Item1}"); }
        sb.AppendLine($"{label}: 이름표 {rects.Count}개 · 겹침 {overlaps}건");
    }

    private static void ResetFirstOpen()
        => typeof(AltarTreeView).GetField("s_openedThisSession", StaticNonPub)?.SetValue(null, false);

    private static async UniTask Shots(string name, int[] atMs)
    {
        int prev = 0;
        foreach (var ms in atMs)
        {
            await UniTask.Delay(Mathf.Max(0, ms - prev), ignoreTimeScale: true);
            prev = ms;
            var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", StaticNonPub);
            if (m != null) await (UniTask)m.Invoke(null, new object[] { $"{name}_{ms:0000}", 0 });
        }
    }

    private static void Invoke(string method)
        => typeof(UILayoutRuntimeProbeEditor).GetMethod(method, StaticNonPub)?.Invoke(null, null);
}
