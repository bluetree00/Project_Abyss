using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Inputs;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using RelicFairy.Monster;

/// <summary>
/// [실측 도구 · 플레이 중] 유물 기본 기능과 보스 클리어 파츠(개화) 14종이 <b>실제로 작동하는지</b> 한 런 안에서 센다(09-25 사용자 요청).
///
/// 흐름: 테스트 허브에서 유물을 골라 런 구조 자동 실측으로 Ch1 첫 전투방까지 간 뒤(몬스터를 죽이기 전에) 멈추고,
/// 몬스터가 모자라면 슬라임을 불러 채운 다음 항목마다 조건을 만들어 결과를 잰다.
/// 파츠는 로드아웃에 하나씩 더하며(실제 드래프트와 같은 경로) 누적된다 — 서로 간섭이 적은 순서로 잰다.
///
/// ⚠️ 시험용 조작(결과 해석에 필요): 정오 게이지 시간(충전 20 · 정오 10 · 황혼 15초)을 3·4·3초로 줄이고,
/// 몬스터 체력을 반사로 직접 낮추며, 몬스터를 플레이어 앞으로 옮긴다. 판정·효과 코드는 건드리지 않는다.
/// 결과: Temp/relic_parts_probe_{유물}.txt
/// </summary>
public static class RelicPartsProbeEditor
{
    private const string AutoRoot  = "RelicFairy/Debug/런 구조 자동 실측/";
    private const string ExtraMonster = "Slime/Slime";
    private const int    WantMonsters = 10;
    private const int    TankHp       = 50000;
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private static string s_relic;
    private static bool   s_armed;

    [MenuItem("RelicFairy/Debug/유물·파츠 실측 — 가웨인 (테스트 허브, 플레이 중)")]
    private static void Gawain() => Begin("Gawain");

    [MenuItem("RelicFairy/Debug/유물·파츠 실측 — 랜슬롯 (테스트 허브, 플레이 중)")]
    private static void Lancelot() => Begin("Lancelot");

    private static void Begin(string relic)
    {
        if (!Application.isPlaying) { Debug.LogWarning("[유물실측] 플레이 모드에서만 동작한다."); return; }
        var launcher = UnityEngine.Object.FindFirstObjectByType<TestHubLauncher>();
        if (launcher == null) { Debug.LogWarning("[유물실측] 테스트 허브에서 시작한다."); return; }

        var relics = typeof(TestHubLauncher).GetField("relics", Inst)?.GetValue(launcher) as RelicClassSO[];
        int idx = relics == null ? -1 : Array.FindIndex(relics, r => r != null && r.Id.ToString() == relic);
        if (idx < 0) { Debug.LogWarning($"[유물실측] 허브 유물 목록에 {relic}이 없다."); return; }
        typeof(TestHubLauncher).GetField("_relicIndex", Inst)?.SetValue(launcher, idx);

        s_relic = relic;
        s_armed = true;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        EditorApplication.ExecuteMenuItem(AutoRoot + "시작 (테스트 허브에서, 플레이 중)");
        Debug.Log($"[유물실측] {relic} 런 시작 요청");
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
            if (player == null) { Debug.LogWarning("[유물실측] 런 플레이어가 없다"); return; }
            RunAsync(player, s_relic, player.GetCancellationTokenOnDestroy()).Forget();
        };
    }

    // ── 공통 ────────────────────────────────────────────────────────
    private sealed class Ctx
    {
        public PlayerController Player;
        public StringBuilder Sb = new();
        public List<MonsterBase> Pool = new();
        public int Pass, Fail;
        public void Line(bool ok, string text) { Sb.AppendLine($"  {(ok ? "✅" : "❌")} {text}"); if (ok) Pass++; else Fail++; }
        public MonsterBase Take()
        {
            for (int i = 0; i < Pool.Count; i++)
            {
                var m = Pool[i];
                Pool.RemoveAt(i--);
                if (m != null && !m.IsDead && m.isActiveAndEnabled && !m.IsDamageImmuneNow) return m;
            }
            return null;
        }
    }

    private static async UniTaskVoid RunAsync(PlayerController player, string relic, CancellationToken ct)
    {
        var c = new Ctx { Player = player };
        string path = $"Temp/relic_parts_probe_{relic}.txt";
        ProbeOutput.Begin(path, "유물실측");
        try
        {
            typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(player, true);
            await Wait(2.5f, ct);                                 // 방 입장 연출·몬스터 등장
            await FillMonstersAsync(c, ct);
            c.Sb.AppendLine($"유물 {relic} · 대상 몬스터 {c.Pool.Count}마리 · 유물 동작 {player.RelicBehavior?.GetType().Name ?? "없음"}");

            if (relic == "Gawain") await GawainAsync(c, ct);
            else                   await LancelotAsync(c, ct);
        }
        catch (OperationCanceledException) { c.Sb.AppendLine("취소됨"); }
        catch (Exception e) { c.Sb.AppendLine("예외: " + e); }

        c.Sb.Insert(0, $"통과 {c.Pass} · 실패 {c.Fail}\n");
        ProbeOutput.Write(path, "유물실측", c.Sb.ToString());
        Debug.Log("[유물실측] 결과\n" + c.Sb);
    }

    // ── 가웨인 ──────────────────────────────────────────────────────
    private static async UniTask GawainAsync(Ctx c, CancellationToken ct)
    {
        var relic = c.Player.RelicBehavior as GawainZenithRelic;
        c.Sb.AppendLine("■ 기본 기능");
        c.Line(relic != null, "유물 동작 = 가웨인(정오의 맹세)");
        if (relic == null) return;
        var gauge = relic.Gauge;
        SetGaugeTimes(gauge, 3f, 4f, 3f);

        var a = c.Take();
        await Hit(c, a, WeaponActionType.GroundLight, ct);
        c.Line(BurnOf(a) > 0f, $"태양의 열기 — 적중 시 화상 부여 (남은 화상 {BurnOf(a):F2}초)");

        var seen = new List<string>();
        float t0 = Time.realtimeSinceStartup;
        var last = gauge.CurrentPhase;
        seen.Add($"{last}");
        while (Time.realtimeSinceStartup - t0 < 14f && seen.Count < 4)
        {
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
            if (gauge.CurrentPhase != last) { last = gauge.CurrentPhase; seen.Add($"{last}@{Time.realtimeSinceStartup - t0:F1}s"); }
        }
        c.Line(seen.Exists(s => s.StartsWith("Noon")) && seen.Exists(s => s.StartsWith("Cooldown")),
               "정오 게이지 순환 (시험용 3·4·3초) — " + string.Join(" → ", seen));

        await QAsync(c, a, ct, "태양강림(Q) — 적 피해");

        c.Sb.AppendLine("■ 파츠(개화)");
        // 불사르기: 화상 입은 적을 직접 치면 남은 화상의 절반이 터진다.
        // 파츠 없이 한 대 / 파츠 켜고 한 대의 체력 감소량 차이로 잰다.
        // 09-25 수정 전엔 같은 타격의 기본 패시브(태양의 열기)가 화상을 곧바로 다시 채워 소모가 없었다 → 남은 화상이 실제로 줄었는지도 본다.
        var b = c.Take();
        await Hit(c, b, WeaponActionType.GroundLight, ct);                       // 화상 심기
        int hA0 = b != null ? b.CurrentHp : 0;
        await Hit(c, b, WeaponActionType.GroundLight, ct);                       // 파츠 없이 한 대
        int dropNoPart = hA0 - (b != null ? b.CurrentHp : 0);
        AddPart(c, "gawain_beh_burst_burn");
        float r0 = BurnOf(b); int hB0 = b != null ? b.CurrentHp : 0;
        await Hit(c, b, WeaponActionType.GroundLight, ct);                       // 파츠 켜고 한 대
        int dropPart = hB0 - (b != null ? b.CurrentHp : 0);
        float r1 = BurnOf(b);
        c.Line(b != null && dropPart > dropNoPart + 1,
               $"불사르기 — 한 대 체력 감소 파츠 없음 {dropNoPart} → 파츠 켬 {dropPart} (터진 화상 ≈ {dropPart - dropNoPart})");
        c.Line(b != null && r0 > 0f && r1 < r0 * 0.75f,
               $"불사르기 소모 — 남은 화상 {r0:F2} → {r1:F2}초 (절반쯤으로 줄어야 한다 · 같은 타격의 재부여가 되채우면 실패)");

        // 화상 전염: 화상 걸린 적이 죽으면 근처 적에게 화상이 옮는다
        AddPart(c, "gawain_eff_burn_spread");
        var d = TakeUnburnt(c);
        await Wait(0.05f, ct);   // 걷어낸 화상 컴포넌트가 프레임 끝에 실제로 사라진 뒤(3차 실측에서 전염분까지 같이 지워졌다)
        PlaceNear(d, b, 1.5f);
        float dBefore = BurnOf(d);
        // 전염은 「4m 안 가장 가까운 한 마리」다 — 붐비는 방에선 이쪽이 고른 이웃이 아닌 적에게 옮는다(09-25 재실측에서 거짓 실패).
        var burnedBefore = LivingWhere(m => BurnOf(m) > 0f);
        Kill(c, b);
        await Wait(0.4f, ct);
        int newlyBurned = CountNew(LivingWhere(m => BurnOf(m) > 0f), burnedBefore, b);
        c.Line(newlyBurned > 0, $"화상 전염 — 새로 불붙은 적 {newlyBurned}마리 (고른 이웃 {dBefore:F2} → {BurnOf(d):F2}초)");

        // 잔염의 검흔: 근접 적중 지점에 불씨 지대
        AddPart(c, "gawain_beh_ember_trail");
        int ff0 = FireFields();
        await Hit(c, d, WeaponActionType.GroundLight, ct);
        await Wait(0.3f, ct);
        c.Line(FireFields() > ff0, $"잔염의 검흔 — 불씨 지대 {ff0} → {FireFields()}개 (설명은 「검흔이 사라진 자리」, 구현은 적중 지점)");

        // 정오의 개화: 정오 진입 순간 발밑에 화상 지대
        AddPart(c, "gawain_trg_noon_bloom");
        int ff1 = FireFields();
        bool noonEntered = await WaitPhase(gauge, ZenithGauge.ZPhase.Noon, 12f, ct);
        await Wait(0.3f, ct);
        c.Line(noonEntered && FireFields() > ff1, $"정오의 개화 — 정오 진입 {noonEntered} · 불씨 지대 {ff1} → {FireFields()}개");

        // 심판의 낙인(코어): 화상 쌓인 적이 체력 20% 이하면 즉시 처형
        AddPart(c, "gawain_core_judgment_brand");
        var e = c.Take();
        await Hit(c, e, WeaponActionType.GroundLight, ct);
        SetHpRatio(e, 0.15f);
        await Hit(c, e, WeaponActionType.GroundLight, ct);
        await Wait(0.2f, ct);
        c.Line(e != null && e.IsDead, $"심판의 낙인 — 화상 + 체력 15% 적 처형 {(e == null ? "(대상 없음)" : e.IsDead.ToString())}");

        // 태양의 재앙(코어): 화상 걸린 적이 죽으면 쌓인 화상 피해가 주변에 번진다
        AddPart(c, "gawain_core_solar_calamity");
        var f = c.Take(); var g = c.Take();
        await Hit(c, f, WeaponActionType.GroundLight, ct);
        PlaceNear(g, f, 2f);
        int gh0 = g != null ? g.CurrentHp : 0;
        Kill(c, f);
        await Wait(0.4f, ct);
        c.Line(g != null && f != null && g.CurrentHp < gh0, $"태양의 재앙{(g == null || f == null ? " (대상 없음)" : "")} — 이웃 체력 {gh0} → {(g != null ? g.CurrentHp : 0)}");

        // 영원한 정오(코어): 황혼을 지워 정오 상시
        AddPart(c, "gawain_core_eternal_noon");
        await WaitPhase(gauge, ZenithGauge.ZPhase.Noon, 12f, ct);
        await Wait(4f + 1.5f, ct);                               // 시험용 정오 4초 + 여유
        c.Line(gauge.IsNoon, $"영원한 정오 — 정오 시간이 지나도 정오 유지 ({gauge.CurrentPhase})");
    }

    // ── 랜슬롯 ──────────────────────────────────────────────────────
    private static async UniTask LancelotAsync(Ctx c, CancellationToken ct)
    {
        var relic = c.Player.RelicBehavior as LancelotMadnessRelic;
        c.Sb.AppendLine("■ 기본 기능");
        c.Line(relic != null, "유물 동작 = 랜슬롯(찢긴 서약의 검)");
        if (relic == null) return;
        var mad = relic.Madness;

        var a = c.Take();
        int s0 = mad.Stacks;
        for (int i = 0; i < 3; i++) await Hit(c, a, WeaponActionType.GroundLight, ct);
        c.Line(mad.Stacks > s0, $"광기 — 적중마다 스택 {s0} → {mad.Stacks} / {mad.MaxStacks}");

        mad.SetStacks(mad.MaxStacks - 1);
        await Hit(c, a, WeaponActionType.GroundLight, ct);
        c.Line(mad.IsFrenzy, $"광란 — 스택 최대 도달 시 진입 (남은 {mad.FrenzyRemaining:F1}초)");

        // 광란의 학살(기본 패시브): 광란 중 처치 → 연장
        float fr0 = mad.FrenzyRemaining;
        Kill(c, c.Take());
        await Wait(0.1f, ct);
        c.Line(mad.FrenzyRemaining > fr0 + 0.25f, $"광란의 학살(기본) — 처치 시 광란 {fr0:F2} → {mad.FrenzyRemaining:F2}초");

        await QAsync(c, a, ct, "심판의 일격(Q) — 적 피해");

        c.Sb.AppendLine("■ 파츠(개화)");
        await WaitFrenzy(mad, false, 12f, ct);

        // 출혈의 낙인: 심판의 일격에 맞은 적 출혈
        AddPart(c, "lancelot_eff_bleed_brand");
        // Q 조준이 이 대상을 안 칠 때가 있어(3차 진단: 체력 변화 0) Q 판정 한 방을 직접 넣어 파츠 조건만 본다.
        var b = c.Take();
        int qh0 = b != null ? b.CurrentHp : 0;
        await Hit(c, b, WeaponActionType.QSkill, ct);
        await Wait(0.3f, ct);
        c.Sb.AppendLine($"    (진단) 심판의 일격 대상 {(b != null ? b.name : "없음")} 체력 {qh0} → {(b != null ? b.CurrentHp : 0)} · 출혈 {BleedOf(b):F1}");
        c.Line(b != null && !b.IsDead && BleedOf(b) > 0f, $"출혈의 낙인{(b == null || b.IsDead ? " (대상 없음·사망)" : "")} — 심판의 일격 뒤 남은 출혈 피해 {BleedOf(b):F1}");

        // 피의 대가: 처치 시 광기 스택 추가(광란 아님)
        await WaitFrenzy(mad, false, 12f, ct);
        AddPart(c, "lancelot_trg_blood_price");
        int p0 = mad.Stacks;
        Kill(c, c.Take());
        await Wait(0.1f, ct);
        c.Line(mad.Stacks > p0, $"피의 대가 — 처치 시 스택 {p0} → {mad.Stacks}");

        // 식지 않는 광기: 광란이 끝나도 스택 절반 유지
        AddPart(c, "lancelot_beh_lasting_madness");
        mad.SetStacks(mad.MaxStacks - 1);
        await Hit(c, b, WeaponActionType.GroundLight, ct);
        await WaitFrenzy(mad, false, 12f, ct);
        c.Line(mad.Stacks >= mad.MaxStacks / 2 - 1, $"식지 않는 광기 — 광란 종료 후 스택 {mad.Stacks} / {mad.MaxStacks}");

        // 피의 갈증: 광란 중 처치마다 광란 연장(기본 패시브 0.5초 + 파츠 1.2초)
        AddPart(c, "lancelot_beh_blood_thirst");
        mad.SetStacks(mad.MaxStacks - 1);
        await Hit(c, b, WeaponActionType.GroundLight, ct);
        float t0 = mad.FrenzyRemaining;
        Kill(c, c.Take());
        await Wait(0.1f, ct);
        float gain = mad.FrenzyRemaining - t0 + 0.1f;
        c.Line(gain > 1.0f, $"피의 갈증 — 처치 한 번에 광란 +{gain:F2}초 (기본 패시브 0.5 + 파츠 1.2 기대) · ⚠ 기본 패시브와 같은 기능");

        // 피의 만찬(코어, 출혈의 낙인 필요): 광란 중 출혈 적 처치 → 주변에 출혈
        AddPart(c, "lancelot_core_blood_feast");
        var e = c.Take(); var f = c.Take();
        if (e != null) MonsterBleed.Apply(e.gameObject, 5f, 6f, c.Player.gameObject);
        c.Sb.AppendLine($"    (진단) 출혈 직접 부여 직후 남은 출혈 피해 {BleedOf(e):F1} · 대상 {(e != null ? e.name : "없음")}");
        PlaceNear(f, e, 2f);
        float fb0 = BleedOf(f);
        if (!mad.IsFrenzy) { mad.SetStacks(mad.MaxStacks - 1); await Hit(c, b, WeaponActionType.GroundLight, ct); }
        var bledBefore = LivingWhere(m => BleedOf(m) > 0f);
        Kill(c, e);
        await Wait(0.3f, ct);
        float fb1 = BleedOf(f);
        int newlyBled = CountNew(LivingWhere(m => BleedOf(m) > 0f), bledBefore, e);
        c.Line(newlyBled > 0, $"피의 만찬 — 새로 출혈 걸린 적 {newlyBled}마리 (고른 이웃 {fb0:F1} → {fb1:F1} · 광란 {mad.IsFrenzy})");

        // 배신자의 낙인(코어): 심판의 일격에 맞은 적이 체력 25% 이하면 처형
        AddPart(c, "lancelot_core_betrayer_brand");
        var g = c.Take();
        SetHpRatio(g, 0.2f);
        await QAsync(c, g, ct, null);
        c.Line(g != null && g.IsDead, $"배신자의 낙인 — 체력 20% 적 처형 {(g == null ? "(대상 없음)" : g.IsDead.ToString())}");

        // 끝나지 않는 광란(코어): 광란이 끝날 때 한 번 더(한 번만).
        AddPart(c, "lancelot_core_endless_frenzy");
        await WaitFrenzy(mad, false, 14f, ct);
        mad.SetStacks(mad.MaxStacks - 1);
        await Hit(c, b, WeaponActionType.GroundLight, ct);
        float firstLeft = mad.FrenzyRemaining;
        await Wait(firstLeft + 0.6f, ct);                          // 원래 끝났어야 할 시각을 지나서
        bool reignited = mad.IsFrenzy;
        float secondLeft = mad.FrenzyRemaining;
        await Wait(secondLeft + 1.0f, ct);                         // 이어진 광란도 끝날 시각을 지나서
        bool endsOnce = !mad.IsFrenzy;
        c.Line(reignited && endsOnce, $"끝나지 않는 광란 — 원래 종료 뒤에도 광란 {reignited}(남은 {secondLeft:F1}초) · 두 번째는 끝남 {endsOnce}");
    }

    // ── 도우미 ──────────────────────────────────────────────────────
    private static async UniTask FillMonstersAsync(Ctx c, CancellationToken ct)
    {
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (!m.IsDead && m.isActiveAndEnabled && m.Grade != MonsterGrade.Boss) c.Pool.Add(m);
        int need = WantMonsters - c.Pool.Count;
        var origin = c.Player.transform.position;
        for (int i = 0; i < need; i++)
        {
            float ang = i * Mathf.PI * 2f / Mathf.Max(1, need);
            var pos = origin + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 5f;
            if (NavMesh.SamplePosition(pos, out var hit, 4f, NavMesh.AllAreas)) pos = hit.position;
            try
            {
                var m = await Managers.ObjectPooler.SpawnAsync<MonsterBase>(ExtraMonster, ObjectPoolerManager.PoolType.Monster, pos, Quaternion.identity);
                if (m != null) c.Pool.Add(m);
            }
            catch (Exception e) { c.Sb.AppendLine("  (몬스터 추가 실패: " + e.Message + ")"); break; }
        }
        await Wait(2.5f, ct);   // 설정 로드(비동기 Init)

        // 방에 막 들어온 몬스터는 등장 연출 동안 무적이다 — 첫 실측(09-25)에서 피해 0이 나와 화상 패시브가 안 붙은 원인.
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 8f && c.Pool.Exists(m => m != null && m.IsDamageImmuneNow))
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        c.Pool.RemoveAll(m => m == null || m.IsDead || m.IsDamageImmuneNow);
        // 범위 공격(태양강림·불 장판)에 시험 대상이 먼저 죽지 않게 체력을 크게 잡는다. 처형 시험만 비율로 따로 낮춘다.
        foreach (var m in c.Pool) SetHp(m, TankHp);
    }

    private static async UniTask Hit(Ctx c, MonsterBase m, WeaponActionType action, CancellationToken ct)
    {
        if (m == null || m.IsDead) return;
        CombatDamage.Deal(new CombatDamage.Request
        {
            Target              = m.gameObject,
            BaseDamage          = 3f,
            Owner               = c.Player.gameObject,
            ActionType          = action,
            KnockbackMultiplier = 0f,
            HitPoint            = m.transform.position + Vector3.up,
            SourcePosition      = c.Player.transform.position,
        });
        await Wait(0.15f, ct);
    }

    private static void Kill(Ctx c, MonsterBase m)
    {
        if (m == null || m.IsDead) return;
        m.TakeDamage(m.CurrentHp * 10f + 100000f, c.Player.gameObject, 0f);   // 플레이어 처치 경로(처치 신호가 파츠까지 간다)
    }

    private static async UniTask QAsync(Ctx c, MonsterBase target, CancellationToken ct, string label)
    {
        if (target == null) { if (label != null) c.Line(false, label + " — 대상 없음"); return; }
        // 플레이어 정면 3m로 옮긴다(심판의 일격 6m·반각 45°)
        var p = c.Player.transform;
        Warp(target, p.position + p.forward * 3f);
        await Wait(0.2f, ct);
        int h0 = target.CurrentHp;
        // 옮긴 자리가 벽·기둥이면 내비메시가 대상을 다른 곳에 내려놓는다 — 대상 하나가 아니라 맞은 적 전체로 본다.
        var hpBefore = new Dictionary<MonsterBase, int>();
        foreach (var m in LivingWhere(_ => true)) hpBefore[m] = m.CurrentHp;
        c.Player.CooldownTracker.ResetCooldown(SkillType.Q);
        c.Player.InputBuffer.Clear();
        c.Player.InputBuffer.Push(Command.QSkill);
        // Q는 시작할 때 마우스 쪽으로 돈다 — 자동 실측에선 마우스가 아무 데나 있어 대상이 등 뒤가 된다(09-25 재실측: 11타 전부 적중 0).
        // 돈 뒤에 대상을 다시 정면으로 옮긴다.
        await Wait(0.12f, ct);
        Warp(target, p.position + p.forward * 3f);
        await Wait(0.58f, ct);
        // Q 도중 화면 — 전용 검·이펙트 눈 확인용(09-25)
        string relicName = c.Player.RelicClass != null ? c.Player.RelicClass.name : "none";
        ScreenCapture.CaptureScreenshot($"Temp/relic_q_{relicName}.png");
        if (relicName.Contains("Lancelot"))
        {
            // 전용 검은 캐릭터 팩의 손 뼈(add_weapon_r)에 붙는다 — JudgmentStrikeRuntime.QSwordBone
            var bone  = FindDeep(c.Player.transform, "add_weapon_r");
            bool held = false;
            if (bone != null)
                for (int i = 0; i < bone.childCount; i++)
                    if (bone.GetChild(i).name.StartsWith("Lancelot_QSword") && bone.GetChild(i).gameObject.activeInHierarchy) held = true;
            CaptureCloseUp(c.Player.transform, $"Temp/relic_q_{relicName}_closeup.png");
            c.Line(held, $"랜슬롯 Q 전용 검 — 손 뼈 {(bone != null ? "있음" : "없음")} · 검 {(held ? "들림" : "없음")} (근접 화면: Temp/relic_q_{relicName}_closeup.png)");
        }
        await Wait(1.8f, ct);
        int struck = 0;
        foreach (var kv in hpBefore) if (kv.Key != null && (kv.Key.IsDead || kv.Key.CurrentHp < kv.Value)) struck++;
        if (label != null) c.Line(struck > 0, $"{label} — 맞은 적 {struck}마리 (고른 대상 체력 {h0} → {target.CurrentHp}{(target.IsDead ? ", 처치" : "")})");
    }

    /// <summary>살아 있는 몬스터 중 조건을 만족하는 것들.</summary>
    private static HashSet<MonsterBase> LivingWhere(Func<MonsterBase, bool> pred)
    {
        var set = new HashSet<MonsterBase>();
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead && pred(m)) set.Add(m);
        return set;
    }

    private static int CountNew(HashSet<MonsterBase> after, HashSet<MonsterBase> before, MonsterBase exclude)
    {
        int n = 0;
        foreach (var m in after) if (m != exclude && !before.Contains(m)) n++;
        return n;
    }

    /// <summary>플레이어를 앞에서 가까이 찍는다 — 전투 카메라는 벽·기둥에 가려질 수 있다(09-25 첫 캡처가 기둥 뒤였다).</summary>
    private static void CaptureCloseUp(Transform player, string path)
    {
        const int Size = 1024;
        var go = new GameObject("~ProbeCloseUpCam");
        var rt = new RenderTexture(Size, Size, 24);
        var tex = new Texture2D(Size, Size, TextureFormat.RGB24, false);
        try
        {
            var cam = go.AddComponent<Camera>();
            Vector3 chest = player.position + Vector3.up * 1.2f;
            go.transform.position = chest + player.forward * 2.2f + player.right * 0.9f + Vector3.up * 0.3f;
            go.transform.LookAt(chest);
            cam.fieldOfView   = 40f;
            cam.nearClipPlane = 0.05f;
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            cam.targetTexture = null;
        }
        finally
        {
            UnityEngine.Object.Destroy(tex);
            rt.Release();
            UnityEngine.Object.Destroy(rt);
            UnityEngine.Object.Destroy(go);
        }
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root == null) return null;
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var hit = FindDeep(root.GetChild(i), name);
            if (hit != null) return hit;
        }
        return null;
    }

    private static void AddPart(Ctx c, string partId)
    {
        var loadout = AppBootstrapper.Instance?.Loadout;
        if (loadout == null) return;
        // AddRelicPart는 코어 첫 획득에 메타 재화(정수) 보너스를 준다 — 실측이 세이브에 재화를 쌓지 않게 보너스 없는 복원 경로로 넣는다.
        if (!loadout.HasRelicPart(partId)) loadout.RestoreRelicParts(partId);
        c.Player.RuneEffects.Parts.SyncFromLoadout(loadout.RelicPartIds);   // 드래프트 획득과 같은 활성 경로
    }

    private static float BurnOf(MonsterBase m)
        => m != null && m.TryGetComponent<MonsterBurnHandler>(out var h) ? h.Remaining : 0f;

    /// <summary>화상이 없는 몬스터 — 전염 시험의 이웃. 전부 타고 있으면 하나의 화상을 걷어낸다(시험용 초기화).</summary>
    private static MonsterBase TakeUnburnt(Ctx c)
    {
        for (int i = 0; i < c.Pool.Count; i++)
        {
            var m = c.Pool[i];
            if (m != null && !m.IsDead && !m.IsDamageImmuneNow && BurnOf(m) <= 0f) { c.Pool.RemoveAt(i); return m; }
        }
        var any = c.Take();
        if (any != null && any.TryGetComponent<MonsterBurnHandler>(out var h)) UnityEngine.Object.Destroy(h);
        return any;
    }

    private static float BleedOf(MonsterBase m) => m != null ? MonsterBleed.Remaining(m.gameObject) : 0f;

    private static int FireFields() => UnityEngine.Object.FindObjectsByType<FireField>(FindObjectsSortMode.None).Length;

    private static void SetHpRatio(MonsterBase m, float ratio)
    {
        if (m == null) return;
        var rt = typeof(MonsterBase).GetField("_runtime", Inst)?.GetValue(m) as MonsterRuntimeData;
        if (rt != null) rt.CurrentHp = Mathf.Max(1, Mathf.RoundToInt(m.EffectiveMaxHp * ratio));
    }

    private static void SetHp(MonsterBase m, int hp)
    {
        if (m == null) return;
        var rt = typeof(MonsterBase).GetField("_runtime", Inst)?.GetValue(m) as MonsterRuntimeData;
        if (rt != null) rt.CurrentHp = hp;
    }

    private static void PlaceNear(MonsterBase who, MonsterBase anchor, float dist)
    {
        if (who == null || anchor == null) return;
        Warp(who, anchor.transform.position + Vector3.right * dist);
    }

    private static void Warp(MonsterBase m, Vector3 pos)
    {
        if (NavMesh.SamplePosition(pos, out var hit, 3f, NavMesh.AllAreas)) pos = hit.position;
        if (m.TryGetComponent<NavMeshAgent>(out var ag) && ag.isActiveAndEnabled && ag.isOnNavMesh) ag.Warp(pos);
        else m.transform.position = pos;
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
            if (g.CurrentPhase == phase) return true;
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        return false;
    }

    /// <summary>광란이 원하는 상태가 될 때까지(시간 초과면 false).</summary>
    private static async UniTask<bool> WaitFrenzy(MadnessStack m, bool frenzy, float timeout, CancellationToken ct)
    {
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < timeout)
        {
            if (m.IsFrenzy == frenzy) return true;
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        return false;
    }

    private static UniTask Wait(float s, CancellationToken ct)
        => UniTask.Delay(TimeSpan.FromSeconds(s), DelayType.Realtime, cancellationToken: ct);
}
