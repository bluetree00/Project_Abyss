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
/// [실측 · 플레이 중] 10-08 재련소 「형」 UI(c3 시안 01~04 · 톤 v2) — 성장 길 · 오른쪽 판 형 칸 4상태(잠긴 형 · 진화 · 단련 · 형 완성) ·
/// 강화 막대 +3 ◇ · +5 ◆ · 단련 창 고르기 → 확정 · 원거리 탭 길 · 판 겹침. 화면 7장.
/// 테스트 허브에서 런을 열고 첫 방에 들어서면(자동 플레이 「방 진입」) 멈추고 돈다.
/// 결과 Temp/crucible_form_ui.txt · Temp/ui_shots/CR_*.png · 로그 「[CrucibleFormUi] 끝」.
/// </summary>
public static class CrucibleFormUiProbeEditor
{
    private const string AutoRoot = "RelicFairy/Debug/런 구조 자동 실측/";
    private const string Nameless = "Assets/RelicFairy/Weapon/Nameless/Data/T0_Nameless.asset";
    private const string Bow      = "Assets/RelicFairy/Weapon/Bow/Data/T1_Bow.asset";
    private const string Great    = "Assets/RelicFairy/Weapon/Greatsword/Data/T1_Greatsword.asset";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static bool s_armed;
    private static string s_job = "ui";
    private static int  s_pass, s_all;

    [MenuItem("RelicFairy/Debug/10-08 재련소 형 UI 실측 (테스트 허브, 플레이 중)")]
    private static void Begin() => Begin("ui");

    /// <summary>진화 카드(시안 02) · HUD 형 게이지(시안 05) — 바뀐 칸만 5장(EV_1 · HG_1~4). 결과 Temp/crucible_form_evo.txt.</summary>
    [MenuItem("RelicFairy/Debug/10-08 재련소 진화 카드 · HUD 형 게이지 실측 (테스트 허브, 플레이 중)")]
    private static void BeginEvo() => Begin("evo");

    private static void Begin(string job)
    {
        s_job = job;
        if (!Application.isPlaying) { Debug.LogWarning("[CrucibleFormUi] 플레이 모드에서만 동작한다."); return; }
        if (Object.FindFirstObjectByType<TestHubLauncher>() == null) { Debug.LogWarning("[CrucibleFormUi] 테스트 허브에서 시작한다."); return; }
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
            if (p == null) { Debug.LogWarning("[CrucibleFormUi] 런 플레이어가 없다"); return; }
            if (s_job == "evo") EvoAsync(p, p.GetCancellationTokenOnDestroy()).Forget();
            else RunAsync(p, p.GetCancellationTokenOnDestroy()).Forget();
        };
    }

    private static async UniTaskVoid RunAsync(PlayerController p, CancellationToken ct)
    {
        var sb = new StringBuilder("재련소 형 UI 실측\n");
        s_pass = s_all = 0;
        Directory.CreateDirectory("Temp/ui_shots");
        var go = new GameObject("~CrucibleFormUiProbe");
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
            run.FuelBank.Add(FuelKind.EnhanceMaterial, 60);
            HubGui("HideTestHubGui");

            var ctrl = go.AddComponent<CrucibleRoomController>();
            ctrl.Initialize(run, await WeaponEnhanceService.EnsureLoadedAsync(), new System.Random(7), null);
            var panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_CruciblePanel>();
            panel.Bind(ctrl);
            await Wait(0.8f, ct);
            F<Button[]>(panel, "_chooseCard")?[0]?.onClick.Invoke();   // 첫 화면 → 근거리
            Call(panel, "RefreshAll");
            await Wait(0.5f, ct);

            var root = panel.transform;
            var btn  = F<Button>(panel, "_evolveBtn");
            var lbl  = F<TMP_Text>(panel, "_evolveLabel");

            // ── 1 무형검 +2 — 지금 = 무형검 · 다음 = +5 형 · 잠긴 형 두 장 · 막대 ◇ ◆ · 겹침 없음
            Check(sb, "성장 길 — 지금 = 무형검", Txt(root, "Node0", "Sub").Contains("지금"), Txt(root, "Node0", "Sub"));
            string next1 = Txt(root, "GrowthTrack", "Next1") + " / " + Txt(root, "GrowthTrack", "Next2");
            Check(sb, "다음 문장 — 형이 깨어나는 자리", next1.Contains("+5") && next1.Contains("칼바람") && next1.Contains("무게"), next1);
            string head = Txt(root, "FormBlock", "FormHead");
            string ca = Txt(root, "FormCardA", "Name"), cb = Txt(root, "FormCardB", "Name");
            Check(sb, "잠긴 형 두 장", head.Contains("잠긴 형") && (ca + cb).Contains("칼바람") && (ca + cb).Contains("무게"), $"{head} · {ca} · {cb}");
            var ticks = F<TMP_Text[]>(panel, "_ticks");
            string t3 = ticks != null && ticks.Length > 4 ? ticks[2].text : "", t5 = ticks != null && ticks.Length > 4 ? ticks[4].text : "";
            Check(sb, "막대 +3 ◇ · +5 ◆", t3.Contains("◇") && t5.Contains("◆"), $"{t3} · {t5}");
            Check(sb, "판 겹침 없음", NoOverlap(root, out string ov), ov);
            var icA = Find(Find(root, "FormCardA"), "Icon")?.GetComponent<Image>();
            float lum = icA != null ? icA.color.r * 0.3f + icA.color.g * 0.59f + icA.color.b * 0.11f : 0f;
            Check(sb, "잠긴 형 그림 — 검은 네모 아님", icA != null && icA.gameObject.activeInHierarchy && lum > 0.3f && Find(Find(root, "FormCardA"), "Rim") == null, $"밝기 {lum:0.00}");
            await Shot("CR_1_Locked", 200);

            // ── 2 +5 — 형이 깨어난다 · 진화 버튼이 형 칸 안
            SetLevel(wm.Weapon0Data, 5);
            Call(panel, "RefreshAll");
            await Wait(0.4f, ct);
            head = Txt(root, "FormBlock", "FormHead");
            bool inBlock = btn != null && btn.gameObject.activeInHierarchy && Inside(btn.transform as RectTransform, Find(root, "FormBlock") as RectTransform);
            Check(sb, "+5 — 형 칸에 진화 버튼", head.Contains("깨어") && inBlock && btn.interactable, $"{head} · {Parsed(lbl)}");
            await Shot("CR_2_Ready", 200);

            // ── 3 진화(카타나) — 지금 = 형 Ⅰ · 단련 Ⅱ 칸 · 무대 부제 = 형
            Call(panel, "ShowEvolvePanel");
            await Wait(0.5f, ct);
            var branches = typeof(UI_CruciblePanel).GetProperty("CurrentBranches", Inst)?.GetValue(panel) as System.Collections.Generic.IReadOnlyList<WeaponEvolutionSO.Branch>;
            int ki = -1;
            for (int i = 0; branches != null && i < branches.Count; i++) if (branches[i].branchId == "katana") ki = i;
            Call(panel, "PickEvolveCard", ki);
            F<Button>(panel, "_evolveConfirm")?.onClick.Invoke();
            float t0 = Time.realtimeSinceStartup;
            while (wm.Weapon0Data?.form == null && Time.realtimeSinceStartup - t0 < 10f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
            await Wait(3f, ct);
            F<GameObject>(panel, "_engravePanel")?.SetActive(false);
            Call(panel, "HideEvolvePanel");
            Call(panel, "RefreshAll");
            await Wait(0.8f, ct);
            string n1 = Txt(root, "Node1", "Sub");
            head = Txt(root, "FormBlock", "FormHead");
            string pa = Txt(root, "FormCardA", "Name") + Txt(root, "FormCardB", "Name");
            Check(sb, "진화 뒤 — 지금 = 칼바람 Ⅰ", n1.Contains("지금") && n1.Contains("칼바람") && Txt(root, "Node0", "Glyph").Contains("◆"), n1);
            Check(sb, "단련 Ⅱ 칸 — 변주 둘 미리 보기", head.Contains("단련") && head.Contains("Ⅱ") && pa.Contains("쌍풍") && pa.Contains("되감는 바람"), $"{head} · {pa}");
            string tt = Parsed(Find(root, "TypeTier")?.GetComponent<TMP_Text>());
            Check(sb, "무대 부제 = 형", tt.Contains("칼바람"), tt);
            Check(sb, "다음 글자가 길 칸 안(단련 Ⅱ)", NextFits(root, out string nf), nf);
            await Shot("CR_3_Form1", 200);

            // ── 4 단련 창 — 고르기(금 테 · 다른 카드 옅게) → 확정 버튼 「쌍풍」 · 도식
            btn?.onClick.Invoke();
            await Wait(0.5f, ct);
            Find(root, "TemperA")?.GetComponent<Button>()?.onClick.Invoke();
            await Wait(0.5f, ct);
            string conf = Txt(root, "TemperConfirm", "Label");
            var gB = Find(root, "TemperB")?.GetComponent<CanvasGroup>();
            var dia = Find(Find(root, "TemperA"), "Diagram");
            Check(sb, "단련 창 — 고르면 확정 「쌍풍」 · 다른 카드 옅게", conf.Contains("쌍풍") && conf.Contains("12") && gB != null && gB.alpha < 0.7f, $"{conf} · B {gB?.alpha:0.00}");
            Check(sb, "변주 도식", dia != null && dia.childCount >= 4, $"{dia?.childCount}");
            await Shot("CR_4_TemperPick", 150);

            // ── 5 확정 → 망치 → 성장 길 Ⅱ = 쌍풍 · 다음 단련 Ⅲ 20
            int fuel0 = run.FuelBank.EnhanceMaterial;
            Find(root, "TemperConfirm")?.GetComponent<Button>()?.onClick.Invoke();
            await Wait(3f, ct);
            var w = wm.Weapon0Data;
            string n2 = Txt(root, "Node2", "Sub");
            head = Txt(root, "FormBlock", "FormHead");
            Check(sb, "확정 — 단계 2 · 재료 −12 · 성장 길 Ⅱ = 쌍풍", w.formTier == 2 && fuel0 - run.FuelBank.EnhanceMaterial == 12 && n2.Contains("쌍풍"),
                  $"단계 {w.formTier} · 재료 {fuel0} → {run.FuelBank.EnhanceMaterial} · {n2}");
            Check(sb, "다음 = 단련 Ⅲ 20", head.Contains("Ⅲ") && Parsed(lbl).Contains("20"), $"{head} · {Parsed(lbl)}");
            Check(sb, "다음 글자가 길 칸 안(단련 Ⅲ)", NextFits(root, out nf), nf);
            await Shot("CR_5_AfterTemper", 100);

            // ── 6 형 완성
            ctrl.TryTemperForm(PlayerWeaponManager.Slot0, "still_wind");
            Call(panel, "RefreshAll");
            await Wait(0.8f, ct);
            head = Txt(root, "FormBlock", "FormHead");
            string sum = Txt(root, "FormBlock", "FormSummary");
            Check(sb, "형 완성 요약", head.Contains("형 완성") && sum.Contains("쌍풍") && sum.Contains("무풍"), $"{head} · {sum.Replace('\n', '/')}");
            await Shot("CR_6_Complete", 200);

            // ── 7 원거리 탭 — 스킬 단계 길 · 형 칸 없음
            Call(panel, "SelectTab", 1);
            await Wait(0.5f, ct);
            string r1 = Txt(root, "Node1", "Name");
            var fb = Find(root, "FormBlock");
            Check(sb, "원거리 탭 — 스킬 단계 길 · 형 칸 숨김", r1.Contains("2단계") && (fb == null || !fb.gameObject.activeInHierarchy), r1);
            Check(sb, "원거리 판 겹침 없음", NoOverlap(root, out ov), ov);
            var row4 = F<TMP_Text>(panel, "_milestoneLabel");
            if (row4 != null) row4.ForceMeshUpdate();
            Check(sb, "원거리 배율 줄 = 한 줄", row4 != null && row4.textInfo.lineCount <= 1, $"{row4?.textInfo.lineCount}줄 「{Parsed(row4).Replace('\n', '/')}」");
            await Shot("CR_7_Ranged", 200);
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            Managers.UI?.CloseAllPopupUI();
            Object.Destroy(go);
            HubGui("RestoreTestHubGui");
            sb.AppendLine($"통과 {s_pass}/{s_all}");
            File.WriteAllText("Temp/crucible_form_ui.txt", sb.ToString());
            Debug.Log("[CrucibleFormUi] 끝\n" + sb);
        }
    }

    // ── 진화 카드 · HUD 형 게이지 ──
    private static async UniTaskVoid EvoAsync(PlayerController p, CancellationToken ct)
    {
        var sb = new StringBuilder("재련소 진화 카드 · HUD 형 게이지 실측\n");
        s_pass = s_all = 0;
        Directory.CreateDirectory("Temp/ui_shots");
        var go = new GameObject("~CrucibleFormEvoProbe");
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
            SetLevel(wm.Weapon0Data, 5);
            run.FuelBank.Add(FuelKind.EnhanceMaterial, 60);
            HubGui("HideTestHubGui");

            var hud = Object.FindFirstObjectByType<CombatPanelView>();
            var pill0 = hud != null ? Find(hud.transform, "FormGauge") : null;
            Check(sb, "무형검 — 형 게이지 숨김", pill0 == null || !pill0.gameObject.activeInHierarchy, pill0 == null ? "없음" : "꺼짐");

            var ctrl = go.AddComponent<CrucibleRoomController>();
            ctrl.Initialize(run, await WeaponEnhanceService.EnsureLoadedAsync(), new System.Random(7), null);
            var panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_CruciblePanel>();
            panel.Bind(ctrl);
            await Wait(0.8f, ct);
            F<Button[]>(panel, "_chooseCard")?[0]?.onClick.Invoke();
            Call(panel, "RefreshAll");
            await Wait(0.3f, ct);
            var root = panel.transform;

            // ── 진화 카드 — 형이 주인공 · 작동 그림 3컷 · 잃는 것 · 수치 칩
            Call(panel, "ShowEvolvePanel");
            await Wait(0.5f, ct);
            var branches = typeof(UI_CruciblePanel).GetProperty("CurrentBranches", Inst)?.GetValue(panel) as System.Collections.Generic.IReadOnlyList<WeaponEvolutionSO.Branch>;
            int ki = -1, gi = -1;
            for (int i = 0; branches != null && i < branches.Count; i++)
            {
                if (branches[i].branchId == "katana") ki = i;
                else gi = i;
            }
            Call(panel, "PickEvolveCard", ki);
            await Wait(0.5f, ct);
            var A = Find(root, ki == 0 ? "EvolveA" : "EvolveB");
            var G = Find(root, gi == 0 ? "EvolveA" : "EvolveB");
            string aHead = Parsed(Find(A, "FormHead")?.GetComponent<TMP_Text>());
            string a3 = Txt(A, "Cut2", "Line1"), lost = Parsed(Find(A, "Lost")?.GetComponent<TMP_Text>());
            int draw0 = Find(Find(A, "Cut0"), "Draw")?.childCount ?? 0;
            Check(sb, "카타나 카드 — 형 칼바람 · ③ 발동 풍절 · 그림", aHead.Contains("칼바람") && a3.Contains("풍절") && draw0 >= 3, $"{aHead} · {a3} · 그림 {draw0}");
            Check(sb, "잃는 것 = 무형참 · 칩 빠른 검 / 연격 계열", lost.Contains("무형참") && Txt(A, "Persona", "Text").Contains("빠른 검") && Txt(A, "Family", "Text").Contains("연격"),
                  $"{lost} · {Txt(A, "Persona", "Text")} · {Txt(A, "Family", "Text")}");
            var pb = Find(A, "Persona") as RectTransform;
            var pt = pb != null ? Find(pb, "Text")?.GetComponent<TMP_Text>() : null;
            var lb = Find(A, "LostChip") as RectTransform;
            var lt = lb != null ? Find(lb, "Text")?.GetComponent<TMP_Text>() : null;
            Check(sb, "칩 테가 글자를 감싼다(성격 · 잃는 것)", pt != null && lt != null && pb.rect.width >= pt.preferredWidth + 8f && lb.rect.width >= lt.preferredWidth + 8f,
                  $"성격 {pb?.rect.width:0}/{pt?.preferredWidth:0} · 잃는 것 {lb?.rect.width:0}/{lt?.preferredWidth:0}");
            string st0 = Txt(A, "Stat0", "Value");
            Check(sb, "수치 칩 4개(지금 무기 대비)", st0.Length > 0 && Find(A, "Stat3") != null, st0);
            string gHead = Parsed(Find(G, "FormHead")?.GetComponent<TMP_Text>()), g3 = Txt(G, "Cut2", "Line2"), gPips = Txt(G, "Cut0", "Pips");
            Check(sb, "대검 카드 — 형 무게 · 반드시 치명 · 원 칸", gHead.Contains("무게") && g3.Contains("치명") && gPips.Contains("●"), $"{gHead} · {g3} · {gPips}");
            string conf = Parsed(F<TMP_Text>(panel, "_evolveConfirmLbl"));
            var gG = G != null ? G.GetComponent<CanvasGroup>() : null;
            Check(sb, "고르면 확정 「클라렌트」 · 다른 카드 옅게", conf.Contains("클라렌트") && gG != null && gG.alpha < 0.7f, $"{conf} · {gG?.alpha:0.00}");
            await Shot("EV_1_Pick", 150);

            F<Button>(panel, "_evolveConfirm")?.onClick.Invoke();
            float t0 = Time.realtimeSinceStartup;
            while (wm.Weapon0Data?.form == null && Time.realtimeSinceStartup - t0 < 10f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
            await Wait(3f, ct);
            F<GameObject>(panel, "_engravePanel")?.SetActive(false);
            Managers.UI?.CloseAllPopupUI();
            await Wait(0.8f, ct);

            // ── HUD 형 게이지 — 칼바람 5칸 · 3/5 · 가득 · 발동 비움 · 피격 붉게 · 원거리 옅게
            int f0 = Time.frameCount;
            await Wait(1f, ct);
            sb.AppendLine($"  (에디터 프레임 {Time.frameCount - f0}/초)");
            var frt  = WeaponFormRuntime.Of(p);
            var form = frt != null ? frt.Current : null;
            var pill = hud != null ? Find(hud.transform, "FormGauge") as RectTransform : null;
            Check(sb, "진화 뒤 — 근접 원 위 형 게이지 5칸", form != null && pill != null && pill.gameObject.activeInHierarchy && Pips(pill) == 5 && pill.parent != null && pill.parent.name == "Weapon_01",
                  $"{form?.Form.DisplayName} · 칸 {Pips(pill)} · 부모 {pill?.parent?.name}");
            SetStacks(form, 3);
            await Wait(0.3f, ct);
            Check(sb, "3/5 — 찬 칸 3", Filled(pill, form) == 3, $"{Filled(pill, form)}");
            var rev = Find(hud.transform, "ReviveBadge") as RectTransform;
            var w1  = Find(hud.transform, "Weapon_01") as RectTransform;
            Check(sb, "「부활」 표가 든 무기 원과 안 겹침", rev == null || !rev.gameObject.activeInHierarchy || !Overlaps(rev, w1), rev == null ? "표 없음" : $"켜짐 {rev.gameObject.activeInHierarchy}");
            await Shot("HG_1_Three", 0);
            SetStacks(form, 5);
            await Wait(0.3f, ct);
            var line = Find(pill, "Top")?.GetComponent<Image>();
            Check(sb, "가득 — 5칸 · 테가 형 색", Filled(pill, form) == 5 && line != null && Near(line.color, form.Form.Tint), $"{Filled(pill, form)} · 테 {line?.color}");
            await Shot("HG_2_Full", 0);

            // 발동(칸을 줄인 뒤 같은 호출에서 발동 알림 — WindbladeForm.Release 순서) — 매 프레임 지켜본다
            SetStacks(form, 0);
            typeof(WeaponFormBase).GetMethod("RaiseTriggered", Inst)?.Invoke(form, new object[] { "풍절" });
            var (red, left, sec) = await WatchAsync(pill, form, 1.5f, ct);
            Check(sb, "발동 — 붉음 없이 오른쪽부터 비움", !red && left == 0, $"붉음 {red} · 남은 {left} · {sec:0.00}초");
            SetStacks(form, 3);
            await Wait(0.3f, ct);
            SetStacks(form, 0);
            (red, left, sec) = await WatchAsync(pill, form, 1.5f, ct);
            Check(sb, "피격(발동 없이 0) — 붉게 비친 뒤 꺼짐", red && left == 0, $"붉음 {red} · 남은 {left} · {sec:0.00}초");

            SetStacks(form, 2);
            await wm.SwitchToSlotAsync(PlayerWeaponManager.Slot1);
            var cg = pill != null ? pill.GetComponent<CanvasGroup>() : null;
            float tw = Time.realtimeSinceStartup;
            while (cg != null && Mathf.Abs(cg.alpha - 0.6f) > 0.05f && Time.realtimeSinceStartup - tw < 2f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
            Check(sb, "원거리를 든 동안 — 작아지고 옅어짐", cg != null && Mathf.Abs(cg.alpha - 0.6f) < 0.05f && pill.localScale.x < 0.8f, $"알파 {cg?.alpha:0.00} · 배율 {pill?.localScale.x:0.00}");
            await Shot("HG_3_Ranged", 0);
            await wm.SwitchToSlotAsync(PlayerWeaponManager.Slot0);
            await Wait(0.4f, ct);

            // ── 대검 — 무게 4칸(원)
            if (await EquipGreatAsync(p, ct))
            {
                await Wait(0.8f, ct);
                form = frt != null ? frt.Current : null;
                pill = hud != null ? Find(hud.transform, "FormGauge") as RectTransform : null;
                var pip0 = pill != null ? Find(Find(pill, "Pip0"), "Fill")?.GetComponent<Image>() : null;
                SetStacks(form, 4);
                await Wait(0.3f, ct);
                Check(sb, "대검 — 무게 4칸 원 · 가득", form != null && form.Form.FormId == "weight" && Pips(pill) == 4 && pip0 != null && pip0.sprite != null && pip0.sprite.name.StartsWith("ThinD") && Filled(pill, form) == 4,
                      $"{form?.Form.DisplayName} · 칸 {Pips(pill)} · {pip0?.sprite?.name} · 찬 {Filled(pill, form)}");
                await Shot("HG_4_Weight", 0);
            }
            else sb.AppendLine("  대검 장착 실패");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            Managers.UI?.CloseAllPopupUI();
            Object.Destroy(go);
            HubGui("RestoreTestHubGui");
            sb.AppendLine($"통과 {s_pass}/{s_all}");
            File.WriteAllText("Temp/crucible_form_evo.txt", sb.ToString());
            Debug.Log("[CrucibleFormUi] 끝\n" + sb);
        }
    }

    private static async UniTask<bool> EquipGreatAsync(PlayerController p, CancellationToken ct)
    {
        var wm = p.WeaponManager;
        var so = AssetDatabase.LoadAssetAtPath<WeaponSO>(Great);
        if (so == null) return false;
        var runtime = WeaponData.FromSO(so);
        var preload = typeof(PlayerWeaponManager).GetMethod("PreloadWeaponClipsAsync", BindingFlags.Static | BindingFlags.NonPublic);
        if (preload != null) await (UniTask)preload.Invoke(null, new object[] { runtime, ct });
        typeof(PlayerWeaponManager).GetMethod("DestroySlotInstance", BindingFlags.Static | BindingFlags.NonPublic)
            ?.Invoke(null, new object[] { wm.slots[PlayerWeaponManager.Slot0] });
        await UniTask.Yield(PlayerLoopTiming.Update, ct);
        await wm.AcquireWeaponToSlotAsync(runtime, PlayerWeaponManager.Slot0, setActive: true, playAppear: false);
        if (wm.CurrentSlotIndex != PlayerWeaponManager.Slot0) await wm.SwitchToSlotAsync(PlayerWeaponManager.Slot0);
        return wm.Weapon0Data != null && wm.Weapon0Data.HasForm;
    }

    /// <summary>쌓인 수를 넣는다 — 형이 스스로 줄이지 않게(칼바람 3초 뒤 감소 · 무게 5초 뒤 0) 마지막 적중 · 휘두름 시각을 미래로 둔다.</summary>
    /// <summary>줄어듦을 매 프레임 지켜본다 — (한 번이라도 붉었나, 다 꺼진 뒤 남은 칸, 걸린 초). 다 꺼지고 붉음도 걷히면 끝.</summary>
    private static async UniTask<(bool red, int left, float sec)> WatchAsync(Transform pill, WeaponFormBase form, float limit, CancellationToken ct)
    {
        bool red = false;
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < limit)
        {
            await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);
            bool hurt = AnyHurt(pill);
            red |= hurt;
            if (!hurt && Filled(pill, form) == 0) break;
        }
        return (red, Filled(pill, form), Time.realtimeSinceStartup - t0);
    }

    private static void SetStacks(WeaponFormBase form, int n)
    {
        if (form == null) return;
        foreach (var f in new[] { "_lastHitAt", "_lastSwingAt" })
            form.GetType().GetField(f, Inst)?.SetValue(form, Time.time + 1000f);
        typeof(WeaponFormBase).GetMethod("SetStacks", Inst)?.Invoke(form, new object[] { n, WeaponFormBase.StackReason.Gain });
    }

    private static int Pips(Transform pill)
    {
        if (pill == null) return 0;
        int n = 0;
        for (int i = 0; i < pill.childCount; i++) if (pill.GetChild(i).name.StartsWith("Pip") && pill.GetChild(i).gameObject.activeSelf) n++;
        return n;
    }

    private static int Filled(Transform pill, WeaponFormBase form)
    {
        if (pill == null || form == null) return -1;
        int n = 0;
        for (int i = 0; i < pill.childCount; i++)
        {
            var c = pill.GetChild(i);
            if (!c.name.StartsWith("Pip")) continue;
            var fill = c.Find("Fill")?.GetComponent<Image>();
            if (fill != null && Near(fill.color, form.Form.Tint)) n++;
        }
        return n;
    }

    private static bool AnyHurt(Transform pill)
    {
        if (pill == null) return false;
        for (int i = 0; i < pill.childCount; i++)
        {
            var fill = pill.GetChild(i).Find("Fill")?.GetComponent<Image>();
            if (fill != null && fill.color.r > 0.85f && fill.color.g < 0.45f && fill.color.b < 0.45f) return true;
        }
        return false;
    }

    private static bool Overlaps(RectTransform a, RectTransform b)
    {
        if (a == null || b == null) return false;
        var ca = new Vector3[4]; a.GetWorldCorners(ca);
        var cb = new Vector3[4]; b.GetWorldCorners(cb);
        return ca[0].x < cb[2].x && ca[2].x > cb[0].x && ca[0].y < cb[2].y && ca[2].y > cb[0].y;
    }

    private static bool Near(Color a, Color b) => Mathf.Abs(a.r - b.r) < 0.06f && Mathf.Abs(a.g - b.g) < 0.06f && Mathf.Abs(a.b - b.b) < 0.06f;

    // ── 판 겹침 — 성장 길 ↔ 무대 · 정보창, 강화 버튼 ↔ 형 칸, 막대 ↔ 무대 바닥 ──
    private static bool NoOverlap(Transform root, out string why)
    {
        var track = Find(root, "GrowthTrack") as RectTransform;
        var stage = Find(root, "StageArea") as RectTransform;
        var info  = Find(root, "InfoPanel") as RectTransform;
        var enh   = Find(info, "Enhance") as RectTransform;
        var block = Find(info, "FormBlock") as RectTransform;
        var gauge = Find(root, "Gauge") as RectTransform;
        if (track == null || stage == null || info == null) { why = "성장 길 · 판 없음"; return false; }
        var sb = new StringBuilder();
        float tb = Bottom(track);
        if (tb < Top(stage) - 0.5f) sb.Append($"길↔무대 {Top(stage) - tb:0} ");
        if (tb < Top(info) - 0.5f)  sb.Append($"길↔정보 {Top(info) - tb:0} ");
        if (enh != null && enh.gameObject.activeInHierarchy && block != null && block.gameObject.activeInHierarchy && Bottom(enh) < Top(block) - 0.5f)
            sb.Append($"강화↔형칸 {Top(block) - Bottom(enh):0} ");
        if (block != null && block.gameObject.activeInHierarchy && Bottom(block) < Bottom(info) - 0.5f) sb.Append("형칸이 판 밖 ");
        if (gauge != null && gauge.gameObject.activeInHierarchy && Bottom(gauge) < Bottom(stage) - 0.5f) sb.Append("막대가 무대 밖 ");
        why = sb.Length == 0 ? "없음" : sb.ToString();
        return sb.Length == 0;
    }

    /// <summary>「다음」 두 줄의 그려진 글자가 길 칸 오른쪽 끝을 넘지 않는다(10-08 CR_3 · CR_5 — 창 바깥까지 샜다).</summary>
    private static bool NextFits(Transform root, out string why)
    {
        var track = Find(root, "GrowthTrack") as RectTransform;
        why = "";
        if (track == null) { why = "길 없음"; return false; }
        float right = Right(track);
        bool ok = true;
        foreach (var n in new[] { "Next1", "Next2" })
        {
            var t = Find(track, n)?.GetComponent<TMP_Text>();
            if (t == null) { ok = false; why += n + " 없음 "; continue; }
            t.ForceMeshUpdate();
            float x = t.rectTransform.TransformPoint(new Vector3(t.textBounds.max.x, 0f, 0f)).x;
            bool fit = x <= right + 0.5f;
            ok &= fit;
            why += $"{n} {(fit ? "안" : $"밖 +{x - right:0}")} ";
        }
        return ok;
    }

    private static float Right(RectTransform rt) { var c = new Vector3[4]; rt.GetWorldCorners(c); return c[2].x; }

    private static float Top(RectTransform rt)    { var c = new Vector3[4]; rt.GetWorldCorners(c); return c[1].y; }
    private static float Bottom(RectTransform rt) { var c = new Vector3[4]; rt.GetWorldCorners(c); return c[0].y; }

    private static bool Inside(RectTransform a, RectTransform b)
    {
        if (a == null || b == null) return false;
        var ca = new Vector3[4]; a.GetWorldCorners(ca);
        var cb = new Vector3[4]; b.GetWorldCorners(cb);
        return ca[0].y >= cb[0].y - 1f && ca[1].y <= cb[1].y + 1f;
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

    private static string Txt(Transform root, string parent, string child)
    {
        var p = Find(root, parent);
        var t = p != null ? Find(p, child)?.GetComponent<TMP_Text>() : null;
        return Parsed(t);
    }

    private static string Parsed(TMP_Text t) => t != null && t.gameObject.activeInHierarchy ? t.GetParsedText() : "";

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
