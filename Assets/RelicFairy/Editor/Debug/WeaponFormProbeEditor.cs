#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Inputs;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;
using TMPro;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// [실측 · 플레이 중] 장비 개선 「형」 — 데이터 · 진화 · 단련 · 칼바람 · 무게 · 변주 · 예산.
/// 테스트 허브에서 런을 열고 첫 방에 들어서면 자동 플레이를 멈추고 이 메뉴의 일을 돈다. 결과 Temp/weapon_form_*.txt
/// 기획: 바탕화면 기획/RelicFairy_장비개선_진화형능력·형단련_20261002.md · 구현계획 구현설계/RelicFairy_장비개선_형능력·형단련_구현계획_20261006.md
/// </summary>
public static class WeaponFormProbeEditor
{
    private const string Root     = "RelicFairy/Debug/장비 형/";
    private const string AutoRoot = "RelicFairy/Debug/런 구조 자동 실측/";
    private const string Nameless = "Assets/RelicFairy/Weapon/Nameless/Data/T0_Nameless.asset";
    private const string Katana   = "Assets/RelicFairy/Weapon/Katana/Data/T1_Katana.asset";
    private const string Great    = "Assets/RelicFairy/Weapon/Greatsword/Data/T1_Greatsword.asset";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static string s_job;
    private static bool   s_armed;
    private static readonly List<string> s_formLog = new();

    [MenuItem(Root + "1 데이터 · 진화 · 단련 (테스트 허브, 플레이 중)")]
    private static void BeginData() => Begin("data");

    [MenuItem(Root + "2 칼바람 (테스트 허브, 플레이 중)")]
    private static void BeginWind() => Begin("wind");

    [MenuItem(Root + "3 무게 (테스트 허브, 플레이 중)")]
    private static void BeginWeight() => Begin("weight");

    [MenuItem(Root + "4 변주 8종 (테스트 허브, 플레이 중)")]
    private static void BeginVariants() => Begin("variants");

    [MenuItem(Root + "7 재련소 화면 — 활 든 채 진화 · 형 단련 (테스트 허브, 플레이 중)")]
    private static void BeginCrucible() => Begin("crucible");

    [MenuItem(Root + "8 평타 이펙트 방향 — 칼 궤적 대조 (테스트 허브, 플레이 중)")]
    private static void BeginSlash() => Begin("slash");

    [MenuItem(Root + "8b 슬래시 기준 촬영만 — 자라는 방향 (테스트 허브, 플레이 중)")]
    private static void BeginSlashRef() => Begin("slash_ref");

    [MenuItem(Root + "5 예산 6칸 (테스트 허브, 플레이 중, 약 5분)")]
    private static void BeginBudget() => Begin("budget");

    [MenuItem(Root + "5b 예산 — 대검 3칸만 (테스트 허브, 플레이 중, 약 3분)")]
    private static void BeginBudgetGreat() => Begin("budget_great");

    [MenuItem(Root + "6 연출 후보 비교 (테스트 허브, 플레이 중)")]
    private static void BeginFx() => Begin("fx");

    private static void Begin(string job)
    {
        if (!Application.isPlaying) { Debug.LogWarning("[WeaponFormProbe] 플레이 모드에서만 동작한다."); return; }
        var launcher = UnityEngine.Object.FindFirstObjectByType<TestHubLauncher>();
        if (launcher == null) { Debug.LogWarning("[WeaponFormProbe] 테스트 허브에서 시작한다."); return; }
        // 예산은 유물 없이 — 가웨인 「정오」 주기 · 낙인 누적이 칸 순서대로 피해를 키워 형 몫을 가렸다(실측 5 1회차 대검 318 → 405 → 552).
        // 인덱스 −1은 허브 화면이 0으로 되돌린다(2회차도 가웨인) → 목록을 「없음」 하나로 바꾼다(Pick → null → 유물 없음).
        if (job.StartsWith("budget")) typeof(TestHubLauncher).GetField("relics", Inst)?.SetValue(launcher, new RelicClassSO[] { null });
        s_job = job;
        s_armed = true;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        EditorApplication.ExecuteMenuItem(AutoRoot + "시작 (테스트 허브에서, 플레이 중)");
    }

    private static void OnLog(string msg, string stack, LogType type)
    {
        if (msg.StartsWith("[WeaponForm]")) s_formLog.Add(msg);
        if (!s_armed || !msg.StartsWith("[RunAuto]") || !msg.Contains("방 진입")) return;
        s_armed = false;
        EditorApplication.delayCall += () =>
        {
            EditorApplication.ExecuteMenuItem(AutoRoot + "중지·기록");
            var p = GameRunBootstrapper.Instance?.Run?.Player;
            if (p == null) { Debug.LogWarning("[WeaponFormProbe] 런 플레이어가 없다"); return; }
            RunAsync(p, p.GetCancellationTokenOnDestroy()).Forget();
        };
    }

    private static async UniTaskVoid RunAsync(PlayerController p, CancellationToken ct)
    {
        var sb = new StringBuilder($"장비 형 실측 — {s_job}\n");
        try
        {
            typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);
            GameRunBootstrapper.Instance?.Run?.CovenantHandler?.Cleanup();
            foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
                if (m != null && !m.IsDead) m.gameObject.SetActive(false);
            await Wait(2f, ct);
            switch (s_job)
            {
                case "data": await DataAsync(p, sb, ct); break;
                case "wind": await WindAsync(p, sb, ct); break;
                case "weight": await WeightAsync(p, sb, ct); break;
                case "variants": await VariantsAsync(p, sb, ct); break;
                case "crucible": await CrucibleAsync(p, sb, ct); break;
                case "budget_great": await BudgetAsync(p, sb, ct, Great); break;
                case "slash": await SlashDirAsync(p, sb, ct); break;
                case "slash_ref": await SlashDirAsync(p, sb, ct, refOnly: true); break;
                case "budget": await BudgetAsync(p, sb, ct); break;
                case "fx": await FxAsync(p, sb, ct); break;
            }
        }
        catch (OperationCanceledException) { sb.AppendLine("취소됨"); }
        catch (Exception e) { sb.AppendLine("예외: " + e); Debug.LogException(e); }
        finally
        {
            File.WriteAllText($"Temp/weapon_form_{s_job}.txt", sb.ToString());
            Debug.Log($"[WeaponFormProbe] 끝 → Temp/weapon_form_{s_job}.txt");
            Application.logMessageReceived -= OnLog;
        }
    }

    // ── 1 데이터 · 진화 · 단련 ──────────────────────────────────
    private static async UniTask DataAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        var wm  = p.WeaponManager;
        var run = GameRunBootstrapper.Instance.Run;
        if (!await EquipAsync(p, Nameless, ct)) { sb.AppendLine("무형검 장착 실패"); return; }
        var w0 = wm.Weapon0Data;
        Check(sb, "무형검 형 없음", !w0.HasForm && w0.formTier == 0);

        w0.enhanceLevel = 5;
        WeaponEvolutionSO.Branch katana = null;
        foreach (var b in w0.evolution.Branches) if (b.branchId == "katana") katana = b;
        Check(sb, "+5에서 카타나 갈래 열림(D6)", katana != null && WeaponEvolutionSO.IsUnlocked(katana, 5));
        bool evolved = await wm.EvolveCurrentWeaponAsync(katana, ct);
        var w1 = wm.Weapon0Data;
        Check(sb, "진화 성공", evolved);
        Check(sb, "진화하면 형이 깨어난다(단계 1 · 칼바람)", w1.HasForm && w1.formTier == 1 && w1.form.DisplayName == "칼바람");

        run.FuelBank.Add(FuelKind.EnhanceMaterial, 40);
        int fuel0 = run.FuelBank.EnhanceMaterial;
        Check(sb, "다음 단련 = Ⅱ · 12", WeaponFormService.CanTemper(w1, out int t2, out int c2) && t2 == 2 && c2 == 12);
        Check(sb, "Ⅲ 변주로 Ⅱ 단련 거절", !WeaponFormService.TryTemper(w1, run.FuelBank, WindbladeForm.StillWind));
        Check(sb, "Ⅱ 쌍풍 단련", WeaponFormService.TryTemper(w1, run.FuelBank, WindbladeForm.TwinWind) && w1.formTier == 2 && w1.HasFormVariant(WindbladeForm.TwinWind));
        Check(sb, "강화재료 −12", run.FuelBank.EnhanceMaterial == fuel0 - 12);
        Check(sb, "Ⅲ 폭풍의 눈 단련", WeaponFormService.TryTemper(w1, run.FuelBank, WindbladeForm.StormEye) && w1.formTier == 3);
        Check(sb, "Ⅲ 뒤 단련 없음", !WeaponFormService.CanTemper(w1, out _, out _));

        var d = new RunSaveData();
        typeof(RunProgressManager).GetMethod("CaptureWeaponEnhance", BindingFlags.Static | BindingFlags.NonPublic)
            ?.Invoke(null, new object[] { d, run });
        Check(sb, "세이브 저장(단계 3 · twin_wind,storm_eye)", d.weapon0FormTier == 3 && d.weapon0FormVariants == "twin_wind,storm_eye");
        d.weapon0FormTier = 9;
        SaveSanitizer.Sanitize(d);
        Check(sb, "Sanitize 9 → 3", d.weapon0FormTier == 3);

        // Review Focus #2 — 원거리를 든 채 진화해도 근접 칸이 진화한다(UI_CruciblePanel.EvolveAsync와 같은 순서)
        if (!await EquipAsync(p, Nameless, ct)) { sb.AppendLine("무형검 재장착 실패"); return; }
        wm.Weapon0Data.enhanceLevel = 5;
        if (wm.Weapon1Data == null)   // 테스트 허브는 2번 칸이 비어 있다 — 비면 「원거리 그대로」가 null == null로 거짓 통과한다(실측 1 2회차)
        {
            var bow = AssetDatabase.LoadAssetAtPath<WeaponSO>("Assets/RelicFairy/Weapon/Bow/Data/T1_Bow.asset");
            if (bow != null) await wm.AcquireWeaponToSlotAsync(WeaponData.FromSO(bow), PlayerWeaponManager.Slot1, setActive: false, playAppear: false);
        }
        var rangedBefore = wm.Weapon1Data;
        await wm.SwitchToSlotAsync(PlayerWeaponManager.Slot1);
        Check(sb, "원거리를 든 상태", wm.CurrentSlotIndex == PlayerWeaponManager.Slot1);
        if (wm.CurrentSlotIndex != PlayerWeaponManager.Slot0) await wm.SwitchToSlotAsync(PlayerWeaponManager.Slot0);
        WeaponEvolutionSO.Branch great = null;
        foreach (var b in wm.Weapon0Data.evolution.Branches) if (b.branchId == "greatsword") great = b;
        bool ok2 = await wm.EvolveCurrentWeaponAsync(great, ct);
        Check(sb, "근접 칸이 대검으로 진화 · 원거리 그대로",
              ok2 && wm.Weapon0Data.form?.DisplayName == "무게" && rangedBefore != null && ReferenceEquals(wm.Weapon1Data, rangedBefore));
    }

    // ── 2 칼바람 ────────────────────────────────────────────────
    private static async UniTask WindAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        if (!await EquipAsync(p, Katana, ct)) { sb.AppendLine("카타나 장착 실패"); return; }
        await Wait(1f, ct);
        var rt = WeaponFormRuntime.Of(p);
        var form = rt?.Current;
        s_home = p.transform.position;
        Check(sb, "구동기 · 칼바람 생성", form is WindbladeForm);
        if (form == null) return;

        Vector3 f = Flat(p.transform.forward), r = Vector3.Cross(Vector3.up, f);
        var near = await TrackAsync(p.transform.position + f * 1.5f, ct);   // 평타가 닿는 적
        var far  = await TrackAsync(p.transform.position + f * 7f, ct);     // 풍절만 닿는 거리
        var side = await TrackAsync(p.transform.position + r * 6f, ct);     // 풍절 줄 밖
        if (near == null || far == null || side == null) { sb.AppendLine("슬라임 부르기 실패"); return; }
        int maxSeen = 0, triggers = 0, damaged = 0;
        form.Triggered += _ => triggers++;
        s_formLog.Clear();
        // 진단(10-06 6회차 풍절 0회): 적중마다 「콤보 단계:칼바람」, 행동 상태가 바뀔 때 <상태>, 피격
        var trace = new StringBuilder();
        string lastAct = null;
        var actField = typeof(PlayerController).GetField("actStateDebug", Inst);
        Action onDmg = () => { damaged++; trace.Append(" [피격]"); };
        Action<HitInfo> onHit = h =>
        {
            if (h.Attacker != p.gameObject || h.ActionType != WeaponActionType.GroundLight) return;
            trace.Append($" {p.Combo.CurrentComboStep}:{form.Stacks}");
        };
        p.OnDamageTaken += onDmg;
        HitFeedbackService.OnHit += onHit;
        bool fullShot = false;
        form.Triggered += FirstTriggerShots("WF_Wind");   // 계획 Task 6 Step 6 화면 — 발동 이펙트 · 첫 발동 3D 글자 · 계정 첫 자막
        float t0 = Time.realtimeSinceStartup;
        try
        {
            while (Time.realtimeSinceStartup - t0 < 14f)
            {
                RefillAll();
                Steer(p, near.M.transform.position);
                p.InputBuffer.Push(Command.Light);
                maxSeen = Mathf.Max(maxSeen, form.Stacks);
                if (!fullShot && form.IsFull) { fullShot = true; await ShotUi("WF_Wind_1_Full", 150); }   // 가득 트레일 · 칼날 빛
                float tNext = Time.realtimeSinceStartup + 0.25f;
                while (Time.realtimeSinceStartup < tNext)   // 프레임마다 <행동 상태 + 콤보 단계>가 바뀌면 남긴다
                {
                    string act = $"{actField?.GetValue(p)}{p.Combo.CurrentComboStep}";
                    if (act != lastAct)
                    {
                        Vector3 toNear = near.M.transform.position - p.transform.position; toNear.y = 0f;
                        trace.Append($" <{act} {Vector3.SignedAngle(p.AimForward, toNear, Vector3.up):0}° {toNear.magnitude:0.0}m>");
                        lastAct = act;
                    }
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
            }
        }
        finally { p.OnDamageTaken -= onDmg; HitFeedbackService.OnHit -= onHit; }
        sb.AppendLine($"   진단 — 적중(단계:칼바람)·<행동>:{trace} · 피격 {damaged}");
        Check(sb, $"칼바람 최대 {maxSeen}/5 도달", maxSeen >= 5);
        Check(sb, $"풍절 발동 {triggers}회", triggers >= 1);
        Check(sb, $"7 m 표적 피해 {far.Dealt:0} (풍절만 닿는다)", far.Dealt > 0);
        Check(sb, $"줄 밖 표적 피해 {side.Dealt:0} = 0", side.Dealt == 0);

        // 3초 못 맞히면 1초에 1씩 준다 — 피격 시험보다 먼저(밀려나거나 표적이 쓰러지면 다시 쌓지 못한다, 실측 2 3~5회차)
        near = await EnsureAliveAsync(near, ct);
        float guard = Time.realtimeSinceStartup;
        while (form.Stacks < 3 && Time.realtimeSinceStartup - guard < 8f) { RefillAll(); Steer(p, near.M.transform.position); p.InputBuffer.Push(Command.Light); await Wait(0.25f, ct); }
        // 남은 입력으로 휘두름이 이어지면 쉬는 시간이 아니다(실측 2 7회차 3 → 3) — 입력을 비우고 행동이 멈춘 뒤 잰다
        p.InputBuffer.Clear();
        var actF = typeof(PlayerController).GetField("actStateDebug", Inst);
        float settle = Time.realtimeSinceStartup;
        while (actF?.GetValue(p)?.ToString() != "None" && Time.realtimeSinceStartup - settle < 3f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
        int before = form.Stacks, idleHits = 0;
        float gameT0 = Time.time;
        Action<HitInfo> onIdleHit = h => { if (h.Attacker == p.gameObject) idleHits++; };
        HitFeedbackService.OnHit += onIdleHit;
        try { await Wait(5.2f, ct); }
        finally { HitFeedbackService.OnHit -= onIdleHit; }
        sb.AppendLine($"   진단 — 쉬는 동안: 게임 시간 {Time.time - gameT0:0.0}초 · 배속 {Time.timeScale:0.00} · 적중 {idleHits} · 행동 {typeof(PlayerController).GetField("actStateDebug", Inst)?.GetValue(p)}");
        Check(sb, $"쉬면 줄어든다 {before} → {form.Stacks}", before >= 3 && idleHits == 0 && form.Stacks <= before - 2);

        // 피격하면 0 — 무적을 잠깐 끄고 피해
        near = await EnsureAliveAsync(near, ct);
        guard = Time.realtimeSinceStartup;
        while (form.Stacks < 2 && Time.realtimeSinceStartup - guard < 8f) { RefillAll(); Steer(p, near.M.transform.position); p.InputBuffer.Push(Command.Light); await Wait(0.25f, ct); }
        int beforeHit = form.Stacks;
        typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, false);
        p.TakeDamage(30);   // 방어로 0이 되면 OnDamageTaken이 안 온다 — 넉넉히
        typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);
        await Wait(0.1f, ct);
        Check(sb, $"피격 → 칼바람 {beforeHit} → 0", beforeHit >= 2 && form.Stacks == 0);
        DetachAll();
    }

    // ── 3 무게 ──────────────────────────────────────────────────
    private static async UniTask WeightAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        if (!await EquipAsync(p, Great, ct)) { sb.AppendLine("대검 장착 실패"); return; }
        await Wait(1f, ct);
        var form = WeaponFormRuntime.Of(p)?.Current;
        s_home = p.transform.position;
        Check(sb, "구동기 · 무게 생성", form is WeightForm);
        if (form == null) return;

        // 확정 치명은 「가득으로 마지막 타 단계에 들어설 때」만 건다(기획 §4-3) — 마지막 타가 가득을 채운 순간 걸면
        // 같은 휘두름의 다음 타 · 다른 장치가 건 확정 치명과 엉킨다. 콤보 단계를 직접 움직이는 단위 시험(플레이어는 서 있다).
        {
            int last = Mathf.Max(1, p.WeaponManager.Weapon0Data.groundEndCount) - 1;
            var fake = new HitInfo(p.gameObject, p.gameObject, p.transform.position, p.transform.forward, 1f, false, WeaponActionType.GroundLight);
            p.InputBuffer.Clear();
            p.Combo.Reset();
            p.RuntimeStats.ConsumeForceCrit(out _);
            for (int i = 0; i < form.MaxStacks - 1; i++) form.OnMeleeHit(fake, true, false);
            for (int i = 0; i < last; i++) p.Combo.IncrementStep();
            await UniTask.DelayFrame(2, cancellationToken: ct);
            bool armedNotFull = p.RuntimeStats.ForceCritArmed;
            form.OnMeleeHit(fake, true, true);                       // 마지막 타가 가득을 채운다
            await UniTask.DelayFrame(2, cancellationToken: ct);
            bool armedOnFill = p.RuntimeStats.ForceCritArmed;
            bool fullAfterFill = form.IsFull;
            p.Combo.ResetStep();
            await UniTask.DelayFrame(2, cancellationToken: ct);
            for (int i = 0; i < last; i++) p.Combo.IncrementStep();  // 가득으로 마지막 타 단계에 들어선다
            await UniTask.DelayFrame(2, cancellationToken: ct);
            bool armedOnEnter = p.RuntimeStats.ForceCritArmed;
            p.Combo.ResetStep();                                      // 마지막 타 없이 끊긴다
            await UniTask.DelayFrame(2, cancellationToken: ct);
            bool armedAfterBreak = p.RuntimeStats.ForceCritArmed;
            Check(sb, $"가득 전 마지막 단계 → 안 건다 (걸림 {armedNotFull})", !armedNotFull);
            Check(sb, $"마지막 타가 가득을 채우면 → 안 건다 (걸림 {armedOnFill} · 가득 {fullAfterFill})", !armedOnFill && fullAfterFill);
            Check(sb, $"가득으로 마지막 단계에 들어서면 → 건다 (걸림 {armedOnEnter})", armedOnEnter);
            Check(sb, $"마지막 타 없이 끊기면 → 푼다 (걸림 {armedAfterBreak})", !armedAfterBreak);
            p.Combo.Reset();
            p.RuntimeStats.ConsumeForceCrit(out _);
            typeof(WeaponFormBase).GetMethod("SetStacks", Inst)?.Invoke(form, new object[] { 0 });
        }

        Vector3 f = Flat(p.transform.forward), r = Vector3.Cross(Vector3.up, f);
        var near  = await TrackAsync(p.transform.position + f * 1.5f, ct);
        var ring  = await TrackAsync(p.transform.position + f * 1.5f + r * 3f, ct);   // 충격파 반경(3.5 m) 안 · 평타 호 밖
        var outer = await TrackAsync(p.transform.position + f * 1.5f + r * 6f, ct);   // 반경 밖
        if (near == null || ring == null || outer == null) { sb.AppendLine("슬라임 부르기 실패"); return; }
        int maxSeen = 0, slams = 0, finisherCrits = 0, finisherHits = 0;
        bool stunSeen = false;
        form.Triggered += _ => slams++;
        int slamsSeen = 0;
        Action<HitInfo> onHit = h =>
        {
            // 내려찍기를 낸 타 — 구동기가 먼저 구독해 이 타에서 이미 내려찍고 무게를 비웠다(실측 3 1회차: 「가득일 때」로 걸러 0/0)
            if (h.Attacker != p.gameObject || h.ActionType != WeaponActionType.GroundLight || slams == slamsSeen) return;
            slamsSeen = slams;
            finisherHits++;
            if (h.IsCritical) finisherCrits++;
        };
        HitFeedbackService.OnHit += onHit;
        bool fullShot = false;
        form.Triggered += FirstTriggerShots("WF_Weight");
        try
        {
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 16f)
            {
                RefillAll();
                Steer(p, near.M.transform.position);
                p.InputBuffer.Push(Command.Light);
                maxSeen  = Mathf.Max(maxSeen, form.Stacks);
                if (!fullShot && form.IsFull) { fullShot = true; await ShotUi("WF_Weight_1_Full", 150); }
                stunSeen |= ring.M != null && ring.M.Status.HasCc("stun");
                await Wait(0.25f, ct);
            }
        }
        finally { HitFeedbackService.OnHit -= onHit; }
        Check(sb, $"무게 최대 {maxSeen}/4 도달", maxSeen >= 4);
        Check(sb, $"내려찍기 {slams}회", slams >= 1);
        Check(sb, $"가득일 때 마지막 타 치명 {finisherCrits}/{finisherHits}", finisherHits > 0 && finisherCrits == finisherHits);
        Check(sb, $"반경 안 표적 피해 {ring.Dealt:0} · 경직 {stunSeen}", ring.Dealt > 0 && stunSeen);
        Check(sb, $"반경 밖 표적 피해 {outer.Dealt:0} = 0", outer.Dealt == 0);

        // 마지막 타를 헛치면 확정 치명이 풀린다 — 가득으로 마지막 타 단계에 들어서는 순간 표적을 치운다
        near = await EnsureAliveAsync(near, ct);
        int lastStep = Mathf.Max(1, p.WeaponManager.Weapon0Data.groundEndCount) - 1;
        float guard = Time.realtimeSinceStartup;
        bool reached = false;
        while (!reached && Time.realtimeSinceStartup - guard < 12f)
        {
            RefillAll(); Steer(p, near.M.transform.position); p.InputBuffer.Push(Command.Light);
            float tNext = Time.realtimeSinceStartup + 0.25f;
            while (Time.realtimeSinceStartup < tNext && !(reached = form.IsFull && p.Combo.CurrentComboStep >= lastStep))
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        p.InputBuffer.Clear();
        foreach (var t in s_tracks) if (t.M != null) t.M.gameObject.SetActive(false);
        await UniTask.DelayFrame(2, cancellationToken: ct);
        bool armedAtStop = p.RuntimeStats.ForceCritArmed;
        var actF = typeof(PlayerController).GetField("actStateDebug", Inst);
        float settle = Time.realtimeSinceStartup;
        while (actF?.GetValue(p)?.ToString() != "None" && Time.realtimeSinceStartup - settle < 3f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
        await Wait(0.3f, ct);
        bool leaked = p.RuntimeStats.ForceCritArmed;
        Check(sb, $"마지막 타를 헛치면 확정 치명이 풀린다 — 도달 {reached} · 걸림 {armedAtStop} → 남음 {leaked} (무게 {form.Stacks})", reached && armedAtStop && !leaked);
        DetachAll();
    }

    // ── 4 변주 ──────────────────────────────────────────────────
    private static async UniTask VariantsAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        await VariantCaseAsync(p, sb, Katana, "twin_wind,storm_eye", new[] { "쌍풍", "폭풍의 눈" }, dodge: false, skill: false, ct);
        await VariantCaseAsync(p, sb, Katana, "return_wind,still_wind", new[] { "되감는 바람", "무풍" }, dodge: true, skill: false, ct);
        await VariantCaseAsync(p, sb, Great, "aftershock,heavy_hand", new[] { "여진", "무거운 손" }, dodge: false, skill: true, ct);
        await VariantCaseAsync(p, sb, Great, "fissure,crush", new[] { "균열", "짓누름" }, dodge: false, skill: false, ct);
    }

    private static async UniTask VariantCaseAsync(PlayerController p, StringBuilder sb, string weaponPath, string variants,
                                                  string[] expectLogs, bool dodge, bool skill, CancellationToken ct)
    {
        if (!await EquipAsync(p, weaponPath, ct)) { sb.AppendLine($"장착 실패 {weaponPath}"); return; }
        var w = p.WeaponManager.Weapon0Data;
        w.formTier = 3;
        w.formVariants = variants;
        var rt = WeaponFormRuntime.Of(p);
        rt.Rebuild();
        DetachAll();
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        await Wait(0.5f, ct);
        s_home = p.transform.position;
        Vector3 f = Flat(p.transform.forward), r = Vector3.Cross(Vector3.up, f);
        var near = await TrackAsync(p.transform.position + f * 1.5f, ct);
        await TrackAsync(p.transform.position + Quaternion.AngleAxis(12f, Vector3.up) * f * 6f, ct);    // 쌍풍 오른쪽 갈래
        await TrackAsync(p.transform.position + Quaternion.AngleAxis(-12f, Vector3.up) * f * 6f, ct);   // 쌍풍 왼쪽 갈래
        await TrackAsync(p.transform.position + f * 1.5f + r * 4.5f, ct);                               // 여진만 닿는 거리(3.5 < 4.5 < 4.9)
        if (near == null) { sb.AppendLine("슬라임 부르기 실패"); return; }
        s_formLog.Clear();
        // 쌍풍은 「넓게」(기획 §4-2) — 두 갈래 어디에 들었든 적마다 한 번. 발동 순간 두 갈래 안의 서로 다른 적 수와 맞힘 수를 맞댄다
        int twinChecked = 0, twinOff = 0;
        string twinLast = null;
        var trigForm = rt.Current;
        Action<string> onTrig = _ =>
        {
            string line = s_formLog.FindLast(l => l.Contains("쌍풍 — 맞힘"));
            if (line == null || !int.TryParse(line.Substring(line.LastIndexOf(' ') + 1), out int n)) return;
            int distinct = CountInTwin(p, trigForm.Form.TriggerRange);
            twinChecked++;
            if (n != distinct) { twinOff++; twinLast = $"{n} ≠ {distinct}"; }
        };
        bool twin = variants.Contains("twin_wind") && trigForm != null;
        if (twin) trigForm.Triggered += onTrig;
        // 진단(실측 4 3~4회차 「맞힘 0」): 발동 순간 [집에서 거리 · 앞 표적 거리(꺼짐) · 겨눈 쪽과 앞 표적 각 · 9.4 m 안 적 수]
        var trigTrace = new StringBuilder();
        Action<string> onTrigDiag = name =>
        {
            var nm = near?.M;
            Vector3 to = nm != null ? nm.transform.position - p.transform.position : Vector3.zero; to.y = 0f;
            int around = CombatQuery.GetNearbyEnemies(p.transform.position, 9.4f, p.gameObject, 32, s_twinBuf);
            trigTrace.Append($" [{name} 집 {Vector3.Distance(p.transform.position, s_home):0.0} · 앞 {to.magnitude:0.0}{(nm != null && nm.gameObject.activeInHierarchy ? "" : " 꺼짐")} · {Vector3.SignedAngle(p.AimForward, to, Vector3.up):0}° · 적 {around}]");
        };
        if (trigForm != null) trigForm.Triggered += onTrigDiag;
        bool usedThisFull = false;   // 가득 한 번에 회피 · E 한 번 — 나머지는 평타로 마지막 타(발동)까지 간다
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 18f)
        {
            RefillAll();
            HideStrays();   // 전투방 파도 몬스터 — 조준 보정이 끌려가고, 발동에 죽어 셈이 어긋난다(실측 4 3회차 「맞힘 0」 · 6 ≠ 3)
            near = await EnsureAliveAsync(near, ct);
            Steer(p, near.M.transform.position);
            var form = rt.Current;
            bool full = form != null && form.IsFull;
            if (!full) usedThisFull = false;
            if (full && !usedThisFull && dodge)      { p.InputBuffer.Push(Command.Dodge);  usedThisFull = true; }
            else if (full && !usedThisFull && skill) { p.InputBuffer.Push(Command.ESkill); usedThisFull = true; }
            else p.InputBuffer.Push(Command.Light);
            await Wait(0.25f, ct);
        }
        foreach (var name in expectLogs)
        {
            int n = s_formLog.FindAll(l => l.Contains(name)).Count;
            Check(sb, $"{variants} — 「{name}」 로그 {n}회", n > 0);
        }
        if (trigForm != null) trigForm.Triggered -= onTrigDiag;
        sb.AppendLine($"   진단 — 발동:{trigTrace}");
        if (twin)
        {
            trigForm.Triggered -= onTrig;
            Check(sb, $"{variants} — 쌍풍은 적마다 한 번 (발동 {twinChecked}회 중 어긋남 {twinOff}{(twinLast != null ? " · 마지막 " + twinLast : "")})", twinChecked > 0 && twinOff == 0);
        }
        DetachAll();
    }

    private static readonly List<MonsterBase> s_twinBuf = new(32);

    /// <summary>표적 슬라임이 아닌 살아 있는 몬스터를 끈다.</summary>
    private static void HideStrays()
    {
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead && !s_tracks.Exists(t => t.M == m)) m.gameObject.SetActive(false);
    }

    /// <summary>지금 플레이어 자리 · 겨눈 쪽에서 ±12° 두 갈래(폭 0.9 + 0.4 m — FormStrike.Line과 같은 판정) 안의 서로 다른 적 수.</summary>
    private static int CountInTwin(PlayerController p, float length)
    {
        Vector3 from = p.transform.position, dir = Flat(p.AimForward);
        Vector3 toL = from + Quaternion.AngleAxis(-12f, Vector3.up) * dir * length;
        Vector3 toR = from + Quaternion.AngleAxis( 12f, Vector3.up) * dir * length;
        int n = CombatQuery.GetNearbyEnemies(from, length + 1.8f, p.gameObject, 32, s_twinBuf), c = 0;
        for (int i = 0; i < n; i++)
        {
            var e = s_twinBuf[i];
            if (e == null || e.IsDead) continue;
            Vector3 at = e.transform.position;
            if (FormStrike.DistToSegmentXZ(at, from, toL) <= 1.3f || FormStrike.DistToSegmentXZ(at, from, toR) <= 1.3f) c++;
        }
        return c;
    }

    // ── 5 예산 ──────────────────────────────────────────────────
    private static async UniTask BudgetAsync(PlayerController p, StringBuilder sb, CancellationToken ct, string onlyPath = null)
    {
        var cases = new (string path, int tier, string variants, string label)[]
        {
            (Katana, 0, "",                    "카타나 +5 형 끔"),
            (Katana, 1, "",                    "카타나 +5 형 Ⅰ"),
            (Katana, 3, "twin_wind,storm_eye", "카타나 +5 형 Ⅲ(쌍풍 · 폭풍의 눈)"),
            (Great,  0, "",                    "대검 +5 형 끔"),
            (Great,  1, "",                    "대검 +5 형 Ⅰ"),
            (Great,  3, "aftershock,crush",    "대검 +5 형 Ⅲ(여진 · 짓누름)"),
        };
        // 기획 §6: 단일 표적 DPS(범위 · 경직 · 원거리 같은 기능 값은 DPS에 넣지 않는다). 1회차는 표적 3마리 합계라 풍절 · 내려찍기 범위가 부풀었다.
        var loadoutRelic = AppBootstrapper.Instance != null ? AppBootstrapper.Instance.Loadout?.Relic : null;   // p.RelicClass는 프리팹 기본값(3회차 「가웨인」 오표기)
        sb.AppendLine($"유물 {(loadoutRelic != null ? loadoutRelic.name : "없음")} · 단일 표적 · 칸당 정착 3초 + 측정 45초");
        float baseDps = 0f, baseSwings = 0f;
        foreach (var c in cases)
        {
            if (onlyPath != null && c.path != onlyPath) continue;   // 바뀐 칸만 다시 잰다
            if (!await EquipAsync(p, c.path, ct)) { sb.AppendLine($"장착 실패 {c.label}"); continue; }
            var w = p.WeaponManager.Weapon0Data;
            w.enhanceLevel = 5;
            w.RecomputeEnhancedStats();
            w.formTier = c.tier;
            w.formVariants = c.variants;
            WeaponFormRuntime.Of(p).Rebuild();
            p.WeaponManager.RaiseEquippedWeaponRefreshed();   // 공격력 다시 적용
            DetachAll();
            foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
                if (m != null && !m.IsDead) m.gameObject.SetActive(false);
            await Wait(0.5f, ct);
            s_home = p.transform.position;
            Vector3 f = Flat(p.transform.forward);
            var t0Track = await TrackAsync(p.transform.position + f * 1.6f, ct);
            if (t0Track == null) { sb.AppendLine($"슬라임 부르기 실패 {c.label}"); continue; }
            float settle = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - settle < 3f) { RefillAll(); Steer(p, t0Track.M.transform.position); p.InputBuffer.Push(Command.Light); await Wait(0.25f, ct); }
            foreach (var t in s_tracks) t.Dealt = 0f;   // 정착 동안 쌓인 것은 버린다
            // 진단(2회차 카타나 Ⅰ −33%): 평타 적중 수 · 평타 피해 · 치명 · 형 발동 수 — DPS 차이가 어디서 오는지 가른다
            int swings = 0, crits = 0, triggers = 0;
            float basic = 0f;
            float lastHitAt = -1f;
            Action<HitInfo> onHit = h =>
            {
                if (h.Attacker != p.gameObject || h.ActionType != WeaponActionType.GroundLight || t0Track.M == null || h.Target != t0Track.M.gameObject) return;
                basic += h.Damage;
                if (h.IsCritical) crits++;
                if (Time.time - lastHitAt > 0.12f) swings++;
                lastHitAt = Time.time;
            };
            Action<string> onTrig = _ => triggers++;
            var formNow = WeaponFormRuntime.Of(p).Current;
            HitFeedbackService.OnHit += onHit;
            if (formNow != null) formNow.Triggered += onTrig;
            float t0 = Time.realtimeSinceStartup;
            try
            {
                while (Time.realtimeSinceStartup - t0 < 45f)
                {
                    RefillAll();
                    Steer(p, t0Track.M.transform.position);
                    p.InputBuffer.Push(Command.Light);
                    await Wait(0.25f, ct);
                }
            }
            finally
            {
                HitFeedbackService.OnHit -= onHit;
                if (formNow != null) formNow.Triggered -= onTrig;
            }
            float dealt = 0f;
            foreach (var t in s_tracks) dealt += t.Dealt;
            float dps = dealt / 45f;
            if (c.tier == 0) { baseDps = dps; baseSwings = swings; }
            string delta = c.tier == 0 || baseDps <= 0f ? "" : $" ({(dps / baseDps - 1f) * 100f:+0;-0}%";
            // 평타 횟수 보정(실측 5b 3회차: 형 끔 55회 · 형 Ⅰ 62회 — 휘두른 횟수 차이가 형 몫보다 컸다)
            if (delta.Length > 0) delta += swings > 0 && baseSwings > 0 ? $" · 평타 수 보정 {(dps / swings) / (baseDps / baseSwings) * 100f - 100f:+0.0;-0.0}%)" : ")";
            sb.AppendLine($"{c.label}: DPS {dps:0.0}{delta} · 평타 {swings}회 {basic / 45f:0.0}/초 · 치명 {crits} · 형 발동 {triggers} · 그 밖 {(dealt - basic) / 45f:0.0}/초");
        }
        DetachAll();
    }

    // ── 6 연출 후보 비교 ────────────────────────────────────────
    // (경로, 크기, 내려찍기 여부)
    // 1회차: 입장 부감 카메라 중 촬영. 2회차: 풍절 = Slash wave(정면 초승달 검기)가 맞다 · 특수기술 팩 내려찍기 3종은 금빛 폭발+화면 어둡힘 / 붉은 가시 / 불 폭발이라
    // 「무게」의 묵직한 충격파와 다름 → 3회차: 바닥 충격파 계열(Hovl 노바 · 운석 착탄 · 대검 피격) + Slash wave 크게
    // 4회차: 실측 3 화면에서 내려찍기 0.25초에 화면 반을 덮는 베이지 막 — 노바 ×3 단독으로 크기별 확인
    private static readonly (string path, float scale, bool slam)[] FxCandidates =
    {
        ("Assets/RelicFairy/_Imported/EffectSource/Hovl Studio/AAA Projectiles Vol 1/Prefabs/Hit 18 nova orange.prefab", 3f, true),
        ("Assets/RelicFairy/_Imported/EffectSource/Hovl Studio/AAA Projectiles Vol 1/Prefabs/Hit 18 nova orange.prefab", 2f, true),
        ("Assets/RelicFairy/_Imported/EffectSource/Hovl Studio/AAA Projectiles Vol 1/Prefabs/Hit 18 nova orange.prefab", 1.5f, true),
    };

    private static async UniTask FxAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        await Wait(4f, ct);   // 첫 전투방 입장 부감 카메라가 끝날 때까지(1회차는 부감 중에 찍혔다)
        var cam = Camera.main;
        Vector3 fwd = Flat(p.transform.forward);
        for (int i = 0; i < FxCandidates.Length; i++)
        {
            var (path, scale, slam) = FxCandidates[i];
            HideStrays();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { sb.AppendLine($"#{i} 로드 실패 {path}"); continue; }
            // 게임과 같은 자리 — 풍절: 플레이어 가슴 높이에서 정면으로 · 내려찍기: 정면 1.5 m 바닥
            Vector3 at = slam ? p.transform.position + fwd * 1.5f : p.transform.position + Vector3.up;
            var go = UnityEngine.Object.Instantiate(prefab, at, Quaternion.LookRotation(fwd));
            go.transform.localScale *= scale;
            float[] times = { 0.06f, 0.15f, 0.3f };
            float prev = 0f;
            for (int k = 0; k < times.Length; k++)
            {
                await Wait(times[k] - prev, ct);
                prev = times[k];
                CaptureMain(cam, $"Temp/weapon_form_fx_{i}_{(char)('a' + k)}.png");
            }
            UnityEngine.Object.Destroy(go);
            sb.AppendLine($"#{i} {Path.GetFileNameWithoutExtension(path)} ×{scale}");
            await Wait(1f, ct);
        }
    }

    /// <summary>게임 카메라 화면 그대로 한 장(UI 오버레이 제외) — LancelotQSwordProbeEditor.CaptureMain과 같다.</summary>
    private static void CaptureMain(Camera cam, string path)
    {
        if (cam == null) return;
        int w = 960, h = 540;
        var rt = new RenderTexture(w, h, 24);
        var prevTarget = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = prevTarget;
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.Destroy(rt);
        UnityEngine.Object.Destroy(tex);
    }

    // ── 8 평타 이펙트 방향 ──────────────────────────────────────
    // 사용자(10-07): 「검의 방향과 이펙트의 방향을 맞춰야 해」 — 평타 휘두름 이펙트. 슬래시는 몸 방향 × 단계별 고정 회전(WeaponAbilitySO
    // rotationEuler)으로 나오고 칼 뼈대를 보지 않는다. 이펙트가 나오는 순간(SpawnSlashEffectN) 칼끝 이동 방향을 등 뒤 시점 각도로 잰다
    // (오른쪽 = 0°, 위 = 90°, 왼쪽 = 180°, 아래 = −90°). 등 뒤 카메라로 +1프레임 · +0.08초를 찍는다. 기준: 슬래시 프리팹 회전 0° · 90°.
    private static readonly string[] SlashWeapons = { Nameless, Katana, Great };

    private static async UniTask SlashDirAsync(PlayerController p, StringBuilder sb, CancellationToken ct, bool refOnly = false)
    {
        Directory.CreateDirectory("Temp/ui_shots");
        var camGo = new GameObject("~SlashProbeCam");
        var cam = camGo.AddComponent<Camera>();
        if (Camera.main != null) cam.CopyFrom(Camera.main);
        cam.enabled = false;
        cam.fieldOfView = 50f;
        var recv = p.GetComponentInChildren<PlayerAnimationEventReceiver>();
        try
        {
            // 기준 촬영 — 슬래시 프리팹을 몸 앞에 회전 0° · 90°(Z)로 띄워 어느 쪽에서 어느 쪽으로 쓰는지 본다
            HideStrays();
            Vector3 fwd0 = Flat(p.AimForward);
            var slashPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RelicFairy/Prefabs/UseEffect/PlayerEffect/SlashAttack.prefab");
            foreach (float roll in new[] { 0f, 90f })
            {
                if (slashPrefab == null) break;
                var rot = Quaternion.LookRotation(fwd0) * Quaternion.Euler(0f, 0f, roll);
                var fx = UnityEngine.Object.Instantiate(slashPrefab, p.transform.position + Vector3.up + fwd0 * 0.5f, rot);
                float[] at = refOnly ? new[] { 0.005f, 0.02f, 0.04f, 0.07f } : new[] { 0.03f, 0.1f, 0.2f };   // 8b: 초승달이 어느 끝에서 자라는지
                float prev = 0f;
                for (int k = 0; k < at.Length; k++)
                {
                    await Wait(at[k] - prev, ct); prev = at[k];
                    PlaceBehind(cam, p, fwd0);
                    CaptureMain(cam, $"Temp/ui_shots/WF_Slash_ref_roll{roll:0}_{(char)('a' + k)}.png");
                }
                UnityEngine.Object.Destroy(fx);
                await Wait(0.5f, ct);
            }

            foreach (var path in SlashWeapons)
            {
                if (refOnly) break;
                if (!await EquipAsync(p, path, ct)) { sb.AppendLine($"장착 실패 {path}"); continue; }
                string wname = Path.GetFileNameWithoutExtension(path);
                var wi = p.WeaponManager.CurrentWeaponInstance != null ? p.WeaponManager.CurrentWeaponInstance.GetComponentInChildren<WeaponInstance>() : null;
                var tip = wi != null ? wi.tipPoint : null;
                if (tip == null) { sb.AppendLine($"{wname}: 칼끝(tipPoint) 없음"); continue; }
                HideStrays();
                await Wait(0.6f, ct);
                s_home = p.transform.position;
                Vector3 fwd = Flat(p.AimForward);
                Vector3 aim = p.transform.position + fwd * 4f;

                var samples = new List<(float t, Vector3 local)>(512);
                var normT   = new List<float>(512);   // 같은 순간의 공격 클립 진행(0~1) — 칼끝이 가장 빠른 순간을 클립 진행으로 옮겨 이벤트 자리를 정한다
                var events  = new List<(float t, int evt, int combo, float nt)>(8);
                Action<int> onEvt = n =>
                {
                    events.Add((Time.time, n, p.Combo.CurrentComboStep, p.Anim != null ? p.Anim.GetCurrentAnimatorStateInfo(0).normalizedTime : -1f));
                    int combo = p.Combo.CurrentComboStep;
                    UniTask.Void(async () =>
                    {
                        await UniTask.Yield(PlayerLoopTiming.PostLateUpdate);
                        PlaceBehind(cam, p, fwd);
                        CaptureMain(cam, $"Temp/ui_shots/WF_Slash_{wname}_c{combo}_e{n}_a.png");
                        await UniTask.Delay(80, DelayType.DeltaTime);
                        PlaceBehind(cam, p, fwd);
                        CaptureMain(cam, $"Temp/ui_shots/WF_Slash_{wname}_c{combo}_e{n}_b.png");
                    });
                };
                if (recv != null) recv.OnEffectStep += onEvt;
                try
                {
                    float t0 = Time.realtimeSinceStartup, nextPush = 0f;
                    while (Time.realtimeSinceStartup - t0 < 3.2f)
                    {
                        if (Time.realtimeSinceStartup - t0 < 2.2f && Time.realtimeSinceStartup >= nextPush)
                        {
                            nextPush = Time.realtimeSinceStartup + 0.15f;
                            AimAt(aim); RequestFace(p, aim);
                            p.InputBuffer.Push(Command.Light);
                        }
                        // 몸 기준 좌표 — 슬래시가 몸 transform 기준으로 나온다. 1회차는 명령 정면 기준이라 몸이 도는 것 · 전진이 칼 휘두름에 섞였다
                        samples.Add((Time.time, p.transform.InverseTransformPoint(tip.position)));
                        normT.Add(p.Anim != null ? p.Anim.GetCurrentAnimatorStateInfo(0).normalizedTime : -1f);
                        await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    }
                }
                finally { if (recv != null) recv.OnEffectStep -= onEvt; }

                var abil = p.WeaponManager.Weapon0Data;
                foreach (var e in events)
                {
                    Vector3 a = TipAt(samples, e.t - 0.03f), b = TipAt(samples, e.t + 0.03f), at = TipAt(samples, e.t);
                    Vector3 v = b - a;
                    float ang = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
                    // 칼끝이 가장 빠른 순간 — 이벤트가 든 같은 클립 안에서(클립 진행이 줄면 다른 클립) 앞뒤 한 칸 중심 차분
                    int peak = -1; float peakSpeed = 0f;
                    for (int i = 1; i < samples.Count - 1; i++)
                    {
                        if (Mathf.Abs(samples[i].t - e.t) > 0.45f) continue;
                        if (normT[i + 1] < normT[i] || normT[i] < normT[i - 1]) continue;   // 클립이 바뀌는 자리
                        float dt = samples[i + 1].t - samples[i - 1].t;
                        if (dt <= 1e-4f) continue;
                        float sp = (samples[i + 1].local - samples[i - 1].local).magnitude / dt;
                        if (sp > peakSpeed) { peakSpeed = sp; peak = i; }
                    }
                    string peakTxt = "없음";
                    if (peak > 0)
                    {
                        Vector3 pv = samples[peak + 1].local - samples[peak - 1].local;
                        peakTxt = $"{samples[peak].t - e.t:+0.00;-0.00}초 · 클립 {normT[peak]:0.00} · {peakSpeed:0.0} m/s · 그때 방향 {Mathf.Atan2(pv.y, pv.x) * Mathf.Rad2Deg:0}°";
                    }
                    sb.AppendLine($"{wname} 콤보 {e.combo} · 이벤트 {e.evt}(클립 {e.nt:0.00}): 칼끝 이동 {ang:0}° (가로 {v.x:+0.00;-0.00} · 세로 {v.y:+0.00;-0.00} · 앞뒤 {v.z:+0.00;-0.00} m/0.06초) · 칼끝 자리 (오른쪽 {at.x:0.00} · 높이 {at.y:0.00} · 앞 {at.z:0.00}) · 가장 빠른 순간 {peakTxt}");
                }
                if (events.Count == 0) sb.AppendLine($"{wname}: 슬래시 이벤트 0건");
                await Wait(1f, ct);
            }
        }
        finally { UnityEngine.Object.Destroy(camGo); }
    }

    private static void PlaceBehind(Camera cam, PlayerController p, Vector3 fwd)
    {
        Vector3 pos = p.transform.position;
        cam.transform.position = pos - fwd * 3.4f + Vector3.up * 2.1f;
        cam.transform.LookAt(pos + fwd * 1.2f + Vector3.up * 1.0f);
    }

    private static Vector3 TipAt(List<(float t, Vector3 local)> s, float t)
    {
        if (s.Count == 0) return Vector3.zero;
        if (t <= s[0].t) return s[0].local;
        for (int i = 1; i < s.Count; i++)
        {
            if (s[i].t < t) continue;
            float span = s[i].t - s[i - 1].t;
            return Vector3.Lerp(s[i - 1].local, s[i].local, span > 1e-5f ? (t - s[i - 1].t) / span : 1f);
        }
        return s[s.Count - 1].local;
    }

    // ── 7 재련소 화면 ───────────────────────────────────────────
    // 계획 Task 5 Step 5 — 활을 든 채 「◆ 진화」 → 근접 칸만 진화(Review Focus #2 UI 경로) · 「◆ 형 단련 Ⅱ」 버튼 · 단련 창 카드 두 장 ·
    // 고르면 결과 줄과 강화재료 −12. 화면 4장 Temp/ui_shots/WF_Crucible_*.png. 임시 재련소 컨트롤러(CrucibleChooserProbeEditor와 같은 길).
    private static async UniTask CrucibleAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        var wm  = p.WeaponManager;
        var run = GameRunBootstrapper.Instance.Run;
        if (!await EquipAsync(p, Nameless, ct)) { sb.AppendLine("무형검 장착 실패"); return; }
        wm.Weapon0Data.enhanceLevel = 5;
        wm.Weapon0Data.RecomputeEnhancedStats();
        if (wm.Weapon1Data == null)
        {
            var bow = AssetDatabase.LoadAssetAtPath<WeaponSO>("Assets/RelicFairy/Weapon/Bow/Data/T1_Bow.asset");
            if (bow != null) await wm.AcquireWeaponToSlotAsync(WeaponData.FromSO(bow), PlayerWeaponManager.Slot1, setActive: false, playAppear: false);
        }
        var bowBefore = wm.Weapon1Data;
        await wm.SwitchToSlotAsync(PlayerWeaponManager.Slot1);
        Check(sb, "활을 든 상태", bowBefore != null && wm.CurrentSlotIndex == PlayerWeaponManager.Slot1);
        run.FuelBank.Add(FuelKind.EnhanceMaterial, 40);

        var go = new GameObject("~WeaponFormCrucibleProbe");
        HubGui("HideTestHubGui");
        try
        {
            var ctrl = go.AddComponent<CrucibleRoomController>();
            ctrl.Initialize(run, await WeaponEnhanceService.EnsureLoadedAsync(), new System.Random(7), null);
            var panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_CruciblePanel>();
            panel.Bind(ctrl);
            await Wait(0.8f, ct);
            PanelField<Button[]>(panel, "_chooseCard")?[0]?.onClick.Invoke();   // 첫 화면 → 근거리
            PanelCall(panel, "RefreshAll");
            await Wait(0.3f, ct);
            var btn = PanelField<Button>(panel, "_evolveBtn");
            var lbl = PanelField<TMP_Text>(panel, "_evolveLabel");
            string l1 = lbl != null ? lbl.GetParsedText() : null;
            Check(sb, $"+5 진화 버튼 「{l1}」", btn != null && btn.gameObject.activeInHierarchy && btn.interactable && l1 != null && l1.Contains("진화 — 형을 고른다"));
            await ShotUi("WF_Crucible_1_Evolve", 300);

            // 활을 든 채 UI로 진화 — 카타나 갈래 카드를 고르고 확정
            PanelCall(panel, "ShowEvolvePanel");
            await Wait(0.5f, ct);
            var branches = typeof(UI_CruciblePanel).GetProperty("CurrentBranches", Inst)?.GetValue(panel) as IReadOnlyList<WeaponEvolutionSO.Branch>;
            int ki = -1;
            for (int i = 0; branches != null && i < branches.Count; i++) if (branches[i].branchId == "katana") ki = i;
            PanelCall(panel, "PickEvolveCard", ki);
            PanelField<Button>(panel, "_evolveConfirm")?.onClick.Invoke();
            float t0 = Time.realtimeSinceStartup;
            while (wm.Weapon0Data?.form == null && Time.realtimeSinceStartup - t0 < 10f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
            await Wait(3f, ct);   // 망치 · 공개 · 도장 연출이 끝날 때까지
            Check(sb, $"활을 든 채 진화 → 근접 칸 {wm.Weapon0Data?.displayName}(형 {wm.Weapon0Data?.form?.DisplayName}) · 활 그대로 {ReferenceEquals(wm.Weapon1Data, bowBefore)}",
                  wm.Weapon0Data?.form?.DisplayName == "칼바람" && ReferenceEquals(wm.Weapon1Data, bowBefore));
            PanelField<GameObject>(panel, "_engravePanel")?.SetActive(false);   // 진화 뒤 각인 제안 — 이 실측의 대상이 아니다
            PanelCall(panel, "HideEvolvePanel");
            PanelCall(panel, "RefreshAll");
            await Wait(0.3f, ct);
            string l2 = lbl != null ? lbl.GetParsedText() : null;
            Check(sb, $"단련 버튼 「{l2}」", btn != null && btn.gameObject.activeInHierarchy && l2 != null && l2.Contains("형 단련 Ⅱ") && l2.Contains("12"));
            await ShotUi("WF_Crucible_2_TemperButton", 300);

            btn?.onClick.Invoke();   // 단련 버튼 → 단련 창
            await Wait(0.4f, ct);
            var names = PanelField<TMP_Text[]>(panel, "_temperName");
            var tp    = PanelField<GameObject>(panel, "_temperPanel");
            string n0 = names?[0] != null ? names[0].GetParsedText() : null, n1 = names?[1] != null ? names[1].GetParsedText() : null;
            Check(sb, $"단련 창 카드 「{n0}」 · 「{n1}」", tp != null && tp.activeInHierarchy && n0 != null && n0.Contains("쌍풍") && n1 != null && n1.Contains("되감는 바람"));
            await ShotUi("WF_Crucible_3_TemperPanel", 300);

            int fuel0 = run.FuelBank.EnhanceMaterial;
            PanelCall(panel, "OnTemperPicked", 0);
            await Wait(0.4f, ct);
            var rt  = PanelField<TMP_Text>(panel, "_resultText");
            string res = rt != null ? rt.GetParsedText() : null;
            var w = wm.Weapon0Data;
            Check(sb, $"결과 「{res}」 · 재료 {fuel0} → {run.FuelBank.EnhanceMaterial} · 단계 {w.formTier} · {w.formVariants}",
                  res != null && res.Contains("형 단련 — 쌍풍") && fuel0 - run.FuelBank.EnhanceMaterial == 12 && w.formTier == 2 && w.formVariants == "twin_wind");
            sb.AppendLine($"   다음 버튼 「{(lbl != null ? lbl.GetParsedText() : null)}」");
            await ShotUi("WF_Crucible_4_Result", 300);
        }
        finally
        {
            Managers.UI?.CloseAllPopupUI();
            UnityEngine.Object.Destroy(go);
            HubGui("RestoreTestHubGui");
        }
    }

    /// <summary>첫 발동 때만 세 장 — 0.06초 · 0.25초(발동 이펙트 · 3D 글자) · 0.7초(계정 첫 자막).</summary>
    private static Action<string> FirstTriggerShots(string prefix)
    {
        bool done = false;
        return _ =>
        {
            if (done) return;
            done = true;
            UniTask.Void(async () =>
            {
                await ShotUi(prefix + "_2_Trigger", 60);
                await ShotUi(prefix + "_3_Trigger", 150);
                await ShotUi(prefix + "_4_After", 400);
            });
        };
    }

    private static T PanelField<T>(UI_CruciblePanel panel, string name) where T : class
        => typeof(UI_CruciblePanel).GetField(name, Inst)?.GetValue(panel) as T;

    private static void PanelCall(UI_CruciblePanel panel, string method, params object[] args)
        => typeof(UI_CruciblePanel).GetMethod(method, Inst)?.Invoke(panel, args.Length == 0 ? null : args);

    private static void HubGui(string method)
        => typeof(UILayoutRuntimeProbeEditor).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);

    private static UniTask ShotUi(string name, int settleMs)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, settleMs }) : UniTask.CompletedTask;
    }

    // ── 표적 · 입력 도우미 ──────────────────────────────────────
    private const int TankHp = 50000;

    private static async UniTask<MonsterBase> SpawnAsync(Vector3 at, CancellationToken ct)
    {
        var m = await Managers.ObjectPooler.SpawnAsync<MonsterBase>("Slime/Slime", ObjectPoolerManager.PoolType.Monster, at, Quaternion.identity);
        if (m == null) return null;
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 6f && m.IsDamageImmuneNow) await UniTask.Yield(PlayerLoopTiming.Update, ct);
        m.Status.ApplyCc("probe_hold", 9999f);   // 걸어오거나 때리지 않게
        SetHp(m, TankHp);
        return m;
    }

    private static void SetHp(MonsterBase m, int hp)
    {
        var rt = typeof(MonsterBase).GetField("_runtime", Inst)?.GetValue(m);
        var f  = rt?.GetType().GetField("CurrentHp", Inst);
        if (f != null) f.SetValue(rt, Convert.ChangeType(hp, f.FieldType));
    }

    /// <summary>
    /// 표적 하나의 받은 피해 — OnHPChanged(현재, 최대)로 깎인 양을 더한다. 최대치로 채워지는 사건(스폰 뒤 초기화)은 세지 않는다.
    /// Refill이 체력을 TankHp로 다시 올려 연타에 죽지 않게 한다(SetHp는 사건을 내지 않는다).
    /// </summary>
    private sealed class HpTrack
    {
        public MonsterBase M;
        public float Dealt;
        private int _last;
        private readonly Vector3 _pin;   // 넉백으로 밀려도 매 틱 제자리로(예산 실측과 같은 이유)
        public Vector3 Pin => _pin;
        public HpTrack(MonsterBase m) { M = m; _pin = m.transform.position; M.OnHPChanged += OnChanged; Refill(); }
        private void OnChanged(int cur, int max)
        {
            if (cur == max) { _last = cur; return; }   // 최대치로 채워짐(스폰 뒤 초기화) — 피해 아님. 50000으로 올린 뒤의 피해는 cur > max라 여기 안 걸린다
            if (cur < _last) Dealt += _last - cur;
            _last = cur;
        }
        public void Refill()
        {
            if (M == null || M.IsDead) return;
            SetHp(M, TankHp);
            _last = TankHp;
            if ((M.transform.position - _pin).sqrMagnitude > 0.04f)
            {
                if (M.TryGetComponent<UnityEngine.AI.NavMeshAgent>(out var ag) && ag.isActiveAndEnabled && ag.isOnNavMesh) ag.Warp(_pin);
                else M.transform.position = _pin;
            }
        }
        public void Detach() { if (M != null) M.OnHPChanged -= OnChanged; }
    }

    private static readonly List<HpTrack> s_tracks = new();

    private static async UniTask<HpTrack> TrackAsync(Vector3 at, CancellationToken ct)
    {
        var m = await SpawnAsync(at, ct);
        if (m == null) return null;
        var t = new HpTrack(m);
        s_tracks.Add(t);
        return t;
    }

    // 전투방 파도 몬스터도 끈다 — 조준 보정이 그쪽으로 끌려가 줄 밖 표적이 맞거나 헛친다(실측 2 6 · 8 · 10회차, 실측 4 3~4회차)
    private static void RefillAll() { HideStrays(); foreach (var t in s_tracks) t.Refill(); }

    /// <summary>표적이 쓰러졌으면 같은 자리에 새로 부른다.</summary>
    private static async UniTask<HpTrack> EnsureAliveAsync(HpTrack t, CancellationToken ct)
    {
        if (t != null && t.M != null && !t.M.IsDead) return t;
        return t == null ? null : await TrackAsync(t.Pin, ct);
    }

    private static void DetachAll()
    {
        foreach (var t in s_tracks) t.Detach();
        s_tracks.Clear();
    }

    private static Vector3 s_home;

    /// <summary>표적을 겨누고 제자리를 지킨다 — 평타의 조준 · 추적 전진은 마우스 쪽을 따르고, 피격 · 넉백으로 밀리면 표적에서 멀어진다
    /// (실측 2 3회차: 밀려난 3.7 m에서 8초 동안 1번 적중). 예산 실측(RelicGrowthV2BudgetProbeEditor)과 같은 방법.</summary>
    private static void Steer(PlayerController p, Vector3 at)
    {
        if ((p.transform.position - s_home).sqrMagnitude > 1f)
        {
            if (p.TryGetComponent<Rigidbody>(out var rb)) { rb.position = s_home; rb.linearVelocity = Vector3.zero; }
            p.transform.position = s_home;
        }
        AimAt(at);
        RequestFace(p, at);
    }

    private static void AimAt(Vector3 world)
    {
        var mouse = Mouse.current;
        var cam   = Camera.main;
        if (mouse == null || cam == null) return;
        Vector3 screen = cam.WorldToScreenPoint(world + Vector3.up * 0.8f);
        if (screen.z <= 0f) return;
        InputSystem.QueueDeltaStateEvent(mouse.position, (Vector2)screen);
    }

    private static void RequestFace(PlayerController p, Vector3 at)
    {
        Vector3 d = Flat(at - p.transform.position);
        p.RequestFacing(Quaternion.LookRotation(d, Vector3.up));
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
    }

    // ── 공통 ────────────────────────────────────────────────────
    private static void Check(StringBuilder sb, string name, bool ok) => sb.AppendLine($"{(ok ? "통과" : "실패")} — {name}");

    private static async UniTask<bool> EquipAsync(PlayerController p, string soPath, CancellationToken ct)
    {
        var wm = p.WeaponManager;
        string key = Path.GetFileNameWithoutExtension(soPath);
        var so = AssetDatabase.LoadAssetAtPath<WeaponSO>(soPath);
        if (so == null) return false;
        var runtime = WeaponData.FromSO(so);
        // 슬롯 인스턴스가 있으면 장착이 데이터만 바꾸고 모델은 그대로 둔다 — 먼저 해제한다(LancelotQSwordProbeEditor와 같은 길)
        typeof(PlayerWeaponManager).GetMethod("DestroySlotInstance", BindingFlags.Static | BindingFlags.NonPublic)
            ?.Invoke(null, new object[] { wm.slots[PlayerWeaponManager.Slot0] });
        await UniTask.Yield(PlayerLoopTiming.Update, ct);
        await wm.AcquireWeaponToSlotAsync(runtime, PlayerWeaponManager.Slot0, setActive: true, playAppear: false);
        if (wm.CurrentSlotIndex != PlayerWeaponManager.Slot0) await wm.SwitchToSlotAsync(PlayerWeaponManager.Slot0);
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 8f)
        {
            var inst = wm.CurrentWeaponInstance;
            if (inst != null && inst.name.StartsWith(key) && inst.activeInHierarchy) return true;
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        return false;
    }

    private static UniTask Wait(float s, CancellationToken ct)
        => UniTask.Delay(TimeSpan.FromSeconds(s), DelayType.Realtime, cancellationToken: ct);
}
#endif
