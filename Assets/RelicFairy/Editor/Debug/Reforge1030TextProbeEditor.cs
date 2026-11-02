#if UNITY_EDITOR
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
/// [촬영 · 플레이 중] 10-30 재련 글 점검(사용자 「무기 재련 쪽의 텍스트들도 잘 설명되는지 체크」) — 재련소 화면마다 사진 + 보이는 글 전부 + 손을 올리면 뜨는 글.
/// 무형검 재련 1회 → 증강 창 → 진화 직전(재련 3회) · 진화 창 → 카타나 진화 직후(형 Ⅰ) · 형 창 → 재련(형 Ⅱ 받음) · 카타나 최대 → 원거리 · 파츠 창.
/// 테스트 허브에서 런을 열고 첫 방에 들어서면(자동 플레이 「방 진입」) 멈추고 돈다.
/// 결과 Temp/reforge1030_texts.txt · Temp/ui_shots/RF30_*.png · 로그 「[RF30] 끝」.
/// </summary>
public static class Reforge1030TextProbeEditor
{
    private const string Tag      = "[RF30]";
    private const string AutoRoot = "RelicFairy/Debug/런 구조 자동 실측/";
    private const string Bow      = "Assets/RelicFairy/Weapon/Bow/Data/T1_Bow.asset";
    private const string Katana   = "Assets/RelicFairy/Weapon/Katana/Data/T1_Katana.asset";
    private const string OutTxt   = "Temp/reforge1030_texts.txt";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static bool s_armed;

    [MenuItem("RelicFairy/Debug/10-30 재련 글 점검 촬영 (테스트 허브, 플레이 중)")]
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
        var sb = new StringBuilder("10-30 재련 글 점검 — 화면마다 보이는 글 · 손을 올리면 뜨는 글\n");
        Directory.CreateDirectory("Temp/ui_shots");
        var go = new GameObject("~Rf30Probe");
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
            RangedPartsState.Current.Log.Clear();
            run.FuelBank.Add(FuelKind.EnhanceMaterial, 300);
            HubGui("HideTestHubGui");

            var ctrl = go.AddComponent<CrucibleRoomController>();
            ctrl.Initialize(run, await WeaponEnhanceService.EnsureLoadedAsync(), new System.Random(7), null);
            var panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_CruciblePanel>();
            panel.Bind(ctrl);
            await Wait(0.9f, ct);

            // ① 첫 화면
            await Screen(sb, panel, "RF30_0_chooser", "첫 화면(무엇을 재련할까)");
            F<Button[]>(panel, "_chooseCard")?[0]?.onClick.Invoke();
            Call(panel, "RefreshAll");
            await Wait(0.6f, ct);

            // ② 무형검 재련 1회 — 근접 탭
            await Screen(sb, panel, "RF30_1_nameless", "무형검 재련 1회 · 근접 탭");

            // ③ 재련 → 증강 고르기
            F<Button>(panel, "_enhanceBtn")?.onClick.Invoke();
            await WaitAnim(panel, ct);
            await WaitPopup(true, ct);
            await Wait(1.2f, ct);
            await Screen(sb, panel, "RF30_2_augment", "증강 고르기 창", popup: true);
            await PickFirst(ct);
            Call(panel, "RefreshAll");
            await Wait(0.4f, ct);
            await Screen(sb, panel, "RF30_3_nameless_after", "증강 고른 뒤(재련 2회)");

            // ④ 진화 직전 — 재련 3회(+9)
            SetLevel(w0, 9);
            Call(panel, "RefreshAll");
            await Wait(0.5f, ct);
            await Screen(sb, panel, "RF30_4_evolve_ready", "무형검 재련 3회 · 진화할 수 있다");
            Call(panel, "ShowEvolvePanel");
            await Wait(0.8f, ct);
            await Screen(sb, panel, "RF30_5_evolve_panel", "진화 창(무기 고르기)");

            // ⑤ 실제로 진화(첫 분기) → 진화 뒤 각인 고르기 창(사용자 「처음 장비 진화를 하고 효과를 선택하는 팝업 — 리소스가 레거시」)
            Call(panel, "OnBranchClicked", 0);
            float te = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - te < 8f && !(F<GameObject>(panel, "_engravePanel")?.activeSelf ?? false)
                   && (wm.Weapon0Data == null || !wm.Weapon0Data.HasForm || Flag(panel, "_animating")))
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            await Wait(1.0f, ct);
            var eg = F<GameObject>(panel, "_engravePanel");
            if (eg != null && eg.activeSelf)
            {
                await Screen(sb, panel, "RF30_6_engrave", "진화 직후 각인 고르기 창");
                Call(panel, "OnEngravePicked", 0);
                await Wait(0.8f, ct);
                if (eg.activeSelf) { await Screen(sb, panel, "RF30_6b_engrave2", "각인 고르기 창(둘째 스킬)"); Call(panel, "OnEngravePicked", 0); await Wait(0.8f, ct); }
            }
            else sb.AppendLine($"  진화 뒤 각인 창 없음(형 {wm.Weapon0Data?.HasForm})");
            if (wm.Weapon0Data == null || !wm.Weapon0Data.HasForm)
            {
                sb.AppendLine("  진화 실패 — 카타나를 직접 쥔다");
                Managers.UI?.CloseAllPopupUI();
                await Wait(0.4f, ct);
                await EquipAsync(p, Katana, ct);
                SetLevel(wm.Weapon0Data, 9);
                panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_CruciblePanel>();
                panel.Bind(ctrl);
                await Wait(0.9f, ct);
                F<Button[]>(panel, "_chooseCard")?[0]?.onClick.Invoke();
            }
            var wk = wm.Weapon0Data;
            sb.AppendLine($"  진화한 무기 {wk?.displayName} · 형 {wk?.form?.DisplayName} {wk?.formTier}");
            Call(panel, "RefreshAll");
            await Wait(0.6f, ct);
            await Screen(sb, panel, "RF30_6c_evolved_form1", "진화 직후 · 형 Ⅰ");
            Call(panel, "ShowEvolvePanel");
            await Wait(0.8f, ct);
            await Screen(sb, panel, "RF30_7_form_panel", "형 창(형 칸을 눌렀을 때)");
            Call(panel, "HideEvolvePanel");
            Call(panel, "RefreshAll");
            await Wait(0.4f, ct);

            // ⑥ 재련 → 형 Ⅱ 받음
            F<Button>(panel, "_enhanceBtn")?.onClick.Invoke();
            await Wait(1.0f, ct);
            await Screen(sb, panel, "RF30_8_form_grant", "재련 → 형 Ⅱ 받은 순간");
            await WaitAnim(panel, ct);
            await WaitPopup(true, ct);
            await Wait(1.2f, ct);
            await Screen(sb, panel, "RF30_9_katana_augment", "카타나 증강 고르기 창", popup: true);
            await PickFirst(ct);
            Call(panel, "RefreshAll");
            await Wait(0.4f, ct);
            await Screen(sb, panel, "RF30_10_katana_form2", "카타나 재련 4회 · 형 Ⅱ");

            // ⑦ 카타나 최대
            SetLevel(wk, ctrl.MaxAt(PlayerWeaponManager.Slot0));
            Call(panel, "RefreshAll");
            await Wait(0.5f, ct);
            await Screen(sb, panel, "RF30_11_katana_max", "카타나 최대 재련");

            // ⑧ 원거리
            Call(panel, "SelectTab", 1);
            Call(panel, "RefreshAll");
            await Wait(0.6f, ct);
            await Screen(sb, panel, "RF30_12_ranged", "원거리 파츠 탭");
            F<Button>(panel, "_enhanceBtn")?.onClick.Invoke();
            await WaitAnim(panel, ct);
            await WaitPopup(true, ct);
            await Wait(1.4f, ct);
            await Screen(sb, panel, "RF30_13_parts", "파츠 카드 창", popup: true);
            await PickFirst(ct);
        }
        catch (System.OperationCanceledException) { }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            Managers.UI?.CloseAllPopupUI();
            Object.Destroy(go);
            File.WriteAllText(OutTxt, sb.ToString());
            Debug.Log($"{Tag} 끝");
        }
    }

    /// <summary>사진 + 보이는 글 전부(오브젝트 이름 · 글) + 손을 올리면 뜨는 글.</summary>
    private static async UniTask Screen(StringBuilder sb, UI_CruciblePanel panel, string shot, string what, bool popup = false)
    {
        await Shot(shot, 250);
        sb.AppendLine($"\n## {shot} — {what}");
        Transform root = popup && UI_MeleeAugmentPopup.Current != null ? UI_MeleeAugmentPopup.Current.transform : panel.transform;
        foreach (var t in root.GetComponentsInChildren<TMP_Text>(false))
        {
            if (!t.gameObject.activeInHierarchy || !t.enabled || t.color.a < 0.05f) continue;
            string s = t.GetParsedText().Replace("\n", " ⏎ ").Trim();
            if (s.Length == 0) continue;
            sb.AppendLine($"  [{t.name}] {s}{(t.isTextOverflowing ? "  ⚠넘침" : "")}");
        }
        foreach (var tip in root.GetComponentsInChildren<UITooltipTrigger>(false))
        {
            if (!tip.gameObject.activeInHierarchy) continue;
            string title = typeof(UITooltipTrigger).GetField("_title", Inst)?.GetValue(tip) as string;
            string body  = typeof(UITooltipTrigger).GetField("_body", Inst)?.GetValue(tip) as string;
            if (string.IsNullOrEmpty(body)) continue;
            sb.AppendLine($"  (손을 올리면 · {tip.name}) {title} — {body.Replace("\n", " ⏎ ")}");
        }
    }

    private static async UniTask WaitPopup(bool open, CancellationToken ct)
    {
        float t0 = Time.realtimeSinceStartup;
        while ((UI_MeleeAugmentPopup.Current != null) != open && Time.realtimeSinceStartup - t0 < 6f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
    }

    private static async UniTask PickFirst(CancellationToken ct)
    {
        var pop = UI_MeleeAugmentPopup.Current;
        if (pop == null) return;
        typeof(UI_MeleeAugmentPopup).GetMethod("OnCardClicked", Inst)?.Invoke(pop, new object[] { 0 });
        await WaitPopup(false, ct);
        await Wait(0.5f, ct);
    }

    private static async UniTask WaitAnim(UI_CruciblePanel panel, CancellationToken ct)
    {
        float t0 = Time.realtimeSinceStartup;
        await Wait(0.2f, ct);
        while (Flag(panel, "_animating") && Time.realtimeSinceStartup - t0 < 8f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
        await Wait(0.3f, ct);
    }

    private static void SetLevel(WeaponData wd, int lv)
    {
        if (wd == null) return;
        wd.enhanceLevel = lv;
        wd.highestEnhanceReached = Mathf.Max(wd.highestEnhanceReached, lv);
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
        return wm.Weapon0Data != null && wm.Weapon0Data.HasForm;
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
