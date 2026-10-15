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
/// [촬영 · 플레이 중] 10-14 재련소 v3 「한눈에」(구현설계/정제소·재련소_v3_벤치마킹설계_20261014.md §1-2 · §3) — 화면 8장 + CHECK.
/// 무기(+2) · 무기 +5 · 진화 들어가기(형 칸 → 4b 진화 화면) · 성공 · 실패 · 이정표(+4 → +5) · 원거리 파츠 · 형 칸(진화 뒤 단련).
/// CHECK — 보이는 글자 최소 16px(크기 태그 반영) · 넘침 0 · 글자 겹침 0 · 기울임 0 · 초점 72 · 96 · 판 1560×880 · 창 밖 글자 0 · 후광 1곳 이하 ·
/// 결과 연출 길이(성공 0.4 · 실패 0.5 · 이정표 1.5 · 진화 1.5 — 망치가 닿은 뒤부터) · 판정 중 창 흔들림 0.
/// 판정은 결정적으로 — 재련소 굴림 줄기(_roomRng)를 정해진 수를 내는 Random으로 바꿔 끼운다(실측 전용 · 확률 · 비용 · 규칙 불변).
/// 테스트 허브에서 런을 열고 첫 방에 들어서면(자동 플레이 「방 진입」) 멈추고 돈다.
/// 결과 Temp/crucible_v3.txt · Temp/ui_shots/CV3_*.png · 로그 「[CrucibleV3] 끝」.
/// </summary>
public static class CrucibleV3ProbeEditor
{
    private const string AutoRoot = "RelicFairy/Debug/런 구조 자동 실측/";
    private const string Nameless = "Assets/RelicFairy/Weapon/Nameless/Data/T0_Nameless.asset";
    private const string Bow      = "Assets/RelicFairy/Weapon/Bow/Data/T1_Bow.asset";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const float MinFont = 16f;
    private static readonly Regex SizePct = new(@"<size=(\d+(?:\.\d+)?)%>");
    private static readonly Regex SizeAbs = new(@"<size=(\d+(?:\.\d+)?)>");
    private static readonly string[] Overlays = { "ForgeChooser", "EvolveDim", "TemperDim", "EngraveDim", "LegendDim", "V3Parked" };
    private static bool s_armed;
    private static int  s_pass, s_all;

    /// <summary>정해진 수를 차례로 내는 굴림(마지막 수를 되풀이) — 성공 0.0 · 잭팟 없음 0.99 · 실패 0.999.</summary>
    private sealed class SeqRandom : System.Random
    {
        private readonly double[] _v;
        private int _i;
        public SeqRandom(params double[] v) : base(1) { _v = v; }
        public override double NextDouble() => _v[Mathf.Min(_i++, _v.Length - 1)];
    }

    private sealed class Timing
    {
        public float reveal = -1f, end = -1f, shake;
        public bool  revealed, done;
        public float Feedback => reveal >= 0f && end >= 0f ? end - reveal : -1f;
        public override string ToString() => $"판정 {reveal:0.00}s · 끝 {end:0.00}s · 결과 {Feedback:0.00}s · 창 이동 최대 {shake:0.0}px";
    }

    [MenuItem("RelicFairy/Debug/10-14 재련소 v3 한눈에 촬영 (테스트 허브, 플레이 중)")]
    private static void Begin()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[CrucibleV3] 플레이 모드에서만 동작한다."); return; }
        if (Object.FindFirstObjectByType<TestHubLauncher>() == null) { Debug.LogWarning("[CrucibleV3] 테스트 허브에서 시작한다."); return; }
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
            if (p == null) { Debug.LogWarning("[CrucibleV3] 런 플레이어가 없다"); return; }
            RunAsync(p, p.GetCancellationTokenOnDestroy()).Forget();
        };
    }

    private static async UniTaskVoid RunAsync(PlayerController p, CancellationToken ct)
    {
        var sb = new StringBuilder("재련소 v3 「한눈에」 촬영(10-14)\n");
        s_pass = s_all = 0;
        Directory.CreateDirectory("Temp/ui_shots");
        var go = new GameObject("~CrucibleV3Probe");
        try
        {
            typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);
            foreach (var mo in Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
                if (mo != null && !mo.IsDead) mo.gameObject.SetActive(false);
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
            SetEvent(ctrl, CrucibleEvent.Fever);   // 원인 칩 「▲ +15 화로」
            var panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_CruciblePanel>();
            panel.Bind(ctrl);
            await Wait(0.9f, ct);
            var v3  = F<RectTransform>(panel, "_v3Root");
            var win = F<RectTransform>(panel, "_window");
            Check(sb, "v3 판이 지어졌다", v3 != null && Flag(panel, "_v3Ready"), v3 != null ? v3.name : "없음");
            if (v3 == null) return;

            // ── 1 무기 +2 — 두 판 · 초점 72 · 96 · 원인 칩 · 실패 한 줄 · 옛 막대 · 성장 길 숨김
            F<Button[]>(panel, "_chooseCard")?[0]?.onClick.Invoke();
            Call(panel, "RefreshAll");
            await Wait(0.6f, ct);
            Check(sb, "판 1560×880(1920×1080 캔버스)", Mathf.Abs(v3.rect.width - 1560f) < 0.5f && Mathf.Abs(v3.rect.height - 880f) < 0.5f,
                  $"{v3.rect.width:0}×{v3.rect.height:0} · 배율 {v3.localScale.x:0.###} · 캔버스 {((RectTransform)panel.transform).rect.size}");
            Check(sb, "원인 칩 「▲ +15 화로」(숫자 옆)", ChipTexts(panel).Contains("▲ +15 화로"), ChipTexts(panel));
            string failLine = Parsed(F<TMP_Text>(panel, "_v3Fail"));
            Check(sb, "실패 한 줄 = 「실패해도 단계는 그대로 · 재료만 사라진다」(+2 · 하락 없음)", failLine == "실패해도 단계는 그대로 · 재료만 사라진다", $"「{failLine}」");
            var track = F<RectTransform>(panel, "_track");
            Check(sb, "성장 길 · 불꽃 막대 숨김(단계 점이 대신)", (track == null || !track.gameObject.activeInHierarchy) && !Visible(Find(panel.transform, "Gauge")),
                  $"길 {track?.gameObject.activeInHierarchy} · 막대 {Visible(Find(panel.transform, "Gauge"))}");
            int pips = ActivePips(panel);
            int max  = ctrl.MaxAt(PlayerWeaponManager.Slot0);
            Check(sb, "단계 점 = 강화 상한(15까지)", pips == Mathf.Min(max, 15), $"점 {pips} · 상한 {max}");
            Screen(sb, "무기 +2", panel, v3, win, melee: true);
            await Shot("CV3_1_Weapon", 300);

            // ── 2 무기 +5 — 형 칸 「깨어날 수 있다 ▶」 · 반값 화로 칩(재료 줄)
            SetEvent(ctrl, CrucibleEvent.Discount);
            SetLevel(wm.Weapon0Data, 5);
            Call(panel, "RefreshAll");
            await Wait(0.5f, ct);
            var row    = F<Button>(panel, "_evolveBtn");
            var rowLbl = F<TMP_Text>(panel, "_evolveLabel");
            Check(sb, "+5 — 형 칸 한 줄(「형 … ▶」 · 누를 수 있음)", row != null && row.gameObject.activeInHierarchy && row.interactable
                  && Parsed(rowLbl).StartsWith("형") && Parsed(rowLbl).Contains("▶"), $"「{Parsed(rowLbl)}」");
            Check(sb, "반값 화로 칩(재료 줄)", Parsed(Find(F<RectTransform>(panel, "_v3MatChipBox"), "Text")?.GetComponent<TMP_Text>()) == "▲ 반값 화로", "");
            Screen(sb, "무기 +5", panel, v3, win, melee: true);
            await Shot("CV3_2_Weapon5", 300);

            // ── 3 진화 들어가기 — 형 칸을 누르면 4b 진화 화면(기존 그대로 · ShowEvolvePanel)
            row?.onClick.Invoke();
            await Wait(0.5f, ct);
            var evo = F<GameObject>(panel, "_evolvePanel");
            Check(sb, "형 칸 → 4b 진화 화면", evo != null && evo.activeInHierarchy, $"{evo?.activeInHierarchy}");
            await Shot("CV3_3_EvolveEntry", 300);
            Call(panel, "HideEvolvePanel");

            // ── 4 성공(+2 → +3) — 결과 0.4초 · 흔들림 0 · 연속 1회
            SetEvent(ctrl, CrucibleEvent.None);
            SetLevel(wm.Weapon0Data, 2);
            Call(panel, "RefreshAll");
            await Wait(0.5f, ct);
            var ts = await EnhanceAsync(panel, ctrl, win, new SeqRandom(0.0, 0.99), "CV3_4_Success", 0.10f, ct);
            Check(sb, "성공 — 결과 0.4초(±0.12)", Mathf.Abs(ts.Feedback - 0.4f) <= 0.12f, ts.ToString());
            Check(sb, "성공 — 판정 중 창 흔들림 0", ts.shake < 0.5f, $"{ts.shake:0.0}px");
            Check(sb, "성공 — +3 · 연속 성공 1회", wm.Weapon0Data.enhanceLevel == 3 && Parsed(F<TMP_Text>(panel, "_v3StreakHead")).Contains("1회"),
                  $"+{wm.Weapon0Data.enhanceLevel} · 「{Parsed(F<TMP_Text>(panel, "_v3StreakHead"))}」");
            CloseEngrave(panel, sb);
            Screen(sb, "성공 뒤(+3)", panel, v3, win, melee: true);

            // ── 5 실패(+3 그대로) — 결과 0.5초 · 흔들림 0 · 연속 0
            var tf = await EnhanceAsync(panel, ctrl, win, new SeqRandom(0.999), "CV3_5_Fail", 0.12f, ct);
            Check(sb, "실패 — 결과 0.5초(±0.12)", Mathf.Abs(tf.Feedback - 0.5f) <= 0.12f, tf.ToString());
            Check(sb, "실패 — 판정 중 창 흔들림 0", tf.shake < 0.5f, $"{tf.shake:0.0}px");
            Check(sb, "실패 — 연속 0회", Parsed(F<TMP_Text>(panel, "_v3StreakHead")).Contains("0회"), Parsed(F<TMP_Text>(panel, "_v3StreakHead")));
            Screen(sb, "실패 뒤", panel, v3, win, melee: true);

            // ── 6 이정표(+4 → +5 형이 깨어난다) — 결과 1.5초
            SetLevel(wm.Weapon0Data, 4);
            Call(panel, "RefreshAll");
            await Wait(0.4f, ct);
            var tm = await EnhanceAsync(panel, ctrl, win, new SeqRandom(0.0, 0.99), "CV3_6_Milestone", 0.55f, ct);
            Check(sb, "이정표(형 열림) — 결과 1.5초(±0.15)", Mathf.Abs(tm.Feedback - 1.5f) <= 0.15f, tm.ToString());
            Check(sb, "이정표 — 판정 중 창 흔들림 0", tm.shake < 0.5f, $"{tm.shake:0.0}px");
            CloseEngrave(panel, sb);

            // ── 7 원거리 파츠 — 근거리와 같은 뼈대(10-15): 성공 확률 · 칩 · 연속 · 재료 · 실패 규칙 · [강화하기] 하나
            Call(panel, "SelectTab", 1);
            await Wait(0.5f, ct);
            var parts = Managers.WeaponParts?.All;
            string pid = parts != null && parts.Count > 0 ? parts[0].part_id : null;
            if (typeof(UI_CruciblePanel).GetField("_selectedPartSlot", Inst)?.GetValue(panel) is int sel0 && sel0 != 0) Call(panel, "SelectPartSlot", 0);
            run.FuelBank.Add(FuelKind.EnhanceMaterial, 300);
            Call(panel, "RefreshAll");
            await Wait(0.3f, ct);
            string rate = Parsed(F<TMP_Text>(panel, "_successText"));
            Check(sb, "원거리 — 성공 확률 숫자(%)", rate.EndsWith("%"), $"「{rate}」");
            var rowBtns = F<Button[]>(panel, "_slotBtn");
            bool anyRowBtn = rowBtns != null && System.Array.Exists(rowBtns, b => b != null && b.gameObject.activeInHierarchy);
            Check(sb, "원거리 — 줄마다 강화 버튼 없음(입구 하나)", !anyRowBtn, $"{anyRowBtn}");
            Check(sb, "원거리 — 재료 줄 · 실패 규칙", F<RectTransform>(panel, "_v3MatRow") is RectTransform mr && mr.gameObject.activeInHierarchy,
                  Parsed(F<TMP_Text>(panel, "_v3Fail")));
            var partFocal = F<TMP_Text>(panel, "_v3PartFocal");
            Check(sb, "원거리 — 고른 파츠 초점 「Lv a → b」", partFocal != null && Parsed(partFocal).StartsWith("Lv"), $"「{Parsed(partFocal)}」");
            Check(sb, "원거리 — [강화하기]가 고른 파츠를 올린다(두 탭 공용)", F<Button>(panel, "_enhanceBtn") is Button eb && eb.gameObject.activeInHierarchy,
                  Parsed(F<TMP_Text>(panel, "_enhanceLabel")));
            Screen(sb, "원거리 파츠", panel, v3, win, melee: false);
            await Shot("CV3_7_Ranged", 300);

            // 7b 파츠 성공 — 판정 한 박자 뒤 공개 · Lv +1
            var pstate = RangedPartsState.Current;
            int plv0 = pstate != null && pid != null ? pstate.LevelOf(pid) : -1;
            var tps = await EnhanceAsync(panel, ctrl, win, new SeqRandom(0.0, 0.99), "CV3_7b_PartSuccess", 0.10f, ct);
            CloseEngrave(panel, sb);
            Check(sb, "파츠 성공 — Lv +1", pstate != null && pstate.LevelOf(pid) == plv0 + 1, $"{plv0} → {pstate?.LevelOf(pid)} · {tps}");

            // 7c 높은 단계 실패 — 다음 단계 8 이상이면 1 하락(Lv7 → 6)
            if (pstate != null && pid != null)
            {
                int need = 7 - pstate.LevelOf(pid);
                if (need > 0) pstate.LevelUp(pid, need);
                Call(panel, "RefreshAll");
                await Wait(0.3f, ct);
                string rule = Parsed(F<TMP_Text>(panel, "_v3Fail"));
                Check(sb, "파츠 Lv7 — 실패 규칙 「−1 하락」", rule.Contains("하락"), $"「{rule}」 · 확률 「{Parsed(F<TMP_Text>(panel, "_successText"))}」");
                var tpf = await EnhanceAsync(panel, ctrl, win, new SeqRandom(0.999), "CV3_7c_PartFail", 0.12f, ct);
                CloseEngrave(panel, sb);
                Check(sb, "파츠 실패 — Lv7 → 6 · 연속 0회", pstate.LevelOf(pid) == 6 && Parsed(F<TMP_Text>(panel, "_v3StreakHead")).Contains("0회"),
                      $"Lv {pstate.LevelOf(pid)} · 「{Parsed(F<TMP_Text>(panel, "_v3StreakHead"))}」 · {tpf}");
                await Shot("CV3_7d_AfterFail", 200);
            }

            // ── 8 형 칸(진화 뒤) — 칼바람으로 진화(이정표 1.5초) → 「형 칼바람 Ⅰ — 단련 Ⅱ · 강화재료 N ▶」 → 누르면 4b 단련 화면
            Call(panel, "SelectTab", 0);
            SetLevel(wm.Weapon0Data, 5);
            run.FuelBank.Add(FuelKind.EnhanceMaterial, 40);
            Call(panel, "RefreshAll");
            await Wait(0.4f, ct);
            Call(panel, "ShowEvolvePanel");
            await Wait(0.4f, ct);
            var branches = typeof(UI_CruciblePanel).GetProperty("CurrentBranches", Inst)?.GetValue(panel) as IReadOnlyList<WeaponEvolutionSO.Branch>;
            int ki = 0;
            for (int i = 0; branches != null && i < branches.Count; i++) if (branches[i].branchId == "katana") ki = i;
            Call(panel, "PickEvolveCard", ki);
            await Wait(0.3f, ct);
            var te = await MeasureAsync(panel, win, () => F<Button>(panel, "_evolveConfirm")?.onClick.Invoke(), null, 0f, ct);
            Check(sb, "진화(이정표) — 결과 1.5초(±0.2)", Mathf.Abs(te.Feedback - 1.5f) <= 0.2f, te.ToString());
            Check(sb, "진화 — 판정 중 창 흔들림 0", te.shake < 0.5f, $"{te.shake:0.0}px");
            CloseEngrave(panel, sb);
            Call(panel, "RefreshAll");
            await Wait(0.5f, ct);
            row    = F<Button>(panel, "_evolveBtn");
            rowLbl = F<TMP_Text>(panel, "_evolveLabel");
            Check(sb, "진화 뒤 — 형 칸 「형 … 단련 …」", row != null && row.gameObject.activeInHierarchy && Parsed(rowLbl).Contains("단련"), $"「{Parsed(rowLbl)}」");
            Screen(sb, "형 칸(진화 뒤)", panel, v3, win, melee: true);
            await Shot("CV3_8_FormRow", 300);
            if (row != null && row.interactable)
            {
                row.onClick.Invoke();
                await Wait(0.4f, ct);
                var temper = F<GameObject>(panel, "_temperPanel");
                Check(sb, "형 칸 → 4b 단련 화면", temper != null && temper.activeInHierarchy, $"{temper?.activeInHierarchy}");
                temper?.SetActive(false);
            }
            else Check(sb, "형 칸 → 4b 단련 화면(재료가 되면 누를 수 있다)", false, $"누를 수 없음 · 재료 {ctrl.FuelAmount}");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            Managers.UI?.CloseAllPopupUI();
            Object.Destroy(go);
            HubGui("RestoreTestHubGui");
            sb.AppendLine($"통과 {s_pass}/{s_all}");
            File.WriteAllText("Temp/crucible_v3.txt", sb.ToString());
            Debug.Log("[CrucibleV3] 끝\n" + sb);
        }
    }

    // ── 강화 한 번 · 연출 길이 ──

    /// <summary>굴림을 바꿔 끼우고 [강화하기]를 누른다. 판정(도장이 켜진 순간) + shotAt초에 촬영, 연출이 끝날 때까지 잰다.</summary>
    private static UniTask<Timing> EnhanceAsync(UI_CruciblePanel panel, CrucibleRoomController ctrl, RectTransform win, System.Random rng,
                                                string shot, float shotAt, CancellationToken ct)
    {
        typeof(CrucibleRoomController).GetField("_roomRng", Inst)?.SetValue(ctrl, rng);
        return MeasureAsync(panel, win, () => F<Button>(panel, "_enhanceBtn")?.onClick.Invoke(), shot, shotAt, ct);
    }

    /// <summary>
    /// act를 부르고 매 프레임 잰다 — 판정 = 판정 도장(_stageResult)이 켜진 순간 · 끝 = _animating이 꺼진 순간. 결과 = 끝 − 판정(망치가 닿은 뒤).
    /// 그동안 창(_window)이 원점에서 벗어난 최대 거리(흔들림 · 펀치)도 잰다.
    /// </summary>
    private static async UniTask<Timing> MeasureAsync(UI_CruciblePanel panel, RectTransform win, System.Action act, string shot, float shotAt, CancellationToken ct)
    {
        var tm = new Timing();
        var stamp = F<TMP_Text>(panel, "_stageResult");
        float t0 = Time.realtimeSinceStartup;
        act();
        WatchAsync(panel, win, stamp, tm, t0, ct).Forget();
        await UniTask.WaitUntil(() => tm.revealed || tm.done, PlayerLoopTiming.Update, ct);
        if (shot != null)
        {
            await Wait(shotAt, ct);
            await Shot(shot, 0);
        }
        await UniTask.WaitUntil(() => tm.done, PlayerLoopTiming.Update, ct);
        return tm;
    }

    private static async UniTaskVoid WatchAsync(UI_CruciblePanel panel, RectTransform win, TMP_Text stamp, Timing tm, float t0, CancellationToken ct)
    {
        try
        {
            while (true)
            {
                float now = Time.realtimeSinceStartup - t0;
                if (win != null) tm.shake = Mathf.Max(tm.shake, win.anchoredPosition.magnitude);
                if (!tm.revealed && stamp != null && stamp.gameObject.activeSelf) { tm.revealed = true; tm.reveal = now; }
                if (!Flag(panel, "_animating") && now > 0.05f) { tm.end = now; break; }
                if (now > 10f) break;
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (System.OperationCanceledException) { }
        tm.done = true;
    }

    /// <summary>스킬 단계에 올라 각인 택1이 떴으면 닫는다(촬영 · CHECK가 덮개 창을 재지 않게).</summary>
    private static void CloseEngrave(UI_CruciblePanel panel, StringBuilder sb)
    {
        var eg = F<GameObject>(panel, "_engravePanel");
        if (eg == null || !eg.activeSelf) return;
        eg.SetActive(false);
        sb.AppendLine("  (각인 택1이 떠서 닫았다)");
    }

    // ── 화면 CHECK ──

    /// <summary>한 화면 — 보이는 글자 최소 16 · 넘침 0 · 겹침 0 · 기울임 0 · 초점 72 · 96 · 창 밖 글자 0 · 후광 1곳 이하.</summary>
    private static void Screen(StringBuilder sb, string what, UI_CruciblePanel panel, RectTransform v3, RectTransform win, bool melee)
    {
        Canvas.ForceUpdateCanvases();
        var texts = VisibleTexts(v3);

        float min = 999f; string minName = "";
        foreach (var t in texts)
        {
            float e = EffectiveMin(t);
            if (e < min) { min = e; minName = $"{Path(t.transform, v3)} 「{Short(t)}」"; }
        }
        Check(sb, $"{what} — 글자 최소 16px", min >= MinFont - 0.05f, $"최소 {min:0.#} · {minName} · 보이는 글 {texts.Count}");

        var over = new StringBuilder();
        foreach (var t in texts) if (Overflows(t, out string why)) over.Append($"{Path(t.transform, v3)}({why}) ");
        Check(sb, $"{what} — 넘침 0", over.Length == 0, over.Length == 0 ? "없음" : over.ToString());

        var hit = new StringBuilder();
        int pairs = 0;
        var boxes = new List<(TMP_Text t, Rect r)>(texts.Count);
        foreach (var t in texts) boxes.Add((t, RenderedRect(t, v3)));
        for (int i = 0; i < boxes.Count; i++)
            for (int j = i + 1; j < boxes.Count; j++)
            {
                if (boxes[i].t.transform.IsChildOf(boxes[j].t.transform) || boxes[j].t.transform.IsChildOf(boxes[i].t.transform)) continue;
                var a = boxes[i].r; var b = boxes[j].r;
                float ox = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
                float oy = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
                if (ox > 1f && oy > 1f) { pairs++; hit.Append($"{boxes[i].t.name}「{Short(boxes[i].t)}」×{boxes[j].t.name}「{Short(boxes[j].t)}」 "); }
            }
        Check(sb, $"{what} — 글자 겹침 0", pairs == 0, pairs == 0 ? "없음" : hit.ToString());

        var ital = new StringBuilder();
        foreach (var t in texts)
            if ((t.fontStyle & FontStyles.Italic) != 0 || t.text.Contains("<i>")) ital.Append(t.name).Append(' ');
        Check(sb, $"{what} — 기울임 0", ital.Length == 0, ital.Length == 0 ? "없음" : ital.ToString());

        var focal = F<TMP_Text>(panel, melee ? "_v3Focal" : "_v3PartFocal");
        var big   = F<TMP_Text>(panel, "_successText");
        bool fOk = focal != null && focal.gameObject.activeInHierarchy && Mathf.Abs(focal.fontSize - 72f) < 0.6f;
        bool bOk = big != null && big.gameObject.activeInHierarchy && Mathf.Abs(big.fontSize - 96f) < 0.6f;
        Check(sb, $"{what} — 초점 72 · 96", fOk && bOk, $"초점 {focal?.fontSize:0.#}「{Short(focal)}」 · 숫자 {big?.fontSize:0.#}「{Short(big)}」");

        int outside = 0;
        var outNames = new StringBuilder();
        if (win != null)
            foreach (var t in win.GetComponentsInChildren<TMP_Text>(false))
            {
                if (t.transform.IsChildOf(v3) || !IsVisible(t) || UnderOverlay(t.transform, win)) continue;
                outside++;
                outNames.Append(t.name).Append(' ');
            }
        Check(sb, $"{what} — v3 판 밖 글자 0", outside == 0, outside == 0 ? "없음" : outNames.ToString());

        Check(sb, $"{what} — 후광 1곳 이하", Halos(panel.transform, out string hl) <= 1, hl);
    }

    private static List<TMP_Text> VisibleTexts(RectTransform scope)
    {
        var list = new List<TMP_Text>();
        if (scope == null) return list;
        foreach (var t in scope.GetComponentsInChildren<TMP_Text>(false))
        {
            if (!IsVisible(t) || UnderOverlay(t.transform, scope)) continue;
            if (t.name == "JackpotBanner") continue;   // 잭팟 순간에만 이름 줄 자리에 뜬다(이름이 같이 옅어진다)
            list.Add(t);
        }
        return list;
    }

    private static bool IsVisible(TMP_Text t)
    {
        if (t == null || !t.isActiveAndEnabled || t.color.a < 0.05f || t.alpha < 0.05f) return false;
        if (t.GetParsedText().Trim().Length == 0) return false;
        float a = 1f;
        foreach (var g in t.GetComponentsInParent<CanvasGroup>(false))
        {
            a *= g.alpha;
            if (g.ignoreParentGroups) break;
        }
        return a >= 0.05f;
    }

    private static bool UnderOverlay(Transform t, Transform stop)
    {
        for (var p = t; p != null && p != stop; p = p.parent)
            foreach (var o in Overlays) if (p.name == o) return true;
        return false;
    }

    /// <summary>보이는 크기 — 자동 크기를 반영한 fontSize × 가장 작은 크기 태그(%) · 절대 크기 태그는 그 값.</summary>
    private static float EffectiveMin(TMP_Text t)
    {
        t.ForceMeshUpdate();
        float s = t.fontSize, m = s;
        foreach (Match x in SizePct.Matches(t.text)) m = Mathf.Min(m, s * float.Parse(x.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) / 100f);
        foreach (Match x in SizeAbs.Matches(t.text)) m = Mathf.Min(m, float.Parse(x.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
        return m;
    }

    /// <summary>넘침 — 말줄임으로 잘렸거나, 그려진 글자 상자가 칸 밖으로 2px 넘게 나갔다.</summary>
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

    /// <summary>그려진 글자 상자 — v3 판 좌표(배율 · 펀치 크기 반영).</summary>
    private static Rect RenderedRect(TMP_Text t, RectTransform v3)
    {
        t.ForceMeshUpdate();
        var b = t.textBounds;
        var a = v3.InverseTransformPoint(t.transform.TransformPoint(b.min));
        var c = v3.InverseTransformPoint(t.transform.TransformPoint(b.max));
        return Rect.MinMaxRect(Mathf.Min(a.x, c.x), Mathf.Min(a.y, c.y), Mathf.Max(a.x, c.x), Mathf.Max(a.y, c.y));
    }

    private static int ActivePips(UI_CruciblePanel panel)
    {
        int n = 0;
        var pips = F<Image[]>(panel, "_v3Pip");
        if (pips != null) foreach (var p in pips) if (p != null && p.gameObject.activeInHierarchy) n++;
        return n;
    }

    private static string ChipTexts(UI_CruciblePanel panel)
    {
        var chips = F<TMP_Text[]>(panel, "_v3Chip");
        var sb = new StringBuilder();
        if (chips != null) foreach (var c in chips) if (c != null && c.gameObject.activeInHierarchy) sb.Append(c.text).Append(" | ");
        return sb.ToString();
    }

    /// <summary>켜진 후광 수 — UIAffordGlow가 켜짐(_on)이고 화면에 살아 있는 것. 이름 목록을 함께.</summary>
    private static int Halos(Transform root, out string names)
    {
        int n = 0;
        var sb = new StringBuilder();
        var on = typeof(UIAffordGlow).GetField("_on", Inst);
        foreach (var g in root.GetComponentsInChildren<UIAffordGlow>(false))
        {
            if (!g.isActiveAndEnabled || on == null || !(bool)on.GetValue(g)) continue;
            n++;
            sb.Append(g.name).Append(' ');
        }
        names = n == 0 ? "없음" : sb.ToString();
        return n;
    }

    // ── 도구 ──

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

    private static void SetEvent(CrucibleRoomController ctrl, CrucibleEvent ev)
        => typeof(CrucibleRoomController).GetField("_event", Inst)?.SetValue(ctrl, ev);

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

    private static string Short(TMP_Text t)
    {
        string s = Parsed(t);
        return s.Length > 18 ? s.Substring(0, 18) + "…" : s;
    }

    private static bool Visible(Transform t) => t != null && t.gameObject.activeInHierarchy;

    private static string Path(Transform t, Transform stop)
    {
        var sb = new StringBuilder(t.name);
        for (var p = t.parent; p != null && p != stop; p = p.parent) sb.Insert(0, p.name + "/");
        return sb.ToString();
    }

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
