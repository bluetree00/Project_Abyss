using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// [실측 · 런 안에서] 09-29 오후 피드백 — 서약서(수치 · 색 · 하단 줄) · 룬 획득 카드(글자 맞춤) · 룬판(배경 · 테두리 · 시너지 줄).
/// 결과: Temp/ui_shots/FB_*.png · Temp/fb0929b_probe.txt. 세이브 · 판 · 보관함은 건드리지 않는다(창만 열고 닫는다).
/// </summary>
public static class UiFeedback0929bProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const BindingFlags StaticNonPub = BindingFlags.Static | BindingFlags.NonPublic;

    private static readonly (string id, string elem)[] Offer =
    {
        ("item_t2_calm_crit", "ICE"), ("item_t3_fire_ember", "FIRE"), ("item_t2_consecutive_power", "LIGHT"), ("item_t1_weight", "GRASS"),
    };

    [MenuItem("RelicFairy/UI/09-29b 서약·룬 카드·룬판 실측 (런 안에서)")]
    private static void Run()
    {
        if (!Application.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null)
        {
            Debug.LogWarning("[Fb0929b] 런 안에서 실행해야 한다.");
            return;
        }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder();
        var run = GameRunBootstrapper.Instance.Run;
        Invoke("HideTestHubGui");
        try
        {
            // ① 서약서 — 원인 첫 장 · 둘째 장(효과 카드 수치가 계수에 따라 바뀐다)
            var cov = await Managers.UI.ShowPopupUIAndGetAsync<UI_CovenantAssemble>();
            cov.Setup(false, new System.Random(7));
            await UniTask.Delay(1400, ignoreTimeScale: true);
            await Shot("FB_Cov_A");
            DumpTexts(sb, "서약 A", cov.transform);
            typeof(UI_CovenantAssemble).GetMethod("Select", Inst)?.Invoke(cov, new object[] { true, 1 });
            await UniTask.Delay(600, ignoreTimeScale: true);
            await Shot("FB_Cov_B");
            DumpTexts(sb, "서약 B(원인 둘째)", cov.transform);
            Managers.UI.CloseAllPopupUI();
            await UniTask.Delay(500, ignoreTimeScale: true);

            // ② 룬 획득 — 긴 조건부 효과 · 두 효과 · 계열 칩 섞어 4장
            var cands = new List<(RuntimeItemData, ItemSO)>();
            foreach (var (id, elem) in Offer)
            {
                var entries = Managers.ItemData?.GetItem(id);
                var d = entries != null ? RuntimeItemData.FromServer(entries) : null;
                if (d == null) { sb.AppendLine($"룬 없음 {id}"); continue; }
                d.element = elem;
                cands.Add((d, ItemSORegistry.Find(id)));
            }
            var pick = await Managers.UI.ShowPopupUIAndGetAsync<UI_RuneSelectPopup>();
            pick.Setup(cands, run.ItemInventory);
            await UniTask.Delay(2200, ignoreTimeScale: true);   // 뒤집기 연출이 끝나게
            await Shot("FB_RuneSelect");
            foreach (var fx in pick.GetComponentsInChildren<RectTransform>(true))
            {
                if (fx.name != "Effects") continue;
                float pref = UnityEngine.UI.LayoutUtility.GetPreferredHeight(fx);
                sb.AppendLine($"효과 칸 {fx.rect.height:0} · 글 {pref:0} → {(pref <= fx.rect.height + 0.5f ? "들어감" : "넘침")}");
            }
            Managers.UI.CloseAllPopupUI();
            await UniTask.Delay(500, ignoreTimeScale: true);

            // ③ 룬판 — 자연 배경 · 외곽선 · 시너지 줄
            if (UI_GridPanel.Instance == null) Managers.UI?.ShowOverlayUI<UI_GridPanel>();
            UI_GridPanel.Instance?.ShowWithNewItem(null);
            await UniTask.Delay(1000, ignoreTimeScale: true);
            await Shot("FB_Grid");
            var outline = UnityEngine.Object.FindFirstObjectByType<UIRectilinearOutline>();
            sb.AppendLine($"판 외곽선 {(outline != null ? "있음" : "없음")}");
            typeof(UI_GridPanel).GetMethod("ClosePanel", Inst)?.Invoke(UI_GridPanel.Instance, null);
            await UniTask.Delay(600, ignoreTimeScale: true);
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            Managers.UI.CloseAllPopupUI();
            Invoke("RestoreTestHubGui");
            File.WriteAllText(Path.Combine("Temp", "fb0929b_probe.txt"), sb.ToString());
            Debug.Log("[Fb0929b] 끝 — Temp/fb0929b_probe.txt\n" + sb);
        }
    }

    private static void DumpTexts(StringBuilder sb, string label, Transform root)
    {
        sb.AppendLine($"── {label}");
        foreach (var name in new[] { "PreviewSentence", "PreviewCondition", "PreviewCoef", "PreviewEffect", "ForgeSummary" })
            foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
                if (t.name == name) sb.AppendLine($"  {name}: {t.text}");
        int k = 0;
        foreach (var card in root.GetComponentsInChildren<UI_AssembleCard>(true))
        {
            var sub = typeof(UI_AssembleCard).GetField("_subText", Inst)?.GetValue(card) as TMP_Text;
            if (sub != null) sb.AppendLine($"  카드{k++}: {sub.text}");
        }
    }

    private static async UniTask Shot(string name)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", StaticNonPub);
        if (m != null) await (UniTask)m.Invoke(null, new object[] { name, 0 });
    }

    private static void Invoke(string method)
        => typeof(UILayoutRuntimeProbeEditor).GetMethod(method, StaticNonPub)?.Invoke(null, null);
}
