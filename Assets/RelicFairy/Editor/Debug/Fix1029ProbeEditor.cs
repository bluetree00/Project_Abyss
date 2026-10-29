#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// 10-29 4c 묶음 실측.
/// A(거점). 지난 세팅 — 원거리 · 유물을 고른 채 성문 기록 → 비우고 거점 다시 읽기 → 그대로 쥐었나 · 고른 유물 제단 숨김 · 성문 준비 · 파츠 공방 꺼짐 ·
///          귀환 자리 = 제단 앞 · 제단 이름표 「다음 칸까지」 · 「시작 파츠 둘」 걷힘 · 시기 칸 5개.
/// B(런). 시작 파츠 = 분열 + 연 파츠 Lv1 · 악몽 길들이기 · 빛 붙잡기 값 · 시작 선물 창(룬 갈래 → 룬 창 · 유물 갈래 → 진화 없는 기억 창).
/// 결과 Temp/fix1029.txt · Temp/ui_shots/F29_*.png · 로그 「[Fix29] 끝」.
/// </summary>
public static class Fix1029ProbeEditor
{
    private const string Tag = "[Fix29]";
    private const string ShotDir = "Temp/ui_shots";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

    private static StringBuilder s_sb;
    private static int s_pass, s_total;

    private static void Check(string what, bool ok, string detail = "")
    {
        s_total++; if (ok) s_pass++;
        s_sb.AppendLine($"CHECK {what}: {ok}{(detail.Length > 0 ? "  — " + detail : "")}");
    }

    // ── A. 거점 ───────────────────────────────────────────────

    [MenuItem("RelicFairy/Debug/10-29 A 거점 지난 세팅 · 파츠 · 제단 이름표 (거점)")]
    private static void RunBaseCamp()
    {
        if (!EditorApplication.isPlaying || BaseCampBootstrapper.Instance == null) { Debug.LogWarning($"{Tag} 거점 플레이 중에"); return; }
        BaseCampAsync().Forget();
    }

    private static async UniTaskVoid BaseCampAsync()
    {
        Begin("10-29 A 거점");
        var data = BackendGameData.Instance?.Data;
        int essenceBefore = data?.abyssEssence ?? 0;
        try
        {
            var lo = AppBootstrapper.Instance.Loadout;
            var forge = Object.FindFirstObjectByType<WeaponForgeAltar>();
            var bow = typeof(WeaponForgeAltar).GetField("bowOption", Any)?.GetValue(forge) as WeaponSO;
            var relic = Object.FindObjectsByType<RelicAltar>(FindObjectsInactive.Include, FindObjectsSortMode.None).Select(a => a.RelicClass).FirstOrDefault(r => r != null);
            Check("준비 — 활 · 유물 찾음", bow != null && relic != null, $"{bow?.name} · {relic?.name}");
            if (bow == null || relic == null) return;

            lo.SetWeaponSlot1(bow);
            lo.SetRelic(relic);
            LastLoadoutMemory.Capture(lo);
            Check("① 성문 통과 때 원거리 · 유물을 적는다", data.lastLoadout == $"{bow.name}|{relic.name}", data.lastLoadout);

            // 귀환 흉내 — 원거리 · 유물을 비우고 거점을 다시 읽는다(런 끝의 Loadout.Clear와 같은 상태 · 무형검은 둔다)
            lo.SetWeaponSlot1(null);
            lo.SetRelic(null);
            var oldBoot = BaseCampBootstrapper.Instance;
            AppBootstrapper.Instance.RequestLoad(Define.Scene.BaseCamp);
            float waited = 0f;
            while ((BaseCampBootstrapper.Instance == null || BaseCampBootstrapper.Instance == oldBoot || !BaseCampBootstrapper.Instance.IsReady) && waited < 40f)
            { await Wait(0.25f); waited += 0.25f; }
            Check("② 거점 다시 읽기 끝", BaseCampBootstrapper.Instance != null && BaseCampBootstrapper.Instance != oldBoot, $"{waited:0.0}초");
            await Wait(1.0f);
            await ShotAsync("F29_arrival");

            Check("③ 원거리 그대로", lo.WeaponSlot1 == bow, lo.WeaponSlot1 != null ? lo.WeaponSlot1.name : "없음");
            Check("④ 유물 그대로", lo.Relic == relic, lo.Relic != null ? lo.Relic.name : "없음");
            Check("⑤ 복원 알림 표시", LastLoadoutMemory.RestoredThisVisit, LastLoadoutMemory.Describe(lo));
            var picked = Object.FindObjectsByType<RelicAltar>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(a => a.RelicClass == relic);
            Check("⑥ 고른 유물 제단은 숨김(고른 상태)", picked != null && !picked.gameObject.activeInHierarchy);
            bool ready = (bool)(typeof(BaseCampDungeonGate).GetMethod("IsLoadoutReady", Any)?.Invoke(null, null) ?? false);
            Check("⑦ 성문 준비(파츠 없이)", ready);
            var parts = Object.FindObjectsByType<StartPartStation>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Check("⑧ 파츠 공방 꺼짐", parts.Length > 0 && parts.All(p => !p.gameObject.activeInHierarchy), $"{parts.Length}개");

            var player = Managers.Player?.PlayerTransform;
            var altar = Object.FindFirstObjectByType<WorldAwakeningAltar>();
            float dist = player != null && altar != null ? Vector3.Distance(player.position, altar.transform.position) : -1f;
            Check("⑨ 귀환 자리 = 제단 앞(10 m 안)", dist >= 0f && dist < 10f, $"{dist:0.0} m");

            // 제단 이름표 — 정수 0이면 「다음 칸까지」
            if (data != null && altar != null)
            {
                data.abyssEssence = 0;
                typeof(WorldAwakeningAltar).GetField("_labelEssence", Any)?.SetValue(altar, -1);
                typeof(WorldAwakeningAltar).GetMethod("RefreshLabel", Any)?.Invoke(altar, null);
                var label = typeof(WorldAwakeningAltar).GetField("_worldText", Any)?.GetValue(altar) as TMP_Text;
                string txt = label != null ? label.text : "";
                Check("⑩ 살 칸 0 → 「…까지 N」", txt.Contains("까지"), txt.Replace("\n", " / "));
                if (player != null && dist > 6f)
                {
                    var cc = player.GetComponent<CharacterController>();
                    if (cc != null) cc.enabled = false;
                    player.position = altar.transform.position + altar.transform.forward * 4f;
                    if (cc != null) cc.enabled = true;
                }
                await Wait(0.8f);
                await ShotAsync("F29_altar_label");
            }

            Check("⑪ 「시작 파츠 둘」 걷힘", MemoryAltarCatalog.Get(MemoryAltarCatalog.StartParts2) == null);
            string[] era = { MemoryAltarCatalog.LibGiftWide, MemoryAltarCatalog.LibReroll, MemoryAltarCatalog.NmLight, MemoryAltarCatalog.NmTame, MemoryAltarCatalog.NmGift };
            var lines = era.Select(id => { var s = MemoryAltarService.GetState(id); return $"{s.Node?.DisplayName}({(s.BlockedByRequirement ? "자물쇠" : "열림")} · {s.Cost})"; });
            Check("⑫ 시기 칸 5개", era.All(id => MemoryAltarCatalog.Get(id) != null), string.Join(" · ", lines));
        }
        catch (System.Exception e) { s_sb.AppendLine("예외: " + e); }
        finally
        {
            if (data != null) data.abyssEssence = essenceBefore;
            End("Temp/fix1029_a.txt");
        }
    }

    // ── B. 런 ─────────────────────────────────────────────────

    [MenuItem("RelicFairy/Debug/10-29 B 시작 파츠 · 악몽 규칙 · 시작 선물 (런 중)")]
    private static void RunInRun()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null) { Debug.LogWarning($"{Tag} 런 안에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        Begin("10-29 B 런");
        var eraField = typeof(StoryProgress).GetField("s_debugOverride", Any);
        object eraBefore = eraField?.GetValue(null);
        var data = BackendGameData.Instance?.Data;
        string unlockedBefore = data?.unlockedIds;
        var run = GameRunBootstrapper.Instance.Run;
        try
        {
            // ① 시작 파츠
            var st = RangedPartsState.Current;
            var sbp = new StringBuilder();
            bool okParts = true;
            foreach (var def in Managers.WeaponParts.All)
            {
                int lv = st.LevelOf(def.part_id);
                bool open = RangedPartsDraft.IsUnlocked(def.part_id);
                sbp.Append($"{def.part_name} {(open ? "열림" : "잠김")} Lv{lv} · ");
                if (open && lv < 1) okParts = false;
                if (!open && lv > 0) okParts = false;
            }
            Check("① 시작 파츠 = 연 파츠 전부 Lv1 이상 · 잠긴 파츠 0", okParts, sbp.ToString());

            // ② 악몽 규칙 — 칸 없음 / 칸 있음
            eraField?.SetValue(null, (int?)2);
            float dodge0 = NightmareRules.DodgeStaminaMultiplier, cd0 = NightmareRules.WeaponSkillCooldownMultiplier;
            if (data != null) data.unlockedIds = (unlockedBefore ?? "") + ",nm_tame:1,nm_light:1";
            float dodge1 = NightmareRules.DodgeStaminaMultiplier, cd1 = NightmareRules.WeaponSkillCooldownMultiplier;
            var rules = new System.Collections.Generic.List<NightmareRules.RuleInfo>();
            NightmareRules.CollectActive(rules);
            string ruleText = string.Join(" / ", rules.Select(r => r.Description));
            Check("② 악몽 길들이기 — 챕터 규칙 약해짐", (run.CurrentChapter != ChapterId.Chapter1 || (Mathf.Approximately(dodge0, 1.4f) && Mathf.Approximately(dodge1, 1.2f)))
                                                     && (run.CurrentChapter != ChapterId.Chapter4 || (Mathf.Approximately(cd0, 1.25f) && Mathf.Approximately(cd1, 1.12f))),
                  $"Ch{(int)run.CurrentChapter} 회피 {dodge0} → {dodge1} · 스킬 {cd0} → {cd1}");
            Check("③ 빛 붙잡기 · 길들임 글", ruleText.Contains("3개까지") && (run.CurrentChapter == ChapterId.Chapter3 || ruleText.Contains("길들임")), ruleText);
            if (data != null) data.unlockedIds = unlockedBefore;

            // ③ 시작 선물 — 해방기
            eraField?.SetValue(null, (int?)1);
            var ct = GameRunBootstrapper.Instance.GetCancellationTokenOnDestroy();
            int oreBefore = run.FuelBank != null ? run.FuelBank.Get(FuelKind.RuneOre) : 0;
            StartGift.OfferAsync(run, ct).Forget();
            await Wait(1.2f);
            Check("④ 해방기 시작 선물 창", StartGift.IsOpen);
            await ShotAsync("F29_gift_window");
            if (!Click("Card_룬 하나")) s_sb.AppendLine("룬 카드 못 찾음");
            await Wait(2.8f);
            var rune = Object.FindFirstObjectByType<UI_RuneSelectPopup>();
            Check("⑤ 룬 갈래 → 룬 고르기 창", rune != null);
            await ShotAsync("F29_gift_rune");
            if (rune != null)
            {
                typeof(UI_RuneSelectPopup).GetMethod("OnSkipClicked", Any)?.Invoke(rune, null);
                await Wait(1.5f);
                int ore = run.FuelBank != null ? run.FuelBank.Get(FuelKind.RuneOre) : 0;
                Check("⑥ 넘기면 원석", ore > oreBefore, $"{oreBefore} → {ore}");
            }

            if (AppBootstrapper.Instance?.Loadout?.Relic != null)
            {
                StartGift.OfferAsync(run, ct).Forget();
                await Wait(1.2f);
                Click("Card_유물 기억 하나");
                await Wait(2.5f);
                var relic = Object.FindFirstObjectByType<UI_RelicPartDraftPopup>();
                var draft = relic != null ? typeof(UI_RelicPartDraftPopup).GetField("_draft", Any)?.GetValue(relic) as RelicDraft : null;
                Check("⑦ 유물 갈래 → 기억 창 · 진화 카드 없음", draft != null && !draft.Cards.Exists(c => c.Kind == RelicDraftCardKind.Evolution),
                      draft != null ? string.Join(" · ", draft.Cards.Select(c => c.Kind.ToString())) : "창 없음");
                await ShotAsync("F29_gift_relic");
                if (relic != null)
                {
                    typeof(UI_RelicPartDraftPopup).GetField("_selected", Any)?.SetValue(relic, 0);
                    typeof(UI_RelicPartDraftPopup).GetMethod("OnConfirmClicked", Any)?.Invoke(relic, null);
                    await Wait(3f);
                }
            }
        }
        catch (System.Exception e) { s_sb.AppendLine("예외: " + e); }
        finally
        {
            eraField?.SetValue(null, eraBefore);
            if (data != null) data.unlockedIds = unlockedBefore;
            End("Temp/fix1029_b.txt");
        }
    }

    // ── 공용 ──────────────────────────────────────────────────

    private static void Begin(string title)
    {
        s_sb = new StringBuilder(title + "\n");
        s_pass = s_total = 0;
        Directory.CreateDirectory(ShotDir);
    }

    private static void End(string path)
    {
        s_sb.AppendLine($"\n통과 {s_pass}/{s_total}");
        File.WriteAllText(path, s_sb.ToString());
        Debug.Log($"{Tag} 끝 — {s_pass}/{s_total} · {path}");
    }

    private static bool Click(string name)
    {
        var go = GameObject.Find("@StartGift");
        var t = go != null ? go.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == name) : null;
        if (t == null) return false;
        t.onClick.Invoke();
        return true;
    }

    private static UniTask Wait(float seconds) => UniTask.Delay(System.TimeSpan.FromSeconds(seconds), DelayType.Realtime);

    private static async UniTask ShotAsync(string name)
    {
        var runner = Object.FindFirstObjectByType<Managers>();
        if (runner == null) return;
        await UniTask.WaitForEndOfFrame(runner);
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        if (tex == null) return;
        File.WriteAllBytes(Path.Combine(ShotDir, name + ".png"), tex.EncodeToPNG());
        Object.Destroy(tex);
    }
}
#endif
