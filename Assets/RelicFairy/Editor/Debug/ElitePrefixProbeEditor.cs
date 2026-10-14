#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// [실측 도구] 10-11 정예 접두어(설계서 §3-4 · §7-2).
/// ① 굴림 표(편집 모드): 시기 × 챕터 × 방 종류마다 2,000방 — 방당 접두어 수 · 상한(일반 1 · 정예방 3 · 봉인기 Ch1 정예방 1) · 같은 접두어 중복 0 ·
///    봉인기 시작 챕터 · 한 몸 최대 2(접두어 + 특성 1) · 같은 분류 금지 · 복수-지휘 한 방 금지를 센다.
/// ② 6종 실측(플레이 · 테스트 허브): 런 구조 자동 실측으로 첫 전투방에 들어가 멈추고(방 몹은 붙들어 뒤로), 접두어마다 늑대인간 정예를
///    플레이어 앞 6 m에 새로 내어 붙인 뒤 25초 동안 행동을 기록 — 로그 「[ElitePrefix]」 줄 + 예고가 시작되는 순간 사진.
///    강철은 앞 · 뒤에서 번갈아, 가시는 0.5초마다 친다. 복수는 곁에 슬라임 셋을 내어 3초마다 하나씩 쓰러뜨린다.
/// ③ 다음 정예 접두어 강제(플레이) — 스포너 굴림 길로 보려면.
/// 결과: Logs/boss_fight_sim/elite_prefix_*.txt · *.png · 로그 「[ElitePrefixProbe] 전부 끝」
/// </summary>
internal static class ElitePrefixProbeEditor
{
    private const string Root     = "RelicFairy/Monster/Elite Prefix/";
    private const string AutoRoot = "RelicFairy/Debug/런 구조 자동 실측/";
    private const string EliteKey = "Werewolf/Werewolf";
    private const string AllyKey  = "Slime/Slime";
    private const string HoldCc   = "probe_hold";
    private const float  RunFor   = 25f;
    private const int    MaxShots = 6;
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private static readonly MonsterTraitKind[] Prefixes =
    {
        MonsterTraitKind.Ironclad, MonsterTraitKind.Charger, MonsterTraitKind.Summoner,
        MonsterTraitKind.Mortar,   MonsterTraitKind.Thorns,  MonsterTraitKind.Avenger,
    };

    private static StringBuilder s_log;
    private static string s_stem;
    private static float  s_t0;
    private static int    s_shots;
    private static bool   s_armed;

    // ── ① 굴림 표 ──────────────────────────────────────────
    [MenuItem(Root + "1 Roll Table (Edit)")]
    private static void RollTable()
    {
        var sb = new StringBuilder("정예 접두어 굴림 표 — 방마다 정예 일반방 3 · 정예방 4(상한이 걸리는지 보려고 넉넉히)\n");
        int violations = 0;
        foreach (var era in new[] { StoryEra.Sealed, StoryEra.Liberated, StoryEra.NightmareMode })
        for (int ch = 1; ch <= 4; ch++)
        foreach (bool eliteRoom in new[] { false, true })
        {
            int rooms = 2000, elites = eliteRoom ? 4 : 3;
            var perRoom = new int[elites + 1];
            var byKind  = new Dictionary<MonsterTraitKind, int>();
            for (int r = 0; r < rooms; r++)
            {
                MonsterRoomLedger.Reset();
                var seen = new HashSet<MonsterTraitKind>();
                int got = 0;
                for (int e = 0; e < elites; e++)
                {
                    var p = MonsterTraitTable.RollPrefix(MonsterGrade.Elite, era, ch, eliteRoom);
                    bool avenger = p == MonsterTraitKind.Avenger || MonsterRoomLedger.HasPrefix(MonsterTraitKind.Avenger);
                    var traits = MonsterTraitTable.Roll(MonsterGrade.Elite, era, MonsterRoomLedger.Traits,
                                                        p.HasValue ? MonsterTraitTable.Of(p.Value).category : (MonsterTraitCategory?)null,
                                                        noCommander: avenger, maxCount: p.HasValue ? 1 : 2);
                    if (!p.HasValue) continue;
                    got++;
                    MonsterRoomLedger.AddPrefix(p.Value);
                    byKind[p.Value] = byKind.TryGetValue(p.Value, out var n) ? n + 1 : 1;
                    if (!seen.Add(p.Value)) violations++;                                                         // 같은 접두어 방당 1
                    if (era == StoryEra.Sealed && ch < MonsterTraitTable.Of(p.Value).sealedMinChapter) violations++;  // 봉인기 시작 챕터
                    if (traits.Count > 1) violations++;                                                           // 한 몸 최대 2
                    foreach (var t in traits)
                        if (MonsterTraitTable.Of(t).category == MonsterTraitTable.Of(p.Value).category) violations++;   // 같은 분류 금지
                    if (avenger && MonsterRoomLedger.HasTrait(MonsterTraitKind.Commander)) violations++;          // 복수-지휘 한 방 금지
                }
                int cap = era == StoryEra.Sealed && ch <= 1 ? (eliteRoom ? 1 : 0) : eliteRoom ? 3 : 1;
                if (got > cap) violations++;
                perRoom[got]++;
            }
            var kinds = new List<string>();
            foreach (var kv in byKind) kinds.Add($"{kv.Key} {kv.Value}");
            var dist = new List<string>();
            for (int i = 0; i < perRoom.Length; i++) if (perRoom[i] > 0) dist.Add($"{i}개 {perRoom[i] * 100f / rooms:0.#}%");
            sb.AppendLine($"{era,-13} Ch{ch} {(eliteRoom ? "정예방" : "일반방")}: 방당 접두어 {string.Join(" · ", dist)} | {string.Join(" ", kinds)}");
        }
        MonsterRoomLedger.Reset();
        sb.AppendLine($"규칙 위반 {violations}건");
        string path = Stem("elite_prefix_roll") + ".txt";
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        Debug.Log($"[ElitePrefixProbe] 굴림 표 — 위반 {violations}건 · {path}\n{sb}");
    }

    [MenuItem(Root + "1 Roll Table (Edit)", true)]
    private static bool RollTableValid() => !EditorApplication.isPlaying;

    // ── ② 6종 실측 ─────────────────────────────────────────
    [MenuItem(Root + "2 Probe All via RunAuto (Play)")]
    private static void ProbeAll()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[ElitePrefixProbe] 플레이 중에만(테스트 허브)"); return; }
        s_armed = true;
        Application.logMessageReceived -= OnRunAuto;
        Application.logMessageReceived += OnRunAuto;
        EditorApplication.ExecuteMenuItem(AutoRoot + "시작 (테스트 허브에서, 플레이 중)");
    }

    // 자동 실측이 첫 전투방에 들어서면 멈추고 실측을 시작한다
    private static void OnRunAuto(string msg, string stack, LogType type)
    {
        if (!s_armed || !msg.StartsWith("[RunAuto]") || !msg.Contains("방 진입")) return;
        s_armed = false;
        Application.logMessageReceived -= OnRunAuto;
        EditorApplication.delayCall += () =>
        {
            EditorApplication.ExecuteMenuItem(AutoRoot + "중지·기록");
            RunAllAsync().Forget();
        };
    }

    private static async UniTaskVoid RunAllAsync()
    {
        var p = GameRunBootstrapper.Instance?.Run?.Player;
        if (p == null) { Debug.LogWarning("[ElitePrefixProbe] 런 플레이어가 없다"); return; }
        var ct = p.GetCancellationTokenOnDestroy();
        try
        {
            typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);
            await UniTask.Delay(5000, ignoreTimeScale: true, cancellationToken: ct);   // 방 입장 입력 잠금 · 등장 연출
            HoldRoomMonsters(p.transform);
            foreach (var k in Prefixes) await ProbeOneAsync(k, p.transform, ct);
        }
        catch (OperationCanceledException) { }
        Debug.Log("[ElitePrefixProbe] 전부 끝");
    }

    private static void HoldRoomMonsters(Transform player)
    {
        var fw = player.forward; fw.y = 0f; fw.Normalize();
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
        {
            if (m == null || m.IsDead) continue;
            m.Status.ApplyCc(HoldCc, 9999f);
            Warp(m, player.position - fw * 16f);
        }
    }

    private static async UniTask ProbeOneAsync(MonsterTraitKind kind, Transform player, System.Threading.CancellationToken ct)
    {
        s_log   = new StringBuilder();
        s_stem  = Stem($"elite_prefix_{kind}");
        s_t0    = Time.time;
        s_shots = 0;
        Application.logMessageReceived += OnLog;
        GameObject dummy = null;
        MonsterBase target = null;
        var allies = new List<MonsterBase>();
        try
        {
            var fw = player.forward; fw.y = 0f; fw = fw.sqrMagnitude > 0.01f ? fw.normalized : Vector3.forward;
            target = await Spawn(EliteKey, player.position + fw * 6f, ct);
            if (target == null) { s_log.AppendLine("정예 소환 실패"); return; }
            await UniTask.Delay(900, ignoreTimeScale: true, cancellationToken: ct);   // 초기화 · 등장 디졸브
            MonsterTraits.Attach(target, new[] { kind }, null, SummonFunc());
            if (kind == MonsterTraitKind.Avenger)
                for (int i = 0; i < 3; i++)
                {
                    var a = await Spawn(AllyKey, target.transform.position + Quaternion.Euler(0f, 120f * i, 0f) * Vector3.forward * 3f, ct);
                    if (a != null) allies.Add(a);
                }
            s_log.AppendLine($"대상 {target.name}({target.Grade}) 체력 {target.CurrentHp}/{target.EffectiveMaxHp} · 이름 「{target.DisplayName}」 · 시기 {StoryProgress.Era} · 포이즈 {(target.Poise != null ? target.Poise.MaxScale.ToString("0.#") : "없음")}");
            await UniTask.Delay(400, ignoreTimeScale: true, cancellationToken: ct);
            Shot("attach");

            dummy = new GameObject("ElitePrefixProbe_Behind");
            float end = Time.time + RunFor, nextHit = Time.time + 1.5f, nextKill = Time.time + 3f, nextAway = Time.time + 3f;
            int hits = 0, kills = 0;
            bool ranged = kind == MonsterTraitKind.Charger || kind == MonsterTraitKind.Mortar || kind == MonsterTraitKind.Summoner;
            while (Time.time < end && target != null && !target.IsDead)
            {
                // 거리 기술(돌격 3~10 m · 박격 3~18 m)은 붙어 있으면 안 나온다 — 4초마다 플레이어를 8 m 밖으로
                if (ranged && Time.time >= nextAway)
                {
                    nextAway = Time.time + 4f;
                    Vector3 away = player.position - target.transform.position; away.y = 0f;
                    away = away.sqrMagnitude > 0.01f ? away.normalized : player.forward;
                    Vector3 pos = target.transform.position + away * 8f;
                    if (NavMesh.SamplePosition(pos, out var nh, 3f, NavMesh.AllAreas)) pos = nh.position;
                    player.position = pos;
                    if (player.TryGetComponent<Rigidbody>(out var prb)) { prb.position = pos; prb.linearVelocity = Vector3.zero; }
                }
                if ((kind == MonsterTraitKind.Ironclad || kind == MonsterTraitKind.Thorns) && Time.time >= nextHit)
                {
                    nextHit = Time.time + (kind == MonsterTraitKind.Thorns ? 0.5f : 0.6f);
                    bool behind = kind == MonsterTraitKind.Ironclad && (hits % 2 == 1);
                    dummy.transform.position = target.transform.position - target.transform.forward * 2f;
                    int before = target.CurrentHp;
                    float dmg = Mathf.Max(1f, target.EffectiveMaxHp * 0.03f);
                    target.TakeDamage(dmg, behind ? dummy : player.gameObject);
                    s_log.AppendLine($"[{Time.time - s_t0,6:0.00}] 타격 {(behind ? "뒤" : "플레이어 쪽")} {dmg:0} → 실제 {before - target.CurrentHp}");
                    hits++;
                }
                if (kind == MonsterTraitKind.Avenger && Time.time >= nextKill && kills < allies.Count)
                {
                    nextKill = Time.time + 3f;
                    var a = allies[kills++];
                    if (a != null && !a.IsDead) { a.TakeSynergyDamage(a.CurrentHp + 10, null, 1f); s_log.AppendLine($"[{Time.time - s_t0,6:0.00}] 곁 아군 처치 {a.name}"); }
                }
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            float speed = target != null && target.TryGetComponent<NavMeshAgent>(out var ag) ? ag.speed : 0f;
            s_log.AppendLine($"끝 — 대상 {(target == null ? "사라짐" : target.IsDead ? "쓰러짐" : $"체력 {target.CurrentHp}")} · 이동 속도 {speed:0.00} · 공격 배율 {(target != null ? target.AttackMultiplierNow : 0f):0.00}");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) { s_log.AppendLine("예외: " + e); }
        finally
        {
            Application.logMessageReceived -= OnLog;
            if (dummy != null) UnityEngine.Object.Destroy(dummy);
            if (target != null && !target.IsDead) target.TakeSynergyDamage(target.CurrentHp + 10, null, 1f);   // 다음 접두어 전에 치운다(사망 디졸브)
            foreach (var a in allies) if (a != null && !a.IsDead) a.TakeSynergyDamage(a.CurrentHp + 10, null, 1f);
            File.WriteAllText(s_stem + ".txt", s_log.ToString(), Encoding.UTF8);
            Debug.Log($"[ElitePrefixProbe] 끝 — {s_stem}.txt");
        }
        await UniTask.Delay(1500, ignoreTimeScale: true, cancellationToken: ct);
    }

    // ── ③ 스포너 굴림 강제 ──────────────────────────────────
    [MenuItem(Root + "3 Force Next Prefix - Charger (Play)")] private static void F1() => Force(MonsterTraitKind.Charger);
    [MenuItem(Root + "3 Force Next Prefix - Mortar (Play)")]  private static void F3() => Force(MonsterTraitKind.Mortar);
    [MenuItem(Root + "3 Force Next Prefix - Off (Play)")]
    private static void FOff() { MonsterTraitTable.DebugForcePrefix = null; Debug.Log("[ElitePrefixProbe] 강제 끔"); }

    private static void Force(MonsterTraitKind k)
    {
        MonsterTraitTable.DebugForcePrefix = k;
        Debug.Log($"[ElitePrefixProbe] 다음 정예 접두어 강제 = {k}");
    }

    // ── 도우미 ─────────────────────────────────────────────
    private static void OnLog(string msg, string stack, LogType type)
    {
        if (s_log == null) return;
        bool prefixLine = msg.StartsWith("[ElitePrefix]");
        if (!prefixLine && type != LogType.Exception && type != LogType.Error) return;
        s_log.AppendLine($"[{Time.time - s_t0,6:0.00}] {msg.Split('\n')[0]}");
        if (prefixLine && (msg.Contains("예고 시작") || msg.Contains("의식 시작") || msg.Contains("복수 ") || msg.Contains("정면 막음")))
            Shot(msg.Contains("돌격") ? "charge" : msg.Contains("박격") ? "mortar" : msg.Contains("가시") ? "thorns"
               : msg.Contains("소환") ? "summon" : msg.Contains("강철") ? "ironclad" : "avenge");
    }

    private static void Shot(string tag)
    {
        if (s_shots >= MaxShots) return;
        ScreenCapture.CaptureScreenshot($"{s_stem}_{++s_shots}_{tag}.png");
    }

    private static string Stem(string name)
    {
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "boss_fight_sim");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{name}_{DateTime.Now:MMdd_HHmmss}");
    }

    private static async UniTask<MonsterBase> Spawn(string key, Vector3 pos, System.Threading.CancellationToken ct)
    {
        if (NavMesh.SamplePosition(pos, out var hit, 4f, NavMesh.AllAreas)) pos = hit.position;
        try { return await Managers.ObjectPooler.SpawnAsync<MonsterBase>(key, ObjectPoolerManager.PoolType.Monster, pos, Quaternion.identity); }
        catch (Exception e) { s_log?.AppendLine($"  (소환 실패 {key}: {e.Message})"); return null; }
    }

    private static void Warp(MonsterBase m, Vector3 pos)
    {
        if (NavMesh.SamplePosition(pos, out var hit, 4f, NavMesh.AllAreas)) pos = hit.position;
        if (m.TryGetComponent<NavMeshAgent>(out var ag) && ag.isActiveAndEnabled && ag.isOnNavMesh) ag.Warp(pos);
        else m.transform.position = pos;
    }

    private static Func<Vector3, UniTask<MonsterBase>> SummonFunc()
    {
        var spawner = UnityEngine.Object.FindFirstObjectByType<MonsterSpawner>();
        var mi = typeof(MonsterSpawner).GetMethod("SpawnSummonedMinionAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        if (spawner == null || mi == null) return null;
        return (Func<Vector3, UniTask<MonsterBase>>)Delegate.CreateDelegate(typeof(Func<Vector3, UniTask<MonsterBase>>), spawner, mi);
    }
}
#endif
