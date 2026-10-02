#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Inputs;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// [실측 · 플레이 중] 유물 성장 v2 가웨인 「해의 궤적」 — 허브 기본 규칙 · 시간대 공명 6 · 조각 14가 실제 전투방에서 돌아가는지.
/// 흐름은 옛 「유물·파츠 실측」과 같다: 테스트 허브에서 가웨인을 고르고 런 구조 자동 실측으로 Ch1 첫 전투방까지 간 뒤 멈추고,
/// 몬스터를 채워(체력 크게) 항목마다 상황을 만든다. 정오 게이지 시간은 3 · 4 · 3초로 줄인다(판정 코드는 그대로).
/// 결과: Temp/relic_growth_v2_gawain.txt (항목마다 ✅/❌ · 피해 · 반경 · 스택 수) · 화면 Temp/rgv2_gawain_*.png
/// </summary>
public static class RelicGrowthV2GawainProbeEditor
{
    private const string AutoRoot     = "RelicFairy/Debug/런 구조 자동 실측/";
    private const string ExtraMonster = "Slime/Slime";
    private const int    WantMonsters = 10;
    private const int    TankHp       = 50000;
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const string OutPath = "Temp/relic_growth_v2_gawain.txt";
    private static bool s_armed;

    [MenuItem("RelicFairy/Debug/유물 성장 v2/5 가웨인 실측 (테스트 허브, 플레이 중)")]
    private static void Begin()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[RGV2가웨인] 플레이 모드에서만 동작한다."); return; }
        var launcher = UnityEngine.Object.FindFirstObjectByType<TestHubLauncher>();
        if (launcher == null) { Debug.LogWarning("[RGV2가웨인] 테스트 허브에서 시작한다."); return; }
        var relics = typeof(TestHubLauncher).GetField("relics", Inst)?.GetValue(launcher) as RelicClassSO[];
        int idx = relics == null ? -1 : Array.FindIndex(relics, r => r != null && r.Id.ToString() == "Gawain");
        if (idx < 0) { Debug.LogWarning("[RGV2가웨인] 허브 유물 목록에 가웨인이 없다."); return; }
        typeof(TestHubLauncher).GetField("_relicIndex", Inst)?.SetValue(launcher, idx);
        s_armed = true;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        EditorApplication.ExecuteMenuItem(AutoRoot + "시작 (테스트 허브에서, 플레이 중)");
        Debug.Log("[RGV2가웨인] 런 시작 요청");
    }

    private static void OnLog(string msg, string stack, LogType type)
    {
        if (!s_armed || !msg.StartsWith("[RunAuto]") || !msg.Contains("방 진입")) return;
        s_armed = false;
        Application.logMessageReceived -= OnLog;
        EditorApplication.delayCall += () =>
        {
            EditorApplication.ExecuteMenuItem(AutoRoot + "중지·기록");
            var player = GameRunBootstrapper.Instance?.Run?.Player;
            if (player == null) { Debug.LogWarning("[RGV2가웨인] 런 플레이어가 없다"); return; }
            RunAsync(player, player.GetCancellationTokenOnDestroy()).Forget();
        };
    }

    // ── 공통 ────────────────────────────────────────────────────

    private sealed class Ctx
    {
        public PlayerController Player;
        public GawainMemoryHub Hub;
        public GawainZenithRelic Relic;
        public ZenithGauge Gauge;
        public PlayerLoadout Loadout;
        public StringBuilder Sb = new();
        public List<MonsterBase> Pool = new();
        public List<MonsterBase> Used = new();
        public Vector3 Park;
        public Dictionary<string, RuneSynergyEntry> Runes;
        public int Pass, Fail;
        public void Line(bool ok, string text) { Sb.AppendLine($"  {(ok ? "✅" : "❌")} {text}"); if (ok) Pass++; else Fail++; Debug.Log($"[RGV2가웨인] {(ok ? "통과" : "실패")} {text}"); }
        public void Note(string text) => Sb.AppendLine("     " + text);
        public float Per => Player.RuntimeStats.GetEffectiveAttack(AttackStatKind.Melee) * GawainMemoryHub.BloomAtkRatio;
    }

    private static async UniTaskVoid RunAsync(PlayerController player, CancellationToken ct)
    {
        var c = new Ctx { Player = player, Loadout = AppBootstrapper.Instance?.Loadout };
        try
        {
            typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(player, true);
            // 자동 플레이가 대기방에서 서약을 맺는다(10-02 d6) — 서약 효과가 실측 적중 · 처치에 끼어들지 않게 비운다
            GameRunBootstrapper.Instance?.Run?.CovenantHandler?.Cleanup();
            await Wait(2.5f, ct);
            await FillMonstersAsync(c, ct);
            c.Relic = player.RelicBehavior as GawainZenithRelic;
            c.Gauge = c.Relic?.Gauge;
            c.Sb.AppendLine($"대상 몬스터 {c.Pool.Count} · 유물 {player.RelicBehavior?.GetType().Name ?? "없음"} · 개화 1스택 {c.Per:0}");
            if (c.Relic == null || c.Gauge == null) { c.Line(false, "가웨인 유물이 아니다"); return; }
            SetGaugeTimes(c.Gauge, 3f, 4f, 3f);
            c.Hub = GawainMemoryHub.Ensure(player);
            FaceOpen(player, c.Park);
            await BaseAsync(c, ct);
            await ResonanceAsync(c, ct);
            await FragmentsAsync(c, ct);
            c.Runes = typeof(RuneSynergyFullProbeEditor).GetMethod("LoadCsv", BindingFlags.Static | BindingFlags.NonPublic)
                ?.Invoke(null, null) as Dictionary<string, RuneSynergyEntry>;
            await ReactionsAsync(c, ct);
            await UiAsync(c, "gawain", ct);
        }
        catch (OperationCanceledException) { c.Sb.AppendLine("취소됨"); }
        catch (Exception e) { c.Sb.AppendLine("예외: " + e); Debug.LogException(e); }
        finally
        {
            c.Sb.Insert(0, $"통과 {c.Pass} · 실패 {c.Fail}\n");
            System.IO.File.WriteAllText(OutPath, c.Sb.ToString());
            Debug.Log($"[RGV2가웨인] 끝 — 통과 {c.Pass} · 실패 {c.Fail} → {OutPath}");
        }
    }

    // ── 1 허브 기본 규칙 ─────────────────────────────────────────

    private static async UniTask BaseAsync(Ctx c, CancellationToken ct)
    {
        c.Sb.AppendLine("■ 허브 기본 규칙");
        c.Line(c.Hub != null, "허브(GawainMemoryHub)가 붙었다");
        await WaitPhase(c.Gauge, ZenithGauge.ZPhase.Charging, 15f, ct);
        c.Gauge.Paused = true;
        var a = Take(c); var b = Take(c); var d = Take(c);
        Place(c, a, 2f, -1.5f); Place(c, b, 2f, 1.5f); Place(c, d, 6f, 0f);
        foreach (var m in new[] { a, b, d }) for (int k = 0; k < 4; k++) { await Hit(c, m, ct); await Wait(0.55f, ct); }
        c.Line(Mark(a) == 3 && Mark(b) == 3, $"여명 적중 → 태양흔 상한 3 (실제 {Mark(a)} · {Mark(b)} · {Mark(d)})");
        c.Gauge.Paused = false;
        var hp = Snapshot(c.Pool, a, b, d);
        bool noon = await WaitPhase(c.Gauge, ZenithGauge.ZPhase.Noon, 8f, ct);
        await Wait(0.4f, ct);
        ScreenCapture.CaptureScreenshot("Temp/rgv2_gawain_harvest.png");
        int dropA = hp[a] - a.CurrentHp;
        c.Line(noon && Mark(a) == 0 && dropA >= Mathf.FloorToInt(c.Per * 3f * 0.7f),
               $"정오 수확 — 태양흔 {Mark(a)} · 체력 감소 {dropA}(기대 ≥ 개화 3스택 {c.Per * 3f:0}, 2m 안 이웃 폭발 포함 가능)");
        c.Note($"수확 체력 감소 — a {hp[a] - a.CurrentHp} · b {hp[b] - b.CurrentHp} · d {hp[d] - d.CurrentHp}");
    }

    // ── 2 시간대 공명 ────────────────────────────────────────────

    private static async UniTask ResonanceAsync(Ctx c, CancellationToken ct)
    {
        c.Sb.AppendLine("■ 시간대 공명");
        // 여명 2 — 상한 5
        SetEchoes(c, ("dawn", 2));
        await WaitPhase(c.Gauge, ZenithGauge.ZPhase.Charging, 15f, ct);
        c.Gauge.Paused = true;
        Retire(c);
        var a = Take(c);
        Place(c, a, 2f, 0f);
        for (int k = 0; k < 6; k++) { await Hit(c, a, ct); await Wait(0.55f, ct); }
        c.Line(c.Hub.TierDawn == 2 && Mark(a) == 5, $"여명 공명 2 — 태양흔 상한 5 (단계 {c.Hub.TierDawn} · 태양흔 {Mark(a)})");
        c.Gauge.Paused = false;

        // 여명 4 — 붙듦 · 낙일로 정오
        SetEchoes(c, ("dawn", 4));
        await WaitPhase(c.Gauge, ZenithGauge.ZPhase.Charging, 15f, ct);
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 6f && !c.Gauge.IsHoldingDawn) await UniTask.Yield(ct);
        bool held = c.Gauge.IsHoldingDawn;
        await Wait(1f, ct);
        bool stillHeld = c.Gauge.IsHoldingDawn && c.Gauge.CurrentPhase == ZenithGauge.ZPhase.Charging;
        await CastQ(c, ct);
        await Wait(0.3f, ct);
        c.Line(held && stillHeld && c.Gauge.IsNoon, $"여명 공명 4 — 여명 끝에서 붙듦({held}) · 1초 뒤에도 붙듦({stillHeld}) · 낙일 → 정오({c.Gauge.IsNoon})");

        // 정오 2 — 연쇄
        SetEchoes(c, ("noon", 2));
        Retire(c);
        var x = Take(c); var y = Take(c);
        Place(c, x, 3f, 0f);
        Warp(y, x.transform.position + Vector3.right * 1.2f);
        await Wait(0.3f, ct);
        RelicMarkStatus.Of(y.gameObject, true).SetSunmark(1, c.Hub.SunmarkCap);
        c.Hub.Bloom(x, 1, GawainMemoryHub.BloomCause.Fragment);
        c.Line(c.Hub.TierNoon == 2 && Mark(y) == 2, $"정오 공명 2 — 개화가 2m 안 태양흔 적에게 튄다 (이웃 태양흔 1 → {Mark(y)})");

        // 정오 4 — 한낮이 길다
        SetEchoes(c, ("noon", 4));
        await WaitPhase(c.Gauge, ZenithGauge.ZPhase.Noon, 15f, ct);
        var z = Take(c);
        int hz = z.CurrentHp;
        for (int k = 0; k < 3; k++) { await Hit(c, z, ct); await Wait(0.55f, ct); }
        c.Line(Mark(z) == 0 && hz - z.CurrentHp > c.Per * 2.5f, $"정오 공명 4 — 정오에도 쌓이고 상한 3이면 그 자리 개화 (태양흔 {Mark(z)} · 감소 {hz - z.CurrentHp})");

        // 황혼 2 — 화상이 다음 여명까지
        SetEchoes(c, ("dusk", 2));
        await WaitPhase(c.Gauge, ZenithGauge.ZPhase.Cooldown, 15f, ct);
        var w = Take(c);
        await Hit(c, w, ct);
        await Wait(1.2f, ct);
        float burn = BurnOf(w), need = c.Gauge.PhaseRemaining;
        c.Line(burn >= need, $"황혼 공명 2 — 남은 화상 {burn:0.0}초 ≥ 황혼 남은 {need:0.0}초");

        // 황혼 4 — 짧은 정오
        SetEchoes(c, ("dusk", 4));
        await WaitPhase(c.Gauge, ZenithGauge.ZPhase.Charging, 15f, ct);
        await WaitPhase(c.Gauge, ZenithGauge.ZPhase.Cooldown, 15f, ct);
        bool shortNoon = false;
        t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 8f)
        {
            if (c.Gauge.IsShortNoon) { shortNoon = true; break; }
            await UniTask.Yield(ct);
        }
        bool backToDawn = await WaitPhase(c.Gauge, ZenithGauge.ZPhase.Charging, 6f, ct);
        c.Line(shortNoon && backToDawn, $"황혼 공명 4 — 황혼 끝에 짧은 정오({shortNoon}) → 황혼 없이 여명({backToDawn})");
        SetEchoes(c);
    }

    // ── 3 조각 ──────────────────────────────────────────────────

    private static async UniTask FragmentsAsync(Ctx c, CancellationToken ct)
    {
        c.Sb.AppendLine("■ 조각(찬란 — 세 줄 모두)");

        // G1 새벽 파종
        AddPart(c, "g_dawn_sowing");
        await WaitPhase(c.Gauge, ZenithGauge.ZPhase.Charging, 15f, ct);
        var s1 = Take(c); var s2 = Take(c);
        Place(c, s1, 1.5f, 0f); Place(c, s2, 3f, 0f);
        await Wait(0.3f, ct);
        int fieldsBefore = FireFields();
        c.Player.RaiseDodgeStart();
        for (int i = 0; i < 6; i++) { WarpPlayer(c, c.Player.transform.position + c.Player.transform.forward * 0.6f); await Wait(0.05f, ct); }
        await Wait(0.5f, ct);
        c.Line(Mark(s1) >= 1 && Mark(s2) >= 1, $"G1 새벽 파종 ① 대시로 지나간 적 태양흔 (적1 {Mark(s1)} · 적2 {Mark(s2)})");
        c.Line(FireFields() > fieldsBefore, $"G1 ② 대시 끝 불씨 (장판 {fieldsBefore} → {FireFields()})");

        // G2 서광의 각인 — 각인 사건을 직접 낸다
        AddPart(c, "g_daybreak_mark");
        var p1 = Take(c);
        Place(c, p1, 4.5f, 0f);
        await Wait(0.3f, ct);
        float d0 = Vector3.Distance(p1.transform.position, c.Player.transform.position);
        int m0 = Mark(p1);
        RaiseEvent(c.Relic, "MarkReached");
        await Wait(0.5f, ct);
        float d1 = Vector3.Distance(p1.transform.position, c.Player.transform.position);
        c.Line(d1 < d0 - 1f, $"G2 서광의 각인 ① 5m 안 끌어당김 ({d0:0.0}m → {d1:0.0}m)");
        c.Line(Mark(p1) == m0 + 1, $"G2 ② 끌려온 적 태양흔 +1 ({m0} → {Mark(p1)})");

        // G3 여명의 맹세 — 피격을 허브에 직접 넣는다(실측 중 무적)
        AddPart(c, "g_dawn_oath");
        await WaitPhase(c.Gauge, ZenithGauge.ZPhase.Charging, 15f, ct);
        var oath = Find<GDawnOathEffect>(c);
        c.Player.RuneEffects.Parts.NotifyDamaged(new HitInfo(Take(c)?.gameObject, c.Player.gameObject, c.Player.transform.position, Vector3.forward, 40f, false, WeaponActionType.none));
        c.Line(oath != null && oath.Heat >= 39f, $"G3 여명의 맹세 ① 여명 피해 → 축열 ({oath?.Heat:0})");

        // G4 아침 사냥
        AddPart(c, "g_morning_hunt");
        await WaitPhase(c.Gauge, ZenithGauge.ZPhase.Charging, 15f, ct);
        c.Gauge.Paused = true;
        ResetPhaseTimer(c.Gauge);
        var h1 = Take(c); var h2 = Take(c);
        Place(c, h1, 2f, 0f); Place(c, h2, 2f, 2f);
        await Wait(0.2f, ct);
        RelicMarkStatus.Of(h1.gameObject, true).SetSunmark(3, c.Hub.SunmarkCap);
        float rem0 = c.Gauge.PhaseRemaining;
        int m2 = Mark(h2);
        SetHp(h1, 1);
        await Hit(c, h1, ct);
        await Wait(0.2f, ct);
        float rem1 = c.Gauge.PhaseRemaining;
        c.Line(h1.IsDead && rem0 - rem1 > 1.5f, $"G4 아침 사냥 ① 태양흔 3 적 처치 → 해 앞당김 (남은 여명 {rem0:0.0} → {rem1:0.0})");
        c.Line(Mark(h2) > m2, $"G4 ② 태양흔이 가까운 적에게 옮는다 ({m2} → {Mark(h2)})");
        c.Gauge.Paused = false;

        // G5 정점 — 정오 개시 느려짐 · 맞춰 올리기 · 즉발
        AddPart(c, "g_zenith");
        await WaitPhase(c.Gauge, ZenithGauge.ZPhase.Charging, 15f, ct);
        var z1 = Take(c); var z2 = Take(c);
        Place(c, z1, 2f, 0f); Place(c, z2, 3f, 1f);
        RelicMarkStatus.Of(z1.gameObject, true).SetSunmark(3, c.Hub.SunmarkCap);
        RelicMarkStatus.Of(z2.gameObject, true).SetSunmark(1, c.Hub.SunmarkCap);
        int hz2 = z2.CurrentHp;
        await WaitPhase(c.Gauge, ZenithGauge.ZPhase.Noon, 8f, ct);
        float scale = Time.timeScale;
        bool instant = c.Relic.InstantCastActive;
        await Wait(0.5f, ct);
        c.Line(scale < 0.9f, $"G5 정점 ① 정오 개시 느려짐 (timeScale {scale:0.00})");
        c.Line(hz2 - z2.CurrentHp >= Mathf.FloorToInt(c.Per * 3f * 0.95f), $"G5 ② 6m 안 태양흔을 가장 높은 적(3)에 맞춤 → 개화 3스택 (감소 {hz2 - z2.CurrentHp} · 기대 ≥ {c.Per * 3f:0})");
        c.Line(instant, "G5 ③ 정점 동안 낙일 즉발 창");

        // G7 해시계 — 정오에 3체 이상 낙일 (G6 두 번째 해도 같은 낙일을 본다 — 먼저 붙인다)
        AddPart(c, "g_second_sun");
        AddPart(c, "g_sundial");
        await WaitPhase(c.Gauge, ZenithGauge.ZPhase.Charging, 15f, ct);
        await WaitPhase(c.Gauge, ZenithGauge.ZPhase.Noon, 8f, ct);
        for (int i = 0; i < 4; i++) { var m = Take(c); if (m != null) Place(c, m, 3f, i - 1.5f); }
        float before = c.Gauge.PhaseRemaining;
        await CastQ(c, ct);
        await Wait(1.2f, ct);
        float after = c.Gauge.PhaseRemaining;
        c.Line(after > before - 1.2f + 1.5f, $"G7 해시계 ① 3체 이상 낙일 → 정오 +2초 (낙일 전 남은 {before:0.0} · 1.2초 뒤 {after:0.0})");

        // G6 두 번째 해 — 위 낙일 뒤 정오 끝에 작은 해
        bool small = await WaitLog("[GawainMemory] 두 번째 해", 10f, ct);
        c.Line(small, "G6 두 번째 해 ① 정오 끝에 작은 해");

        // G8 정오의 개화
        AddPart(c, "g_noon_bloom");
        await WaitPhase(c.Gauge, ZenithGauge.ZPhase.Charging, 15f, ct);
        int ff0 = FireFields();
        await WaitPhase(c.Gauge, ZenithGauge.ZPhase.Noon, 8f, ct);
        await Wait(0.3f, ct);
        c.Line(FireFields() > ff0, $"G8 정오의 개화 ① 정오 개시 화상 지대 ({ff0} → {FireFields()})");
        ScreenCapture.CaptureScreenshot("Temp/rgv2_gawain_noon_bloom.png");

        // G9 잔염의 길 · G10 저무는 해 — 황혼
        AddPart(c, "g_ember_path");
        AddPart(c, "g_setting_sun");
        await WaitPhase(c.Gauge, ZenithGauge.ZPhase.Cooldown, 15f, ct);
        await Wait(0.2f, ct);
        c.Line(c.Relic.BonusCasts >= 1, $"G10 저무는 해 ① 황혼 시작에 작은 낙일 ({c.Relic.BonusCasts}회)");
        var e1 = Take(c);
        Place(c, e1, 2f, 0f);
        int ff1 = FireFields();
        await Hit(c, e1, ct);
        c.Line(FireFields() > ff1, $"G9 잔염의 길 ① 황혼 적중 자리 불씨 ({ff1} → {FireFields()})");
        await Wait(0.8f, ct);
        c.Line(Mark(e1) >= 1, $"G9 ② 불씨가 황혼에도 태양흔 ({Mark(e1)})");

        // G12 심판의 노을 — 황혼 · 태양흔 2 · 체력 10%
        AddPart(c, "g_dusk_judgment");
        var j1 = Take(c);
        Place(c, j1, 2f, 0f);
        RelicMarkStatus.Of(j1.gameObject, true).SetSunmark(2, c.Hub.SunmarkCap);
        SetHpRatio(j1, 0.10f);
        await Hit(c, j1, ct);
        await Wait(0.2f, ct);
        c.Line(!c.Hub.IsDusk || j1.IsDead, $"G12 심판의 노을 ① 처형 (황혼 {c.Hub.IsDusk} · 처치 {j1.IsDead})");

        // G11 불씨 옮기기 — 화상 적 처치
        AddPart(c, "g_ember_carry");
        var k1 = Take(c); var k2 = Take(c);
        Place(c, k1, 2f, 0f); Place(c, k2, 2f, 2f);
        await Wait(0.2f, ct);
        await Hit(c, k1, ct);                                         // 화상 부착(태양의 열기)
        RelicMarkStatus.Of(k1.gameObject, true).SetSunmark(2, c.Hub.SunmarkCap);
        int k2m = Mark(k2);
        SetHp(k1, 1);
        await Hit(c, k1, ct);
        await Wait(0.2f, ct);
        c.Line(BurnOf(k2) > 0f && Mark(k2) > k2m, $"G11 불씨 옮기기 ① 화상 · 태양흔이 옮는다 (화상 {BurnOf(k2):0.0}초 · 태양흔 {k2m} → {Mark(k2)})");

        // G13 새벽에서 한낮으로 · G14 한낮에서 노을로
        AddPart(c, "g_dawn_to_noon");
        AddPart(c, "g_noon_to_dusk");
        await WaitPhase(c.Gauge, ZenithGauge.ZPhase.Charging, 15f, ct);
        var n1 = Take(c);
        Place(c, n1, 2f, 0f);
        for (int k = 0; k < 3; k++) { await Hit(c, n1, ct); await Wait(0.55f, ct); }
        await WaitPhase(c.Gauge, ZenithGauge.ZPhase.Noon, 8f, ct);
        await Wait(0.3f, ct);
        c.Line(Mark(n1) >= 1, $"G13 ① 수확 뒤 1스택 남김 ({Mark(n1)} — 이웃 개화 연쇄가 더할 수 있다)");
        var killMe = Take(c);
        if (killMe != null) { SetHp(killMe, 1); await Hit(c, killMe, ct); }
        bool embers = await WaitLog("[GawainMemory] 한낮에서 노을로", 8f, ct);
        c.Line(embers, "G14 한낮에서 노을로 ① 정오 처치 수만큼 불씨");
        ScreenCapture.CaptureScreenshot("Temp/rgv2_gawain_noon_to_dusk.png");
    }

    // ── 4 일광 반응 ──────────────────────────────────────────────

    private static async UniTask ReactionsAsync(Ctx c, CancellationToken ct)
    {
        c.Sb.AppendLine("■ 일광 반응(찬란 — 룬 단계를 직접 켠다)");
        if (c.Runes == null) { c.Line(false, "룬 표를 못 읽음"); return; }
        var res = c.Player.RuneEffects.Resources;
        c.Gauge.Paused = false;
        await WaitPhase(c.Gauge, ZenithGauge.ZPhase.Charging, 20f, ct);
        c.Gauge.Paused = true;   // 여명에 멈춘다 — 수확 · 정오 조각이 끼어들지 않게
        ResetPhaseTimer(c.Gauge);

        // GR1 해빙(얼음)
        AddPart(c, "g_rx_thaw");
        Rune(c, "IceFrost"); Rune(c, "IceFreeze");
        var t1 = Take(c); var t2 = Take(c);
        Place(c, t1, 2f, 0f); Place(c, t2, 2f, 1.5f);
        await Wait(0.3f, ct);
        t1.Status.ApplySlow("frost", 0.3f, 5f, 2); t1.Status.ApplySlow("frost", 0.3f, 5f, 2);
        t2.Status.ApplySlow("frost", 0.3f, 5f, 2);
        int h1 = t1.CurrentHp, m2 = Mark(t2);
        bool thaw = false;
        void H1(string msg, string st, LogType t) { if (msg.Contains("(Reaction)") && msg.Contains("×2")) thaw = true; }
        Application.logMessageReceived += H1;
        c.Hub.AddSunmark(t1, 1, ignoreGap: true);
        Application.logMessageReceived -= H1;
        c.Line(thaw && t1.Status.GetSlowStacks("frost") == 0, $"GR1 해빙 ① 서리 2 적에 태양흔 → 그 자리 개화 ×2 (감소 {h1 - t1.CurrentHp} · 서리 {t1.Status.GetSlowStacks("frost")})");
        c.Line(Mark(t2) > m2, $"GR1 ② 수증기 — 3 m 안 서리 적 태양흔 +1 ({m2} → {Mark(t2)})");

        // GR2 과열(번개)
        AddPart(c, "g_rx_overheat");
        Rune(c, "ElecStatic"); Rune(c, "ElecDischarge");
        for (int i = 0; i < 10; i++) res.AddStack("ElecStatic", 10, 30f);
        var o1 = Take(c); var o2 = Take(c); var o3 = Take(c);
        Place(c, o1, 2f, 0f); Place(c, o2, 4.5f, 1.5f); Place(c, o3, 4.5f, -1.5f);
        await Wait(0.3f, ct);
        var hp = Snapshot(new List<MonsterBase>(), o2, o3);
        int mo2 = Mark(o2);
        c.Hub.Bloom(o1, 1, GawainMemoryHub.BloomCause.Fragment);
        c.Line(hp[o2] - o2.CurrentHp > 0 && hp[o3] - o3.CurrentHp > 0, $"GR2 과열 ① 정전기 10 개화 → 사슬 (4.7 m 적 감소 {hp[o2] - o2.CurrentHp} · {hp[o3] - o3.CurrentHp})");
        c.Line(Mark(o2) > mo2, $"GR2 ② 튄 적 태양흔 +1 ({mo2} → {Mark(o2)})");
        RelicMarkStatus.Of(o2.gameObject, true).SetSunmark(2, c.Hub.SunmarkCap);
        c.Player.RuneEffects.Parts.NotifySkillUsed();
        c.Line(Mark(o2) == 0, $"GR2 ③ 방전 순간 태양흔 적 즉시 개화 ({Mark(o2)})");

        // GR3 들불(독)
        AddPart(c, "g_rx_wildfire");
        Rune(c, "GrassMist");
        var w1 = Take(c); var w2 = Take(c);
        Place(c, w1, 3f, 0f); Place(c, w2, 3.6f, 0.8f);
        await Wait(0.3f, ct);
        PoisonField.SpawnAt(c.Player, w2.transform.position, new PoisonField.Config { perTick = 1f, life = 8f, radiusMult = 1f, dotMult = 1f });
        int mw = Mark(w2);
        c.Hub.Bloom(w1, 1, GawainMemoryHub.BloomCause.Fragment);
        await Wait(2.3f, ct);
        c.Line(Mark(w2) > mw, $"GR3 들불 ① 개화가 독안개에 닿아 불안개 → 2초마다 태양흔 ({mw} → {Mark(w2)})");

        // GR4 겹해(불)
        AddPart(c, "g_rx_twin_sun");
        Rune(c, "FireEmber"); Rune(c, "FireIgnite");
        var f1 = Take(c);
        Place(c, f1, 2f, 0f);
        await Wait(0.3f, ct);
        RelicMarkStatus.Of(f1.gameObject, true).SetSunmark(1, c.Hub.SunmarkCap);
        await Wait(0.55f, ct);
        await Hit(c, f1, ct);
        c.Line(Mark(f1) >= 2, $"GR4 겹해 ① 잔불이 태양흔 적에 → 시간대 무관 태양흔 +1 ({Mark(f1)})");
        f1.Status.ApplyDot("ignite", 1f, 1f, 5, c.Player.gameObject);
        c.Hub.Bloom(f1, 1, GawainMemoryHub.BloomCause.Fragment);
        c.Line(f1.Status.GetRemainingDotDamage("ignite") <= 0.01f, $"GR4 ③ 점화 적 개화 → 점화 즉시 만료 · 작열 폭발 (남은 점화 {f1.Status.GetRemainingDotDamage("ignite"):0.0})");

        // GR5 일륜(빛)
        AddPart(c, "g_rx_corona");
        Rune(c, "LightRadiance"); Rune(c, "LightBurst");
        var l1 = Take(c); var l2 = Take(c);
        Place(c, l1, 2f, 0f); Place(c, l2, 3.5f, 0.8f);
        await Wait(0.3f, ct);
        RelicMarkStatus.Of(l1.gameObject, true).SetSunmark(2, c.Hub.SunmarkCap);
        bool early = false;
        void H5(string msg, string st, LogType t) { if (msg.Contains("×1 (Reaction)")) early = true; }
        Application.logMessageReceived += H5;
        c.Player.RuneEffects.Parts.NotifyHit(new HitInfo(c.Player.gameObject, l1.gameObject, l1.transform.position, l1.transform.position - c.Player.transform.position, 5f, true, WeaponActionType.ESkill));
        Application.logMessageReceived -= H5;
        c.Line(early, $"GR5 일륜 ① 치명 → 그 적만 1스택 미리 개화 (태양흔 {Mark(l1)})");
        c.Line(c.Hub.BloomCanCrit, "GR5 ③ 개화가 치명 가능");
        RelicMarkStatus.Of(l2.gameObject, true).SetSunmark(2, c.Hub.SunmarkCap);
        Face(c, l2);
        res.AddGauge("LightRadiance", Mathf.Max(0, 9 - res.GetGauge("LightRadiance")), 10);
        await Wait(0.2f, ct);
        res.AddGauge("LightRadiance", 1, 10);
        await Wait(0.3f, ct);
        c.Line(Mark(l2) == 0, $"GR5 ③ 광폭발 원뿔 안 태양흔 적 즉시 개화 ({Mark(l2)})");

        // GR6 일식(어둠)
        AddPart(c, "g_rx_eclipse");
        Rune(c, "DarkErosion");
        res.AddGauge("darkGauge", 60, 100);
        await Wait(0.3f, ct);
        var e1 = Take(c);
        Place(c, e1, 2f, 0f);
        await Wait(0.3f, ct);
        c.Hub.AddSunmark(e1, 4, ignoreGap: true);
        var est = RelicMarkStatus.Of(e1.gameObject, false);
        c.Line(c.Hub.EclipseActive && est != null && est.Blackspot >= 4, $"GR6 일식 ① 잠식 50+ → 흑점(상한 없음) ({est?.Blackspot})");
        c.Gauge.Paused = false;
        res.SetRegister("darkReleaseActive", 1);
        await Wait(0.3f, ct);
        bool paused = c.Gauge.Paused;
        res.SetRegister("darkReleaseActive", 0);
        await Wait(0.3f, ct);
        c.Line(paused && (est == null || est.Blackspot == 0), $"GR6 ③ 암흑 해방 동안 해가 멈추고 끝나는 순간 흑점 전부 개화 (멈춤 {paused} · 흑점 {est?.Blackspot})");
        c.Gauge.Paused = false;
    }

    private static void Face(Ctx c, MonsterBase m)
    {
        Vector3 d = m.transform.position - c.Player.transform.position; d.y = 0f;
        if (d.sqrMagnitude > 0.01f) SetFacing(c.Player, Quaternion.LookRotation(d.normalized));
    }


    // ── 화면(계획 4) — HUD 확장 · 출시용 글자 · 일시정지 「되찾은 기억」 ──────────

    private static async UniTask UiAsync(Ctx c, string tag, CancellationToken ct)
    {
        c.Sb.AppendLine("■ 화면(계획 4)");
        var hud = UnityEngine.Object.FindFirstObjectByType<RelicMemoryHudPresenter>();
        var view = hud != null ? hud.GetComponent<RelicMemoryHudView>() : null;
        c.Line(view != null && view.Visible, $"유물 HUD 확장이 게이지 위에 떠 있다 (Presenter {hud != null} · View {view?.Visible})");
        await Wait(0.3f, ct);
        ScreenCapture.CaptureScreenshot($"Temp/rgv2_{tag}_hud.png");
        await Wait(0.3f, ct);

        var ft = GameObject.Find("@RelicFloatText");
        int live = 0;
        if (ft != null) for (int i = 0; i < ft.transform.childCount; i++) if (ft.transform.GetChild(i).gameObject.activeSelf) live++;
        c.Line(ft != null, $"출시용 반응 · 조각 글자(RelicFloatText)가 떴다 (지금 떠 있는 글자 {live})");

        var menu = UnityEngine.Object.FindFirstObjectByType<UI_EscMenu>(FindObjectsInactive.Include);
        if (menu == null) menu = new GameObject("ProbeEscMenu").AddComponent<UI_EscMenu>();
        menu.Open();
        await Wait(0.4f, ct);
        typeof(UI_EscMenu).GetMethod("OnMemory", Inst)?.Invoke(menu, null);
        await Wait(0.5f, ct);
        var page = UnityEngine.Object.FindFirstObjectByType<UI_RelicMemoryPage>();
        var content = page != null ? page.transform.Find("Panel/Viewport/Content") : null;
        int rows = content != null ? content.childCount : 0;
        ScreenCapture.CaptureScreenshot($"Temp/rgv2_{tag}_page.png");
        await Wait(0.4f, ct);
        c.Line(page != null && page.gameObject.activeSelf && rows > 0, $"일시정지 「되찾은 기억」 쪽 — 조각 줄 {rows}");
        menu.Close();
        await Wait(0.4f, ct);
        c.Line(Time.timeScale > 0.5f, $"닫으면 시간이 다시 흐른다 (timeScale {Time.timeScale:0.00})");
    }

    // ── 도우미 ──────────────────────────────────────────────────

    private static void SetEchoes(Ctx c, params (string anchor, int n)[] set)
    {
        var dict = typeof(PlayerLoadout).GetField("_relicEchoes", Inst)?.GetValue(c.Loadout) as Dictionary<string, int>;
        dict?.Clear();
        foreach (var (anchor, n) in set) for (int i = 0; i < n; i++) c.Loadout.AddRelicEcho(anchor);
        if (set.Length == 0) c.Hub.RecomputeTiers();
    }

    private const string HoldCc = "probe_hold";

    /// <summary>
    /// 트인 방향을 본다 — 16방향 중 1~6 m 점이 모두 발밑 높이의 내비메시 위에 있는 방향(가장 많은 쪽).
    /// 레이로 「아무것도 안 맞는 방향」을 고르면 떠 있는 판의 가장자리 너머를 고른다(09-18 DPS 실측 함정) — 내비메시로 잰다.
    /// </summary>
    private static void FaceOpen(PlayerController p, Vector3 park)
    {
        Vector3 o = p.transform.position;
        Vector3 toPark = park - o; toPark.y = 0f;
        float bestYaw = p.transform.eulerAngles.y;
        int best = -1;
        for (int k = 0; k < 16; k++)
        {
            float yaw = k * 22.5f;
            Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            if (toPark.sqrMagnitude > 1f && Vector3.Angle(dir, toPark) < 100f) continue;   // 주차장 쪽은 보지 않는다
            int ok = 0;
            for (int d = 1; d <= 6; d++)
            {
                Vector3 q = o + dir * d;
                if (!NavMesh.SamplePosition(q, out var hit, 0.4f, NavMesh.AllAreas) || Mathf.Abs(hit.position.y - o.y) > 0.6f) break;
                ok++;
            }
            if (ok > best) { best = ok; bestYaw = yaw; }
        }
        SetFacing(p, Quaternion.Euler(0f, bestYaw, 0f));
    }

    /// <summary>
    /// 플레이어가 보는 쪽을 바꾼다 — 회전은 PlayerFacing이 물리 스텝마다 목표로 다시 맞추므로 transform만 바꾸면 다음 프레임에 되돌아간다
    /// (10-02 실측: 대시 · 심판 콘이 엉뚱한 쪽으로). 목표(RequestFacing)와 지금 자세(transform · Rigidbody)를 함께 맞춘다.
    /// </summary>
    private static void SetFacing(PlayerController p, Quaternion rot)
    {
        p.RequestFacing(rot);
        p.transform.rotation = rot;
        if (p.TryGetComponent<Rigidbody>(out var rb)) rb.rotation = rot;
    }

    private static void Retire(Ctx c)
    {
        int i = 0;
        foreach (var m in c.Used)
        {
            if (m == null || m.IsDead || !m.isActiveAndEnabled) continue;
            var st = RelicMarkStatus.Of(m.gameObject, false);
            if (st != null) { st.ConsumeSunmark(); st.ConsumeBlackspot(); }
            m.Status.Reset();
            m.Status.ApplyCc(HoldCc, 9999f);
            Warp(m, c.Park + new Vector3((i % 4) * 1.5f - 2f, 0f, (i / 4 % 4) * 1.5f - 2f));
            i++;
        }
        c.Used.Clear();
        GroundFieldBase.DespawnAll();   // 앞 항목의 불씨 · 불안개가 다음 항목 적에게 태양흔을 더하지 않게
    }

    private static void ResetPhaseTimer(ZenithGauge g) => typeof(ZenithGauge).GetField("_timer", Inst)?.SetValue(g, 0f);

    /// <summary>플레이어 기준 앞 <paramref name="fwd"/> m · 오른쪽 <paramref name="right"/> m.</summary>
    private static void Place(Ctx c, MonsterBase m, float fwd, float right)
    {
        if (m == null) return;
        var tf = c.Player.transform;
        Vector3 f = tf.forward; f.y = 0f; f.Normalize();
        Warp(m, tf.position + f * fwd + Vector3.Cross(Vector3.up, f) * right);
    }

    private static void Rune(Ctx c, string type)
    {
        if (c.Runes != null && c.Runes.TryGetValue(type, out var e)) c.Player.RuneEffects.Activate(e);
        else c.Note($"(룬 행 없음: {type})");
    }

    private static void AddPart(Ctx c, string id)
    {
        Retire(c);
        FaceOpen(c.Player, c.Park);
        if (!c.Loadout.HasRelicPart(id)) c.Loadout.AddRelicPart(id, RelicMemoryGrade.Radiant);
        c.Player.RuneEffects.Parts.SyncFromLoadout(c.Loadout);
    }

    private static T Find<T>(Ctx c) where T : class
    {
        foreach (var e in c.Player.RuneEffects.Parts.Active) if (e is T t) return t;
        return null;
    }

    private static void RaiseEvent(object target, string evName)
    {
        var f = target.GetType().GetField(evName, Inst);
        (f?.GetValue(target) as Action)?.Invoke();
    }

    private static int Mark(MonsterBase m) => m != null ? (RelicMarkStatus.Of(m.gameObject, false)?.Sunmark ?? 0) : -1;
    private static float BurnOf(MonsterBase m) => m != null && m.TryGetComponent<MonsterBurnHandler>(out var h) ? h.Remaining : 0f;
    private static int FireFields() => UnityEngine.Object.FindObjectsByType<FireField>(FindObjectsSortMode.None).Length;

    private static Dictionary<MonsterBase, int> Snapshot(List<MonsterBase> pool, params MonsterBase[] extra)
    {
        var d = new Dictionary<MonsterBase, int>();
        foreach (var m in pool) if (m != null) d[m] = m.CurrentHp;
        foreach (var m in extra) if (m != null) d[m] = m.CurrentHp;
        return d;
    }

    private static MonsterBase Take(Ctx c)
    {
        for (int i = 0; i < c.Pool.Count; i++)
        {
            var m = c.Pool[i];
            c.Pool.RemoveAt(i--);
            if (m != null && !m.IsDead && m.isActiveAndEnabled && !m.IsDamageImmuneNow) { SetHp(m, TankHp); c.Used.Add(m); return m; }
        }
        return null;
    }

    private static async UniTask FillMonstersAsync(Ctx c, CancellationToken ct)
    {
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (!m.IsDead && m.isActiveAndEnabled && m.Grade != MonsterGrade.Boss) c.Pool.Add(m);
        int need = WantMonsters * 5 - c.Pool.Count;   // 항목이 많다 — 넉넉히
        // 주차장 = 플레이어 뒤 12 m — 실측은 앞에서 한다(반경 · 연쇄 · 「가장 가까운 적」에 끼지 않게)
        var fw = c.Player.transform.forward; fw.y = 0f; fw.Normalize();
        c.Park = c.Player.transform.position - fw * 12f;
        if (NavMesh.SamplePosition(c.Park, out var ph, 6f, NavMesh.AllAreas)) c.Park = ph.position;
        for (int i = 0; i < need; i++)
        {
            float ang = i * Mathf.PI * 2f / Mathf.Max(1, need);
            var pos = c.Park + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (1.5f + (i % 3));
            if (NavMesh.SamplePosition(pos, out var hit, 4f, NavMesh.AllAreas)) pos = hit.position;
            try
            {
                var m = await Managers.ObjectPooler.SpawnAsync<MonsterBase>(ExtraMonster, ObjectPoolerManager.PoolType.Monster, pos, Quaternion.identity);
                if (m != null) c.Pool.Add(m);
            }
            catch (Exception e) { c.Sb.AppendLine("  (몬스터 추가 실패: " + e.Message + ")"); break; }
        }
        await Wait(2.5f, ct);
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 8f && c.Pool.Exists(m => m != null && m.IsDamageImmuneNow))
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        c.Pool.RemoveAll(m => m == null || m.IsDead || m.IsDamageImmuneNow);
        int k = 0;
        foreach (var m in c.Pool)
        {
            SetHp(m, TankHp);
            m.Status.ApplyCc(HoldCc, 9999f);   // 걸어오지 않게 붙든다
            Warp(m, c.Park + new Vector3((k % 6) * 1.2f - 3f, 0f, (k / 6 % 6) * 1.2f - 3f));
            k++;
        }
    }

    private static async UniTask Hit(Ctx c, MonsterBase m, CancellationToken ct)
    {
        if (m == null || m.IsDead) return;
        CombatDamage.Deal(new CombatDamage.Request
        {
            Target = m.gameObject, BaseDamage = 3f, Owner = c.Player.gameObject, ActionType = WeaponActionType.GroundLight,
            KnockbackMultiplier = 0f, HitPoint = m.transform.position + Vector3.up, SourcePosition = c.Player.transform.position,
        });
        await Wait(0.05f, ct);
    }

    private static async UniTask CastQ(Ctx c, CancellationToken ct)
    {
        c.Player.CooldownTracker.ResetCooldown(SkillType.Q);
        c.Player.InputBuffer.Clear();
        c.Player.InputBuffer.Push(Command.QSkill);
        await Wait(0.15f, ct);
    }

    private static async UniTask<bool> WaitLog(string needle, float timeout, CancellationToken ct)
    {
        bool hit = false;
        void H(string msg, string st, LogType t) { if (msg.Contains(needle)) hit = true; }
        Application.logMessageReceived += H;
        try
        {
            float t0 = Time.realtimeSinceStartup;
            while (!hit && Time.realtimeSinceStartup - t0 < timeout) await UniTask.Yield(ct);
        }
        finally { Application.logMessageReceived -= H; }
        return hit;
    }

    private static void SetHp(MonsterBase m, int hp)
    {
        var rt = typeof(MonsterBase).GetField("_runtime", Inst)?.GetValue(m) as MonsterRuntimeData;
        if (rt != null) rt.CurrentHp = hp;
    }

    private static void SetHpRatio(MonsterBase m, float ratio)
    {
        var rt = typeof(MonsterBase).GetField("_runtime", Inst)?.GetValue(m) as MonsterRuntimeData;
        if (rt != null) rt.CurrentHp = Mathf.Max(1, Mathf.RoundToInt(m.EffectiveMaxHp * ratio));
    }

    private static void Warp(MonsterBase m, Vector3 pos)
    {
        if (m == null) return;
        if (NavMesh.SamplePosition(pos, out var hit, 3f, NavMesh.AllAreas)) pos = hit.position;
        if (m.TryGetComponent<NavMeshAgent>(out var ag) && ag.isActiveAndEnabled && ag.isOnNavMesh) ag.Warp(pos);
        else m.transform.position = pos;
    }

    private static void WarpPlayer(Ctx c, Vector3 pos)
    {
        if (NavMesh.SamplePosition(pos, out var hit, 2f, NavMesh.AllAreas)) pos = hit.position;
        if (c.Player.TryGetComponent<Rigidbody>(out var rb)) { rb.position = pos; rb.linearVelocity = Vector3.zero; }
        c.Player.transform.position = pos;
    }

    private static void SetGaugeTimes(ZenithGauge g, float charge, float noon, float cool)
    {
        var t = typeof(ZenithGauge);
        t.GetField("_chargeTime", Inst)?.SetValue(g, charge);
        t.GetField("_noonTime", Inst)?.SetValue(g, noon);
        t.GetField("_cooldownTime", Inst)?.SetValue(g, cool);
    }

    private static async UniTask<bool> WaitPhase(ZenithGauge g, ZenithGauge.ZPhase phase, float timeout, CancellationToken ct)
    {
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < timeout)
        {
            if (g.CurrentPhase == phase && !g.IsHoldingDawn) return true;
            if (g.IsHoldingDawn && phase == ZenithGauge.ZPhase.Noon) g.OpenNoonNow();
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        return false;
    }

    private static UniTask Wait(float s, CancellationToken ct)
        => UniTask.Delay(TimeSpan.FromSeconds(s), DelayType.Realtime, cancellationToken: ct);
}
#endif
