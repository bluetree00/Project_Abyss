#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-26 무기 개편 — 재련으로 자라는 증강 카드(그림자 파트너 · 검풍)의 「재련 n회」 · 「다음 재련 X → Y」 줄과 대검 새 증강 두 장 글을 띄워 본다(런 중).
/// 무기 0의 강화 단계를 잠깐 바꿔 재련 1회 · 4회(다 자람) 두 번 띄운다. 결과 Temp/aug1026.txt · Temp/ui_shots/AUG26_*.png · 로그 「[Aug26] 끝」.
/// </summary>
public static class Aug1026ProbeEditor
{
    private const string Tag = "[Aug26]";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem("RelicFairy/Debug/10-26 재련 자람 증강 카드 · 대검 새 증강 글 (런 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null) { Debug.LogWarning($"{Tag} 런 안에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("10-26 재련 자람 증강 카드\n");
        int pass = 0, total = 0;
        void Check(string what, bool ok) { total++; if (ok) pass++; sb.AppendLine($"CHECK {what}: {ok}"); }
        var w = GameRunBootstrapper.Instance.Run.Player.WeaponManager.Weapon0Data;
        int lv0 = w != null ? w.enhanceLevel : 0;
        try
        {
            if (w == null) { sb.AppendLine("  무기 0 없음"); return; }
            int evolvedAt = MeleeAugmentCatalog.EvolvedAt;
            foreach (var (lv, tag) in new[] { (evolvedAt + MeleeAugmentDraft.LevelStep, "n1"), (evolvedAt + MeleeAugmentDraft.LevelStep * 4, "n4") })
            {
                w.enhanceLevel = lv;
                var (texts, pop) = await ShowAsync(w, AugmentTier.Gold, "aug_katana_shadow", "aug_katana_gust");
                sb.AppendLine($"  [{tag} · +{lv} · 재련 {MeleeAugmentCatalog.ReforgesSinceEvolution(w)}회]");
                foreach (var t in texts) sb.AppendLine("    " + t.Replace("\n", " / "));
                bool rankOk = texts.Count == 2 && texts.TrueForAll(t => t.Contains($"재련 {MeleeAugmentCatalog.ReforgesSinceEvolution(w)}회"));
                Check($"자람 카드 단계 자리 = 「재련 n회」({tag})", rankOk);
                if (tag == "n1") Check("자람 카드 아래 「다음 재련 X → Y」(그림자 = 따라 치는 피해 · 검풍 = 확률 · 검기 피해)",
                                       texts.Count == 2 && texts[0].Contains("다음 재련  따라 치는 피해") && texts[1].Contains("확률") && texts[1].Contains("검기 피해"));
                else Check("다 자랐으면(재련 4회) 「다 자랐다」", texts.Count == 2 && texts.TrueForAll(t => t.Contains("다 자랐다")));
                await Shot($"AUG26_growth_{tag}");
                if (pop != null) Managers.UI.ClosePopupUI(pop);
                await UniTask.Delay(600, ignoreTimeScale: true);
            }

            // 대검 자람 증강 두 장(10-26 대검 R 개편 — 칼끝 베기 · 휩쓸기) — 이름표 · 단위(휩쓸기 = 초)
            w.enhanceLevel = evolvedAt + MeleeAugmentDraft.LevelStep;
            var (tt, tp) = await ShowAsync(w, AugmentTier.Gold, MeleeAugmentCatalog.GreatTip, MeleeAugmentCatalog.GreatSweep);
            sb.AppendLine("  [대검 자람 증강]");
            foreach (var t in tt) sb.AppendLine("    " + t.Replace("\n", " / "));
            Check("칼끝 베기 = 「칼끝 추가 피해 X% → Y%」 · 휩쓸기 = 「한 명당 R 대기 …초 · 지속 …초」",
                  tt.Count == 2 && tt[0].Contains("칼끝 추가 피해") && tt[1].Contains("한 명당 R 대기") && tt[1].Contains("초"));
            await Shot("AUG26_great_growth");
            if (tp != null) Managers.UI.ClosePopupUI(tp);
            await UniTask.Delay(600, ignoreTimeScale: true);

            // 대검 새 증강 두 장(단계 있음) — 글만 본다
            w.enhanceLevel = lv0;
            var (gt, gp) = await ShowAsync(w, AugmentTier.Silver, "aug_great_reach", "aug_great_final");
            sb.AppendLine("  [대검 새 증강]");
            foreach (var t in gt) sb.AppendLine("    " + t.Replace("\n", " / "));
            Check("대검 새 증강 두 장 글이 찬다", gt.Count == 2 && gt.TrueForAll(t => t.Length > 10));
            await Shot("AUG26_great");
            if (gp != null) Managers.UI.ClosePopupUI(gp);
            await UniTask.Delay(600, ignoreTimeScale: true);
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            if (w != null) w.enhanceLevel = lv0;
            sb.AppendLine($"CHECK 합계 {pass}/{total}");
            File.WriteAllText("Temp/aug1026.txt", sb.ToString());
            Debug.Log($"{Tag} 끝\n" + sb);
        }
    }

    /// <summary>증강 고르기 창을 이 두 장으로 띄우고 다 뒤집힌 뒤 카드마다 「이름 | 단계 | 효과 | 지금(다음 재련)」을 돌려준다.</summary>
    private static async UniTask<(System.Collections.Generic.List<string>, UI_MeleeAugmentPopup)> ShowAsync(WeaponData w, AugmentTier tier, string a, string b)
    {
        var list = new System.Collections.Generic.List<string>();
        var pop = await Managers.UI.ShowPopupUIAndGetAsync<UI_MeleeAugmentPopup>();
        if (pop == null) return (list, null);
        pop.Show(new MeleeAugmentOffer { Weapon = w, Level = w.enhanceLevel, Tier = tier, Cards = new[] { new MeleeAugmentCard(a, 1), new MeleeAugmentCard(b, 1) } });
        await UniTask.Delay(2600, ignoreTimeScale: true);
        var cards = typeof(UI_MeleeAugmentPopup).GetField("_cards", Inst)?.GetValue(pop) as System.Array;
        if (cards == null) return (list, pop);
        foreach (var c in cards)
        {
            if (c == null) continue;
            string F(string n) => (c.GetType().GetField(n)?.GetValue(c) as TMP_Text)?.text ?? "";
            list.Add($"{F("Name")} | {F("Rank")} | {F("Effect")} | {F("Now")}");
        }
        return (list, pop);
    }

    private static UniTask Shot(string name)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, 0 }) : UniTask.CompletedTask;
    }
}
#endif
