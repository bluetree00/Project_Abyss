#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 10-14 빌드 시너지 UI(83) 촬영 · CHECK — 테스트 허브에서(런이 없으면 가웨인 Ch1 런을 연다 · 「10-13 테스트 허브 런 시작」과 같은 절차).
/// ① 기억 카드 창 4장 · 진화 카드(문장 · 금 고리 · 겹선 · 조건 두 줄) ② 진화 순간(화면 연출) ③ HUD Q 칸 진화 표시 + 진화 알림 한 줄(다시 묶여도 한 줄)
/// ④ 각인 1 · 2 · 3단계 알림 ⑤ 속성 존 단계 알림 + 룬판이 열린 동안 보류 ⑥ 룬 카드 「연타형 / 한 방형」 칩(3장 · 4장).
/// 화면마다 글자 최소 16(기준 1080 px · 배율 반영) · 넘침 0 · 글자 겹침 0. 결과: Temp/ui_shots/SYN_*.png · Temp/synergy_ui_shot.txt · 로그 「[SynergyUiShot] 끝」.
/// 끝나면 유물 기억 · 각인 · 룬 차트 주입 · 존 단계 주입을 되돌린다(진화 효과는 이 플레이어에 묶인 채 남는다 — 플레이 모드를 다시 켤 것).
/// </summary>
public static class SynergyUiShotProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const BindingFlags Stat = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private const float MinFont = 16f;
    private static readonly Regex SizePct = new(@"<size=(\d+(?:\.\d+)?)%>");
    private static readonly Regex SizeAbs = new(@"<size=(\d+(?:\.\d+)?)>");
    private static readonly string[] RuneIds = { "item_t3_fire_spark", "item_t2_heavy_weight", "item_t3_grass_venom", "item_t2_trig_same" };

    private static int s_pass, s_all;

    [MenuItem("RelicFairy/Debug/10-14 빌드 시너지 UI 촬영 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[SynergyUiShot] 플레이 모드(테스트 허브)에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("10-14 빌드 시너지 UI 촬영\n");
        s_pass = s_all = 0;
        PlayerLoadout lo = null;
        string savedIds = null, savedExtra = null;
        var clear    = typeof(PlayerLoadout).GetMethod("ClearRelicMemory", Inst);
        var injected = new Dictionary<string, List<ItemEntry>>();
        Dictionary<string, HashSet<int>> applied = null;
        var appliedBackup = new Dictionary<string, HashSet<int>>();
        try
        {
            if (!await EnsureRunAsync(sb)) return;
            var run = GameRunBootstrapper.Instance.Run;
            var p   = run.Player;
            lo = AppBootstrapper.Instance.Loadout;
            savedIds = lo.SerializeRelicParts(); savedExtra = lo.SerializeRelicExtra();
            string relicId = lo.Relic != null ? lo.Relic.Id.ToString().ToLower() : null;
            var wd = p.WeaponManager != null ? p.WeaponManager.CurrentWeaponData : null;
            sb.AppendLine($"유물 {relicId} · 무기 {wd?.weaponType} · 평타 계수 {(wd != null ? ProcCoefficient.Of(wd.weaponType, WeaponActionType.GroundLight, SkillTierResolver.IsRanged(wd)) : -1f):0.##}");
            HubGui("HideTestHubGui");
            var panel = Object.FindFirstObjectByType<CombatPanelView>(FindObjectsInactive.Include);

            // ── ① 기억 카드 창 — 진화 카드 ──
            clear?.Invoke(lo, null);
            lo.RestoreRelicParts(string.Empty, string.Empty);   // 비운 기억을 알린다 — HUD 진화 표시도 걷힌다
            int added = 0;
            var all = Managers.RelicParts.GetByRelic(relicId);
            foreach (var e in all)
            {
                if (added >= RelicEvolution.FragmentsNeeded) break;
                if (e == null || e.IsReaction || lo.HasRelicPart(e.part_id)) continue;
                lo.AddRelicPart(e.part_id, RelicMemoryGrade.Faint); added++;
            }
            run.EffectManager.Rebuild();
            ForceImprint(BuildFamily.Combo, RelicEvolution.ImprintNeeded);
            var def = RelicEvolution.Ready(lo, relicId) ?? FirstDef(relicId);
            var draft = RelicDraftComposer.Compose(relicId, new List<RelicPartEntry>(all), lo, 0, 0, _ => false, new System.Random(7));
            int evoIdx = draft.Cards.FindIndex(c => c.Kind == RelicDraftCardKind.Evolution);
            if (evoIdx < 0 && def != null)
            {
                draft.Cards.Add(new RelicDraftCard { Kind = RelicDraftCardKind.Evolution, EvolutionId = def.Id, Grade = RelicMemoryGrade.Radiant });
                evoIdx = draft.Cards.Count - 1;
                sb.AppendLine("  (조합기가 진화 카드를 안 냈다 — 손으로 붙였다)");
            }
            Check(sb, $"① 진화 정의 {def?.Id} 「{def?.Name}」 · 카드 {draft.Cards.Count}장 · 진화 자리 {evoIdx}", def != null && evoIdx >= 0);

            var draftPopup = await Managers.UI.ShowPopupUIAndGetAsync<UI_RelicPartDraftPopup>();
            if (draftPopup != null && def != null)
            {
                draftPopup.Setup(draft, false, lo.Relic != null ? lo.Relic.DisplayName : "유물");
                await Wait(2.2f);   // 금 고리 · 겹선이 그려진다(0.15 + 0.5 실시간) — 에디터 프레임이 끊기면 실시간이 덜 흘러 1.2초엔 0.94였다(1차 실측)
                await Shot("SYN_1_DraftEvolution");
                var cards = typeof(UI_RelicPartDraftPopup).GetField("_cards", Inst)?.GetValue(draftPopup) as System.Collections.IList;
                var view  = cards != null && evoIdx < cards.Count ? cards[evoIdx] : null;
                var rt    = view != null ? view.GetType().GetField("Rt")?.GetValue(view) as RectTransform : null;
                var crestImg = Find<Image>(rt, "SymbolCrest");
                var wantCrest = lo.Relic != null ? lo.Relic.Crest : null;
                Check(sb, $"① 진화 문양 = 유물 문장 「{(crestImg != null && crestImg.sprite != null ? crestImg.sprite.name : "(없음)")}」 · 글자 「진」 없음",
                      crestImg != null && crestImg.sprite != null && (wantCrest == null || crestImg.sprite == wantCrest) && Find<TMP_Text>(rt, "SymbolMark") == null);
                var ring  = Find<Image>(rt, "CompletionRing");
                var frame = Find<Image>(rt, "CompletionFrame");
                Check(sb, $"① 「완성」 금 고리 · 겹선 그려짐(채움 {ring?.fillAmount:0.##} · {frame?.fillAmount:0.##})",
                      ring != null && frame != null && ring.fillAmount > 0.99f && frame.fillAmount > 0.99f);
                string partsTxt = Parsed(Find<TMP_Text>(Find<RectTransform>(rt, "EvoCheckParts"), "Label"));
                string impTxt   = Parsed(Find<TMP_Text>(Find<RectTransform>(rt, "EvoCheckImprint"), "Label"));
                Check(sb, $"① 조건 두 줄 「{partsTxt}」 · 「{impTxt}」(조각 {lo.RelicPartIds.Count}/{RelicEvolution.FragmentsNeeded} · 각인 {BuildImprint.Count(def.Family)}/{RelicEvolution.ImprintNeeded})",
                      partsTxt.Contains($"{lo.RelicPartIds.Count}/{RelicEvolution.FragmentsNeeded}")
                      && impTxt.Contains($"{BuildImprint.Count(def.Family)}/{RelicEvolution.ImprintNeeded}") && impTxt.Contains(BuildFamilyRules.Label(def.Family)));
                Check(sb, $"① 꼬리표 「{Parsed(Find<TMP_Text>(rt, "AnchorTag"))}」 · 「{Parsed(Find<TMP_Text>(rt, "FamilyTag"))}」",
                      Parsed(Find<TMP_Text>(rt, "AnchorTag")).Contains("진화") && Parsed(Find<TMP_Text>(rt, "FamilyTag")).Contains($"+{RelicEvolution.ImprintBonus}"));
                Screen(sb, "① 진화 카드", rt);

                typeof(UI_RelicPartDraftPopup).GetMethod("SetSelected", Inst)?.Invoke(draftPopup, new object[] { evoIdx });
                await Wait(0.5f);
                await Shot("SYN_1b_DraftEvolutionSelected");
                var sel = view?.GetType().GetField("Frame")?.GetValue(view) as UIThinFrame;
                Check(sb, $"① 고르면 금 테(Ready) — {sel?.State}", sel != null && sel.State == ThinFrameState.Ready);
            }
            Managers.UI.CloseAllPopupUI();
            await Wait(0.5f);
            Time.timeScale = 1f;

            // ── ② 진화 순간 · ③ HUD Q 칸 ──
            int evoRows0 = CountRows(panel, "ItemNotice_Build_evo");
            lo.SetRelicEvolution(def?.Id);   // 실제 길 — RelicPartsChanged → HUD 진화 순간
            await Wait(0.6f);
            await Shot("SYN_2_EvolutionMoment", 0);
            var moment = GameObject.Find("EvolutionMoment")?.transform as RectTransform;
            var mCrest = Find<Image>(moment, "Crest");
            Check(sb, $"② 진화 순간 판 켜짐 · 문장 {(mCrest != null && mCrest.sprite != null ? mCrest.sprite.name : "(없음)")}",
                  moment != null && mCrest != null && mCrest.sprite != null);
            Screen(sb, "② 진화 순간", moment);

            await Wait(1.7f);   // 머묾 · 날아감 · 내려앉음
            var qName = panel != null ? Find<TMP_Text>(panel.transform, "EvoQName") : null;
            var qRing = panel != null ? Find<Image>(panel.transform, "EvoQRing") : null;
            var qGroup = qName != null ? qName.GetComponentInParent<CanvasGroup>() : null;
            Check(sb, $"③ Q 덧말 「{Parsed(qName)}」 · 고리 채움 {qRing?.fillAmount:0.##} · 덧말 알파 {qGroup?.alpha:0.##}",
                  def != null && Parsed(qName).Contains(def.Name) && qRing != null && qRing.fillAmount > 0.99f && qGroup != null && qGroup.alpha > 0.99f);
            Check(sb, "③ 진화 순간 판 걷힘", GameObject.Find("EvolutionMoment") == null);
            int evoRows1 = CountRows(panel, "ItemNotice_Build_evo");
            typeof(GameRunBootstrapper).GetMethod("ActivateRelicParts", Inst)?.Invoke(GameRunBootstrapper.Instance, new object[] { p });   // 효과가 다시 묶임 → RelicEvolutionNotice
            await Wait(0.4f);
            int evoRows2 = CountRows(panel, "ItemNotice_Build_evo");
            Check(sb, $"③ 진화 알림 한 줄 — 전 {evoRows0} · 순간 뒤 {evoRows1} · 효과 다시 묶은 뒤 {evoRows2}", evoRows1 == evoRows0 + 1 && evoRows2 == evoRows1);
            var evoRow = Find<RectTransform>(panel != null ? panel.transform : null, "ItemNotice_Build_evo");
            Check(sb, $"③ 진화 알림 줄 — 문장 {Find<Image>(evoRow, "Crest") != null} · 칩 「{Parsed(Find<TMP_Text>(evoRow, "Chip"))}」",
                  evoRow != null && Find<Image>(evoRow, "Crest") != null && Parsed(Find<TMP_Text>(evoRow, "Chip")) == "유물 진화");
            await Shot("SYN_3_HudEvolvedQ");
            Screen(sb, "③ Q 덧말", qName != null ? qName.transform.parent as RectTransform : null);
            CheckAgainstHud(sb, "③ Q 덧말이 HUD 글자와 안 겹침", panel, qName);
            Screen(sb, "③ 진화 알림 줄", evoRow);

            // ── ④ 각인 1 · 2 · 3단계 알림 ──
            typeof(CombatPanelView).GetMethod("ClearItemNotices", Inst)?.Invoke(panel, null);
            ForceImprint(BuildFamily.Combo, 2); ForceImprint(BuildFamily.Crit, 4); ForceImprint(BuildFamily.Ranged, 6);
            ItemEffectVfxHelper.ShowBuildNotice(BuildNotice.Imprint(BuildFamily.Combo, 1));
            ItemEffectVfxHelper.ShowBuildNotice(BuildNotice.Imprint(BuildFamily.Crit, 2));
            ItemEffectVfxHelper.ShowBuildNotice(BuildNotice.Imprint(BuildFamily.Ranged, 3));
            await Wait(0.7f);
            await Shot("SYN_4_ImprintNotices", 0);
            var root = panel != null ? Find<RectTransform>(panel.transform, "ItemNoticeRoot") : null;
            foreach (var (f, tier) in new[] { (BuildFamily.Combo, 1), (BuildFamily.Crit, 2), (BuildFamily.Ranged, 3) })
            {
                var row = Find<RectTransform>(root, "ItemNotice_Build_imprint." + f);
                int on = Pips(row, "PipOn"), off = Pips(row, "PipOff");
                Check(sb, $"④ 각인 {BuildFamilyRules.Label(f)} {tier}단계 — 칩 「{Parsed(Find<TMP_Text>(row, "Chip"))}」 눈금 켜짐 {on} · 꺼짐 {off} · 「{Parsed(Find<TMP_Text>(row, "Text"))}」",
                      row != null && on == tier && off == 3 - tier && Parsed(Find<TMP_Text>(row, "Chip")) == BuildFamilyRules.Label(f)
                      && Parsed(Find<TMP_Text>(row, "Text")).StartsWith($"각인 {tier}단계") && Parsed(Find<TMP_Text>(row, "Detail")).StartsWith("룬 효과"));
            }
            CheckNoticeClearOfWeapons(sb, "④ 알림 줄이 무기 원 · 형 알약과 안 겹침", panel, root);
            Screen(sb, "④ 각인 알림 묶음", root);

            // ── ⑤ 속성 존 단계 알림 + 룬판 보류 ──
            var bridge = MerlinRuneBridge.Instance;
            applied = bridge != null ? typeof(MerlinRuneBridge).GetField("_appliedThresholds", Inst)?.GetValue(bridge) as Dictionary<string, HashSet<int>> : null;
            if (applied != null)
            {
                typeof(CombatPanelView).GetMethod("ClearItemNotices", Inst)?.Invoke(panel, null);
                Inject(applied, appliedBackup, "FIRE", 6, 10);
                ItemEffectVfxHelper.ShowBuildNotice(BuildNotice.Zone("FIRE", 2));
                await Wait(0.7f);
                await Shot("SYN_5_ZoneNotice", 0);
                var zr = Find<RectTransform>(root, "ItemNotice_Build_zone.FIRE");
                Check(sb, $"⑤ 존 알림 — 칩 「{Parsed(Find<TMP_Text>(zr, "Chip"))}」 눈금 {Pips(zr, "PipOn")}/{Pips(zr, "PipOn") + Pips(zr, "PipOff")} · 「{Parsed(Find<TMP_Text>(zr, "Text"))}」",
                      zr != null && Pips(zr, "PipOn") == 2 && Pips(zr, "PipOff") == 2 && Parsed(Find<TMP_Text>(zr, "Text")).StartsWith("존 2단계"));
                Screen(sb, "⑤ 존 알림", zr);
                Restore(applied, appliedBackup);

                if (UI_GridPanel.Instance == null) Managers.UI?.ShowOverlayUI<UI_GridPanel>();
                UI_GridPanel.Instance?.ShowWithNewItem(null);
                await Wait(0.9f);
                Inject(applied, appliedBackup, "ICE", 6);
                ItemEffectVfxHelper.ShowBuildNotice(BuildNotice.Zone("ICE", 1));
                await Wait(0.5f);
                // 룬판 시너지 토스트 — 브릿지가 넘기는 「ICE: 효과 내부 이름」이 존 이름 · 단계로 보이나(10-14)
                typeof(UI_GridPanel).GetMethod("ShowSynergyActivated", Inst)?.Invoke(UI_GridPanel.Instance, new object[] { "ICE: probe_internal_effect" });
                await Wait(0.35f);
                var toast = typeof(UI_GridPanel).GetField("_synergyToastText", Inst)?.GetValue(UI_GridPanel.Instance) as TMP_Text;
                string toastText = toast != null ? toast.text : "(없음)";
                Check(sb, $"⑤ 룬판 시너지 토스트 「{toastText}」 — 존 이름 · 단계 · 내부 id 없음",
                      toast != null && toastText.Contains("존") && !toastText.Contains("probe_internal_effect"));
                await Shot("SYN_5c_BoardToast", 0);
                bool heldWhileOpen = Find<RectTransform>(root, "ItemNotice_Build_zone.ICE") == null;
                typeof(UI_GridPanel).GetMethod("ClosePanel", Inst)?.Invoke(UI_GridPanel.Instance, null);
                await Wait(0.9f);
                bool shownAfter = Find<RectTransform>(root, "ItemNotice_Build_zone.ICE") != null;
                Check(sb, $"⑤ 룬판이 열린 동안 보류 {heldWhileOpen} · 닫은 뒤 보임 {shownAfter}", heldWhileOpen && shownAfter);
                await Shot("SYN_5b_ZoneAfterBoard", 0);
                Restore(applied, appliedBackup);
                Time.timeScale = 1f;
            }
            else Check(sb, "⑤ MerlinRuneBridge 없음", false);

            // ── ⑥ 룬 카드 「연타형 / 한 방형」 ──
            injected = InjectItems(RuneIds);
            float coef = wd != null ? ProcCoefficient.Of(wd.weaponType, WeaponActionType.GroundLight, SkillTierResolver.IsRanged(wd)) : -1f;
            await RuneCardsAsync(sb, run, 3, coef, "SYN_6_RuneProc3");
            await RuneCardsAsync(sb, run, 4, coef, "SYN_6b_RuneProc4");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            Managers.UI?.CloseAllPopupUI();
            if (applied != null) Restore(applied, appliedBackup);
            if (lo != null)
            {
                clear?.Invoke(lo, null);
                lo.RestoreRelicParts(savedIds, savedExtra);
            }
            GameRunBootstrapper.Instance?.Run?.EffectManager?.Rebuild();
            RemoveItems(injected);
            HubGui("RestoreTestHubGui");
            Time.timeScale = 1f;
            sb.AppendLine($"CHECK 합계 {s_pass}/{s_all}");
            Directory.CreateDirectory("Temp");
            File.WriteAllText(Path.Combine("Temp", "synergy_ui_shot.txt"), sb.ToString());
            Debug.Log("[SynergyUiShot] 끝\n" + sb);
        }
    }

    // ── ⑥ 룬 카드 ──

    private static async UniTask RuneCardsAsync(StringBuilder sb, GameRunSession run, int n, float coef, string shot)
    {
        var cands = new List<(RuntimeItemData, ItemSO)>();
        for (int i = 0; i < RuneIds.Length && cands.Count < n; i++)
        {
            var rows = Managers.ItemData.GetItem(RuneIds[i]);
            var d = rows != null ? RuntimeItemData.FromServer(rows) : null;
            if (d != null) cands.Add((d, ItemSORegistry.Find(RuneIds[i])));
        }
        var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_RuneSelectPopup>();
        if (popup == null) { Check(sb, $"⑥ 룬 선택 창({n}장) 못 엶", false); return; }
        popup.Setup(cands, run.ItemInventory);
        await Wait(2.2f);   // 내려앉음 · 뒤집힘
        await Shot(shot);
        var cards = typeof(UI_RuneSelectPopup).GetField("_cards", Inst)?.GetValue(popup) as System.Collections.IList;
        float cardW = (float)(typeof(UI_RuneSelectPopup).GetField("_cardW", Inst)?.GetValue(popup) ?? 0f);
        sb.AppendLine($"  ⑥ {n}장 · 카드 폭 {cardW:0}");
        for (int i = 0; i < cands.Count && cards != null && i < cards.Count; i++)
        {
            var view = cards[i];
            var rt   = view.GetType().GetField("Rt")?.GetValue(view) as RectTransform;
            var chip = Find<RectTransform>(rt, "ProcChip");
            var label = Find<TMP_Text>(chip, "Label");
            var badge = Find<TMP_Text>(rt, "PlaceBadge");
            string want = Expect(cands[i].Item1, coef, out bool heavy);
            string got  = Parsed(label);
            bool ok = want == null ? chip == null
                    : got.StartsWith(heavy ? "한 방형" : "연타형") && (got == want || want.StartsWith(got) || got == Short(want, heavy, coef));
            Check(sb, $"⑥ {cands[i].Item1.itemId} 칩 「{got}」(기대 「{want}」 또는 짧은 꼴)", ok);
            if (chip != null && badge != null)
                Check(sb, $"⑥ {cands[i].Item1.itemId} 칩 · 룬 수 글 「{Parsed(badge)}」 안 겹침 · 카드 안", !Overlap(label, badge, rt) && Inside(chip, rt) && !badge.isTextTruncated);
            if (chip != null || want != null) Screen(sb, $"⑥ {cands[i].Item1.itemId} 칩 줄", chip);
        }
        Managers.UI.CloseAllPopupUI();
        await Wait(0.5f);
        Time.timeScale = 1f;
    }

    /// <summary>코드(UI_RuneSelectPopup.ProcStyleOf)와 같은 규칙으로 기대 글 — 효과 칸 순서대로 처음 걸리는 것.</summary>
    private static string Expect(RuntimeItemData d, float coef, out bool heavy)
    {
        heavy = false;
        if (d?.effects == null) return null;
        foreach (var s in d.effects)
        {
            if (s == null) continue;
            if (s.trigger is "OnHeavyHit" or "AfterHeavyHit") { heavy = true; return "한 방형 · 강타 때"; }
            if (s.trigger is "OnHitCount" or "SameTarget") { heavy = false; return "연타형 · 타수로"; }
            if (s.effectType is "Freeze" or "Stun" or "Petrify" or "ExtraAttack" or "TeleportSwap") { heavy = true; return coef > 0f ? $"한 방형 · 확률 ×{coef:0.#}" : "한 방형"; }
            if (s.effectType == "PoisonOnHit") { heavy = d.element == "FIRE"; string nm = heavy ? "한 방형" : "연타형"; return coef > 0f ? $"{nm} · 확률 ×{coef:0.#}" : nm; }
        }
        return null;
    }

    private static string Short(string want, bool heavy, float coef)
        => want.Contains("확률") && coef > 0f ? $"{(heavy ? "한 방형" : "연타형")} ×{coef:0.#}" : (heavy ? "한 방형" : "연타형");

    // ── 화면 CHECK(글자 최소 16 · 넘침 0 · 글자 겹침 0) ──

    private static void Screen(StringBuilder sb, string what, Transform scope)
    {
        if (scope == null) { Check(sb, $"{what} — 범위 없음", false); return; }
        Canvas.ForceUpdateCanvases();
        var texts = VisibleTexts(scope);
        var root  = RootOf(scope);
        float min = 999f; string minName = "";
        foreach (var t in texts)
        {
            float e = EffSize(t, root);
            if (e < min) { min = e; minName = $"{t.name}「{Parsed(t)}」"; }
        }
        Check(sb, $"{what} — 글자 최소 16px(1080 기준 · 배율 반영)", texts.Count > 0 && min >= MinFont - 0.05f, $"최소 {min:0.#} · {minName} · 보이는 글 {texts.Count}");

        var over = new StringBuilder();
        foreach (var t in texts) if (Overflows(t, out string why)) over.Append($"{t.name}({why}) ");
        Check(sb, $"{what} — 넘침 0", over.Length == 0, over.Length == 0 ? "없음" : over.ToString());

        var hit = new StringBuilder();
        for (int i = 0; i < texts.Count; i++)
            for (int j = i + 1; j < texts.Count; j++)
            {
                if (texts[i].transform.IsChildOf(texts[j].transform) || texts[j].transform.IsChildOf(texts[i].transform)) continue;
                if (Overlap(texts[i], texts[j], root)) hit.Append($"{texts[i].name}×{texts[j].name} ");
            }
        Check(sb, $"{what} — 글자 겹침 0", hit.Length == 0, hit.Length == 0 ? "없음" : hit.ToString());

        var off = new StringBuilder();
        foreach (var t in texts)
        {
            var r = Rendered(t, root);
            var c = root.rect;
            if (r.xMin < c.xMin - 1f || r.xMax > c.xMax + 1f || r.yMin < c.yMin - 1f || r.yMax > c.yMax + 1f) off.Append(t.name).Append(' ');
        }
        Check(sb, $"{what} — 화면 밖 글자 0", off.Length == 0, off.Length == 0 ? "없음" : off.ToString());
    }

    /// <summary>한 글자(Q 덧말)가 전투 패널의 다른 보이는 글자와 겹치지 않는가.</summary>
    private static void CheckAgainstHud(StringBuilder sb, string what, CombatPanelView panel, TMP_Text t)
    {
        if (panel == null || t == null) { Check(sb, what + " — 대상 없음", false); return; }
        var root = RootOf(t.transform);
        var hit = new StringBuilder();
        foreach (var o in VisibleTexts(panel.transform))
        {
            if (o == t || o.transform.IsChildOf(t.transform)) continue;
            if (Overlap(t, o, root)) hit.Append($"{o.name}「{Parsed(o)}」 ");
        }
        Check(sb, what, hit.Length == 0, hit.Length == 0 ? "없음" : hit.ToString());
    }

    private static List<TMP_Text> VisibleTexts(Transform scope)
    {
        var list = new List<TMP_Text>();
        foreach (var t in scope.GetComponentsInChildren<TMP_Text>(false))
            if (IsVisible(t)) list.Add(t);
        return list;
    }

    private static bool IsVisible(TMP_Text t)
    {
        if (t == null || !t.isActiveAndEnabled || t.color.a < 0.05f) return false;
        if (t.GetParsedText().Trim().Length == 0) return false;
        float a = 1f;
        foreach (var g in t.GetComponentsInParent<CanvasGroup>(false))
        {
            a *= g.alpha;
            if (g.ignoreParentGroups) break;
        }
        return a >= 0.05f;
    }

    private static RectTransform RootOf(Transform t)
    {
        var c = t != null ? t.GetComponentInParent<Canvas>() : null;
        return c != null ? c.rootCanvas.transform as RectTransform : t as RectTransform;
    }

    /// <summary>보이는 크기(1080 기준 px) — fontSize × (글자 배율 ÷ 루트 캔버스 배율) × 가장 작은 크기 태그.</summary>
    private static float EffSize(TMP_Text t, RectTransform root)
    {
        t.ForceMeshUpdate();
        float k = root != null ? t.transform.lossyScale.y / Mathf.Max(1e-6f, root.lossyScale.y) : 1f;
        float s = t.fontSize, m = s;
        foreach (Match x in SizePct.Matches(t.text)) m = Mathf.Min(m, s * float.Parse(x.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) / 100f);
        foreach (Match x in SizeAbs.Matches(t.text)) m = Mathf.Min(m, float.Parse(x.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
        return m * k;
    }

    private static bool Overflows(TMP_Text t, out string why)
    {
        t.ForceMeshUpdate();
        why = "";
        if (t.isTextTruncated) { why = "말줄임"; return true; }
        var b = t.textBounds;
        var r = t.rectTransform.rect;
        if (b.size.x <= 0f) return false;
        float l = r.xMin - b.min.x, rr = b.max.x - r.xMax, bo = r.yMin - b.min.y, to = b.max.y - r.yMax;
        if (l > 2f || rr > 2f) { why = $"가로 {Mathf.Max(l, rr):0}px"; return true; }
        if (bo > 3f || to > 3f) { why = $"세로 {Mathf.Max(bo, to):0}px"; return true; }
        return false;
    }

    private static Rect Rendered(TMP_Text t, RectTransform root)
    {
        t.ForceMeshUpdate();
        var b = t.textBounds;
        var a = root.InverseTransformPoint(t.transform.TransformPoint(b.min));
        var c = root.InverseTransformPoint(t.transform.TransformPoint(b.max));
        return Rect.MinMaxRect(Mathf.Min(a.x, c.x), Mathf.Min(a.y, c.y), Mathf.Max(a.x, c.x), Mathf.Max(a.y, c.y));
    }

    private static bool Overlap(TMP_Text a, TMP_Text b, Transform scope)
    {
        if (a == null || b == null) return false;
        var root = RootOf(scope != null ? scope : a.transform);
        var ra = Rendered(a, root); var rb = Rendered(b, root);
        float ox = Mathf.Min(ra.xMax, rb.xMax) - Mathf.Max(ra.xMin, rb.xMin);
        float oy = Mathf.Min(ra.yMax, rb.yMax) - Mathf.Max(ra.yMin, rb.yMin);
        return ox > 1f && oy > 1f;
    }

    private static bool Inside(RectTransform a, RectTransform b)
    {
        if (a == null || b == null) return false;
        var ca = new Vector3[4]; var cb = new Vector3[4];
        a.GetWorldCorners(ca); b.GetWorldCorners(cb);
        const float eps = 0.5f;
        return ca[0].x >= cb[0].x - eps && ca[0].y >= cb[0].y - eps && ca[2].x <= cb[2].x + eps && ca[2].y <= cb[2].y + eps;
    }

    // ── 도구 ──

    private static async UniTask<bool> EnsureRunAsync(StringBuilder sb)
    {
        if (GameRunBootstrapper.Instance?.Run?.Player != null) return true;
        var launcher = Object.FindFirstObjectByType<TestHubLauncher>();
        if (launcher == null) { Check(sb, "테스트 허브(TestHubLauncher) 없음 — 테스트 허브에서 플레이할 것", false); return false; }
        var relics = typeof(TestHubLauncher).GetField("relics", Inst)?.GetValue(launcher) as RelicClassSO[];
        int idx = 0;
        if (relics != null)
            for (int i = 0; i < relics.Length; i++)
                if (relics[i] != null && relics[i].Id.ToString() == "Gawain") { idx = i; break; }
        typeof(TestHubLauncher).GetField("_relicIndex", Inst)?.SetValue(launcher, idx);
        typeof(TestHubLauncher).GetField("_chapter", Inst)?.SetValue(launcher, 1);
        typeof(TestHubLauncher).GetField("_bossApproach", Inst)?.SetValue(launcher, false);
        launcher.TryLaunch();
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 60f && GameRunBootstrapper.Instance?.Run?.Player == null)
            await UniTask.Delay(500, DelayType.Realtime);
        var ensure = typeof(ServiceRoomTourProbeEditor).GetMethod("EnsureProcRunAsync", Stat);
        if (ensure != null) await (UniTask<bool>)ensure.Invoke(null, null);
        await UniTask.Delay(2500, DelayType.Realtime);   // 첫 방 입장 연출
        bool ok = GameRunBootstrapper.Instance?.Run?.Player != null;
        Check(sb, "런 시작(가웨인 Ch1)", ok);
        return ok;
    }

    private static RelicEvolution.Def FirstDef(string relicId)
    {
        foreach (var d in RelicEvolution.All) if (d.RelicId == relicId && d.Family == BuildFamily.Combo) return d;
        return RelicEvolution.All.Length > 0 ? RelicEvolution.All[0] : null;
    }

    private static void ForceImprint(BuildFamily f, int count)
    {
        if (typeof(BuildImprint).GetField("s_counts", Stat)?.GetValue(null) is int[] c) c[(int)f] = count;
    }

    private static void Inject(Dictionary<string, HashSet<int>> applied, Dictionary<string, HashSet<int>> backup, string zone, params int[] thresholds)
    {
        if (!backup.ContainsKey(zone)) backup[zone] = applied.TryGetValue(zone, out var had) && had != null ? new HashSet<int>(had) : null;
        applied[zone] = new HashSet<int>(thresholds);
    }

    private static void Restore(Dictionary<string, HashSet<int>> applied, Dictionary<string, HashSet<int>> backup)
    {
        foreach (var kv in backup)
        {
            if (kv.Value == null) applied.Remove(kv.Key);
            else applied[kv.Key] = new HashSet<int>(kv.Value);
        }
        backup.Clear();
    }

    /// <summary>
    /// 실측 룬 행을 프로젝트 JSON(Resources/ITEM_DATA — 4b 재설계 판)으로 메모리에서만 갈아 끼운다. CDN 판이 옛 행이면
    /// 「강타 때」 룬이 옛 발동으로 읽혀 칩이 없었다(1차 실측 heavy_weight). 끝나면 원래 행(없었으면 뺌)으로 되돌린다.
    /// </summary>
    private static Dictionary<string, List<ItemEntry>> InjectItems(string[] ids)
    {
        var backup = new Dictionary<string, List<ItemEntry>>();
        var chart = Managers.ItemData;
        if (chart == null) return backup;
        var map = typeof(ItemDataManager).GetField("_itemById", Inst)?.GetValue(chart) as Dictionary<string, List<ItemEntry>>;
        var json = Resources.Load<TextAsset>("ITEM_DATA");
        if (map == null || json == null) return backup;
        var want = new HashSet<string>(ids);
        foreach (var e in JsonUtility.FromJson<ItemEntryCollection>(json.text).items)
        {
            string id = string.IsNullOrEmpty(e.item_id) ? e.passive_id : e.item_id;
            if (id == null || !want.Contains(id)) continue;
            if (!backup.ContainsKey(id))
            {
                backup[id] = map.TryGetValue(id, out var old) ? old : null;
                map[id] = new List<ItemEntry>();
            }
            map[id].Add(e);
        }
        return backup;
    }

    private static void RemoveItems(Dictionary<string, List<ItemEntry>> backup)
    {
        var chart = Managers.ItemData;
        if (chart == null || backup == null || backup.Count == 0) return;
        var map = typeof(ItemDataManager).GetField("_itemById", Inst)?.GetValue(chart) as Dictionary<string, List<ItemEntry>>;
        if (map == null) return;
        foreach (var kv in backup)
        {
            if (kv.Value == null) map.Remove(kv.Key);
            else map[kv.Key] = kv.Value;
        }
    }

    /// <summary>알림 줄(보이는 것)이 무기 원 · 형 알약 사각형과 겹치지 않는가(명조형 HUD — 화면 단위).</summary>
    private static void CheckNoticeClearOfWeapons(StringBuilder sb, string what, CombatPanelView panel, RectTransform noticeRoot)
    {
        var slots = typeof(CombatPanelView).GetField("_weaponSlotRoot", Inst)?.GetValue(panel) as RectTransform[];
        var pill  = typeof(CombatPanelView).GetField("_formPill", Inst)?.GetValue(panel) as RectTransform;
        if (noticeRoot == null || slots == null) { Check(sb, what + " — 대상 없음", false); return; }
        var hit = new StringBuilder();
        var targets = new List<RectTransform>(slots);
        if (pill != null) targets.Add(pill);
        for (int i = 0; i < noticeRoot.childCount; i++)
        {
            if (!(noticeRoot.GetChild(i) is RectTransform row) || !row.gameObject.activeInHierarchy) continue;
            var a = WorldRect(row);
            foreach (var t in targets)
            {
                if (t == null || !t.gameObject.activeInHierarchy) continue;
                var b = WorldRect(t);
                if (Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin) > 1f && Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin) > 1f)
                    hit.Append($"{row.name}×{t.name} ");
            }
        }
        Check(sb, what, hit.Length == 0, hit.Length == 0 ? "없음" : hit.ToString());
    }

    private static Rect WorldRect(RectTransform rt)
    {
        var c = new Vector3[4];
        rt.GetWorldCorners(c);
        return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
    }

    private static int CountRows(CombatPanelView panel, string name)
    {
        if (panel == null) return 0;
        int n = 0;
        foreach (var t in panel.GetComponentsInChildren<RectTransform>(false)) if (t.name == name) n++;
        return n;
    }

    private static int Pips(Transform row, string prefix)
    {
        if (row == null) return 0;
        int n = 0;
        foreach (var t in row.GetComponentsInChildren<Image>(false)) if (t.name.StartsWith(prefix)) n++;
        return n;
    }

    private static T Find<T>(Transform root, string name) where T : Component
    {
        if (root == null) return null;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name && t.TryGetComponent<T>(out var c)) return c;
        return null;
    }

    private static string Parsed(TMP_Text t) => t != null ? t.GetParsedText() : "";

    private static void Check(StringBuilder sb, string what, bool ok, string detail = null)
    {
        s_all++;
        if (ok) s_pass++;
        sb.AppendLine(detail == null ? $"  CHECK {what}: {ok}" : $"  CHECK {what}: {ok} ({detail})");
    }

    private static void HubGui(string method) => typeof(UILayoutRuntimeProbeEditor).GetMethod(method, Stat)?.Invoke(null, null);

    private static UniTask Wait(float s) => UniTask.Delay((int)(s * 1000), DelayType.Realtime);

    private static UniTask Shot(string name, int settleMs = 300)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", Stat);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, settleMs }) : UniTask.CompletedTask;
    }
}
#endif
