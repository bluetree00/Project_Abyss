#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Inputs;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

/// <summary>
/// [실측 · 플레이 중] 유물 성장 v2 힘 예산(설계 v2 §7) — 조각 단위 · 묶음 단위 DPS.
/// 테스트 허브에서 그 유물로 Ch1 첫 전투방까지 간 뒤 멈추고, 슬라임 3체를 앞에 묶어 두고(체력은 매 프레임 다시 채움)
/// 실제 입력 경로(평타 · Q · 4초마다 대시)로 45초씩 때려 체력 감소 합 ÷ 45 = DPS를 잰다.
/// <list type="bullet">
/// <item>기본(조각 0) · 옛 3픽 ×4(옛 v1 파츠 7종에서) · 새 4픽 ×6(봉인기 등급 65/28/7, 반응 제외) · 조각마다 찬란 단독.</item>
/// <item>기준선(§7): 봉인기 4픽 평균 ≈ 옛 3픽 평균 ±10% · 두 유물 차이 ±10%.</item>
/// <item>한계: 표적이 죽지 않아 처치형 줄(아침 사냥 · 피 냄새 · 축제 …)은 0으로 나온다 · 몹이 공격하지 않아 피격형(여명의 맹세 · 긴급 회피)도 0.</item>
/// </list>
/// 결과: Temp/relic_growth_v2_budget_&lt;relic&gt;.txt
/// </summary>
public static class RelicGrowthV2BudgetProbeEditor
{
    private const string AutoRoot   = "RelicFairy/Debug/런 구조 자동 실측/";
    private const string Monster    = "Slime/Slime";
    private const float  Settle     = 3f;
    private const float  Measure    = 45f;     // 가웨인 하루(20 + 10 + 15) 한 바퀴
    private const float  PushGap    = 0.05f;
    private const float  DodgeEvery = 4f;
    private const int    TankHp     = 50000;
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private static readonly string[] LegacyGawain   = { "gawain_burst_burn", "gawain_burn_spread", "gawain_solar_calamity", "gawain_judgment_brand", "gawain_eternal_noon", "gawain_noon_bloom", "gawain_ember_trail" };
    private static readonly string[] LegacyLancelot = { "lancelot_endless_frenzy", "lancelot_blood_price", "lancelot_blood_thirst", "lancelot_lasting_madness", "lancelot_bleed_brand", "lancelot_betrayer_brand", "lancelot_blood_feast" };

    private static string s_relic;
    private static bool   s_armed;
    private static bool   s_short;   // 짧게 — 기본 · 허브 · 옛3 · 새4 · 기본(끝)만(단독 칸 생략)

    [MenuItem("RelicFairy/Debug/유물 성장 v2/7 예산 실측 — 가웨인 (테스트 허브, 플레이 중, 약 20분)")]
    private static void BeginGawain() => Begin("Gawain");

    [MenuItem("RelicFairy/Debug/유물 성장 v2/8 예산 실측 — 랜슬롯 (테스트 허브, 플레이 중, 약 22분)")]
    private static void BeginLancelot() => Begin("Lancelot");

    [MenuItem("RelicFairy/Debug/유물 성장 v2/8s 예산 실측 짧게 — 랜슬롯 (단독 칸 생략, 플레이 중, 약 11분)")]
    private static void BeginLancelotShort() => Begin("Lancelot", true);

    private static void Begin(string relic, bool shortRun = false)
    {
        if (!Application.isPlaying) { Debug.LogWarning("[RGV2예산] 플레이 모드에서만 동작한다."); return; }
        var launcher = UnityEngine.Object.FindFirstObjectByType<TestHubLauncher>();
        if (launcher == null) { Debug.LogWarning("[RGV2예산] 테스트 허브에서 시작한다."); return; }
        var relics = typeof(TestHubLauncher).GetField("relics", Inst)?.GetValue(launcher) as RelicClassSO[];
        int idx = relics == null ? -1 : Array.FindIndex(relics, r => r != null && r.Id.ToString() == relic);
        if (idx < 0) { Debug.LogWarning($"[RGV2예산] 허브 유물 목록에 {relic}이 없다."); return; }
        typeof(TestHubLauncher).GetField("_relicIndex", Inst)?.SetValue(launcher, idx);
        s_relic = relic;
        s_short = shortRun;
        s_armed = true;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        EditorApplication.ExecuteMenuItem(AutoRoot + "시작 (테스트 허브에서, 플레이 중)");
        Debug.Log($"[RGV2예산] 런 시작 요청 — {relic}");
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
            if (player == null) { Debug.LogWarning("[RGV2예산] 런 플레이어가 없다"); return; }
            RunAsync(player, player.GetCancellationTokenOnDestroy()).Forget();
        };
    }

    // ── 실측 ────────────────────────────────────────────────────

    private sealed class Case
    {
        public string   Label;
        public string   Kind;      // 기본 · 옛3 · 새4 · 단독
        public string   Ids = "";  // "id:등급,…"
        public string[] Legacy = Array.Empty<string>();
        public float    Dps;
        public string   Diag = "";
    }

    private static async UniTaskVoid RunAsync(PlayerController player, CancellationToken ct)
    {
        bool gawain = s_relic == "Gawain";
        string outPath = $"Temp/relic_growth_v2_budget_{(gawain ? "gawain" : "lancelot")}.txt";
        var sb = new StringBuilder();
        var cases = BuildCases(gawain);
        var targets = new List<MonsterBase>();
        try
        {
            typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(player, true);
            // 자동 플레이가 대기방에서 서약을 맺는다(10-02 d6) — 서약 효과가 실측 적중 · 처치에 끼어들지 않게 비운다
            GameRunBootstrapper.Instance?.Run?.CovenantHandler?.Cleanup();
            await Wait(2.5f, ct);
            await SpawnTargetsAsync(player, targets, ct);
            sb.AppendLine($"유물 {s_relic} · 표적 슬라임 {targets.Count} · 칸당 정착 {Settle}초 + 측정 {Measure}초 · 칸 {cases.Count}");
            Debug.Log($"[RGV2예산] 시작 — {cases.Count}칸 · 예상 {cases.Count * (Settle + Measure + 1f) / 60f:0.0}분");
            for (int i = 0; i < cases.Count; i++)
            {
                var c = cases[i];
                await PrepareAsync(player, c, gawain, ct);
                c.Dps = await MeasureAsync(player, targets, c, ct);
                Debug.Log($"[RGV2예산] {i + 1}/{cases.Count} {c.Label} → DPS {c.Dps:0.0} · {c.Diag}");
            }
        }
        catch (OperationCanceledException) { sb.AppendLine("취소됨"); }
        catch (Exception e) { sb.AppendLine("예외: " + e); Debug.LogException(e); }
        finally
        {
            Report(sb, cases);
            System.IO.File.WriteAllText(outPath, sb.ToString());
            Debug.Log($"[RGV2예산] 끝 → {outPath}");
        }
    }

    private static List<Case> BuildCases(bool gawain)
    {
        var rng = new System.Random(gawain ? 7 : 11);
        var list = new List<Case>
        {
            new() { Label = "기본(조각 0)", Kind = "기본" },
            new() { Label = "허브만(조각 없이 허브)", Kind = "허브" },
        };
        var legacy = gawain ? LegacyGawain : LegacyLancelot;
        for (int k = 0; k < 4; k++)
        {
            var pick = legacy.OrderBy(_ => rng.Next()).Take(3).ToArray();
            list.Add(new Case { Label = "옛 3픽 #" + (k + 1) + " " + string.Join("+", pick.Select(Short)), Kind = "옛3", Legacy = pick });
        }
        var all = Managers.RelicParts?.GetByRelic(gawain ? "gawain" : "lancelot")?.Where(e => e.IsV2 && !e.IsReaction).Select(e => e.part_id).ToList() ?? new List<string>();
        for (int k = 0; k < 6; k++)
        {
            var pick = all.OrderBy(_ => rng.Next()).Take(4).ToArray();
            string ids = string.Join(",", pick.Select(id => id + ":" + (int)SealGrade(rng)));
            list.Add(new Case { Label = "새 4픽 #" + (k + 1) + " " + ids, Kind = "새4", Ids = ids });
        }
        if (s_short)
        {
            list.Add(new Case { Label = "기본(끝)", Kind = "기본2" });
            return list;
        }
        list.Add(new Case { Label = "기본(가운데)", Kind = "기본2" });
        foreach (var id in all)
            list.Add(new Case { Label = "단독 " + id + " 찬란", Kind = "단독", Ids = id + ":3" });
        list.Add(new Case { Label = "기본(끝)", Kind = "기본2" });
        list.Add(new Case { Label = "허브만(끝)", Kind = "허브" });
        return list;
    }

    private static string Short(string key) => key.Replace("gawain_", "").Replace("lancelot_", "");

    /// <summary>봉인기 띠 0 등급 확률 65 / 28 / 7.</summary>
    private static RelicMemoryGrade SealGrade(System.Random rng)
    {
        int r = rng.Next(100);
        return r < 65 ? RelicMemoryGrade.Faint : r < 93 ? RelicMemoryGrade.Clear : RelicMemoryGrade.Radiant;
    }

    /// <summary>칸 준비 — 조각을 비우고 허브를 치운 뒤 이 칸의 조각만 다시 붙인다. 유물 자원도 처음 상태로.</summary>
    private static async UniTask PrepareAsync(PlayerController player, Case c, bool gawain, CancellationToken ct)
    {
        var parts = player.RuneEffects.Parts;
        parts.Detach();
        if (player.TryGetComponent<GawainMemoryHub>(out var gh)) UnityEngine.Object.Destroy(gh);
        if (player.TryGetComponent<LancelotMemoryHub>(out var lh)) UnityEngine.Object.Destroy(lh);
        if (player.TryGetComponent<GrudgeStore>(out var gs)) gs.TakeAll();
        await UniTask.Yield(PlayerLoopTiming.Update, ct);
        await UniTask.Yield(PlayerLoopTiming.Update, ct);
        GroundFieldBase.DespawnAll();

        if (gawain && player.RelicBehavior is GawainZenithRelic gz && gz.Gauge != null)
        {
            var t = typeof(ZenithGauge);
            t.GetField("_phase", Inst)?.SetValue(gz.Gauge, ZenithGauge.ZPhase.Charging);
            t.GetField("_timer", Inst)?.SetValue(gz.Gauge, 0f);
            t.GetField("_holding", Inst)?.SetValue(gz.Gauge, false);
            gz.Gauge.Paused = false;
            // 정오 버프(공속 · 공격력)를 여명 상태로 다시 맞춘다 — 구간만 되돌리면 앞 칸(영원한 정오)의 정오 버프가 다음 칸까지 남았다(10-03 진단)
            typeof(GawainZenithRelic).GetMethod("RefreshBuffs", Inst)?.Invoke(gz, null);
        }
        if (!gawain && player.RelicBehavior is LancelotMadnessRelic lm && lm.Madness != null)
        {
            var t = typeof(MadnessStack);
            t.GetField("_endlessUsed", Inst)?.SetValue(lm.Madness, true);
            t.GetField("_frenzyEnd", Inst)?.SetValue(lm.Madness, Time.time - 0.01f);
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
            lm.Madness.SetStacks(0);
            lm.HoldFrenzyAtMax = false;
        }

        var loadout = AppBootstrapper.Instance?.Loadout;
        ClearParts(loadout);   // RestoreRelicParts는 더하기만 한다 — 앞 칸 조각 · 메아리를 비운다
        loadout?.RestoreRelicParts(c.Ids, "");
        if (loadout != null) parts.SyncFromLoadout(loadout);
        foreach (var key in c.Legacy) parts.AddSystemEffect(RelicPartEffectFactory.Create(key));
        if (c.Kind == "허브")
        {
            if (gawain) GawainMemoryHub.Ensure(player);
            else LancelotMemoryHub.Ensure(player);
        }
        player.Stamina?.Refund(9999f, player.RuntimeStats.MaxStamina);
        WarpPlayerHome(player);
    }

    private static void WarpPlayerHome(PlayerController p)
    {
        Vector3 pos = s_home;
        if (p.TryGetComponent<Rigidbody>(out var rb)) { rb.position = pos; rb.linearVelocity = Vector3.zero; }
        p.transform.position = pos;
    }

    /// <summary>정착 3초 뒤 45초 — 매 프레임 표적을 묶고 체력을 채우며 감소량을 더한다.</summary>
    private static async UniTask<float> MeasureAsync(PlayerController player, List<MonsterBase> targets, Case c, CancellationToken ct)
    {
        float t0 = Time.realtimeSinceStartup, nextPush = 0f, nextDodge = DodgeEvery;
        long dealt = 0;
        // 진단 — 칸 순서에 따라 값이 오르는지 가리려고 잰다
        int casts = 0, frames = 0; float scaleSum = 0f, atk0 = player.RuntimeStats.GetEffectiveAttack(AttackStatKind.Melee), spd0 = player.RuntimeStats.AttackSpeedMultiplier, atkMax = atk0;
        Action onJudg = () => casts++;
        Action<ZenithGauge.ZPhase, ZenithGauge.ZPhase> onPhase = (a, b) => { if (b == ZenithGauge.ZPhase.Noon) casts++; };
        var lm = player.RelicBehavior as LancelotMadnessRelic;
        var gz = player.RelicBehavior as GawainZenithRelic;
        if (lm != null) lm.JudgmentBegan += onJudg;
        if (gz?.Gauge != null) gz.Gauge.PhaseChanged += onPhase;
        try
        {
        var spots = Spots(player);
        while (true)
        {
            float t = Time.realtimeSinceStartup - t0;
            if (t >= Settle + Measure) break;
            for (int i = 0; i < targets.Count; i++)
            {
                var m = targets[i];
                if (m == null || m.IsDead) continue;
                int drop = TankHp - m.CurrentHp;
                if (drop > 0 && t >= Settle) dealt += drop;
                if (drop > 0) SetHp(m, TankHp);
                if ((m.transform.position - spots[i]).sqrMagnitude > 0.04f) Warp(m, spots[i]);
            }
            AimAt((spots[0] + spots[1] + spots[2]) / 3f);   // Q(심판 · 낙일)는 마우스 쪽으로 돈다 — 조준점을 표적 묶음에
            Vector3 center = (spots[0] + spots[1] + spots[2]) / 3f - player.transform.position; center.y = 0f;
            if (center.sqrMagnitude > 0.01f) player.RequestFacing(Quaternion.LookRotation(center.normalized));   // 회전은 PlayerFacing이 소유 — transform 대입은 다음 물리 스텝에 되돌아간다
            if (t >= nextPush)
            {
                nextPush = t + PushGap;
                if (player.InputBuffer.Count < 2) player.InputBuffer.Push(Command.Light);
                if (player.RelicBehavior != null && player.RelicBehavior.CanUseSkill(SkillType.Q)) player.InputBuffer.Push(Command.QSkill);
            }
            if (t >= nextDodge)
            {
                nextDodge = t + DodgeEvery;
                player.InputBuffer.Push(Command.Dodge);
            }
            // 대시가 끝난 뒤(0.6초 여유) 제자리에서 멀어져 있으면 되돌린다 — 표적 묶음 사거리 밖으로 흘러가면 그 칸 DPS가 0이 된다
            if (nextDodge - t < DodgeEvery - 0.6f)
            {
                Vector3 off = player.transform.position - s_home; off.y = 0f;
                if (off.sqrMagnitude > HomeLeash * HomeLeash) WarpPlayerHome(player);
            }
            if (t >= Settle) { frames++; scaleSum += Time.timeScale; atkMax = Mathf.Max(atkMax, player.RuntimeStats.GetEffectiveAttack(AttackStatKind.Melee)); }
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        }
        finally
        {
            if (lm != null) lm.JudgmentBegan -= onJudg;
            if (gz?.Gauge != null) gz.Gauge.PhaseChanged -= onPhase;
        }
        c.Diag = $"공격력 시작 {atk0:0} · 최고 {atkMax:0} · 공속 {spd0:0.00} · {(lm != null ? "심판" : "정오")} {casts}회 · 시간 배율 평균 {(frames > 0 ? scaleSum / frames : 1f):0.00}";
        return dealt / Measure;
    }

    private static void Report(StringBuilder sb, List<Case> cases)
    {
        var baseCase = cases.FirstOrDefault(c => c.Kind == "기본");
        float b = baseCase != null && baseCase.Dps > 0f ? baseCase.Dps : 1f;
        float Avg(string kind) { var s = cases.Where(c => c.Kind == kind && c.Dps > 0f).ToList(); return s.Count > 0 ? s.Average(c => c.Dps) : 0f; }
        float old3 = Avg("옛3"), new4 = Avg("새4");
        sb.AppendLine($"기본 {b:0.0}");
        sb.AppendLine($"옛 3픽 평균 {old3:0.0} ({(old3 / b - 1f) * 100f:+0;-0}%) · 새 4픽 평균 {new4:0.0} ({(new4 / b - 1f) * 100f:+0;-0}%) · 새4 ÷ 옛3 = {(old3 > 0f ? new4 / old3 : 0f):0.00} (기준 0.90 ~ 1.10)");
        sb.AppendLine();
        foreach (var c in cases)
            sb.AppendLine($"  {c.Kind,-3} {c.Dps,7:0.0}  {(c.Dps / b - 1f) * 100f,+5:+0;-0}%  {c.Label}  [{c.Diag}]");
    }

    // ── 표적 ────────────────────────────────────────────────────

    private static List<Vector3> s_spots;
    private static Vector3 s_home;   // 플레이어 제자리 — 칸마다 · 멀어질 때마다 여기로(대시 · 평타 전진이 쌓여 표적을 벗어났다, 10-03 2회차)
    private const float HomeLeash = 2f;

    private static List<Vector3> Spots(PlayerController player)
    {
        if (s_spots != null) return s_spots;
        var tf = player.transform;
        Vector3 f = tf.forward; f.y = 0f; f.Normalize();
        Vector3 r = Vector3.Cross(Vector3.up, f);
        s_spots = new List<Vector3>();
        foreach (var off in new[] { f * 2f, f * 2.6f - r * 1.1f, f * 2.6f + r * 1.1f })
        {
            var p = tf.position + off;
            if (NavMesh.SamplePosition(p, out var hit, 2f, NavMesh.AllAreas)) p = hit.position;
            s_spots.Add(p);
        }
        return s_spots;
    }

    private static async UniTask SpawnTargetsAsync(PlayerController player, List<MonsterBase> targets, CancellationToken ct)
    {
        s_spots = null;
        s_home = player.transform.position;
        var spots = Spots(player);
        // 방 몬스터는 치운다(표적 사이에 끼어 타격 · 연쇄를 가져간다)
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        for (int i = 0; i < spots.Count; i++)
        {
            var m = await Managers.ObjectPooler.SpawnAsync<MonsterBase>(Monster, ObjectPoolerManager.PoolType.Monster, spots[i], Quaternion.identity);
            if (m != null) targets.Add(m);
        }
        await Wait(2.5f, ct);
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 8f && targets.Exists(m => m != null && m.IsDamageImmuneNow))
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        foreach (var m in targets)
        {
            SetHp(m, TankHp);
            m.Status.ApplyCc("probe_hold", 9999f);   // 걸어오거나 때리지 않게 붙든다
        }
    }

    /// <summary>
    /// 마우스를 표적 묶음 위로 — 유물 Q는 RotateToMouse로 마우스 쪽을 본다. 사람 없이 돌리면 커서가 아무 데나 있어
    /// 심판 막타가 표적 0~3체를 맞혀 칸마다 값이 크게 흔들렸다(10-02 1회차: 기본 칸 막타 적중 1 · 0 · 1). DPS 실측 도구와 같은 방법.
    /// </summary>
    private static void AimAt(Vector3 world)
    {
        var mouse = Mouse.current;
        var cam   = Camera.main;
        if (mouse == null || cam == null) return;
        Vector3 screen = cam.WorldToScreenPoint(world + Vector3.up * 0.8f);
        if (screen.z <= 0f) return;
        InputSystem.QueueDeltaStateEvent(mouse.position, (Vector2)screen);
    }

    private static void ClearParts(PlayerLoadout l)
    {
        if (l == null) return;
        var t = typeof(PlayerLoadout);
        (t.GetField("_relicPartIds", Inst)?.GetValue(l) as System.Collections.IList)?.Clear();
        (t.GetField("_relicPartGrades", Inst)?.GetValue(l) as System.Collections.IDictionary)?.Clear();
        (t.GetField("_relicEchoes", Inst)?.GetValue(l) as System.Collections.IDictionary)?.Clear();
    }

    private static void SetHp(MonsterBase m, int hp)
    {
        var rt = typeof(MonsterBase).GetField("_runtime", Inst)?.GetValue(m) as MonsterRuntimeData;
        if (rt != null) rt.CurrentHp = hp;
    }

    private static void Warp(MonsterBase m, Vector3 pos)
    {
        if (m.TryGetComponent<NavMeshAgent>(out var ag) && ag.isActiveAndEnabled && ag.isOnNavMesh) ag.Warp(pos);
        else m.transform.position = pos;
    }

    private static UniTask Wait(float s, CancellationToken ct)
        => UniTask.Delay(TimeSpan.FromSeconds(s), DelayType.Realtime, cancellationToken: ct);
}
#endif
