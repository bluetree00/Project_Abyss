#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 11-02 Q26 멀린 룬 보관 실측(테스트 허브 런 중) — 결과 Temp/merlin_keep1102.txt · 사진 Temp/ui_shots/MK1102_*.png · 로그 「[MK1102] 끝」.
/// 11-02 밤 새 창(등급별 정렬): 이번 런 룬(판 · 보관함)에 네 등급을 둘씩 더해 고르기 창 → 카드가 등급 높은 순인가 · 처음 골라진 카드 = 가장 높은 등급 →
/// 셋째 카드를 눌러 [이 룬을 맡긴다] → 슬롯 메타 keptRune → 다음 런 시작 지급(GrantPending) → 돌려받은 룬은 다음 후보(CaptureOwned)에서 빠지는가.
/// 끝나면 더한 룬 · 보관함에 넣은 룬을 버리고 meta keptRune을 처음 값으로 되돌린다.
/// </summary>
public static class MerlinKeep1102ProbeEditor
{
    private static readonly ItemRarity[] Fill = { ItemRarity.Common, ItemRarity.Legendary, ItemRarity.Rare, ItemRarity.Epic };

    [MenuItem("RelicFairy/Debug/11-02 멀린 룬 보관 실측 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[MK1102] 플레이 중에만"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("11-02 멀린 룬 보관(등급별 새 창)\n");
        Directory.CreateDirectory("Temp/ui_shots");
        var gd = BackendGameData.Instance;
        string before = gd?.Data?.keptRune;
        RuntimeItemData granted = null;
        var run = GameRunBootstrapper.Instance?.Run;
        try
        {
            if (run == null || gd?.Data == null) { sb.AppendLine($"런 {run != null} · 메타 {gd?.Data != null}"); return; }
            var list = MerlinRuneKeep.CaptureOwned(run);
            sb.AppendLine($"이번 런 룬(판 · 보관함) {list.Count}");
            foreach (var rarity in Fill)   // 섞인 순서로 더한다 — 창이 등급별로 정렬하는지 본다
            {
                var pool = ItemSORegistry.GetByRarity(rarity);
                for (int i = 0; pool != null && i < pool.Count && i < 2; i++)
                {
                    var d = RuntimeItemData.FromSO(pool[i]);
                    if (d == null) continue;
                    d.EnsureElement();
                    list.Add(d);
                }
            }
            sb.AppendLine($"  네 등급 둘씩 더함 → {list.Count}");

            var pick = MerlinRuneKeepView.PickAsync(list, default);
            await UniTask.Delay(1300, ignoreTimeScale: true);
            ScreenCapture.CaptureScreenshot("Temp/ui_shots/MK1102_pick.png");
            await UniTask.Delay(300, ignoreTimeScale: true);
            var overlay = GameObject.Find("@MerlinRuneKeep");
            if (overlay == null) { sb.AppendLine("FAIL 고르기 창 안 뜸"); return; }

            // 카드 순서 = 등급 높은 순(이름으로 목록의 등급을 찾는다)
            var cards = new List<Button>();
            Button keepBtn = null;
            foreach (var b in overlay.GetComponentsInChildren<Button>())
            {
                if (b.name == "Card") cards.Add(b);
                if (b.name == "Btn_이 룬을 맡긴다") keepBtn = b;
            }
            var rarities = new List<int>();
            foreach (var c in cards)
            {
                var t = c.GetComponentInChildren<TMP_Text>();
                string shown = t != null ? t.text : "";
                int r = -1;
                foreach (var it in list) if (Loc.T(it.displayName) == shown) { r = (int)it.rarity; break; }
                rarities.Add(r);
            }
            bool sorted = true;
            for (int i = 1; i < rarities.Count; i++) if (rarities[i] > rarities[i - 1]) sorted = false;
            sb.AppendLine($"{(sorted ? "PASS" : "FAIL")} 카드 {cards.Count}장 등급 순서 [{string.Join(",", rarities)}](3 전설 → 0 일반)");
            var name = FindText(overlay, "Detail", 0);
            sb.AppendLine($"처음 골라진 룬(오른쪽) 「{name}」 · 맡기기 단추 {(keepBtn != null && keepBtn.interactable ? "열림" : "잠김")}");
            if (cards.Count < 3 || keepBtn == null) return;

            cards[2].onClick.Invoke();
            await UniTask.Delay(350, ignoreTimeScale: true);
            ScreenCapture.CaptureScreenshot("Temp/ui_shots/MK1102_selected.png");
            await UniTask.Delay(250, ignoreTimeScale: true);
            string third = FindText(overlay, "Detail", 0);
            keepBtn.onClick.Invoke();
            var result = await pick;
            bool sameAsThird = result != null && Loc.T(result.displayName) == third;
            sb.AppendLine($"{(sameAsThird ? "PASS" : "FAIL")} 고른 룬 = {(result != null ? result.displayName + " · " + result.element : "없음")}(셋째 카드 「{third}」)");
            if (result == null) return;

            MerlinRuneKeep.Keep(result);
            string saved = gd.Data.keptRune;
            bool okSave = saved == result.itemId + "|" + result.element;
            sb.AppendLine($"{(okSave ? "PASS" : "FAIL")} 메타 keptRune 「{saved}」 · 맡긴 것 있음 {MerlinRuneKeep.HasPending}");

            int stage0 = run.ItemInventory.StagingCount;
            bool wasPending = run.KeptRunePending;
            run.KeptRunePending = false;
            granted = MerlinRuneKeep.GrantPending(run);
            bool okGrant = granted != null && granted.itemId == result.itemId && granted.element == result.element
                           && run.ItemInventory.IsStaging(granted.instanceId) && run.ItemInventory.StagingCount == stage0 + 1 && granted.fromMerlinKeep;
            sb.AppendLine($"{(okGrant ? "PASS" : "FAIL")} 다음 런 지급 — 보관함 {stage0} → {run.ItemInventory.StagingCount} · 돌려받은 표시 {granted?.fromMerlinKeep}");
            bool notAgain = !MerlinRuneKeep.CaptureOwned(run).Contains(granted);
            sb.AppendLine($"{(notAgain ? "PASS" : "FAIL")} 돌려받은 룬은 다음 맡기기 후보에서 빠진다");
            bool keptUntilSave = !string.IsNullOrEmpty(gd.Data.keptRune) && run.KeptRunePending && MerlinRuneKeep.GrantPending(run) == null;
            sb.AppendLine($"{(keptUntilSave ? "PASS" : "FAIL")} 첫 런 저장 전엔 메타에 남는다(대기방에서 꺼도 안 사라짐) · 두 번 지급 없음");
            MerlinRuneKeep.CommitClaim(run);
            bool cleared = string.IsNullOrEmpty(gd.Data.keptRune) && !run.KeptRunePending;
            sb.AppendLine($"{(cleared ? "PASS" : "FAIL")} 첫 런 저장(CommitClaim) 뒤 메타 비움");
            run.KeptRunePending = wasPending;
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            if (granted != null && run?.ItemInventory != null && run.ItemInventory.IsStaging(granted.instanceId)) run.ItemInventory.DiscardFromStaging(granted);
            if (gd?.Data != null && gd.Data.keptRune != (before ?? ""))
            {
                gd.Data.keptRune = before ?? "";
                gd.SaveAsync().Forget();
            }
            File.WriteAllText("Temp/merlin_keep1102.txt", sb.ToString());
            Debug.Log("[MK1102] 끝 → Temp/merlin_keep1102.txt");
        }
    }

    /// <summary>이름이 <paramref name="panel"/>인 칸 안 n번째 글(빈 글은 건너뜀).</summary>
    private static string FindText(GameObject root, string panel, int n)
    {
        var p = root.transform.Find(panel);
        if (p == null) return "";
        int i = 0;
        foreach (var t in p.GetComponentsInChildren<TMP_Text>())
        {
            if (string.IsNullOrEmpty(t.text)) continue;
            if (i++ == n) return t.text;
        }
        return "";
    }
}
#endif
