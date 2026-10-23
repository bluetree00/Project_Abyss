#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// [촬영 · 플레이 중] 10-18 재련 개편 화면 — 설계 기획/RelicFairy_재련개편_설계_20261018.md.
/// ① 첫 화면 「무기 재련 · 파츠 재련」 ② 근접 탭: 큰 버튼 [재련] · 숫자 = 재련 비용 · 「실패 없음」 · 가진 증강 목록
/// ③ [재련] → +3 · 증강 고르기 창 → 고르면 목록에 붙고 다음 비용이 오른다 ④ 원거리 탭: [재련] → 파츠 카드 창(3장) → 고르면 파츠 합이 오른다.
/// 테스트 허브에서 런을 열고 첫 방에 들어서면(자동 플레이 「방 진입」) 멈추고 돈다.
/// 결과 Temp/fb1018_reforge.txt · Temp/ui_shots/FB18_ref_*.png · 로그 「[FB18ref] 끝」.
/// </summary>
public static class Fb1018ReforgeUiProbeEditor
{
    private const string Tag      = "[FB18ref]";
    private const string AutoRoot = "RelicFairy/Debug/런 구조 자동 실측/";
    private const string Bow      = "Assets/RelicFairy/Weapon/Bow/Data/T1_Bow.asset";
    private const string OutTxt   = "Temp/fb1018_reforge.txt";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static bool s_armed;
    private static int  s_pass, s_all;

    [MenuItem("RelicFairy/Debug/10-18 재련 개편 화면 촬영 (테스트 허브, 플레이 중)")]
    private static void Begin()
    {
        if (!Application.isPlaying) { Debug.LogWarning($"{Tag} 플레이 모드에서만 동작한다."); return; }
        if (Object.FindFirstObjectByType<TestHubLauncher>() == null) { Debug.LogWarning($"{Tag} 테스트 허브에서 시작한다."); return; }
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
            if (p == null) { Debug.LogWarning($"{Tag} 런 플레이어가 없다"); return; }
            RunAsync(p, p.GetCancellationTokenOnDestroy()).Forget();
        };
    }

    private static async UniTaskVoid RunAsync(PlayerController p, CancellationToken ct)
    {
        var sb = new StringBuilder("10-18 재련 개편 화면\n");
        s_pass = s_all = 0;
        Directory.CreateDirectory("Temp/ui_shots");
        var go = new GameObject("~Fb1018refProbe");
        try
        {
            typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);
            foreach (var mo in Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
                if (mo != null && !mo.IsDead) mo.gameObject.SetActive(false);
            await Wait(1.5f, ct);

            var wm  = p.WeaponManager;
            var run = GameRunBootstrapper.Instance.Run;
            if (wm.Weapon1Data == null)
            {
                var bow = AssetDatabase.LoadAssetAtPath<WeaponSO>(Bow);
                if (bow != null) await wm.AcquireWeaponToSlotAsync(WeaponData.FromSO(bow), PlayerWeaponManager.Slot1, setActive: false, playAppear: false);
            }
            var w0 = wm.Weapon0Data;
            w0.enhanceLevel = 3; w0.highestEnhanceReached = 3; w0.augments = ""; w0.reforgeLog = ""; w0.RecomputeEnhancedStats();
            RangedPartsState.Current.Log.Clear();   // 10-19 재련 기록 — 실측마다 빈 기록에서
            run.FuelBank.Add(FuelKind.EnhanceMaterial, 200);
            HubGui("HideTestHubGui");

            var ctrl = go.AddComponent<CrucibleRoomController>();
            ctrl.Initialize(run, await WeaponEnhanceService.EnsureLoadedAsync(), new System.Random(7), null);
            var panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_CruciblePanel>();
            panel.Bind(ctrl);
            await Wait(0.9f, ct);
            var cn0 = F<TMP_Text[]>(panel, "_chooseName");
            Check(sb, "첫 화면 「무기 재련 · 파츠 재련」", cn0 != null && Parsed(cn0[0]).StartsWith("무기 재련") && Parsed(cn0[1]).StartsWith("파츠 재련"),
                  cn0 != null ? $"「{Parsed(cn0[0])}」 「{Parsed(cn0[1])}」" : "없음");
            var cb0 = F<TMP_Text[]>(panel, "_chooseBig");
            Check(sb, "10-19 첫 화면 근접 = 「재련 1회 → 2회」(+N 없음)", cb0 != null && Parsed(cb0[0]).Contains("재련 1회") && Parsed(cb0[0]).Contains("2회") && !Parsed(cb0[0]).Contains("+"),
                  cb0 != null ? $"「{Parsed(cb0[0])}」" : "없음");
            await Shot("FB18_ref_0_chooser", 200);
            F<Button[]>(panel, "_chooseCard")?[0]?.onClick.Invoke();
            Call(panel, "RefreshAll");
            await Wait(0.6f, ct);

            // ── ② 근접 탭
            var btn  = F<Button>(panel, "_enhanceBtn");
            var lbl  = F<TMP_Text>(panel, "_enhanceLabel");
            var rate = F<TMP_Text>(panel, "_successText");
            var rlb  = F<TMP_Text>(panel, "_rateLabel");
            var fail = F<TMP_Text>(panel, "_v3Fail");
            var own  = F<TMP_Text>(panel, "_v3StreakHead");
            var ownRule = F<TMP_Text>(panel, "_v3StreakRule");
            int cost = ctrl.ReforgeCost(false);
            Check(sb, "근접 — 큰 버튼 「재련」 · 숫자 = 재련 비용 · 실패 없음 · 가진 증강",
                  Parsed(lbl).StartsWith("재련") && Parsed(rlb) == "재련 비용" && Parsed(rate) == cost.ToString() && Parsed(fail).Contains("실패 없음") && Parsed(own).Contains("가진 증강"),
                  $"「{Parsed(lbl)}」 · 「{Parsed(rlb)}」 {Parsed(rate)}(비용 {cost}) · 「{Parsed(fail)}」 · 「{Parsed(own)}」");
            var focal = F<TMP_Text>(panel, "_v3Focal");
            var pcap  = F<TMP_Text>(panel, "_v3PathCap");
            Check(sb, "10-19 근접 초점 = 「1회 → 2회」 · 단계 점 머리 「재련」 · 규칙 줄에 +3 없음",
                  Parsed(focal).Contains("1회") && Parsed(focal).Contains("2회") && !Parsed(focal).Contains("+") && Parsed(pcap) == "재련" && !Parsed(fail).Contains("+3"),
                  $"「{Parsed(focal)}」 · 「{Parsed(pcap)}」 · 「{Parsed(fail)}」");
            await Shot("FB18_ref_1_melee", 300);

            // ── ③ 재련 → 증강 고르기
            int fuel0 = ctrl.FuelAmount;
            btn.onClick.Invoke();
            await WaitAnim(panel, ct);
            float t0 = Time.realtimeSinceStartup;
            while (UI_MeleeAugmentPopup.Current == null && Time.realtimeSinceStartup - t0 < 6f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
            var pop = UI_MeleeAugmentPopup.Current;
            Check(sb, "재련 → +3 · 재료는 비용만큼 · 증강 고르기 창", pop != null && w0.enhanceLevel == 6 && ctrl.FuelAmount == fuel0 - cost,
                  $"+{w0.enhanceLevel} · 재료 {fuel0} → {ctrl.FuelAmount} · 창 {pop != null}");
            await Wait(1.2f, ct);
            string brow = pop != null ? Parsed(typeof(UI_MeleeAugmentPopup).GetField("_eyebrow", Inst)?.GetValue(pop) as TMP_Text) : "";
            Check(sb, "10-19 증강 창 머리 = 「재련 2회째」", brow == "재련 2회째", $"「{brow}」");
            await Shot("FB18_ref_2_augment", 200);
            if (pop != null)
            {
                typeof(UI_MeleeAugmentPopup).GetMethod("OnCardClicked", Inst)?.Invoke(pop, new object[] { 0 });
                t0 = Time.realtimeSinceStartup;
                while (UI_MeleeAugmentPopup.Current != null && Time.realtimeSinceStartup - t0 < 4f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            await Wait(0.5f, ct);
            Call(panel, "RefreshAll");
            await Wait(0.3f, ct);
            int cost2 = ctrl.ReforgeCost(false);
            Check(sb, "고른 뒤 — 가진 증강이 붙는다 · 이번 방문 2번째 비용이 오른다", !string.IsNullOrEmpty(w0.augments) && Parsed(own).Contains("1") && cost2 > cost,
                  $"증강 「{w0.augments}」 · 「{Parsed(own)}」 · 「{Parsed(ownRule)}」 · 다음 비용 {cost2}");
            var row0 = F<TMP_Text[]>(panel, "_ownedName")?[0];
            Check(sb, "10-19 재련 기록 — 머리 「재련 기록 1회」 · 첫 줄 「재련 1 · 고른 증강」 · 기록 한 줄",
                  Parsed(own).Contains("재련 기록") && Parsed(row0).StartsWith("재련 1") && w0.reforgeLog.Split(',').Length == 1,
                  $"「{Parsed(own)}」 · 「{Parsed(row0)}」 · 기록 「{w0.reforgeLog}」");
            await Shot("FB18_ref_3_melee_after", 300);

            // ── ④ 원거리 탭 → 파츠 카드
            Call(panel, "SelectTab", 1);
            Call(panel, "RefreshAll");
            await Wait(0.6f, ct);
            int rc = ctrl.ReforgeCost(true);
            Check(sb, "원거리 — 큰 버튼 「재련」 · 파츠 카드 규칙", Parsed(lbl).StartsWith("재련") && Parsed(fail).Contains("파츠 카드"),
                  $"「{Parsed(lbl)}」 · 비용 {Parsed(rate)}(={rc}) · 「{Parsed(fail)}」");
            await Shot("FB18_ref_4_ranged", 300);
            var st = RangedPartsState.Current;
            int sum0 = st != null ? st.TotalLevel : -1;
            btn.onClick.Invoke();
            await WaitAnim(panel, ct);
            t0 = Time.realtimeSinceStartup;
            while (UI_MeleeAugmentPopup.Current == null && Time.realtimeSinceStartup - t0 < 6f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
            pop = UI_MeleeAugmentPopup.Current;
            bool partsMode = pop != null && typeof(UI_MeleeAugmentPopup).GetField("_parts", Inst)?.GetValue(pop) is bool pm && pm;
            Check(sb, "원거리 재련 → 파츠 카드 창(파츠 모드 · 3장)", partsMode && RangedPartsDraft.Offered.Count == RangedPartsDraft.CardCount,
                  $"창 {pop != null} · 파츠 {partsMode} · 카드 {RangedPartsDraft.Offered.Count}");
            await Wait(1.4f, ct);
            await Shot("FB18_ref_5_parts", 200);
            if (pop != null)
            {
                typeof(UI_MeleeAugmentPopup).GetMethod("OnCardClicked", Inst)?.Invoke(pop, new object[] { 0 });
                t0 = Time.realtimeSinceStartup;
                while (UI_MeleeAugmentPopup.Current != null && Time.realtimeSinceStartup - t0 < 4f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            await Wait(0.5f, ct);
            Call(panel, "RefreshAll");
            await Wait(0.3f, ct);
            int sum1 = st != null ? st.TotalLevel : -1;
            Check(sb, "파츠 고른 뒤 — 파츠 합이 오른다 · 재련 기록 목록(10-19)", sum1 > sum0 && Parsed(own).Contains("재련 기록") && st.Log.Count == 1,
                  $"합 {sum0} → {sum1} · 「{Parsed(own)}」 · 「{Parsed(ownRule)}」");
            await Shot("FB18_ref_6_ranged_after", 300);

            // ── ⑤ 10-19 HUD 무기 칸 — 「+N」 대신 「재련 n」
            Managers.UI?.CloseAllPopupUI();
            wm.RaiseEquippedWeaponRefreshed();
            await Wait(0.8f, ct);
            var cpv = Object.FindFirstObjectByType<CombatPanelView>(FindObjectsInactive.Exclude);
            var wl  = cpv != null ? typeof(CombatPanelView).GetField("_weaponLevel", Inst)?.GetValue(cpv) as TMP_Text[] : null;
            var wl0 = wl != null && wl.Length > 0 ? wl[0] : null;
            string tag0 = wl0 != null && wl0.gameObject.activeInHierarchy ? wl0.GetParsedText() : "";
            Check(sb, "10-19 HUD 무기 칸 = 「재련 n」 · 넘침 없음", tag0.StartsWith("재련") && !wl0.isTextOverflowing,
                  $"「{tag0}」 · 강화 {w0.enhanceLevel} · 넘침 {(wl0 != null && wl0.isTextOverflowing)}");
            await Shot("FB18_ref_7_hud", 300);
        }
        catch (System.OperationCanceledException) { }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            Managers.UI?.CloseAllPopupUI();
            Object.Destroy(go);
            sb.AppendLine($"합계 {s_pass}/{s_all}");
            File.WriteAllText(OutTxt, sb.ToString());
            Debug.Log($"{Tag} 끝\n{sb}");
        }
    }

    /// <summary>버튼 아래 한 줄 — 16px 이상 · 말줄임 없음 · 오른쪽 판 바닥 안.</summary>
    private static void SubLineFit(StringBuilder sb, string what, TMP_Text t)
    {
        t.ForceMeshUpdate();
        var board = t.transform.parent as RectTransform;
        var c = new Vector3[4];
        var b = new Vector3[4];
        t.rectTransform.GetWorldCorners(c);
        if (board != null) board.GetWorldCorners(b);
        bool inside = board == null || c[0].y >= b[0].y - 0.5f;
        Check(sb, $"{what} — 아래 한 줄 16px 이상 · 넘침 없음 · 판 안", t.fontSize >= 15.95f && !t.isTextOverflowing && inside,
              $"{t.fontSize:0.#}px · 넘침 {t.isTextOverflowing} · 판 안 {inside}");
    }

    private static bool HaloOn(Component c)
    {
        var g = c != null ? c.GetComponent<UIAffordGlow>() : null;
        return g != null && typeof(UIAffordGlow).GetField("_on", Inst)?.GetValue(g) is bool on && on;
    }

    private static async UniTask WaitAnim(UI_CruciblePanel panel, CancellationToken ct)
    {
        float t0 = Time.realtimeSinceStartup;
        await Wait(0.2f, ct);
        while (Flag(panel, "_animating") && Time.realtimeSinceStartup - t0 < 8f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
        await Wait(0.3f, ct);
    }

    private static void CloseEngrave(UI_CruciblePanel panel, StringBuilder sb)
    {
        var eg = F<GameObject>(panel, "_engravePanel");
        if (eg == null || !eg.activeSelf) return;
        eg.SetActive(false);
        sb.AppendLine("  (각인 택1이 떠서 닫았다)");
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
        wd.highestEnhanceReached = Mathf.Max(wd.highestEnhanceReached, 30);   // 증강 고르기 창은 이 실측의 대상이 아니다 — 뜨지 않게
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

    private static string Parsed(TMP_Text t) => t != null && t.gameObject.activeInHierarchy ? t.GetParsedText() : "";

    private static bool Visible(Component c) => c != null && c.gameObject.activeInHierarchy;

    private static Transform Find(Transform t, string name)
    {
        if (t == null) return null;
        if (t.name == name) return t;
        for (int i = 0; i < t.childCount; i++) { var hit = Find(t.GetChild(i), name); if (hit != null) return hit; }
        return null;
    }

    private static T F<T>(UI_CruciblePanel panel, string name) where T : class
        => typeof(UI_CruciblePanel).GetField(name, Inst)?.GetValue(panel) as T;

    private static bool Flag(UI_CruciblePanel panel, string name)
        => typeof(UI_CruciblePanel).GetField(name, Inst)?.GetValue(panel) is bool b && b;

    private static void Call(UI_CruciblePanel panel, string method, params object[] args)
        => typeof(UI_CruciblePanel).GetMethod(method, Inst)?.Invoke(panel, args.Length == 0 ? null : args);

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
