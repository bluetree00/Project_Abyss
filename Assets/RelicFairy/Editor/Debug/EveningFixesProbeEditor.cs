#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-14 저녁 사용자 요청 묶음 확인 — ① 상점 룬 카드 효과 글 숨김(누르면 상세 열) ② 챕터 4 해금 걷음(마지막 챕터 = 레지스트리 · 제단 노드 없음 · 환급)
/// ③ 정제소 영웅 · 전설 확률(제단 해금으로 접지 않음). 결과: Temp/evening_fixes_probe.txt · Temp/ui_shots/EVE_Shop.png · 로그 「[EveningFixes] 끝」. 플레이 중(테스트 허브).
/// </summary>
public static class EveningFixesProbeEditor
{
    private const BindingFlags Any = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem("RelicFairy/Debug/10-14 상점 룬 카드 · 챕터 4 · 정제 등급 확인 (플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[EveningFixes] 플레이 모드에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("상점 룬 카드 · 챕터 4 · 정제 등급 확인\n");
        int pass = 0, total = 0;
        void Check(string what, bool ok) { total++; if (ok) pass++; sb.AppendLine($"CHECK {what}: {ok}"); }
        GameObject ctrlGo = null;
        UI_ShopPanel shop = null;
        try
        {
            // ── ① 상점 ──
            var run = GameRunBootstrapper.Instance?.Run;
            shop = await Managers.UI.ShowPopupUIAndGetAsync<UI_ShopPanel>();
            ctrlGo = new GameObject("~EveProbeShop");
            var sc = ctrlGo.AddComponent<ShopRoomController>();
            typeof(ShopRoomController).GetField("_run", Any)?.SetValue(sc, run);
            typeof(ShopRoomController).GetField("_peddler", Any)?.SetValue(sc, AbyssPeddlerCatalog.Build(new System.Random(7)));
            shop.Bind(sc);
            await UniTask.Delay(900, ignoreTimeScale: true);
            int rune = -1, other = -1;
            for (int i = 0; i < sc.Products.Count; i++)
            {
                if (sc.Products[i].Category == ShopProductCategory.Rune) { if (rune < 0) rune = i; }
                else if (other < 0) other = i;
            }
            sb.AppendLine($"  진열 {sc.Products.Count}장 · 룬 카드 {rune} · 다른 카드 {other}");
            var runeEff  = rune  >= 0 ? shop.transform.Find($"Window/Card{rune}")?.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.name == "Effect") : null;
            var otherEff = other >= 0 ? shop.transform.Find($"Window/Card{other}")?.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.name == "Effect") : null;
            Check("룬 카드 효과 줄 비어 있음", rune >= 0 && runeEff != null && string.IsNullOrEmpty(runeEff.text));
            Check($"다른 상품 카드 효과 줄은 그대로(「{otherEff?.text}」)", other < 0 || (otherEff != null && !string.IsNullOrEmpty(otherEff.text)));
            if (rune >= 0)
            {
                typeof(UI_ShopPanel).GetMethod("Select", Any)?.Invoke(shop, new object[] { rune });
                await UniTask.Delay(300, ignoreTimeScale: true);
                var sel = typeof(UI_ShopPanel).GetField("_selEffect", Any)?.GetValue(shop) as TMP_Text;
                Check("룬 카드를 누르면 상세 열에 효과", sel != null && !string.IsNullOrEmpty(sel.text));
            }
            var deal = sc.SpecialDeal;
            var dealCat = typeof(UI_ShopPanel).GetField("_dealCat", Any)?.GetValue(shop) as TMP_Text;
            if (deal != null && dealCat != null)
                Check($"특가 줄(「{dealCat.text}」) — 룬이면 효과 글 없음", deal.Category != ShopProductCategory.Rune || !dealCat.text.Contains("·"));
            var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
            if (m != null) await (UniTask)m.Invoke(null, new object[] { "EVE_Shop", 0 });

            // ── ② 챕터 4 ──
            var regGuid = AssetDatabase.FindAssets("t:ChapterRegistry").FirstOrDefault();
            var reg = regGuid != null ? AssetDatabase.LoadAssetAtPath<ChapterRegistry>(AssetDatabase.GUIDToAssetPath(regGuid)) : null;
            var s = new GameRunSession();
            s.BindFinalChapter(reg);
            var final = (ChapterId)typeof(GameRunSession).GetProperty("FinalChapter", Any).GetValue(s);
            Check($"마지막 챕터 = 레지스트리({(reg != null ? reg.FinalChapter.ToString() : "없음")}) · 지금 {final}", reg != null && final == reg.FinalChapter && final == ChapterId.Chapter4);
            Check("제단 카탈로그에 「챕터 4 개방」 없음", MemoryAltarCatalog.Get(MemoryAltarCatalog.Chapter4) == null);
            var abyss = MemoryAltarCatalog.Get(MemoryAltarCatalog.AbyssDepth);
            Check("「심연 입장」 부모 없음(조건 = 엔딩 그대로)", abyss != null && abyss.Parents.Count == 0);
            var layout = MemoryAltarLayout.Compute(MemoryAltarCatalog.All);
            Check($"제단 배치가 노드 전부를 놓음({layout.Count}/{MemoryAltarCatalog.All.Count})", layout.Count == MemoryAltarCatalog.All.Count);
            var ugd = new UserGameData();
            typeof(UserGameData).GetField("unlockedIds", Any)?.SetValue(ugd, MemoryAltarCatalog.Chapter4);
            int ess0 = ugd.abyssEssence;
            var batch = typeof(MemoryAltarService).GetMethod("RefundBatch", Any);
            int r1 = (int)batch.Invoke(null, new object[] { ugd, MemoryAltarCatalog.RemovedNodes1014, MemoryAltarCatalog.Rec.RemovedNodesRefunded1014 });
            int r2 = (int)batch.Invoke(null, new object[] { ugd, MemoryAltarCatalog.RemovedNodes1014, MemoryAltarCatalog.Rec.RemovedNodesRefunded1014 });
            Check($"「챕터 4 개방」 산 세이브 환급 800 한 번만(1차 {r1} · 2차 {r2} · 정수 {ess0} → {ugd.abyssEssence})", r1 == 800 && r2 == 0 && ugd.abyssEssence == ess0 + 800);

            // ── ③ 정제 등급 ──
            var (rare, epic, legend) = new RefineryService(null, null).CurrentOdds();
            sb.AppendLine($"  정제 확률 희귀 {rare:P0} · 영웅 {epic:P0} · 전설 {legend:P0}");
            Check("정제소에서 영웅 · 전설이 나옴(제단 해금과 무관)", epic > 0.3f && legend > 0.1f && Mathf.Abs(rare + epic + legend - 1f) < 0.001f);
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            if (shop != null) Managers.UI.ClosePopupUI(shop);
            if (ctrlGo != null) Object.Destroy(ctrlGo);
            sb.AppendLine($"CHECK 합계 {pass}/{total}");
            File.WriteAllText("Temp/evening_fixes_probe.txt", sb.ToString());
            Debug.Log("[EveningFixes] 끝\n" + sb);
        }
    }
}
#endif
