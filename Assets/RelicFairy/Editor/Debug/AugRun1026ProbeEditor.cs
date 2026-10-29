#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-26 사용자 「런 중 새로운 카타나 증강이랑 대검 증강 나오는 거 체크해 줄래 · 팝업 이미지랑」.
/// 런 안에서 무기 0을 카타나 · 대검(진화 뒤 +12 단계)으로 바꿔 끼우고, <b>실제 뽑기</b>(MeleeAugmentDraft.TryOfferForEnhance)가 새 증강
/// (카타나 그림자 파트너 · 검풍 / 대검 칼끝 베기 · 휩쓸기)을 내는 판을 찾아 실제 고르기 창(MeleeAugmentDraftHost가 여는 것)을 띄워 찍는다.
/// 같은 씨앗을 먼저 흉내(RollTier → Draw)로 훑어 찾고 — 찾은 씨앗으로 진짜 뽑기를 부른다(창 · 카드 그림 그대로). 몇 판째에 나왔는지도 적는다.
/// 결과 Temp/augrun1026.txt · Temp/ui_shots/AUGRUN_*.png · 로그 「[AugRun26] 끝」. 고른 카드는 무기에 붙는다(테스트 런).
/// </summary>
public static class AugRun1026ProbeEditor
{
    private const string Tag = "[AugRun26]";
    private const BindingFlags Any = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem("RelicFairy/Debug/10-26 런 중 새 카타나 · 대검 증강 제시 · 고르기 창 촬영 (런 중)")]
    private static void Run()
    {
        var p = GameRunBootstrapper.Instance?.Run?.Player;
        if (!EditorApplication.isPlaying || p == null) { Debug.LogWarning($"{Tag} 런 안에서"); return; }
        RunAsync(p, p.GetCancellationTokenOnDestroy()).Forget();
    }

    private static async UniTaskVoid RunAsync(PlayerController p, CancellationToken ct)
    {
        var sb = new StringBuilder("10-26 런 중 새 증강 제시 · 고르기 창\n");
        int pass = 0, total = 0;
        void Check(string what, bool ok, string detail = "") { total++; if (ok) pass++; sb.AppendLine($"CHECK {what}: {ok}{(detail.Length > 0 ? "  — " + detail : "")}"); }
        try
        {
            typeof(PlayerController).GetField("debugInvincible", Any)?.SetValue(p, true);
            foreach (var mo in Object.FindObjectsByType<RelicFairy.Monster.MonsterBase>(FindObjectsSortMode.None))
                if (mo != null && !mo.IsDead) mo.gameObject.SetActive(false);
            var equip = typeof(CrucibleV3ProbeEditor).GetMethod("EquipAsync", BindingFlags.Static | BindingFlags.NonPublic);
            var draw  = typeof(MeleeAugmentDraft).GetMethod("Draw", Any);

            foreach (var (label, path, wanted, shot) in new[]
            {
                ("카타나", "Assets/RelicFairy/Weapon/Katana/Data/T1_Katana.asset", new[] { MeleeAugmentCatalog.KatanaShadow, MeleeAugmentCatalog.KatanaGust }, "AUGRUN_katana"),
                ("대검",   "Assets/RelicFairy/Weapon/Greatsword/Data/T1_Greatsword.asset", new[] { MeleeAugmentCatalog.GreatTip, MeleeAugmentCatalog.GreatSweep }, "AUGRUN_greatsword"),
            })
            {
                if (equip != null) await (UniTask<bool>)equip.Invoke(null, new object[] { p, path, ct });   // 돌려준 값은 「진화 있는 무기」 기준이라 T1엔 늘 false — 아래 키로 본다
                var wm = p.WeaponManager;
                if (wm.CurrentSlotIndex != PlayerWeaponManager.Slot0) await wm.SwitchToSlotAsync(PlayerWeaponManager.Slot0);
                var w = wm.Weapon0Data;
                bool ok = w != null && path.EndsWith("/" + w.weaponSOKey + ".asset");
                sb.AppendLine($"\n■ {label} — 장착 {ok} · 무기 「{w?.displayName}」 · 갈래 {MeleeAugmentCatalog.KindOf(w)}");
                if (!ok || w == null) { Check($"{label} 장착", false); continue; }
                int level = MeleeAugmentCatalog.EvolvedAt + MeleeAugmentDraft.LevelStep;   // +12 — 진화 뒤 첫 증강 화면
                w.enhanceLevel = level;
                w.highestEnhanceReached = MeleeAugmentCatalog.EvolvedAt;
                w.RecomputeEnhancedStats();

                // 흉내로 씨앗 훑기(TryOfferForEnhance와 같은 순서: RollTier → Draw) — 지금 무기 상태 그대로(고르지 않으니 안 바뀐다)
                int found = -1, offers = 0, hits = 0;
                var seen = new Dictionary<string, int>();
                for (int s = 1; s <= 200; s++)
                {
                    var rng = new System.Random(s);
                    var tier = MeleeAugmentDraft.RollTier(level, false, w.augmentSilverStreak, rng);
                    var cards = draw.Invoke(null, new object[] { w, tier, rng, 2, null }) as List<MeleeAugmentCard>;
                    if (cards == null || cards.Count == 0) continue;
                    offers++;
                    bool has = cards.Any(c => wanted.Contains(c.Id));
                    if (has) { hits++; if (found < 0) found = s; }
                    foreach (var c in cards) seen[c.Id] = seen.TryGetValue(c.Id, out var n) ? n + 1 : 1;
                }
                foreach (var id in wanted)
                {
                    seen.TryGetValue(id, out int n);
                    sb.AppendLine($"  「{MeleeAugmentCatalog.Get(id)?.Name}」 — 화면 {offers}개 중 {n}번({n * 100f / Mathf.Max(1, offers):0}%)");
                }
                Check($"{label} — +12 화면에 새 증강이 나온다", hits > 0, $"새 증강이 낀 화면 {hits}/{offers}({hits * 100f / Mathf.Max(1, offers):0}%) · 첫 씨앗 {found}");
                if (found < 0) continue;

                // 찾은 씨앗으로 진짜 뽑기 — 고르기 창이 열린다
                bool opened = MeleeAugmentDraft.TryOfferForEnhance(w, level, false, new System.Random(found));
                await UniTask.Delay(2200, DelayType.Realtime);
                var pending = MeleeAugmentDraft.Pending;
                string cardsTxt = pending != null ? string.Join(" | ", pending.Cards.Select(c => $"{c.Def?.Name}({MeleeAugmentDef.TierName(c.Def != null ? c.Def.Tier : AugmentTier.Silver)})")) : "없음";
                var popup = Object.FindFirstObjectByType<UI_MeleeAugmentPopup>();
                Check($"{label} — 진짜 뽑기가 고르기 창을 연다 · 새 증강 카드 포함", opened && popup != null && pending != null && pending.Cards.Any(c => wanted.Contains(c.Id)), $"카드 {cardsTxt}");
                await Shot(shot);

                // 새 증강 카드를 고른다(창이 닫히고 무기에 붙는다)
                int pick = pending != null ? System.Array.FindIndex(pending.Cards, c => wanted.Contains(c.Id)) : -1;
                if (pick >= 0) MeleeAugmentDraft.Choose(pick);
                await UniTask.Delay(900, DelayType.Realtime);
                Check($"{label} — 고른 새 증강이 무기에 붙음", pick >= 0 && w.AugmentRank(pending.Cards[pick].Id) > 0);
                Managers.UI.CloseAllPopupUI();
                await UniTask.Delay(400, DelayType.Realtime);
            }
        }
        catch (System.OperationCanceledException) { sb.AppendLine("취소"); }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            sb.AppendLine($"CHECK 합계 {pass}/{total}");
            File.WriteAllText("Temp/augrun1026.txt", sb.ToString());
            Debug.Log($"{Tag} 끝\n" + sb);
        }
    }

    private static UniTask Shot(string name)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, 0 }) : UniTask.CompletedTask;
    }
}
#endif
