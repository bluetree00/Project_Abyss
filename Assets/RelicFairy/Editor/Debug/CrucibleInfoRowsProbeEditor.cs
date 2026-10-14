#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// [촬영 · 플레이 중] 10-13 재련소 · ESC 정보 줄(유물·장비 UI 요청서 7~9장) — 화면 7장 + 줄 글 전부.
/// 강화 줄 「공격 → (+%)」 · 판정 도장 아래 「스킬 N단계 — …」 · 진화 카드 「N단계로 시작」 · 마스터리 초 단위 전후 ·
/// 원거리 파츠 「지금 → LvN에」(Lv0 · 계단 사이 · 최대 — 파츠 5종 × 0~상한 전부 글로) · ESC 무기 칸(한글 이름 · 강화 · 형 · 스킬 단계 표).
/// 테스트 허브에서 런을 열고 첫 방에 들어서면(자동 플레이 「방 진입」) 멈추고 돈다.
/// 결과 Temp/crucible_info_rows.txt · Temp/ui_shots/CIR_*.png · 로그 「[CrucibleInfoRows] 끝」.
/// ⚠️ ESC 무기 칸(CharacterInfoPageView)은 지금 게임 어디에도 붙어 있지 않다(EscBookPopup이 @UIRoot에서 걷혔다) — 여기서 임시 캔버스에 띄워 본다.
/// </summary>
public static class CrucibleInfoRowsProbeEditor
{
    private const string AutoRoot = "RelicFairy/Debug/런 구조 자동 실측/";
    private const string Nameless = "Assets/RelicFairy/Weapon/Nameless/Data/T0_Nameless.asset";
    private const string Bow      = "Assets/RelicFairy/Weapon/Bow/Data/T1_Bow.asset";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const BindingFlags Stat = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly Regex Tags = new("<[^>]+>");
    private static bool s_armed;
    private static int  s_pass, s_all;

    [MenuItem("RelicFairy/Debug/10-13 재련소 · ESC 정보 줄 촬영 (테스트 허브, 플레이 중)")]
    private static void Begin()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[CrucibleInfoRows] 플레이 모드에서만 동작한다."); return; }
        if (Object.FindFirstObjectByType<TestHubLauncher>() == null) { Debug.LogWarning("[CrucibleInfoRows] 테스트 허브에서 시작한다."); return; }
        s_armed = true;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        EditorApplication.ExecuteMenuItem(AutoRoot + "시작 (테스트 허브에서, 플레이 중)");
    }

    private static void OnLog(string msg, string stack, LogType type)
    {
        if (!s_armed || !msg.StartsWith("[RunAuto]") || !msg.Contains("방 진입")) return;
        s_armed = false;
        Application.logMessageReceived -= OnLog;
        EditorApplication.delayCall += () =>
        {
            EditorApplication.ExecuteMenuItem(AutoRoot + "중지·기록");
            var p = GameRunBootstrapper.Instance?.Run?.Player;
            if (p == null) { Debug.LogWarning("[CrucibleInfoRows] 런 플레이어가 없다"); return; }
            RunAsync(p, p.GetCancellationTokenOnDestroy()).Forget();
        };
    }

    private static async UniTaskVoid RunAsync(PlayerController p, CancellationToken ct)
    {
        var sb = new StringBuilder("재련소 · ESC 정보 줄(10-13)\n");
        s_pass = s_all = 0;
        Directory.CreateDirectory("Temp/ui_shots");
        var go = new GameObject("~CrucibleInfoRowsProbe");
        GameObject escCanvas = null;
        var parts = RangedPartsState.Current;
        var partsBefore = new List<RangedPartsState.Equipped>(parts.Equipped_);
        try
        {
            typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);
            foreach (var m in Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
                if (m != null && !m.IsDead) m.gameObject.SetActive(false);
            await Wait(1.5f, ct);

            var wm  = p.WeaponManager;
            var run = GameRunBootstrapper.Instance.Run;
            if (!await EquipAsync(p, Nameless, ct)) { sb.AppendLine("무형검 장착 실패"); return; }
            if (wm.Weapon1Data == null)
            {
                var bow = AssetDatabase.LoadAssetAtPath<WeaponSO>(Bow);
                if (bow != null) await wm.AcquireWeaponToSlotAsync(WeaponData.FromSO(bow), PlayerWeaponManager.Slot1, setActive: false, playAppear: false);
            }
            if (wm.CurrentSlotIndex != PlayerWeaponManager.Slot0) await wm.SwitchToSlotAsync(PlayerWeaponManager.Slot0);
            SetLevel(wm.Weapon0Data, 2);
            run.FuelBank.Add(FuelKind.EnhanceMaterial, 200);
            parts.Restore(new[] { new RangedPartsState.Equipped { partId = "part_split", level = 1 } });   // 파츠 합 1
            HubGui("HideTestHubGui");

            var table = await WeaponEnhanceService.EnsureLoadedAsync();
            var ctrl = go.AddComponent<CrucibleRoomController>();
            ctrl.Initialize(run, table, new System.Random(7), null);
            typeof(CrucibleRoomController).GetField("_event", Inst)?.SetValue(ctrl, CrucibleEvent.None);
            var panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_CruciblePanel>();
            panel.Bind(ctrl);
            await Wait(0.8f, ct);
            F<Button[]>(panel, "_chooseCard")?[0]?.onClick.Invoke();   // 입구 → 근거리
            Call(panel, "RefreshAll");
            await Wait(0.5f, ct);
            var root = panel.transform;
            var w0 = wm.Weapon0Data;

            // ── 1 강화 줄 — 「공격 5.0 → 5.4 (+8%)」
            var atk = F<TMP_Text[]>(panel, "_cardAtk")?[0];
            string atkLine = Plain(atk);
            float now = WeaponEnhanceService.EffectiveAttack(w0, table), next = WeaponEnhanceService.EffectiveAttackAt(w0, table, w0.enhanceLevel + 1);
            sb.AppendLine($"[강화 줄] +{w0.enhanceLevel} 「{atkLine}」 · 서비스 {now:F2} → {next:F2}");
            Check(sb, "강화 줄 = 공격 지금 → 다음 (+%)", atkLine.Contains($"{now:F1}") && atkLine.Contains($"→ {next:F1}") && atkLine.Contains("%)"), atkLine);
            await Shot("CIR_1_Attack", 300);

            // ── 2 판정 도장 아래 — +2 → +3 성공(스킬 2단계 임계)
            SetLevel(w0, 3);
            Call(panel, "RefreshAll");
            var viewType = typeof(UI_CruciblePanel).GetNestedType("EnhanceView", BindingFlags.NonPublic);
            var r23 = new EnhanceResult { outcome = EnhanceOutcome.Success, beforeLevel = 2, afterLevel = 3, chance = 1f };
            typeof(UI_CruciblePanel).GetMethod("PlayEnhanceSequence", Inst)?.Invoke(panel, new object[] { r23, System.Enum.ToObject(viewType, 0) });
            TMP_Text stampTier = null;
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 4f)
            {
                stampTier = F<TMP_Text>(panel, "_stampTier");
                if (stampTier != null && stampTier.gameObject.activeInHierarchy) break;
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            string stamp = Plain(F<TMP_Text>(panel, "_stageResult")), tierLine = Plain(stampTier);
            sb.AppendLine($"[판정 도장] 「{stamp}」 / 아래 「{tierLine}」");
            Check(sb, "도장 아래 = 「스킬 2단계 — …」", tierLine.StartsWith("스킬 2단계"), tierLine);
            await Shot("CIR_2_Stamp", 150);
            t0 = Time.realtimeSinceStartup;
            while (F<object>(panel, "_animating") is bool anim && anim && Time.realtimeSinceStartup - t0 < 6f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
            await Wait(0.3f, ct);
            F<GameObject>(panel, "_engravePanel")?.SetActive(false);
            // 단계를 넘지 않는 성공(+3 → +4)엔 줄이 없다
            string none = Invoke<string>(panel, "TierReachedLine", new EnhanceResult { outcome = EnhanceOutcome.Success, beforeLevel = 3, afterLevel = 4 }, false);
            Check(sb, "임계 밖 성공(+3 → +4) = 줄 없음", string.IsNullOrEmpty(none), none ?? "null");

            // ── 3 진화 카드 — E · R 옆 「2단계로 시작」
            SetLevel(w0, 5);
            Call(panel, "RefreshAll");
            Call(panel, "ShowEvolvePanel");
            await Wait(0.5f, ct);
            var branches = typeof(UI_CruciblePanel).GetProperty("CurrentBranches", Inst)?.GetValue(panel) as IReadOnlyList<WeaponEvolutionSO.Branch>;
            int ki = -1;
            for (int i = 0; branches != null && i < branches.Count; i++) if (branches[i].branchId == "katana") ki = i;
            Call(panel, "PickEvolveCard", ki);
            await Wait(0.4f, ct);
            string skA = Plain(Find(Find(root, "EvolveA"), "Skills")?.GetComponent<TMP_Text>());
            string skB = Plain(Find(Find(root, "EvolveB"), "Skills")?.GetComponent<TMP_Text>());
            sb.AppendLine($"[진화 카드] A 「{skA}」 · B 「{skB}」 (지금 단계 {SkillTierResolver.Resolve(w0)})");
            Check(sb, "진화 카드 — 「2단계로 시작」", skA.Contains("2단계로 시작") && skB.Contains("2단계로 시작"), $"{skA} / {skB}");
            await Shot("CIR_3_Evolve", 300);

            // ── 4 마스터리 — 진화(카타나) 뒤 마스터리 구간 · 초 단위 전후
            F<Button>(panel, "_evolveConfirm")?.onClick.Invoke();
            t0 = Time.realtimeSinceStartup;
            while (wm.Weapon0Data?.form == null && Time.realtimeSinceStartup - t0 < 10f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
            await Wait(3f, ct);
            F<GameObject>(panel, "_engravePanel")?.SetActive(false);
            Call(panel, "HideEvolvePanel");
            var w = wm.Weapon0Data;
            int cap = WeaponEnhanceService.BaseEnhanceCap(w, table), max = WeaponEnhanceService.MaxEnhance(w, table);
            sb.AppendLine($"[마스터리] {w.displayName} · 형 {(w.HasForm ? w.form.DisplayName : "없음")} · 기본 상한 +{cap} · 최대 +{max}");
            sb.AppendLine($"  여는 줄(+{cap + 1} 마스터리 1단계 — …) 「{Tags.Replace(Invoke<string>(panel, "MasteryDelta", w, 0, 1) ?? "", "")}」");
            if (max > cap + 1)
            {
                SetLevel(w, cap + 1);
                string m1 = Tags.Replace(Invoke<string>(panel, "MasteryText", w) ?? "", "");
                sb.AppendLine($"  +{cap + 1}(마스터리 1 → 2) 「{m1}」");
                Check(sb, "마스터리 전후 = 「E 재사용 a → b초 · 스킬 피해 +x% → +y%」", m1.Contains("재사용") && m1.Contains("초") && m1.Contains("→") && m1.Contains("스킬 피해"), m1);
            }
            SetLevel(w, max);
            Call(panel, "RefreshAll");
            await Wait(0.4f, ct);
            string mRow = Plain(F<TMP_Text>(panel, "_detailFail"));
            sb.AppendLine($"  +{max}(최대) 표 「마스터리」 칸 「{mRow}」 · 다음 「{Txt(root, "GrowthTrack", "Next1")} / {Txt(root, "GrowthTrack", "Next2")}」");
            Check(sb, "최대 — 마스터리 칸 = 지금 값(초 · 피해)", max <= cap || (mRow.Contains("초") && mRow.Contains("스킬 피해") && !mRow.Contains("→")), mRow);
            Check(sb, "성장 길 끝 칸 = 「전설」(영문 0)", Txt(root, "Node4", "Name") == "전설", Txt(root, "Node4", "Name"));
            string leg = RewardPresentation.RarityLabel(ItemRarity.Legendary);
            Check(sb, "룬 등급 글 — 「★」 0(폰트 화이트리스트)", !leg.Contains("★"), leg);
            await Shot("CIR_4_Mastery", 300);

            // ── 5 · 6 원거리 파츠 — 5종 × Lv0 ~ 상한 글 전부, 화면은 꺼짐 · 계단 사이
            var defs = Managers.WeaponParts?.All;
            var step = typeof(UI_CruciblePanel).GetMethod("PartStepLine", Stat);
            if (defs != null && step != null)
            {
                foreach (var d in defs)
                {
                    sb.AppendLine($"[파츠] {d.part_name}({d.kind}) 상한 {d.max_level} · 계단 {d.milestone_every}");
                    int top = d.max_level > 0 ? d.max_level : 12;
                    for (int lv = 0; lv <= top; lv++)
                        sb.AppendLine($"  Lv{lv,-2} 상세 「{Tags.Replace((string)step.Invoke(null, new object[] { d, lv, false }), "")}」 · 목록 「{Tags.Replace((string)step.Invoke(null, new object[] { d, lv, true }), "")}」");
                }
                var split = Def(defs, RangedPartKind.Split);
                if (split != null)
                {
                    string l0 = Tags.Replace((string)step.Invoke(null, new object[] { split, 0, false }), "");
                    string l5 = Tags.Replace((string)step.Invoke(null, new object[] { split, 5, true }), "");
                    string l10 = Tags.Replace((string)step.Invoke(null, new object[] { split, 10, false }), "");
                    Check(sb, "분열 Lv0 = 「Lv1에 켜짐: 2발」", l0 == "Lv1에 켜짐: 2발", l0);
                    Check(sb, "분열 Lv5 목록 = 계단 사이 「3발 → Lv7에 4발」(2발 → 2발 0)", l5 == "3발 → Lv7에 4발", l5);
                    Check(sb, "분열 Lv10 = 상한 밖 Lv13 없음 · 최대", !l10.Contains("Lv13") && l10.Contains("최대"), l10);
                }
            }

            // 섞은 상태 — 분열 Lv5(계단 사이) · 관통 Lv11(값 최대) · 폭발 Lv0(꺼짐) · 유도 Lv10(상한) · 거력 Lv2
            parts.Restore(new[]
            {
                new RangedPartsState.Equipped { partId = "part_split",  level = 5 },
                new RangedPartsState.Equipped { partId = "part_pierce", level = 11 },
                new RangedPartsState.Equipped { partId = "part_homing", level = 10 },
                new RangedPartsState.Equipped { partId = "part_power",  level = 2 },
            });
            // 원거리 스킬 단계 임계(파츠 합 1 → 2)를 넘는 파츠 성공의 도장 줄 — 글만
            var bowData = wm.Weapon1Data;
            if (bowData != null && SkillTierResolver.IsRanged(bowData))
            {
                var keep = new List<RangedPartsState.Equipped>(parts.Equipped_);
                parts.Restore(new[] { new RangedPartsState.Equipped { partId = "part_split", level = 2 } });
                string rl = Invoke<string>(panel, "TierReachedLine", new EnhanceResult { outcome = EnhanceOutcome.Success, beforeLevel = 1, afterLevel = 2 }, true);
                sb.AppendLine($"[파츠 도장] 파츠 합 1 → 2 「{rl}」");
                Check(sb, "원거리 — 파츠 합 임계를 넘으면 도장 아래 「스킬 2단계」", rl != null && rl.StartsWith("스킬 2단계"), rl ?? "null");
                parts.Restore(keep);
            }
            Call(panel, "SelectTab", 1);
            await Wait(0.4f, ct);
            int iExp = Index(defs, RangedPartKind.Explode), iSplit = Index(defs, RangedPartKind.Split);
            SetSelected(panel, iExp);
            await Wait(0.3f, ct);
            string dOff = Plain(F<TMP_Text[]>(panel, "_cardAtk")?[1]);
            sb.AppendLine($"[파츠 상세] 폭발 Lv0 「{dOff}」");
            var slotVals = F<TMP_Text[]>(panel, "_slotVal");
            for (int i = 0; slotVals != null && i < slotVals.Length; i++) sb.AppendLine($"  목록 {i} 「{Plain(slotVals[i])}」");
            Check(sb, "꺼진 파츠 상세 = 「Lv1에 켜짐: …」(켜진 듯 보이지 않음)", dOff.StartsWith("Lv1에 켜짐"), dOff);
            await Shot("CIR_5_PartsOff", 250);
            SetSelected(panel, iSplit);
            await Wait(0.3f, ct);
            string dStep = Plain(F<TMP_Text[]>(panel, "_cardAtk")?[1]);
            sb.AppendLine($"[파츠 상세] 분열 Lv5 「{dStep}」");
            Check(sb, "계단 사이 상세 = 「지금 3발 → Lv7에 4발」", dStep == "지금 3발 → Lv7에 4발", dStep);
            await Shot("CIR_6_PartsStep", 250);

            // ── 7 ESC 무기 칸 — 임시 캔버스(양피지 판)에 CharacterInfoPageView를 띄운다
            Managers.UI?.CloseAllPopupUI();
            await Wait(0.3f, ct);
            var tierTable = typeof(CharacterInfoPageView).GetMethod("SkillTierTable", Stat);
            var nso = AssetDatabase.LoadAssetAtPath<WeaponSO>(Nameless);
            if (tierTable != null && nso != null)
            {
                var probeW = WeaponData.FromSO(nso);
                foreach (int lv in new[] { 0, 3, 6 })
                {
                    probeW.enhanceLevel = lv;
                    sb.AppendLine($"[ESC 단계 표] 무형검 +{lv}\n  " + Tags.Replace((string)tierTable.Invoke(null, new object[] { probeW }), "").Replace("\n", "\n  "));
                }
            }
            escCanvas = new GameObject("~CIR_EscCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var cv = escCanvas.GetComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceOverlay;
            cv.sortingOrder = 4000;
            var sc = escCanvas.GetComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920f, 1080f);
            sc.matchWidthOrHeight = 0.5f;
            var page = new GameObject("PageSkill", typeof(RectTransform), typeof(Image));
            page.transform.SetParent(escCanvas.transform, false);
            var prt = (RectTransform)page.transform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(1500f, 860f);
            page.GetComponent<Image>().color = new Color(0.92f, 0.86f, 0.72f, 1f);   // 책 그림 대신 양피지 판
            var view = page.AddComponent<CharacterInfoPageView>();
            view.Refresh();
            await Wait(0.3f, ct);
            string e0 = Tags.Replace(typeof(CharacterInfoPageView).GetField("_weapon0Info", Inst)?.GetValue(view) is TMP_Text a0 ? a0.text : "", "");
            string e1 = Tags.Replace(typeof(CharacterInfoPageView).GetField("_weapon1Info", Inst)?.GetValue(view) is TMP_Text a1 ? a1.text : "", "");
            sb.AppendLine("[ESC 무기 1]\n  " + e0.Replace("\n", "\n  "));
            sb.AppendLine("[ESC 무기 2]\n  " + e1.Replace("\n", "\n  "));
            Check(sb, "ESC — 한글 이름(영문 enum 0) · 강화 · 형 · 스킬 단계 표 · 지금 칸",
                  e0.Contains(w.displayName) && !e0.Contains(w.weaponType.ToString()) && e0.Contains("강화 +") && e0.Contains("형 「") && e0.Contains("스킬 단계") && e0.Contains("· 지금"), e0.Split('\n')[0]);
            await Shot("CIR_7_Esc", 300);
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            Managers.UI?.CloseAllPopupUI();
            if (escCanvas != null) Object.Destroy(escCanvas);
            Object.Destroy(go);
            parts.Restore(partsBefore);
            HubGui("RestoreTestHubGui");
            sb.AppendLine($"통과 {s_pass}/{s_all}");
            File.WriteAllText("Temp/crucible_info_rows.txt", sb.ToString());
            Debug.Log("[CrucibleInfoRows] 끝\n" + sb);
        }
    }

    // ── 도구 ──

    private static WeaponPartEntry Def(IReadOnlyList<WeaponPartEntry> defs, RangedPartKind kind)
    {
        int i = Index(defs, kind);
        return i >= 0 ? defs[i] : null;
    }

    private static int Index(IReadOnlyList<WeaponPartEntry> defs, RangedPartKind kind)
    {
        for (int i = 0; defs != null && i < defs.Count; i++) if (defs[i].Kind == kind) return i;
        return -1;
    }

    private static void SetSelected(UI_CruciblePanel panel, int index)
    {
        typeof(UI_CruciblePanel).GetField("_selectedPartSlot", Inst)?.SetValue(panel, index);
        Call(panel, "RefreshAll");
    }

    private static void Check(StringBuilder sb, string what, bool ok, string detail)
    {
        s_all++;
        if (ok) s_pass++;
        sb.AppendLine($"  CHECK {what}: {ok} ({detail})");
    }

    private static void SetLevel(WeaponData wd, int lv)
    {
        if (wd == null) return;
        wd.enhanceLevel = lv;
        wd.RecomputeEnhancedStats();
    }

    private static async UniTask<bool> EquipAsync(PlayerController p, string soPath, CancellationToken ct)
    {
        var wm = p.WeaponManager;
        var so = AssetDatabase.LoadAssetAtPath<WeaponSO>(soPath);
        if (so == null) return false;
        var runtime = WeaponData.FromSO(so);
        var preload = typeof(PlayerWeaponManager).GetMethod("PreloadWeaponClipsAsync", BindingFlags.Static | BindingFlags.NonPublic);
        if (preload != null) await (UniTask)preload.Invoke(null, new object[] { runtime, ct });
        typeof(PlayerWeaponManager).GetMethod("DestroySlotInstance", BindingFlags.Static | BindingFlags.NonPublic)
            ?.Invoke(null, new object[] { wm.slots[PlayerWeaponManager.Slot0] });
        await UniTask.Yield(PlayerLoopTiming.Update, ct);
        await wm.AcquireWeaponToSlotAsync(runtime, PlayerWeaponManager.Slot0, setActive: true, playAppear: false);
        if (wm.CurrentSlotIndex != PlayerWeaponManager.Slot0) await wm.SwitchToSlotAsync(PlayerWeaponManager.Slot0);
        await Wait(0.5f, ct);
        return wm.Weapon0Data != null && wm.Weapon0Data.evolution != null;
    }

    /// <summary>보이는 글(태그 뺀 원문) — 꺼져 있으면 빈 글.</summary>
    private static string Plain(TMP_Text t) => t != null && t.gameObject.activeInHierarchy ? Tags.Replace(t.text ?? "", "") : "";

    private static string Txt(Transform root, string parent, string child)
        => Plain(Find(Find(root, parent), child)?.GetComponent<TMP_Text>());

    private static Transform Find(Transform t, string name)
    {
        if (t == null) return null;
        if (t.name == name) return t;
        for (int i = 0; i < t.childCount; i++) { var hit = Find(t.GetChild(i), name); if (hit != null) return hit; }
        return null;
    }

    private static T F<T>(UI_CruciblePanel panel, string name) where T : class
        => typeof(UI_CruciblePanel).GetField(name, Inst)?.GetValue(panel) as T;

    private static void Call(UI_CruciblePanel panel, string method, params object[] args)
        => typeof(UI_CruciblePanel).GetMethod(method, Inst)?.Invoke(panel, args.Length == 0 ? null : args);

    private static T Invoke<T>(UI_CruciblePanel panel, string method, params object[] args) where T : class
        => typeof(UI_CruciblePanel).GetMethod(method, Inst)?.Invoke(panel, args) as T;

    private static void HubGui(string method)
        => typeof(UILayoutRuntimeProbeEditor).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);

    private static UniTask Shot(string name, int settleMs)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, settleMs }) : UniTask.CompletedTask;
    }

    private static UniTask Wait(float s, CancellationToken ct)
        => UniTask.Delay(System.TimeSpan.FromSeconds(s), DelayType.Realtime, PlayerLoopTiming.Update, ct);
}
#endif
