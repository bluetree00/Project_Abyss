#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 11-02 Q26 멀린 룬 보관 실측(테스트 허브 런 중) — 결과 Temp/merlin_keep1102.txt · 사진 Temp/ui_shots/MK1102_*.png · 로그 「[MK1102] 끝」.
/// 판의 룬(모자라면 Rare 셋)으로 고르기 창 → 둘째 카드를 눌러 [이 룬을 맡긴다] → 슬롯 메타 keptRune → 다음 런 시작 지급(GrantPending) → 보관함에 같은 룬 · 메타 비움.
/// 끝나면 보관함에 넣은 룬을 버리고 meta keptRune을 처음 값으로 되돌린다.
/// </summary>
public static class MerlinKeep1102ProbeEditor
{
    [MenuItem("RelicFairy/Debug/11-02 멀린 룬 보관 실측 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[MK1102] 플레이 중에만"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("11-02 멀린 룬 보관\n");
        Directory.CreateDirectory("Temp/ui_shots");
        var gd = BackendGameData.Instance;
        string before = gd?.Data?.keptRune;
        RuntimeItemData granted = null;
        var run = GameRunBootstrapper.Instance?.Run;
        try
        {
            if (run == null || gd?.Data == null) { sb.AppendLine($"런 {run != null} · 메타 {gd?.Data != null}"); return; }
            var list = MerlinRuneKeep.CapturePlaced(run);
            sb.AppendLine($"판에 놓인 룬 {list.Count}");
            if (list.Count < 3)
            {
                var pool = ItemSORegistry.GetByRarity(ItemRarity.Rare);
                for (int i = 0; pool != null && i < pool.Count && list.Count < 3; i++)
                {
                    var d = RuntimeItemData.FromSO(pool[i]);
                    if (d == null) continue;
                    d.element = i % 2 == 0 ? "FIRE" : "DARK";
                    list.Add(d);
                }
                sb.AppendLine($"  모자라 Rare로 채움 → {list.Count}");
            }

            var pick = MerlinRuneKeepView.PickAsync(list, default);
            await UniTask.Delay(900, ignoreTimeScale: true);
            ScreenCapture.CaptureScreenshot("Temp/ui_shots/MK1102_pick.png");
            await UniTask.Delay(300, ignoreTimeScale: true);
            var overlay = GameObject.Find("@MerlinRuneKeep");
            if (overlay == null) { sb.AppendLine("FAIL 고르기 창 안 뜸"); return; }
            var cards = new List<Button>();
            Button keepBtn = null;
            foreach (var b in overlay.GetComponentsInChildren<Button>())
            {
                if (b.name == "Card") cards.Add(b);
                if (b.name == "Btn_이 룬을 맡긴다") keepBtn = b;
            }
            sb.AppendLine($"카드 {cards.Count} · 맡기기 단추 {(keepBtn != null ? (keepBtn.interactable ? "눌림(고르기 전인데)" : "잠김(고르기 전)") : "없음")}");
            if (cards.Count < 2 || keepBtn == null) return;
            cards[1].onClick.Invoke();
            await UniTask.Delay(250, ignoreTimeScale: true);
            ScreenCapture.CaptureScreenshot("Temp/ui_shots/MK1102_selected.png");
            await UniTask.Delay(200, ignoreTimeScale: true);
            sb.AppendLine($"둘째 카드 고름 → 맡기기 단추 {(keepBtn.interactable ? "열림" : "잠김")}");
            keepBtn.onClick.Invoke();
            var result = await pick;
            bool sameAsSecond = result != null && ReferenceEquals(result, list[1]);
            sb.AppendLine($"{(sameAsSecond ? "PASS" : "FAIL")} 고른 룬 = {(result != null ? result.displayName + " · " + result.element : "없음")}(둘째 카드 {list[1].displayName})");
            if (result == null) return;

            MerlinRuneKeep.Keep(result);
            string saved = gd.Data.keptRune;
            bool okSave = saved == result.itemId + "|" + result.element;
            sb.AppendLine($"{(okSave ? "PASS" : "FAIL")} 메타 keptRune 「{saved}」 · 맡긴 것 있음 {MerlinRuneKeep.HasPending}");

            int stage0 = run.ItemInventory.StagingCount;
            granted = MerlinRuneKeep.GrantPending(run);
            bool okGrant = granted != null && granted.itemId == result.itemId && granted.element == result.element
                           && run.ItemInventory.IsStaging(granted.instanceId) && run.ItemInventory.StagingCount == stage0 + 1;
            sb.AppendLine($"{(okGrant ? "PASS" : "FAIL")} 다음 런 지급 — 보관함 {stage0} → {run.ItemInventory.StagingCount} · {(granted != null ? granted.displayName + " · " + granted.element : "없음")}");
            bool okClear = string.IsNullOrEmpty(gd.Data.keptRune) && !MerlinRuneKeep.HasPending && MerlinRuneKeep.GrantPending(run) == null;
            sb.AppendLine($"{(okClear ? "PASS" : "FAIL")} 지급 뒤 비움 · 두 번째 지급 없음");
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
}
#endif
