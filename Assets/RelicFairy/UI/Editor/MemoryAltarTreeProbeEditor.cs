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
/// 기억의 제단 트리(09-29 개편 · 10-02 재설계 「기억을 모시는 제단」) 검증 도구.
/// <para>① 그래프 · 배치 검증(에디터) — 부모 존재 · 같은 갈래 · 순환 없음 · 자물쇠는 하나(심연 입장) ·
/// 노드 겹침 0 · 시기별 노드 수 · (10-14 강화형) 포인트 고리 값 · 고리마다 닿을 수 있음(10-14 시기 드러남 폐기 — 모든 칸으로).</para>
/// <para>② 화면 실측(플레이 중) — 봉인기 새 계정 · 봉인기 중간(해금 연출 보통 · 열쇠) · 해방기 고리가 드러나는 순간 · 악몽 고리 · 전부를
/// 이야기 오버라이드로 바꿔 가며 찍는다(끝나면 오버라이드 원래대로).
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
                // 10-14 — 부모는 논리 선행(예: 영웅 룬 확률 ← 영웅 룬)이라 값 순서는 따지지 않는다 · 대신 바깥 고리가 부모가 될 수는 없다
                if (parent.RequiredPoints > n.RequiredPoints)
                { bad++; sb.AppendLine($"✗ 부모가 더 바깥 고리: {n.DisplayName}({n.RequiredPoints}포인트) ← {parent.DisplayName}({parent.RequiredPoints}포인트)"); }
            }
            if (Array.IndexOf(MemoryAltarCatalog.TierPoints, n.RequiredPoints) < 0)
            { bad++; sb.AppendLine($"✗ 고리 값이 아니다: {n.DisplayName} {n.RequiredPoints}포인트(고리 {string.Join("/", MemoryAltarCatalog.TierPoints)})"); }
            for (int lv = 1; lv <= n.MaxLevel; lv++)
                if (n.CostForLevel(lv, false) <= 0) { bad++; sb.AppendLine($"✗ 값 0: {n.DisplayName} Lv{lv}"); }
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
        if (locks != 1) { bad++; sb.AppendLine($"✗ 자물쇠 노드가 {locks}개(10-14 정본: 심연 입장 하나 — 챕터 4 개방은 뺐다)"); }

        // 10-14 포인트 고리 — 갈래의 각 고리에 안쪽 고리 칸만으로 닿을 수 있어야 한다. 자물쇠 칸은 포인트로 안 친다.
        int total = 0;
        foreach (var n in all) for (int lv = 1; lv <= n.MaxLevel; lv++) total += n.CostForLevel(lv, false);
        foreach (var branch in MemoryAltarLayout.BranchOrder)
        foreach (var n in all)
        {
            if (n.Branch != branch || n.RequiredPoints <= 0) continue;
            int pts = 0;
            foreach (var m in all)
                if (m.Branch == branch && m.RequiredPoints < n.RequiredPoints && !m.ConditionRequired && m.Branch != AltarBranch.Memory)
                    pts += m.MaxLevel;
            if (pts < n.RequiredPoints)
            { bad++; sb.AppendLine($"✗ 닿을 수 없다: {n.DisplayName} {n.RequiredPoints}포인트 — 안쪽 고리 최대 {pts}"); }
        }

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
        var perEra = new int[4];
        var perTier = new int[5];
        foreach (var p in place.Values) { maxDepth = Mathf.Max(maxDepth, p.Depth); perEra[p.Era]++; perTier[Mathf.Clamp(p.Depth, 0, 4)]++; }
        sb.Insert(0, $"기억의 제단 트리 검증 — 노드 {all.Count}(봉인기 {perEra[1]} · 해방기 {perEra[2]} · 악몽 {perEra[3]}) · 갈래 4 + 가운데 기억 · " +
                     $"포인트 고리 0/3/8/15 칸 {perTier[1]}/{perTier[2]}/{perTier[3]}/{perTier[4]} · 전부 강화 정수 {total:N0} · " +
                     $"최대 깊이 {maxDepth} · 자물쇠 {locks} · 가장 가까운 두 노드 {minGap:0}px({pair})\n" +
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
        int    keepOverride = EditorPrefs.GetInt(StoryProgress.DebugOverridePrefsKey, -1);
        var sb = new StringBuilder();
        Invoke("HideTestHubGui");
        try
        {
            // A — 새 계정: 아무것도 없고 정수 800 · 모든 칸이 보이고(10-14 시기 드러남 폐기) 포인트 고리 칸은 잠김
            SetEra(0);
            data.unlockedIds = ""; data.abyssEssence = 800;
            ResetFirstOpen();
            var panel = await OpenAsync();
            await Shots("Altar_A_Open", new[] { 60, 220, 420, 900 });
            DumpLabels(sb, "A 봉인기 새 계정", panel);
            Managers.UI.CloseAllPopupUI();
            await UniTask.Delay(300, ignoreTimeScale: true);

            // B — 봉인기 중간(10-14 강화형): 갈래마다 몇 단계 + 정수 8000 · 룬 갈래는 7포인트(8포인트 고리 바로 앞)
            data.unlockedIds = string.Join(",", new[]
            {
                $"{MemoryAltarCatalog.RuneStorage1}:2", $"{MemoryAltarCatalog.RuneRareOdds}:3", $"{MemoryAltarCatalog.OreGain}:2",
                MemoryAltarCatalog.CovenantParts1,
                MemoryAltarCatalog.PartPierce, $"{MemoryAltarCatalog.SigilSmith}:2", MemoryAltarCatalog.WeaponCrossbow, MemoryAltarCatalog.PartPower,
                MemoryAltarCatalog.Revive, $"{MemoryAltarCatalog.MaxHpUp}:2", MemoryAltarCatalog.SigilMerchant,
            });
            data.abyssEssence = 8000;
            panel = await OpenAsync();
            await Shots("Altar_B_Open", new[] { 60, 300 });
            DumpLabels(sb, "B 봉인기 중간", panel);

            // B-0 포인트 고리(세피리아식) — 룬 7포인트면 8포인트 칸은 잠김 · 원석 한 단계 더 사면(8) 열린다 · 단계 산 값이 다음 단계 값으로
            var pick = MemoryAltarService.GetState(MemoryAltarCatalog.RefinePick);
            sb.AppendLine($"B-0 룬 {MemoryAltarService.BranchPoints(AltarBranch.Rune)}포인트 — 「정제 두 장」 포인트 잠김 {pick.BlockedByPoints} · 살 수 있음 {pick.CanBuy} (true · false여야)");
            var ore0 = MemoryAltarService.GetState(MemoryAltarCatalog.OreGain);
            await UnlockAndShoot(panel, MemoryAltarCatalog.OreGain, "Altar_B_LevelUp", sb);
            var ore1 = MemoryAltarService.GetState(MemoryAltarCatalog.OreGain);
            pick = MemoryAltarService.GetState(MemoryAltarCatalog.RefinePick);
            sb.AppendLine($"B-0 원석 Lv {ore0.Level}→{ore1.Level} · 값 {ore0.Cost}→{ore1.Cost} · 룬 {MemoryAltarService.BranchPoints(AltarBranch.Rune)}포인트 — 「정제 두 장」 잠김 {pick.BlockedByPoints} · 살 수 있음 {pick.CanBuy} (false · true여야)");
            sb.AppendLine($"B-0 효과 — 원석 배율 {MemoryAltarService.OreGainMult:0.00} · 희귀 확률 +{MemoryAltarService.RareOddsBonus:0}%p · 최대 체력 +{MemoryAltarService.MaxHpPct * 100f:0}% · 상점 −{MemoryAltarService.ShopDiscount * 100f:0}% · 재련 +{MemoryAltarService.SmithBonus * 100f:0}%p");

            // B-1 보통 노드 해금(정제 두 장) · B-2 열쇠 해금(영웅 룬) — 저장 없이 연출만
            await UnlockAndShoot(panel, MemoryAltarCatalog.RefinePick, "Altar_B_UnlockNormal", sb);
            await UnlockAndShoot(panel, MemoryAltarCatalog.RuneEpic,   "Altar_B_UnlockKeystone", sb);
            // 가운데 기억 노드는 유물 성장 개편 전까지 살 수 없어야 한다
            var mem = MemoryAltarService.GetState(MemoryAltarCatalog.MemRedraw);
            sb.AppendLine($"가운데 「다시 떠올리기」: 준비 중 {mem.Pending} · 살 수 있음 {mem.CanBuy} (준비 중이면 false여야)");
            await Shots("Altar_B_Idle", new[] { 1500 });
            Managers.UI.CloseAllPopupUI();
            await UniTask.Delay(300, ignoreTimeScale: true);

            // E — 전부 연 계정
            var allIds = new List<string>();
            foreach (var n in MemoryAltarCatalog.All) allIds.Add(n.MaxLevel > 1 ? $"{n.Id}:{n.MaxLevel}" : n.Id);
            data.unlockedIds = string.Join(",", allIds);
            data.abyssEssence = 12000;
            panel = await OpenAsync();
            await Shots("Altar_E_Full", new[] { 400 });
            DumpLabels(sb, "E 전부", panel);
            Managers.UI.CloseAllPopupUI();
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            // 세이브를 건드리지 않는다 — 메모리 값과 오버라이드만 되돌린다(이 실측은 저장을 부르지 않는다).
            data.unlockedIds = keepUnlocked; data.records = keepRecords; data.abyssEssence = keepEssence;
            EditorPrefs.SetInt(StoryProgress.DebugOverridePrefsKey, keepOverride);
            StoryProgress.RefreshDebugOverride();
            Invoke("RestoreTestHubGui");
            File.WriteAllText("Temp/altar_tree_probe.txt", sb.ToString());
            Debug.Log("[AltarTreeProbe] 화면 실측 끝 → Temp/altar_tree_probe.txt · Temp/ui_shots/Altar_*.png (세이브 무변경)");
        }
    }

    /// <summary>이야기 오버라이드로 시기를 바꾼다 — 0 봉인기 · 1 해방기 · 2 악몽 모드 · 3 엔딩 뒤(악몽 끔).</summary>
    private static void SetEra(int overrideValue)
    {
        EditorPrefs.SetInt(StoryProgress.DebugOverridePrefsKey, overrideValue);
        StoryProgress.RefreshDebugOverride();
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
