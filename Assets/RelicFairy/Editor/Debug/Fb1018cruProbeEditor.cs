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
/// [촬영 · 플레이 중] 10-18 빌드 피드백 ⑧ 재련소 무형검 진화 흐름 — 사용자 「강화하다 강화 버튼에서 선택하는 걸로 가야 해. 지금은 계속 무형검을 강화하게 되는 UX」.
/// ① +9 — 큰 버튼 [강화하기] · 버튼 아래 예고 「◆ +10에서 다른 검으로 진화」 ② +10 — 큰 버튼 [진화 — 무기 고르기](누름 · 후광 한 곳) · 숫자 「무료」 ·
/// 버튼 아래 「진화 미루고 무형검 강화 …」 ③ 첫 화면 근거리 카드 「진화」 ④ 진화 미루기(+10 → +11 · 굴림 고정) ⑤ 큰 버튼 → 갈래 카드(강화 · 재료 그대로) ·
/// 고른 뒤 확정 「「클라렌트」로 진화」 · 「벼린다」 없음 · 이어짐 한 줄 자리(꺼짐) ⑥ 진화 뒤 — +10 계승 · 큰 버튼 [강화하기] · 아래 한 줄 없음 ⑦ 단련 확정 「단련」.
/// 테스트 허브에서 런을 열고 첫 방에 들어서면(자동 플레이 「방 진입」) 멈추고 돈다. 무형검 단계는 WeaponData 값으로 맞춘다(증강 고르기 창은 뜨지 않게 최고 단계를 올려 둔다).
/// 결과 Temp/fb1018_cru.txt · Temp/ui_shots/FB18_cru_*.png · 로그 「[FB18cru] 끝」.
/// </summary>
public static class Fb1018cruProbeEditor
{
    private const string Tag      = "[FB18cru]";
    private const string AutoRoot = "RelicFairy/Debug/런 구조 자동 실측/";
    private const string Nameless = "Assets/RelicFairy/Weapon/Nameless/Data/T0_Nameless.asset";
    private const string OutTxt   = "Temp/fb1018_cru.txt";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static bool s_armed;
    private static int  s_pass, s_all;

    /// <summary>정해진 수를 차례로 내는 굴림(마지막 수를 되풀이) — 성공 0.0 · 잭팟 없음 0.99.</summary>
    private sealed class SeqRandom : System.Random
    {
        private readonly double[] _v;
        private int _i;
        public SeqRandom(params double[] v) : base(1) { _v = v; }
        public override double NextDouble() => _v[Mathf.Min(_i++, _v.Length - 1)];
    }

    [MenuItem("RelicFairy/Debug/10-18 재련소 무형검 진화 버튼 촬영 (테스트 허브, 플레이 중)")]
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
        var sb = new StringBuilder("10-18 재련소 무형검 진화 흐름 촬영(빌드 피드백 ⑧)\n");
        s_pass = s_all = 0;
        Directory.CreateDirectory("Temp/ui_shots");
        var go = new GameObject("~Fb1018cruProbe");
        try
        {
            typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);
            foreach (var mo in Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
                if (mo != null && !mo.IsDead) mo.gameObject.SetActive(false);
            await Wait(1.5f, ct);

            var wm  = p.WeaponManager;
            var run = GameRunBootstrapper.Instance.Run;
            if (!await EquipAsync(p, Nameless, ct)) { sb.AppendLine("무형검 장착 실패"); return; }
            SetLevel(wm.Weapon0Data, 9);
            run.FuelBank.Add(FuelKind.EnhanceMaterial, 120);
            HubGui("HideTestHubGui");

            var ctrl = go.AddComponent<CrucibleRoomController>();
            ctrl.Initialize(run, await WeaponEnhanceService.EnsureLoadedAsync(), new System.Random(7), null);
            var panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_CruciblePanel>();
            panel.Bind(ctrl);
            await Wait(0.9f, ct);
            Check(sb, "v3 판이 지어졌다", Flag(panel, "_v3Ready"), "");
            var chooserTitle = Find(panel.transform, "ChooserTitle")?.GetComponent<TMP_Text>();
            Check(sb, "첫 화면 제목 「무엇을 강화할까」(「벼릴까」 → 「강화」)", Parsed(chooserTitle) == "무엇을 강화할까", $"「{Parsed(chooserTitle)}」");
            F<Button[]>(panel, "_chooseCard")?[0]?.onClick.Invoke();   // 첫 화면 → 근거리
            Call(panel, "RefreshAll");
            await Wait(0.6f, ct);

            var btn    = F<Button>(panel, "_enhanceBtn");
            var lbl    = F<TMP_Text>(panel, "_enhanceLabel");
            var rate   = F<TMP_Text>(panel, "_successText");
            var rateLb = F<TMP_Text>(panel, "_rateLabel");
            var fail   = F<TMP_Text>(panel, "_v3Fail");
            var sub    = F<TMP_Text>(panel, "_v3EvolveSub");
            var subBtn = F<Button>(panel, "_v3EvolveSubBtn");
            var row    = F<Button>(panel, "_evolveBtn");
            if (btn == null || lbl == null || sub == null || subBtn == null) { sb.AppendLine("부품을 못 찾았다(_enhanceBtn · _enhanceLabel · _v3EvolveSub)"); return; }

            // ── ① +9 — 강화하기 + 예고 한 줄
            Check(sb, "+9 — 큰 버튼 「강화하기」", Parsed(lbl).StartsWith("강화하기"), $"「{Parsed(lbl)}」");
            Check(sb, "+9 — 버튼 아래 예고 「+10에서 다른 검으로 진화」(누르지 않는 글)", Visible(sub) && Parsed(sub).Contains("+10에서 다른 검으로 진화") && !subBtn.interactable,
                  $"「{Parsed(sub)}」 · 누름 {subBtn.interactable}");
            SubLineFit(sb, "+9", sub);
            await Shot("FB18_cru_1_Plus9", 300);

            // ── ② +10 — 큰 버튼이 진화
            SetLevel(wm.Weapon0Data, 10);
            Call(panel, "RefreshAll");
            await Wait(0.5f, ct);
            Check(sb, "+10 — 큰 버튼 「진화 — 무기 고르기」 · 누를 수 있음 · 후광", Parsed(lbl) == "진화 — 무기 고르기" && btn.interactable && HaloOn(btn),
                  $"「{Parsed(lbl)}」 · 누름 {btn.interactable} · 후광 {HaloOn(btn)}");
            Check(sb, "+10 — 오른쪽 숫자 「진화 비용 · 무료」 · 실패 줄 「실패 없음」", Parsed(rateLb) == "진화 비용" && Parsed(rate) == "무료" && Parsed(fail).StartsWith("실패 없음"),
                  $"「{Parsed(rateLb)}」 「{Parsed(rate)}」 「{Parsed(fail)}」");
            Check(sb, "+10 — 버튼 아래 「진화 미루고 무형검 강화 +10 → +11」(누르는 글)", Visible(sub) && Parsed(sub).Contains("진화 미루고") && Parsed(sub).Contains("+11") && subBtn.interactable,
                  $"「{Parsed(sub)}」 · 누름 {subBtn.interactable}");
            SubLineFit(sb, "+10", sub);
            Check(sb, "+10 — 형 칸 한 줄은 후광 없음(후광 한 곳 = 큰 버튼)", row == null || !HaloOn(row), $"형 칸 후광 {(row != null && HaloOn(row))}");
            await Shot("FB18_cru_2_Plus10", 300);

            // ── ③ 첫 화면 근거리 카드(+10) — 진화를 먼저 말한다
            Call(panel, "ShowChooser");
            Call(panel, "RefreshAll");
            await Wait(0.4f, ct);
            string big0 = Parsed(F<TMP_Text[]>(panel, "_chooseBig")?[0]), l10 = Parsed(F<TMP_Text[]>(panel, "_chooseLine1")?[0]);
            Check(sb, "첫 화면 근거리 카드 — 「+10 진화」 · 「다른 검으로 진화할 수 있다」", big0.Contains("진화") && l10.Contains("다른 검으로 진화"), $"「{big0}」 · 「{l10}」");
            await Shot("FB18_cru_3_Chooser10", 200);
            F<Button[]>(panel, "_chooseCard")?[0]?.onClick.Invoke();
            Call(panel, "RefreshAll");
            await Wait(0.4f, ct);

            // ── ④ 진화 미루기 — 버튼 아래 글을 누르면 무형검 그대로 +1(굴림 고정 · 지금 규칙상 +12까지)
            typeof(CrucibleRoomController).GetField("_roomRng", Inst)?.SetValue(ctrl, new SeqRandom(0.0, 0.99));
            subBtn.onClick.Invoke();
            await WaitAnim(panel, ct);
            CloseEngrave(panel, sb);
            Call(panel, "RefreshAll");
            await Wait(0.4f, ct);
            Check(sb, "진화 미루기 — 무형검 +10 → +11 · 큰 버튼은 그대로 진화", wm.Weapon0Data.enhanceLevel == 11 && wm.Weapon0Data.evolution != null && Parsed(lbl) == "진화 — 무기 고르기",
                  $"{wm.Weapon0Data.displayName} +{wm.Weapon0Data.enhanceLevel} · 「{Parsed(lbl)}」");
            await Shot("FB18_cru_4_Deferred11", 200);
            SetLevel(wm.Weapon0Data, 10);
            Call(panel, "RefreshAll");
            await Wait(0.4f, ct);

            // ── ⑤ 큰 버튼 → 갈래 카드(강화 안 됨) · ⑧ 환영 이식(10-18) — 무형검 증강 둘 + 비어 있는 칼집 II를 쥐여 둔다
            wm.Weapon0Data.augments = "aug_nameless_mist:2,aug_nameless_formless:1,aug_nameless_sheath:2";
            int lv0 = wm.Weapon0Data.enhanceLevel, fuel0 = ctrl.FuelAmount;
            btn.onClick.Invoke();
            await Wait(0.6f, ct);
            var evo = F<GameObject>(panel, "_evolvePanel");
            Check(sb, "큰 버튼 → 갈래 카드 화면 · 강화 · 재료 그대로", evo != null && evo.activeInHierarchy && wm.Weapon0Data.enhanceLevel == lv0 && ctrl.FuelAmount == fuel0,
                  $"창 {evo != null && evo.activeInHierarchy} · +{wm.Weapon0Data.enhanceLevel} · 재료 {fuel0} → {ctrl.FuelAmount}");
            var conf = F<TMP_Text>(panel, "_evolveConfirmLbl");
            Check(sb, "제목 「진화할 무기를 고른다」 · 고르기 전 확정 「무기를 고른다」", Parsed(F<TMP_Text>(panel, "_evolveTitle")).Contains("진화할 무기를 고른다") && Parsed(conf).Contains("무기를 고른다"),
                  $"「{Parsed(F<TMP_Text>(panel, "_evolveTitle"))}」 · 「{Parsed(conf)}」 · 「{Parsed(F<TMP_Text>(panel, "_evolveSub"))}」");
            var carry = F<TMP_Text>(panel, "_evolveCarry");
            Check(sb, "⑧ 이어짐 한 줄 — 무형검 증강 2개 → 환영 · 칼집 II → 새 무기 증강 2개", carry != null && carry.gameObject.activeInHierarchy
                  && Parsed(carry).Contains("무형검 증강 2개") && Parsed(carry).Contains("환영이 이어서 씀") && Parsed(carry).Contains("칼집 II → 새 무기 증강 2개"),
                  $"「{Parsed(carry)}」");
            await Shot("FB18_cru_5_Cards", 300);

            var branches = typeof(UI_CruciblePanel).GetProperty("CurrentBranches", Inst)?.GetValue(panel) as IReadOnlyList<WeaponEvolutionSO.Branch>;
            int ki = 0;
            for (int i = 0; branches != null && i < branches.Count; i++) if (branches[i].branchId == "katana") ki = i;
            string target = branches != null && ki < branches.Count && branches[ki].target != null ? branches[ki].target.displayName : "?";
            Call(panel, "PickEvolveCard", ki);
            await Wait(0.4f, ct);
            var hint = Find(F<RectTransform>(panel, "_evolveContent"), "Hint")?.GetComponent<TMP_Text>();   // 새로 지은 내용 안(구운 옛 자식은 꺼져 있다)
            var confBtn = F<Button>(panel, "_evolveConfirm");
            Check(sb, $"고른 뒤 확정 「「{target}」로 진화」 · 「벼린다」 없음", Parsed(conf).Contains($"「{target}」") && Parsed(conf).Contains("진화") && !Parsed(conf).Contains("벼")
                  && !Parsed(hint).Contains("벼") && confBtn != null && confBtn.interactable, $"「{Parsed(conf)}」 · 「{Parsed(hint)}」");
            Check(sb, "⑧ 카드 고른 뒤 — 칼집이 「카타나 증강 2개」", Parsed(carry).Contains("카타나 증강 2개"), $"「{Parsed(carry)}」");
            await Shot("FB18_cru_5b_Picked", 200);

            // ── ⑥ 확정 → 진화 연출(기존) → 새 검의 [강화하기]
            confBtn?.onClick.Invoke();
            float t0 = Time.realtimeSinceStartup;
            while (wm.Weapon0Data?.form == null && Time.realtimeSinceStartup - t0 < 10f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
            await WaitAnim(panel, ct);
            CloseEngrave(panel, sb);
            Call(panel, "RefreshAll");
            await Wait(0.6f, ct);
            var w1 = wm.Weapon0Data;
            Check(sb, $"진화 뒤 — {target} · 강화 +{lv0} 계승", w1 != null && w1.form != null && w1.displayName == target && w1.enhanceLevel == lv0,
                  $"{w1?.displayName} +{w1?.enhanceLevel} · 형 {w1?.form?.DisplayName}");
            Check(sb, "진화 뒤 — 큰 버튼 「강화하기」 · 숫자 % · 아래 한 줄 없음", Parsed(lbl).StartsWith("강화하기") && Parsed(rate).EndsWith("%") && !Visible(sub),
                  $"「{Parsed(lbl)}」 · 「{Parsed(rate)}」 · 아래 한 줄 {Visible(sub)}");
            var resultTxt = F<TMP_Text>(panel, "_resultText");
            Check(sb, "⑧ 진화 뒤 — 무형검 증강 2개 남음 · 환영 몫 · 결과 글 「무형검 환영이 증강 2개」", MeleeAugmentCatalog.HasPhantom(w1)
                  && w1.augments.Contains("aug_nameless_mist:2") && w1.augments.Contains("aug_nameless_formless:1") && !w1.augments.Contains("aug_nameless_sheath")
                  && Parsed(resultTxt).Contains("무형검 환영이 증강 2개"), $"증강 「{w1?.augments}」 · 결과 「{Parsed(resultTxt)}」");
            await Shot("FB18_cru_6_AfterEvolve", 400);

            // ── ⑦ 단련 확정 글 — 「벼린다」 → 「단련」
            if (ctrl.CanTemperForm(PlayerWeaponManager.Slot0, out _, out int tc)) run.FuelBank.Add(FuelKind.EnhanceMaterial, tc + 10);
            Call(panel, "ShowTemperPanel");
            await Wait(0.5f, ct);
            Call(panel, "PickTemperCard", 0);
            await Wait(0.3f, ct);
            var tconf = F<TMP_Text>(panel, "_temperConfirmLbl");
            Check(sb, "단련 확정 「「…」으로 단련」 · 「벼린다」 없음", Parsed(tconf).Contains("단련") && !Parsed(tconf).Contains("벼"), $"「{Parsed(tconf)}」");
            await Shot("FB18_cru_7_TemperConfirm", 200);
            F<GameObject>(panel, "_temperPanel")?.SetActive(false);
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            Managers.UI?.CloseAllPopupUI();
            Object.Destroy(go);
            HubGui("RestoreTestHubGui");
            sb.AppendLine($"통과 {s_pass}/{s_all}");
            File.WriteAllText(OutTxt, sb.ToString());
            Debug.Log($"{Tag} 끝\n{sb}");
        }
    }

    // ── 도구 ──

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
