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
/// [실측 · 플레이 중] 유물 성장 v2 랜슬롯 「광기의 계단」 — 허브 기본 규칙 · 계단 공명 · 조각 16 · 타락 반응 6이 실제 전투방에서 돌아가는지.
/// 흐름은 가웨인 실측과 같다: 테스트 허브에서 랜슬롯을 고르고 Ch1 첫 전투방까지 간 뒤 멈추고, 몬스터를 채워(체력 크게) 항목마다 상황을 만든다.
/// 광기는 직접 맞춰 두고(감쇠는 멈춤), 광란은 길이를 정해 직접 연다. 심판은 한 방짜리(PerformJudgmentStrike 0/1)로 친다 — 콘 · 찢기 · 원한은 같은 경로다.
/// 반응은 룬 표(MERLIN_RUNE_SYNERGY_DATA.csv) 행으로 그 단계 룬 효과를 직접 켠다.
/// 결과: Temp/relic_growth_v2_lancelot.txt · 화면 Temp/rgv2_lancelot_*.png
/// </summary>
public static class RelicGrowthV2LancelotProbeEditor
{
    private const string AutoRoot     = "RelicFairy/Debug/런 구조 자동 실측/";
    private const string ExtraMonster = "Slime/Slime";
    private const int    WantMonsters = 40;
    private const int    TankHp       = 50000;
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const string OutPath = "Temp/relic_growth_v2_lancelot.txt";
    private static bool s_armed;

    [MenuItem("RelicFairy/Debug/유물 성장 v2/6 랜슬롯 실측 (테스트 허브, 플레이 중)")]
    private static void Begin()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[RGV2랜슬롯] 플레이 모드에서만 동작한다."); return; }
        var launcher = UnityEngine.Object.FindFirstObjectByType<TestHubLauncher>();
        if (launcher == null) { Debug.LogWarning("[RGV2랜슬롯] 테스트 허브에서 시작한다."); return; }
        var relics = typeof(TestHubLauncher).GetField("relics", Inst)?.GetValue(launcher) as RelicClassSO[];
        int idx = relics == null ? -1 : Array.FindIndex(relics, r => r != null && r.Id.ToString() == "Lancelot");
        if (idx < 0) { Debug.LogWarning("[RGV2랜슬롯] 허브 유물 목록에 랜슬롯이 없다."); return; }
        typeof(TestHubLauncher).GetField("_relicIndex", Inst)?.SetValue(launcher, idx);
        s_armed = true;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        EditorApplication.ExecuteMenuItem(AutoRoot + "시작 (테스트 허브에서, 플레이 중)");
        Debug.Log("[RGV2랜슬롯] 런 시작 요청");
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
            if (player == null) { Debug.LogWarning("[RGV2랜슬롯] 런 플레이어가 없다"); return; }
            RunAsync(player, player.GetCancellationTokenOnDestroy()).Forget();
        };
    }

    // ── 공통 ────────────────────────────────────────────────────

    private sealed class Ctx
    {
        public PlayerController Player;
        public LancelotMemoryHub Hub;
        public LancelotMadnessRelic Relic;
        public MadnessStack Madness;
        public PlayerLoadout Loadout;
        public Dictionary<string, RuneSynergyEntry> Runes;
        public StringBuilder Sb = new();
        public List<MonsterBase> Pool = new();
        public List<MonsterBase> Used = new();
        public Vector3 Park;
        public List<string> Log = new();
        public int Pass, Fail;
        public void Line(bool ok, string text) { Sb.AppendLine($"  {(ok ? "✅" : "❌")} {text}"); if (ok) Pass++; else Fail++; Debug.Log($"[RGV2랜슬롯] {(ok ? "통과" : "실패")} {text}"); }
        public void Note(string text) => Sb.AppendLine("     " + text);
        public float Atk => Player.RuntimeStats.GetEffectiveAttack(AttackStatKind.Melee);
        public int Grudge => Hub.Grudge != null ? Hub.Grudge.Value : 0;
    }

    private static async UniTaskVoid RunAsync(PlayerController player, CancellationToken ct)
    {
        var c = new Ctx { Player = player, Loadout = AppBootstrapper.Instance?.Loadout };
        void OnAnyLog(string msg, string st, LogType t) { if (msg.StartsWith("[LancelotMemory]")) c.Log.Add(msg); }
        Application.logMessageReceived += OnAnyLog;
        try
        {
            typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(player, true);
            // 자동 플레이가 대기방에서 서약을 맺는다(10-02 d6) — 서약 효과가 실측 적중 · 처치에 끼어들지 않게 비운다
            GameRunBootstrapper.Instance?.Run?.CovenantHandler?.Cleanup();
            await Wait(2.5f, ct);
            await FillMonstersAsync(c, ct);
            c.Relic = player.RelicBehavior as LancelotMadnessRelic;
            c.Madness = c.Relic?.Madness;
            c.Sb.AppendLine($"대상 몬스터 {c.Pool.Count} · 유물 {player.RelicBehavior?.GetType().Name ?? "없음"} · 유물 공격력 {c.Atk:0} · X자 1회 {c.Atk * LancelotMemoryHub.TearAtkRatio:0}");
            if (c.Relic == null || c.Madness == null) { c.Line(false, "랜슬롯 유물이 아니다"); return; }
            SetDecay(c, 999f, 1f);   // 실측 동안 광기는 맞춰 둔 값에 머문다
            c.Hub = LancelotMemoryHub.Ensure(player);
            FaceOpen(player, c.Park);
            c.Runes = typeof(RuneSynergyFullProbeEditor).GetMethod("LoadCsv", BindingFlags.Static | BindingFlags.NonPublic)
                ?.Invoke(null, null) as Dictionary<string, RuneSynergyEntry>;
            await BaseAsync(c, ct);
            await LadderAsync(c, ct);
            await FragmentsAsync(c, ct);
            await ReactionsAsync(c, ct);
            await UiAsync(c, "lancelot", ct);
        }
        catch (OperationCanceledException) { c.Sb.AppendLine("취소됨"); }
        catch (Exception e) { c.Sb.AppendLine("예외: " + e); Debug.LogException(e); }
        finally
        {
            Application.logMessageReceived -= OnAnyLog;
            c.Sb.Insert(0, $"통과 {c.Pass} · 실패 {c.Fail}\n");
            System.IO.File.WriteAllText(OutPath, c.Sb.ToString());
            Debug.Log($"[RGV2랜슬롯] 끝 — 통과 {c.Pass} · 실패 {c.Fail} → {OutPath}");
        }
    }

    // ── 1 허브 기본 규칙 ─────────────────────────────────────────

    private static async UniTask BaseAsync(Ctx c, CancellationToken ct)
    {
        c.Sb.AppendLine("■ 허브 기본 규칙");
        c.Line(c.Hub != null, "허브(LancelotMemoryHub)가 붙었다");
        var a = Take(c);
        c.Madness.SetStacks(10);
        await Hit(c, a, ct);
        c.Line(Brand(a) == 0, $"광기 10 적중 → 낙인 없음 ({Brand(a)})");
        c.Madness.SetStacks(20);
        for (int k = 0; k < 4; k++) { await Wait(0.55f, ct); await Hit(c, a, ct); }
        c.Line(Brand(a) == 3, $"광기 20+ 적중 → 낙인 상한 3 ({Brand(a)})");
        int g0 = c.Grudge;
        await Wait(0.55f, ct);
        await Hit(c, a, ct);
        c.Line(c.Grudge == g0 + 1, $"상한에서 또 새기면 넘친 몫 → 원한 ({g0} → {c.Grudge})");

        // 심판 — 찢기 · 원한 참격파
        EnterFrenzy(c, 60f);
        c.Line(c.Relic.JudgmentCharges == 1 && c.Relic.CanUseSkill(SkillType.Q), $"광란 진입 → 심판 1회 ({c.Relic.JudgmentCharges})");
        Place(c, a, 2.5f, 0f);
        var w1 = Take(c); var w2 = Take(c);
        Place(c, w1, 5f, -1.2f); Place(c, w2, 5f, 1.2f);
        await Wait(0.3f, ct);
        c.Hub.Grudge.TakeAll(); c.Hub.Grudge.Add(2);
        var hp = Snapshot(a, w1, w2);
        int mark = c.Log.Count;
        Judge(c);
        await Wait(0.4f, ct);
        ScreenCapture.CaptureScreenshot("Temp/rgv2_lancelot_tear.png");
        float xDmg = c.Atk * LancelotMemoryHub.TearAtkRatio * 3f;
        c.Line(LogHas(c, mark, "낙인 3(센 3) · X자 3") && Brand(a) <= 1 && hp[a] - a.CurrentHp >= xDmg * 0.95f, $"심판 막타 → 낙인 3 찢기 (찢은 뒤 막타가 새긴 낙인 {Brand(a)} · 체력 감소 {hp[a] - a.CurrentHp} · X자 3 = {xDmg:0} + 심판)");
        c.Line(c.Grudge == 0 && LogHas(c, mark, "원한 2 → 참격파 2줄"), $"막타 뒤 원한 2 → 참격파 2줄 (원한 {c.Grudge})");
        c.Note($"참격파 대상 체력 감소 — w1 {hp[w1] - w1.CurrentHp} · w2 {hp[w2] - w2.CurrentHp} (1줄 {c.Atk * LancelotMemoryHub.WaveAtkRatio:0})");

        // 실제 Q 입력 — 런타임이 심판 1회를 쓴다
        await CastQ(c, ct);
        await Wait(0.3f, ct);
        c.Line(c.Relic.JudgmentCharges == 0 && !c.Relic.CanUseSkill(SkillType.Q), $"Q 시전 → 심판 횟수 소모 ({c.Relic.JudgmentCharges})");
        await Wait(3.6f, ct);
        await EndFrenzy(c, ct);

        Retire(c);
        // 계단 넘기 사건
        int rungs = 0;
        void R(int k) => rungs++;
        c.Hub.RungCrossed += R;
        c.Madness.SetStacks(0);
        c.Madness.SetStacks(25);
        c.Hub.RungCrossed -= R;
        c.Line(rungs == 2, $"광기 0 → 25 — 계단 10 · 20 넘기 사건 ({rungs})");
    }

    // ── 2 계단 공명 ──────────────────────────────────────────────

    private static async UniTask LadderAsync(Ctx c, CancellationToken ct)
    {
        c.Sb.AppendLine("■ 계단 공명");
        SetEchoes(c, "r10", "r20");
        SetDecay(c, 0.2f, 40f);
        c.Madness.SetStacks(28);
        await Wait(1.2f, ct);
        int held = c.Madness.Stacks;
        c.Madness.SetStacks(15);
        await Wait(1.2f, ct);
        c.Line(c.Hub.Ladder == 2 && held == 20 && c.Madness.Stacks == 0, $"이어진 계단 2 — 받침 20 (28 → {held}) · 받침 아래는 그대로 감쇠 (15 → {c.Madness.Stacks})");

        SetEchoes(c, "r10", "r20", "r30");
        c.Madness.SetStacks(35);
        EnterFrenzy(c, 0.4f);
        await Wait(0.9f, ct);
        c.Line(!c.Madness.IsFrenzy && c.Madness.Stacks == 30, $"이어진 계단 3 — 광란 뒤 광기 30에서 재출발 ({c.Madness.Stacks})");
        SetDecay(c, 999f, 1f);

        SetEchoes(c, "r10", "r20", "r30", "r40");
        EnterFrenzy(c, 3f);
        float rem0 = c.Madness.FrenzyRemaining;
        c.Relic.AddStack(c.Madness.MaxStacks);
        c.Line(c.Madness.RefillDuringFrenzy && c.Relic.JudgmentCharges == 2 && c.Madness.FrenzyRemaining > rem0 + 2f,
               $"이어진 계단 4 — 광란 중 다시 가득 → 광란 이어짐 ({rem0:0.0} → {c.Madness.FrenzyRemaining:0.0}초) · 심판 {c.Relic.JudgmentCharges}회");
        await EndFrenzy(c, ct);
        SetEchoes(c);
        c.Line(c.Madness.Floor == 0 && !c.Madness.RefillDuringFrenzy, $"메아리를 걷으면 공명이 꺼진다 (받침 {c.Madness.Floor})");
    }

    // ── 3 조각 ──────────────────────────────────────────────────

    private static async UniTask FragmentsAsync(Ctx c, CancellationToken ct)
    {
        c.Sb.AppendLine("■ 조각(찬란 — 세 줄 모두)");
        float stCost = c.Player.CharacterData.dodgeStaminaCost * NightmareRules.DodgeStaminaMultiplier;
        float stMax  = c.Player.RuntimeStats.MaxStamina;

        // L1 갈라진 맹세
        AddPart(c, "l_split_oath");
        c.Madness.SetStacks(15);
        var s1 = Take(c); var s2 = Take(c); var s3 = Take(c);
        Place(c, s1, 1.4f, 0f); Place(c, s2, 2.6f, 0f); Place(c, s3, 3.8f, 0f);
        await Wait(0.3f, ct);
        var hp = Snapshot(s1, s2, s3);
        float atk1 = c.Atk;   // 베는 순간 공격력(광기가 오르면 공격력도 오른다)
        c.Player.Stamina.TryConsume(stMax * 0.75f, stMax, 30f);   // 회복 지연 30초 — 되돌림만 보인다
        float st0 = c.Player.Stamina.Current;
        c.Player.RaiseDodgeStart();
        for (int i = 0; i < 6; i++) { WarpPlayer(c, c.Player.transform.position + c.Player.transform.forward * 0.75f); await Wait(0.05f, ct); }
        await Wait(0.5f, ct);
        float cut = Mathf.Floor(atk1 * 0.6f * 0.95f);
        c.Line(hp[s1] - s1.CurrentHp >= cut && hp[s2] - s2.CurrentHp >= cut && hp[s3] - s3.CurrentHp >= cut && c.Madness.Stacks >= 21,
               $"L1 갈라진 맹세 ① 대시 관통 베기 (감소 {hp[s1] - s1.CurrentHp} · {hp[s2] - s2.CurrentHp} · {hp[s3] - s3.CurrentHp} · 기대 ≥ {cut:0}) · 광기 15 → {c.Madness.Stacks}");
        c.Line(Brand(s1) >= 1 && Brand(s2) >= 1 && Brand(s3) >= 1, $"L1 ② 관통한 적 낙인 +1 ({Brand(s1)} · {Brand(s2)} · {Brand(s3)})");
        c.Line(c.Player.Stamina.Current >= st0 + stCost * 0.95f, $"L1 ③ 3체 관통 → 대시 1회 회복 (게이지 {st0:0} → {c.Player.Stamina.Current:0} · 1회 {stCost:0})");
        c.Player.Stamina.Refund(stMax, stMax);

        // L2 피 냄새
        AddPart(c, "l_blood_scent");
        c.Madness.SetStacks(15);
        var b1 = Take(c); var b2 = Take(c);
        Place(c, b1, 2f, 0f); Place(c, b2, 3.5f, 1f);
        await Wait(0.3f, ct);
        SetHpRatio(b1, 0.25f);
        Notify(c, b1, WeaponActionType.GroundLight);   // 피해 없이 「30% 이하에서 맞음」만
        SetBrand(c, b1, 2);
        int bb0 = Brand(b2);
        SetHp(b1, 1);
        await Hit(c, b1, ct);
        await Wait(0.2f, ct);
        c.Line(b1.IsDead && c.Madness.HoldDecayUntil > Time.time, $"L2 피 냄새 ① 몰린 적 처치 → 광기 감쇠 멈춤 ({c.Madness.HoldDecayUntil - Time.time:0.0}초 남음)");
        c.Line(Brand(b2) >= bb0 + 2, $"L2 ② 낙인 2가 가까운 적에게 ({bb0} → {Brand(b2)})");

        // L3 검은 잔상
        AddPart(c, "l_black_afterimage");
        c.Madness.SetStacks(25);
        var t3 = Take(c);
        Place(c, t3, 2f, 0f);
        await Wait(0.3f, ct);
        int m3 = c.Log.Count;
        for (int k = 0; k < 3; k++) { await Hit(c, t3, ct); await Wait(0.2f, ct); }
        await Wait(0.7f, ct);
        c.Line(LogHas(c, m3, "검은 잔상 1"), $"L3 검은 잔상 ① 3번째 평타 → 0.5초 뒤 되풀이 ({LogLine(c, m3, "검은 잔상")})");

        // L4 배신자의 걸음
        AddPart(c, "l_betrayer_step");
        c.Madness.SetStacks(25);
        var d4 = Take(c);
        Place(c, d4, 2f, 0f);
        await Wait(0.3f, ct);
        int m4 = c.Log.Count;
        FakePerfectDodge(c, d4);
        c.Line(c.Madness.Stacks == 33 && Brand(d4) == c.Hub.BrandCap, $"L4 배신자의 걸음 ① 긴급 회피 → 광기 25 → {c.Madness.Stacks} · 피한 적 낙인 {Brand(d4)}/{c.Hub.BrandCap}");
        await Hit(c, d4, ct);
        c.Line(LogHas(c, m4, "배신자의 걸음 ②"), "L4 ② 반격 첫 공격 → 그림자 베기");
        EnterFrenzy(c, 60f);
        int ch4 = c.Relic.JudgmentCharges;
        FakePerfectDodge(c, d4);
        FakePerfectDodge(c, d4);
        c.Line(c.Relic.JudgmentCharges == ch4 + 1, $"L4 ③ 광란 중 긴급 회피 → 심판 +1 (광란당 1번: {ch4} → {c.Relic.JudgmentCharges})");
        await EndFrenzy(c, ct);

        // L5 찢긴 맹세의 검
        AddPart(c, "l_torn_oath_blade");
        c.Madness.SetStacks(35);
        var t5 = Take(c);
        Place(c, t5, 2f, 0f);
        await Wait(0.3f, ct);
        int m5 = c.Log.Count;
        for (int k = 0; k < 4; k++) { SetBrand(c, t5, 2); await Hit(c, t5, ct); await Wait(0.2f, ct); }
        await Wait(0.2f, ct);
        c.Line(LogHas(c, m5, "작은 심판") && !LogHas(c, m5, "찢은 낙인 0"), $"L5 찢긴 맹세의 검 ① 4번째 평타 → 작은 심판 · 낙인 1 찢기 ({LogLine(c, m5, "작은 심판")})");

        // L6 광기의 눈
        AddPart(c, "l_madness_eye");
        c.Madness.SetStacks(35);
        int cap35 = c.Hub.BrandCap;
        c.Madness.SetStacks(40);
        int cap40 = c.Hub.BrandCap;
        c.Madness.SetStacks(25);
        c.Line(cap35 == 5 && cap40 == 7 && c.Hub.BrandCap == 3, $"L6 광기의 눈 ①② 낙인 상한 30+ {cap35} · 40 {cap40} · 25 {c.Hub.BrandCap}");
        var t6 = Take(c);
        Place(c, t6, 2f, 0f);
        await Wait(0.3f, ct);
        SetBrand(c, t6, 2);
        await Wait(0.55f, ct);
        await Hit(c, t6, ct);           // 상한(3)에 찬다 → 다음 첫 타 경직
        await Hit(c, t6, ct);
        c.Line(t6.Status.HasCc("stun"), $"L6 ③ 상한에 찬 적 첫 타 경직 (낙인 {Brand(t6)} · 경직 {t6.Status.HasCc("stun")})");

        // L7 끝의 문턱
        AddPart(c, "l_last_threshold");
        c.Madness.SetStacks(c.Madness.MaxStacks - 1);
        var t7 = Take(c); var o7 = Take(c);
        Place(c, t7, 2f, 0f); Place(c, o7, 6f, 3f);
        await Wait(0.3f, ct);
        SetBrand(c, o7, 1);
        int g7 = c.Grudge;
        int m7 = c.Log.Count;
        await Hit(c, t7, ct);
        bool heldAtMax = !c.Madness.IsFrenzy && c.Madness.Stacks == c.Madness.MaxStacks;
        float t0 = Time.realtimeSinceStartup;
        while (!c.Madness.IsFrenzy && Time.realtimeSinceStartup - t0 < 4f) { await Hit(c, t7, ct); await Wait(0.25f, ct); }
        float waited = Time.realtimeSinceStartup - t0;
        float rem7 = c.Madness.FrenzyRemaining;
        c.Line(heldAtMax && c.Madness.IsFrenzy && waited >= 2.7f && rem7 >= c.Relic.FrenzyDuration + 2.3f,
               $"L7 끝의 문턱 ① 40에서 {waited:0.0}초 미룸 → 광란 {rem7:0.0}초 (기본 {c.Relic.FrenzyDuration:0.#})");
        c.Line(c.Grudge > g7, $"L7 ② 미루는 동안 적중 → 원한 ({g7} → {c.Grudge})");
        c.Line(Brand(o7) == c.Hub.BrandCap, $"L7 ③ 3초를 버티면 화면 안 낙인 적 상한 ({Brand(o7)}/{c.Hub.BrandCap})");
        await EndFrenzy(c, ct);

        // L8 광기의 왕관
        AddPart(c, "l_madness_crown");
        c.Madness.SetStacks(10);
        var k1 = Take(c); var k2 = Take(c); var k3 = Take(c);
        Place(c, k1, -3f, 0f);      // 뒤 — 왕관 심판(앞 콘)에 안 찢긴다
        Place(c, k2, -1.5f, 6.6f);  // 6 m 밖 · 원한 2 → 8 m 안
        Place(c, k3, 2f, 0f);       // 앞 — 왕관의 심판이 찢는다
        await Wait(0.3f, ct);
        SetBrand(c, k1, 0); SetBrand(c, k2, 0); SetBrand(c, k3, 0);
        c.Hub.Grudge.TakeAll(); c.Hub.Grudge.Add(2);
        int m8 = c.Log.Count;
        EnterFrenzy(c, 60f);
        await Wait(0.3f, ct);
        ScreenCapture.CaptureScreenshot("Temp/rgv2_lancelot_crown.png");
        c.Line(Brand(k1) >= 2, $"L8 광기의 왕관 ① 광란 진입 → 6 m 안 낙인 +2 ({Brand(k1)})");
        c.Line(Brand(k2) >= 2, $"L8 ② 원한 2 → 반경 8 m (6.8 m 적 낙인 {Brand(k2)})");
        c.Line(LogHas(c, m8, "번째 — 찢은 낙인") && c.Grudge == 0 && Brand(k3) <= 1, $"L8 ③ 원한 2를 써서 광란 시작 심판 (원한 {c.Grudge} · {LogLine(c, m8, "번째 — 찢은 낙인")})");
        await EndFrenzy(c, ct);

        // L9 끝나지 않는 광란
        AddPart(c, "l_endless_frenzy");
        int m9 = c.Log.Count;
        EnterFrenzy(c, 0.5f);
        await Wait(0.8f, ct);
        bool still = c.Madness.IsFrenzy;
        c.Line(still && LogHas(c, m9, "끝나지 않는 광란 — 이어짐(전체)"), $"L9 끝나지 않는 광란 ①② 광란이 한 번 더(전체 길이) (0.8초 뒤 광란 {still})");
        c.Line(c.Relic.JudgmentCharges >= 2, $"L9 ③ 이어지는 광란 심판 +1 ({c.Relic.JudgmentCharges})");
        await EndFrenzy(c, ct);

        // L10 피의 광란
        AddPart(c, "l_blood_frenzy");
        EnterFrenzy(c, 60f);
        var f1 = Take(c); var f2 = Take(c);
        Place(c, f1, 2f, 0f); Place(c, f2, 3.5f, 1.5f);
        await Wait(0.3f, ct);
        SetBrand(c, f1, 3);
        await Hit(c, f1, ct);
        float bleed = MonsterBleed.Remaining(f1.gameObject);
        c.Line(bleed > 0f, $"L10 피의 광란 ① 광란 중 공격 → 출혈(낙인 3배) 남은 피해 {bleed:0}");
        c.Hub.Grudge.TakeAll();
        int g10 = c.Grudge;
        SetHp(f1, 1);
        await Hit(c, f1, ct);
        await Wait(0.2f, ct);
        c.Line(MonsterBleed.Remaining(f2.gameObject) > 0f, $"L10 ② 출혈 적이 죽으면 출혈이 옮는다 ({MonsterBleed.Remaining(f2.gameObject):0})");
        c.Line(c.Grudge > g10, $"L10 ③ 출혈로 죽은 적 = 원한 +1 ({g10} → {c.Grudge})");

        // L11 광란의 걸음 (광란 이어서)
        AddPart(c, "l_frenzy_step");
        var e1 = Take(c); var e2 = Take(c);
        Place(c, e1, 1.4f, 0f); Place(c, e2, 2.8f, 0f);
        await Wait(0.3f, ct);
        SetBrand(c, e1, 0); SetBrand(c, e2, 0);
        hp = Snapshot(e1, e2);
        float atk11 = c.Atk;
        c.Player.Stamina.TryConsume(stMax * 0.75f, stMax, 30f);
        st0 = c.Player.Stamina.Current;
        c.Player.RaiseDodgeStart();
        for (int i = 0; i < 5; i++) { WarpPlayer(c, c.Player.transform.position + c.Player.transform.forward * 0.75f); await Wait(0.05f, ct); }
        await Wait(0.5f, ct);
        c.Line(c.Player.Stamina.Current >= st0 + stCost * 0.95f && hp[e1] - e1.CurrentHp >= Mathf.Floor(atk11 * 0.7f * 0.95f),
               $"L11 광란의 걸음 ① 광란 중 대시 = 베기 · 대기 없음 (게이지 {st0:0} → {c.Player.Stamina.Current:0} · 감소 {hp[e1] - e1.CurrentHp})");
        c.Line(Brand(e1) >= 1 && Brand(e2) >= 1, $"L11 ② 대시로 벤 적 낙인 +1 ({Brand(e1)} · {Brand(e2)})");
        int m11 = c.Log.Count;
        await EndFrenzy(c, ct);
        c.Line(LogHas(c, m11, "광란의 걸음 ③ — 궤적 1줄"), $"L11 ③ 광란 끝 → 대시 궤적 베기 ({LogLine(c, m11, "광란의 걸음 ③")})");
        c.Player.Stamina.Refund(stMax, stMax);

        // L12 배신의 축제
        AddPart(c, "l_betrayal_feast");
        EnterFrenzy(c, 60f);
        var q1 = Take(c); var q2 = Take(c); var q3 = Take(c); var q4 = Take(c);
        Place(c, q1, 3f, 0f); Place(c, q2, 3f, 2f); Place(c, q3, 4.5f, -1f); Place(c, q4, 2f, -2f);
        await Wait(0.3f, ct);
        SetBrand(c, q1, 2); SetBrand(c, q2, 0); SetBrand(c, q3, 0); SetBrand(c, q4, 0);
        // 상태 피해로 처치 — 평타로 죽이면 그 한 대가 4번째 평타일 때 찢긴 맹세의 검(작은 심판)이 낙인 1을 찢어 간다
        CombatQuery.DealSynergyDamage(q1, 999999f, c.Player.gameObject);
        await Wait(0.2f, ct);
        int spread = (Brand(q2) >= 2 ? 1 : 0) + (Brand(q3) >= 2 ? 1 : 0) + (Brand(q4) >= 2 ? 1 : 0);
        c.Line(q1.IsDead && spread == 3, $"L12 배신의 축제 ①② 광란 처치 → 낙인 2가 3체에 ({Brand(q2)} · {Brand(q3)} · {Brand(q4)})");

        // L13 찢는 심판 (광란 이어서)
        AddPart(c, "l_tearing_judgment");
        var j = new[] { Take(c), Take(c), Take(c), Take(c) };
        Place(c, j[0], 2f, -1.2f); Place(c, j[1], 3f, 1.2f); Place(c, j[2], 4f, -0.6f); Place(c, j[3], 5f, 0.8f);
        await Wait(0.3f, ct);
        foreach (var m in j) SetBrand(c, m, 2);
        int m13 = c.Log.Count;
        Judge(c);
        await Wait(0.4f, ct);
        ScreenCapture.CaptureScreenshot("Temp/rgv2_lancelot_threads.png");
        c.Line(Threads(c, m13) >= 3, $"L13 찢는 심판 ① 낙인 적끼리 배신의 실 ({LogLine(c, m13, "찢는 심판 —")})");
        c.Line(LogHas(c, m13, "찢는 심판 ③"), "L13 ③ 실 3줄 이상 → 막타 범위 2배");

        // L14 원한의 칼날
        AddPart(c, "l_grudge_blade");
        c.Line(c.Hub.Grudge.Cap >= 8, $"L14 원한의 칼날 ① 원한 상한 8 ({c.Hub.Grudge.Cap})");
        var u1 = Take(c); var u2 = Take(c);
        Place(c, u1, 2.5f, 0f); Place(c, u2, -4f, 3f);
        await Wait(0.3f, ct);
        SetBrand(c, u1, 3); SetBrand(c, u2, 2);
        c.Hub.Grudge.TakeAll(); c.Hub.Grudge.Add(c.Hub.Grudge.Cap);
        float frem = c.Madness.FrenzyRemaining;
        int m14 = c.Log.Count;
        Judge(c);
        await Wait(0.4f, ct);
        c.Line(Brand(u2) <= 1 && LogHas(c, m14, "원한의 칼날 ① — 원한 가득 → 콘 밖 낙인 적") && !LogHas(c, m14, "콘 밖 낙인 적 0체"), $"L14 ① 원한 가득 → 콘 밖 낙인 적에게도 막타 ({LogLine(c, m14, "원한의 칼날 ①")} · 뒤쪽 적 찢은 뒤 낙인 {Brand(u2)})");
        int x14 = MaxX(c, m14);
        c.Line(x14 > 3 && x14 <= LancelotMemoryHub.MaxXPerTear, $"L14 ② X자 = 원한 × 낙인 (상한 {LancelotMemoryHub.MaxXPerTear}) — 이번 심판 최대 X자 {x14} ({LogLine(c, m14, "찢기")})");
        c.Line(c.Madness.FrenzyRemaining > frem + 1.2f, $"L14 ③ 원한을 쓴 심판 뒤 광란 +2초 ({frem:0.0} → {c.Madness.FrenzyRemaining:0.0})");

        // L15 배신자의 낙인
        AddPart(c, "l_betrayer_brand");
        var x1 = Take(c); var x2 = Take(c);
        Place(c, x1, 2f, 0f); Place(c, x2, 9f, 4f);
        await Wait(0.3f, ct);
        SetBrand(c, x1, 2);
        SetHpRatio(x1, 0.15f);
        c.Hub.Grudge.TakeAll();
        Vector3 crackAt = x1.transform.position;
        Notify(c, x1, WeaponActionType.QSkill);   // 피해 없는 심판 적중 — 처형 판정만 본다(실제 한 타는 15% 슬라임을 먼저 죽인다)
        await Wait(0.2f, ct);
        c.Line(x1.IsDead && c.Grudge >= 2, $"L15 배신자의 낙인 ① 심판에 맞은 20% 이하 적 처형 · 낙인 → 원한 (처치 {x1.IsDead} · 원한 {c.Grudge})");
        Warp(x2, crackAt);
        SetBrand(c, x2, 0);
        await Wait(0.5f, ct);
        c.Line(Brand(x2) == c.Hub.BrandCap, $"L15 ② 처형 자리 균열 → 지나간 적 낙인 상한 ({Brand(x2)}/{c.Hub.BrandCap})");

        // L16 두 번째 심판
        AddPart(c, "l_second_judgment");
        await EndFrenzy(c, ct);
        EnterFrenzy(c, 60f);
        var v = new[] { Take(c), Take(c), Take(c) };
        Place(c, v[0], 2f, -0.8f); Place(c, v[1], 3f, 0.8f); Place(c, v[2], 4f, 0f);
        await Wait(0.3f, ct);
        foreach (var m in v) SetBrand(c, m, 3);
        c.Hub.Grudge.TakeAll();
        int m16 = c.Log.Count;
        int j16 = c.Hub.JudgmentIndex;
        Judge(c);
        c.Hub.Grudge.Add(3);   // 첫 심판의 원한 쓰기 뒤에 채운다 — 두 번째(한 방) 심판이 쓰지 않아야 남는다
        float frem16 = c.Madness.FrenzyRemaining;
        await Wait(0.7f, ct);   // 0.35초 뒤 한 방 심판
        c.Line(LogHas(c, m16, "두 번째 심판 —") && c.Hub.JudgmentIndex == j16 + 2, $"L16 두 번째 심판 ① 한 번에 낙인 6+ 찢기 → 곧바로 한 방 심판 (심판 {j16} → {c.Hub.JudgmentIndex})");
        c.Line(c.Grudge == 3, $"L16 ② 두 번째 심판은 원한을 쓰지 않는다 (원한 {c.Grudge})");
        c.Line(c.Madness.FrenzyRemaining > frem16 - 0.7f + 1.2f, $"L16 ③ 두 번째 심판 뒤 광란 +2초 ({frem16:0.0} → {c.Madness.FrenzyRemaining:0.0})");
        await EndFrenzy(c, ct);
    }

    // ── 4 타락 반응 ──────────────────────────────────────────────

    private static async UniTask ReactionsAsync(Ctx c, CancellationToken ct)
    {
        c.Sb.AppendLine("■ 타락 반응(찬란 — 룬 단계를 직접 켠다)");
        if (c.Runes == null) { c.Line(false, "룬 표를 못 읽음"); return; }
        var fx = c.Player.RuneEffects;
        var res = fx.Resources;

        // LR1 전이(번개)
        AddPart(c, "l_rx_transfer");
        c.Hub.Grudge.TakeAll();
        c.Player.RuneEffects.Clear();
        Rune(c, "ElecStatic");
        for (int i = 0; i < 10; i++) res.AddStack("ElecStatic", 10, 30f);
        c.Madness.SetStacks(25);
        var t = Take(c); var n1 = Take(c); var n2 = Take(c);
        Place(c, t, 2f, 0f); Place(c, n1, 3f, 1.8f); Place(c, n2, 3.5f, -1.8f);
        await Wait(0.6f, ct);
        SetBrand(c, n1, 0); SetBrand(c, n2, 0); SetBrand(c, t, 0);
        await Hit(c, t, ct);
        c.Line(Brand(n1) >= 1 && Brand(n2) >= 1, $"LR1 전이 ① 정전기 10 + 새긴 낙인이 2체에 튄다 ({Brand(n1)} · {Brand(n2)})");
        SetBrand(c, t, 2);
        t.ApplyDamageTakenAmp(0.08f, 3f, "shocked");
        int mr1 = c.Log.Count;
        c.Hub.Tear(t, LancelotMemoryHub.TearCause.Fragment);
        c.Line(LogHas(c, mr1, "X자 4"), $"LR1 ③ 감전 적 낙인은 두 번 찢긴다 ({LogLine(c, mr1, "찢기")})");

        // LR2 파쇄(얼음)
        AddPart(c, "l_rx_shatter");
        c.Player.RuneEffects.Clear();
        Rune(c, "IceFrost"); Rune(c, "IceFreeze");
        var i1 = Take(c);
        Place(c, i1, 2f, 0f);
        await Wait(0.6f, ct);
        SetBrand(c, i1, 0);
        i1.Status.ApplySlow("frost", 0.3f, 5f, 2); i1.Status.ApplySlow("frost", 0.3f, 5f, 2);
        await Hit(c, i1, ct);
        c.Line(Brand(i1) >= 2, $"LR2 파쇄 ① 서리 2 → 낙인 (낙인 {Brand(i1)} · 서리 {i1.Status.GetSlowStacks("frost")})");
        EnterFrenzy(c, 60f);
        SetBrand(c, i1, 2);
        i1.Status.ApplyCc("freeze", 3f);
        Face(c, i1);
        int mr2 = c.Log.Count;
        c.Hub.Grudge.TakeAll();
        Judge(c);
        await Wait(0.3f, ct);
        c.Line(LogHas(c, mr2, "낙인 2(센 4)"), $"LR2 ③ 빙결 적은 심판에서 낙인 두 배 ({LogLine(c, mr2, "찢기 " + i1.name)})");
        await EndFrenzy(c, ct);

        // LR3 부패(독)
        AddPart(c, "l_rx_decay");
        c.Player.RuneEffects.Clear();
        Rune(c, "GrassMist"); Rune(c, "GrassMistPlus");
        var p1 = Take(c);
        Place(c, p1, 3f, 0f);
        await Wait(0.4f, ct);
        SetBrand(c, p1, 2);
        var cfg = new PoisonField.Config { perTick = 1f, life = 8f, radiusMult = 1f, dotMult = 1f };
        PoisonField.SpawnAt(c.Player, p1.transform.position, cfg);
        await Wait(0.8f, ct);
        c.Line(MonsterBleed.Remaining(p1.gameObject) > 0f, $"LR3 부패 ① 독안개 안 낙인 적 출혈 ({MonsterBleed.Remaining(p1.gameObject):0})");
        PoisonField.SpawnAt(c.Player, p1.transform.position, cfg);
        c.Madness.SetStacks(25);
        SetBrand(c, p1, 0);
        await Wait(0.6f, ct);
        await Hit(c, p1, ct);
        c.Line(Brand(p1) == 2, $"LR3 ③ 겹친 안개 → 낙인 2씩 ({Brand(p1)})");
        int mr3 = c.Log.Count;
        MonsterBleed.Apply(p1.gameObject, 5f, 3f, c.Player.gameObject);
        SetHp(p1, 1);
        await Hit(c, p1, ct);
        await Wait(0.2f, ct);
        c.Line(LogHas(c, mr3, "부패 ②"), "LR3 ② 출혈 적 처치 자리 독안개");

        // LR4 낙철(불)
        AddPart(c, "l_rx_brand_iron");
        c.Player.RuneEffects.Clear();
        Rune(c, "FireEmber"); Rune(c, "FireIgnite");
        c.Madness.SetStacks(5);
        var r1 = Take(c);
        Place(c, r1, 2f, 0f);
        await Wait(0.6f, ct);
        SetBrand(c, r1, 1);
        await Wait(0.55f, ct);
        await Hit(c, r1, ct);
        c.Line(Brand(r1) == 2, $"LR4 낙철 ① 광기 5 · 잔불이 낙인 적에게 → 낙인 +1 ({Brand(r1)})");
        int g4 = c.Grudge;
        r1.Status.ApplyDot("ignite", 1f, 0.3f, 2, c.Player.gameObject);
        await Wait(1.5f, ct);
        c.Line(c.Grudge > g4, $"LR4 ③ 점화가 끝난 낙인 적 → 원한 ({g4} → {c.Grudge})");

        // LR5 폭로(빛)
        AddPart(c, "l_rx_expose");
        c.Player.RuneEffects.Clear();
        Rune(c, "LightRadiance"); Rune(c, "LightBurst");
        var l1 = Take(c); var l2 = Take(c);
        Place(c, l1, 2f, 0f); Place(c, l2, 3.5f, 0.8f);
        await Wait(0.6f, ct);
        SetBrand(c, l1, 1); SetBrand(c, l2, 1);
        c.Player.RuneEffects.Parts.NotifyHit(new HitInfo(c.Player.gameObject, l1.gameObject, l1.transform.position, l1.transform.position - c.Player.transform.position, 5f, true, WeaponActionType.GroundLight));
        var lst = RelicMarkStatus.Of(l1.gameObject, false);
        c.Line(lst != null && lst.IsExposed && lst.BrandCap >= c.Hub.BrandCap + 2, $"LR5 폭로 ① 치명 → 낙인 상한 +2 ({lst?.BrandCap})");
        SetBrand(c, l1, 1);
        int mr5 = c.Log.Count;
        c.Hub.Tear(l1, LancelotMemoryHub.TearCause.Fragment);
        c.Line(LogHas(c, mr5, "X자 2"), $"LR5 ② 폭로된 적 X자 두 줄 ({LogLine(c, mr5, "찢기")})");
        Face(c, l2);
        res.AddGauge("LightRadiance", 9, 10);
        await Wait(0.2f, ct);
        res.AddGauge("LightRadiance", 1, 10);
        await Wait(0.3f, ct);
        c.Line(Brand(l2) >= c.Hub.BrandCap, $"LR5 ③ 광폭발이 맞힌 적 낙인 상한 ({Brand(l2)}/{c.Hub.BrandCap})");

        // LR6 타락(어둠)
        AddPart(c, "l_rx_corrupt");
        c.Player.RuneEffects.Clear();
        Rune(c, "DarkErosion");
        int capBefore = c.Hub.Grudge.Cap;
        res.AddGauge("darkGauge", 60, 100);
        await Wait(0.3f, ct);
        c.Line(c.Hub.Grudge.Cap == capBefore + 3, $"LR6 타락 ① 잠식 50+ → 원한 상한 +3 ({capBefore} → {c.Hub.Grudge.Cap})");
        int gg0 = res.GetGauge("darkGauge");
        c.Hub.Grudge.TakeAll();
        c.Hub.SaveGrudge(1);
        c.Line(res.GetGauge("darkGauge") >= gg0 + 5, $"LR6 ② 원한 저축 → 잠식 +5 ({gg0} → {res.GetGauge("darkGauge")})");
        var h1 = Take(c);
        Place(c, h1, 4f, 2f);
        await Wait(0.4f, ct);
        SetBrand(c, h1, 2);
        int gh = c.Grudge;
        res.SetRegister("darkReleaseActive", 1);
        await Wait(0.3f, ct);
        res.SetRegister("darkReleaseActive", 0);
        c.Line(Brand(h1) == 0 && c.Grudge >= gh + 2, $"LR6 ③ 암흑 해방 순간 낙인 → 원한 (낙인 {Brand(h1)} · 원한 {gh} → {c.Grudge})");

        Retire(c);
        // 엇갈린 빛 — 반응 조각 6 · 위에서 서로 다른 반응이 10초 안에 여럿
        bool cross = c.Hub.HasCrossLight || c.Log.Exists(l => l.Contains("엇갈린 빛 —"));
        c.Line(cross, "엇갈린 빛 — 서로 다른 반응 둘을 10초 안 → 다음 찢기 한 번 남김 준비");
        if (!c.Hub.HasCrossLight) { c.Hub.NotifyReaction("probe_a"); c.Hub.NotifyReaction("probe_b"); }
        var z = Take(c);
        Place(c, z, 2f, 0f);
        await Wait(0.3f, ct);
        SetBrand(c, z, 2);
        c.Hub.Tear(z, LancelotMemoryHub.TearCause.Fragment);
        c.Line(Brand(z) == 2 && !c.Hub.HasCrossLight, $"엇갈린 빛 — 이번 찢기는 낙인을 남긴다 ({Brand(z)}) · 한 번 쓰면 사라짐");
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

    private static void Rune(Ctx c, string type)
    {
        if (c.Runes != null && c.Runes.TryGetValue(type, out var e)) c.Player.RuneEffects.Activate(e);
        else c.Note($"(룬 행 없음: {type})");
    }

    private static void SetEchoes(Ctx c, params string[] rungs)
    {
        var dict = typeof(PlayerLoadout).GetField("_relicEchoes", Inst)?.GetValue(c.Loadout) as Dictionary<string, int>;
        dict?.Clear();
        foreach (var r in rungs) c.Loadout.AddRelicEcho(r);
        c.Hub.RecomputeTiers();
    }

    private static void AddPart(Ctx c, string id)
    {
        Retire(c);
        FaceOpen(c.Player, c.Park);
        if (!c.Loadout.HasRelicPart(id)) c.Loadout.AddRelicPart(id, RelicMemoryGrade.Radiant);
        c.Player.RuneEffects.Parts.SyncFromLoadout(c.Loadout);
    }

    private const string HoldCc = "probe_hold";

    /// <summary>앞 항목에 쓴 몬스터를 뒤쪽 주차장으로 돌려보낸다(낙인 · 상태 비움) — 다음 항목의 반경 · 콘에 끼지 않게.</summary>
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
            SetBrand(c, m, 0);
            m.Status.Reset();
            m.Status.ApplyCc(HoldCc, 9999f);
            Warp(m, c.Park + new Vector3((i % 4) * 1.5f - 2f, 0f, (i / 4 % 4) * 1.5f - 2f));
            i++;
        }
        c.Used.Clear();
        GroundFieldBase.DespawnAll();   // 앞 항목의 독안개 · 장판이 다음 항목에 끼지 않게
    }

    private static void SetDecay(Ctx c, float delay, float rate)
    {
        typeof(MadnessStack).GetField("_decayDelay", Inst)?.SetValue(c.Madness, delay);
        typeof(MadnessStack).GetField("_decayRate", Inst)?.SetValue(c.Madness, rate);
    }

    private static void EnterFrenzy(Ctx c, float seconds)
    {
        if (c.Madness.IsFrenzy) return;
        c.Madness.SetStacks(c.Madness.MaxStacks);
        c.Madness.EnterFrenzy(seconds);
    }

    /// <summary>광란을 지금 끝낸다(끝나지 않는 광란의 재점화는 쓴 것으로).</summary>
    private static async UniTask EndFrenzy(Ctx c, CancellationToken ct)
    {
        if (!c.Madness.IsFrenzy) return;
        typeof(MadnessStack).GetField("_endlessUsed", Inst)?.SetValue(c.Madness, true);
        typeof(MadnessStack).GetField("_frenzyEnd", Inst)?.SetValue(c.Madness, Time.time - 0.01f);
        await UniTask.Yield(PlayerLoopTiming.Update, ct);
        await Wait(0.1f, ct);
    }

    /// <summary>긴급 회피 발동 신호만 낸다(슬로모 · 무적 없이) — 원인 적을 심고 Triggered를 부른다.</summary>
    private static void FakePerfectDodge(Ctx c, MonsterBase source)
    {
        var pd = typeof(PlayerController).GetProperty("PerfectDodge", Inst)?.GetValue(c.Player);
        if (pd == null) return;
        pd.GetType().GetField("_counterSource", Inst)?.SetValue(pd, source.transform);
        (pd.GetType().GetField("Triggered", Inst)?.GetValue(pd) as Action<float>)?.Invoke(1f);
    }

    private static void Judge(Ctx c) => c.Relic.PerformJudgmentStrike(c.Player.transform, 0, 1);

    /// <summary>피해 없이 유물 조각에 적중 신호만 보낸다(체력이 작은 슬라임을 죽이지 않고 판정만 볼 때).</summary>
    private static void Notify(Ctx c, MonsterBase m, WeaponActionType action)
    {
        Vector3 d = m.transform.position - c.Player.transform.position;
        c.Player.RuneEffects.Parts.NotifyHit(new HitInfo(c.Player.gameObject, m.gameObject, m.transform.position + Vector3.up, d, 1f, false, action));
    }

    private static int Brand(MonsterBase m) => m != null ? (RelicMarkStatus.Of(m.gameObject, false)?.Brand ?? 0) : -1;

    private static void SetBrand(Ctx c, MonsterBase m, int n)
    {
        if (m == null) return;
        var st = RelicMarkStatus.Of(m.gameObject, true);
        st.ConsumeBrand();
        if (n > 0) st.AddBrand(n, Mathf.Max(n, c.Hub.BrandCap), out _, true);
    }

    private static bool LogHas(Ctx c, int from, string needle)
    {
        for (int i = from; i < c.Log.Count; i++) if (c.Log[i].Contains(needle)) return true;
        return false;
    }

    private static int Threads(Ctx c, int from)
    {
        var line = LogLine(c, from, "찢는 심판 — 실 ");
        int i = line.IndexOf("실 "), j = line.IndexOf("줄");
        return i >= 0 && j > i && int.TryParse(line.Substring(i + 2, j - i - 2), out var n) ? n : -1;
    }

    /// <summary>from 뒤 찢기 줄들의 X자 최댓값(곱이 걸렸는지 — 낙인만이면 3 이하).</summary>
    private static int MaxX(Ctx c, int from)
    {
        int best = 0;
        for (int i = from; i < c.Log.Count; i++)
        {
            var m = System.Text.RegularExpressions.Regex.Match(c.Log[i], @"X자 (\d+)");
            if (c.Log[i].Contains("찢기") && m.Success && int.TryParse(m.Groups[1].Value, out int v) && v > best) best = v;
        }
        return best;
    }

    private static string LogLine(Ctx c, int from, string needle)
    {
        for (int i = c.Log.Count - 1; i >= from; i--) if (c.Log[i].Contains(needle)) return c.Log[i].Replace("[LancelotMemory] ", "");
        return "로그 없음";
    }

    private static Dictionary<MonsterBase, int> Snapshot(params MonsterBase[] ms)
    {
        var d = new Dictionary<MonsterBase, int>();
        foreach (var m in ms) if (m != null) d[m] = m.CurrentHp;
        return d;
    }

    /// <summary>플레이어 기준 앞 <paramref name="fwd"/> m · 오른쪽 <paramref name="right"/> m에 세운다(플레이어는 그대로).</summary>
    private static void Place(Ctx c, MonsterBase m, float fwd, float right)
    {
        var tf = c.Player.transform;
        Vector3 f = tf.forward; f.y = 0f; f.Normalize();
        Vector3 r = Vector3.Cross(Vector3.up, f);
        Warp(m, tf.position + f * fwd + r * right);
    }

    private static void Face(Ctx c, MonsterBase m)
    {
        Vector3 d = m.transform.position - c.Player.transform.position; d.y = 0f;
        if (d.sqrMagnitude > 0.01f) SetFacing(c.Player, Quaternion.LookRotation(d.normalized));
    }

    private static MonsterBase Take(Ctx c)
    {
        for (int i = 0; i < c.Pool.Count; i++)
        {
            var m = c.Pool[i];
            c.Pool.RemoveAt(i--);
            if (m != null && !m.IsDead && m.isActiveAndEnabled && !m.IsDamageImmuneNow) { SetHp(m, TankHp); c.Used.Add(m); return m; }
        }
        Debug.LogWarning("[RGV2랜슬롯] 몬스터가 모자란다");
        return null;
    }

    private static async UniTask FillMonstersAsync(Ctx c, CancellationToken ct)
    {
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (!m.IsDead && m.isActiveAndEnabled && m.Grade != MonsterGrade.Boss) c.Pool.Add(m);
        int need = WantMonsters + 25 - c.Pool.Count;
        // 주차장 = 플레이어 뒤 12 m — 실측은 앞에서 한다(콘 · 반경에 끼지 않게)
        var f = c.Player.transform.forward; f.y = 0f; f.Normalize();
        c.Park = c.Player.transform.position - f * 12f;
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
            m.Status.ApplyCc(HoldCc, 9999f);   // 걸어오지 않게 붙든다(경직 「stun」과 다른 이름)
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

    private static UniTask Wait(float s, CancellationToken ct)
        => UniTask.Delay(TimeSpan.FromSeconds(s), DelayType.Realtime, cancellationToken: ct);
}
#endif
