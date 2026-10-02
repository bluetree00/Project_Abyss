using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// [실측 도구 · 플레이 중 · 전투방에서] 10-01 몬스터 수정분이 실제로 도는지 잰다(약 40초).
///   ① 기본 공격 상태 몬스터(달팽이)가 플레이어에게 피해를 주는가
///   ② 잠복 몬스터(선인장 · 성난 버섯): 멀면 잠복 유지 · 4m 안에 들면 깨는가 · 룬(시너지) 피해로도 깨는가
///   ③ 선인장 가시 반격이 플레이어에게 피해를 주는가
///   ④ 느린 몬스터(골렘 · 슬라임 · 달팽이 · 선인장)의 추격 속도
///   ⑤ HP 조건이 난이도 배율이 곱해진 최대 HP 기준으로 서는가
/// 방에 있던 몬스터는 멀리 세우고 기절시켜 둔다. 플레이어 무적은 끄되 매 실측 전에 체력을 채운다. 실측용 몬스터는 끝나면 거둔다.
/// 결과: Temp/monster_fix_probe.txt
/// </summary>
public static class MonsterFixProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private static StringBuilder s_sb;
    private static int s_pass, s_fail;
    private static PlayerController s_player;
    private static readonly List<MonsterBase> s_spawned = new();
    private static readonly Dictionary<MonsterBase, Vector3> s_pin = new();
    private static int s_playerHits;
    private static CancellationTokenSource s_cts;

    [MenuItem("RelicFairy/Debug/몬스터 동작 실측 — 잠복·피해·속도 (전투방에서)")]
    private static void Run()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[MonsterFixProbe] 플레이 모드에서만"); return; }
        s_player = GameRunBootstrapper.Instance?.Run?.Player;
        if (s_player == null) { Debug.LogWarning("[MonsterFixProbe] 런이 없다"); return; }
        s_cts?.Cancel();
        s_cts = new CancellationTokenSource();
        RunAsync(s_cts.Token).Forget();
    }

    private static async UniTaskVoid RunAsync(CancellationToken ct)
    {
        s_sb = new StringBuilder();
        s_pass = s_fail = 0;
        s_spawned.Clear(); s_pin.Clear();
        var inv = typeof(PlayerController).GetField("debugInvincible", Inst);
        bool invWas = inv != null && (bool)inv.GetValue(s_player);
        EditorApplication.update -= Pin;
        EditorApplication.update += Pin;
        try
        {
            ParkRoomMonsters();
            Vector3 p = s_player.transform.position;
            Vector3 f = s_player.transform.forward; f.y = 0f; f = f.sqrMagnitude > 0.01f ? f.normalized : Vector3.forward;

            // ① 달팽이 — 기본 공격 상태
            Head("① 기본 공격 상태(달팽이) 피해");
            inv?.SetValue(s_player, false);
            var snail = await Spawn("Snail/Snail", p + f * 1.3f, ct);
            if (snail != null)
            {
                s_pin[snail] = snail.transform.position;
                await WaitReady(snail, ct);
                int hp0 = TopUp();
                int hits0 = s_playerHits;
                await Wait(7f, ct);
                int lost = hp0 - s_player.RuntimeStats.Hp;
                Check(lost > 0, $"달팽이 곁 1.3m 7초: 플레이어 체력 −{lost} (피해 0이면 옛 결함)");
                Remove(snail);
            }
            inv?.SetValue(s_player, true);

            // ② 잠복 — 선인장 · 성난 버섯
            Head("② 잠복 해제(선인장 · 성난 버섯)");
            var cactus = await Spawn("Cactus/Cactus", p + f * 7f, ct);
            var mush   = await Spawn("MushroomAngry/MushroomAngry", p - f * 7f, ct);
            if (cactus != null) { s_pin[cactus] = cactus.transform.position; await WaitReady(cactus, ct); }
            if (mush != null)   { s_pin[mush]   = mush.transform.position;   await WaitReady(mush, ct); }
            await Wait(1.5f, ct);
            if (cactus != null) Check(Dormant(cactus), $"선인장 7m: 잠복 유지 {Yes(Dormant(cactus))}");
            if (mush != null)   Check(Dormant(mush),   $"성난 버섯 7m: 잠복 유지 {Yes(Dormant(mush))}");

            if (cactus != null)
            {
                s_pin[cactus] = OnMesh(p + f * 3.2f);   // 4m 안으로
                await Wait(1.2f, ct);
                Check(!Dormant(cactus), $"선인장 3.2m로 옮김: 깸 {Yes(!Dormant(cactus))} (문턱 4m)");
            }
            if (mush != null)
            {
                mush.TakeSynergyDamage(1f, s_player.gameObject);   // 룬 · 지속 피해 경로(피격 플래그 없음)
                await Wait(0.6f, ct);
                Check(!Dormant(mush), $"성난 버섯 7m에서 룬 피해 1: 깸 {Yes(!Dormant(mush))}");
            }

            // ③ 선인장 가시 반격
            Head("③ 선인장 가시 반격 → 플레이어");
            if (cactus != null)
            {
                inv?.SetValue(s_player, false);
                s_pin[cactus] = OnMesh(p + f * 1.6f);
                await Wait(0.8f, ct);
                int hp0 = TopUp();
                ((IDamageable)cactus).TakeDamage(1f, s_player.gameObject);
                await Wait(1.0f, ct);
                int lost = hp0 - s_player.RuntimeStats.Hp;
                Check(lost > 0, $"선인장을 한 대 침(곁 1.6m): 플레이어 체력 −{lost} (가시 피해 0이면 옛 결함)");
                inv?.SetValue(s_player, true);
            }
            if (cactus != null) Remove(cactus);
            if (mush != null)
            {
                // ⑤ HP 조건 — 유효 최대 HP 기준
                Head("⑤ HP 조건(성난 버섯 포자 50%)");
                var cond = AssetDatabase.LoadAssetAtPath<MonsterHpConditionSO>("Assets/RelicFairy/Characters/Monster/Monster/MushroomAngry/SO/MushroomAngryHpCond.asset");
                var ctx = typeof(MonsterBase).GetField("_ctx", Inst)?.GetValue(mush) as MonsterContext;
                var rt = Runtime(mush);
                if (cond != null && ctx != null && rt != null)
                {
                    int eff = mush.EffectiveMaxHp, baseMax = ctx.Stat.maxHp;
                    rt.CurrentHp = Mathf.RoundToInt(eff * (cond.threshold + 0.05f));
                    bool above = cond.Evaluate(ctx);
                    rt.CurrentHp = Mathf.RoundToInt(eff * (cond.threshold - 0.05f));
                    bool below = cond.Evaluate(ctx);
                    Check(!above && below, $"문턱 {cond.threshold:P0}: 유효 최대 HP {eff}(기본 {baseMax}) 기준 {cond.threshold + 0.05f:P0}에서 {Yes(above)} · {cond.threshold - 0.05f:P0}에서 {Yes(below)}");
                    rt.CurrentHp = eff;
                }
                else s_sb.AppendLine("  · HP 조건을 읽지 못했다");
                Remove(mush);
            }

            // ④ 추격 속도
            Head("④ 추격 속도(몬스터 → 멈춰 선 플레이어)");
            var speeds = new (string key, float expect)[]
            {
                ("Golem/Golem", 1.8f), ("Slime/Slime", 2.0f), ("Snail/Snail", 1.6f),
            };
            foreach (var (key, expect) in speeds)
            {
                var m = await Spawn(key, OnMesh(p + f * 11f), ct);
                if (m == null) continue;
                await WaitReady(m, ct);
                float best = await MeasureChase(m, 2.5f, ct);
                Check(best >= expect * 0.85f && best <= expect * 1.2f, $"{key}: 추격 속도 {best:0.00} m/s (설정 {expect:0.0})");
                Remove(m);
            }
            var cac2 = await Spawn("Cactus/Cactus", OnMesh(p + f * 3.5f), ct);   // 4m 안 — 곧바로 깨서 쫓아온다
            if (cac2 != null)
            {
                await WaitReady(cac2, ct);
                float best = await MeasureChase(cac2, 2.5f, ct);
                Check(best >= 1.7f && best <= 2.4f, $"Cactus/Cactus(깬 뒤): 추격 속도 {best:0.00} m/s (설정 2.0)");
                Remove(cac2);
            }
        }
        catch (OperationCanceledException) { s_sb.AppendLine("취소됨"); }
        catch (Exception e) { s_fail++; s_sb.AppendLine($"✗ 예외: {e.GetType().Name} {e.Message}"); Debug.LogException(e); }
        finally
        {
            EditorApplication.update -= Pin;
            foreach (var m in s_spawned.ToArray()) Remove(m);
            inv?.SetValue(s_player, invWas);
            if (s_player != null) s_player.RuntimeStats.SetHp(s_player.RuntimeStats.MaxHp);
            s_sb.Insert(0, $"몬스터 동작 실측 — 통과 {s_pass} · 실패 {s_fail}\n");
            File.WriteAllText(Path.Combine("Temp", "monster_fix_probe.txt"), s_sb.ToString());
            Debug.Log("[MonsterFixProbe] 완료 — Temp/monster_fix_probe.txt\n" + s_sb);
        }
    }

    // ── 선인장 추격 진단 ─────────────────────────────────────────

    [MenuItem("RelicFairy/Debug/몬스터 동작 실측 — 선인장 추격 진단 (전투방에서)")]
    private static void RunCactusDiag()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[CactusDiag] 플레이 모드에서만"); return; }
        s_player = GameRunBootstrapper.Instance?.Run?.Player;
        if (s_player == null) { Debug.LogWarning("[CactusDiag] 런이 없다"); return; }
        s_cts?.Cancel();
        s_cts = new CancellationTokenSource();
        CactusDiagAsync(s_cts.Token).Forget();
    }

    /// <summary>깬 선인장이 왜 안 쫓아오는가 — 0.5초마다 상태 · 에이전트 · 애니메이터를 적는다(3.5m 곧바로 깸 · 6m에서 맞아 깸).</summary>
    private static async UniTaskVoid CactusDiagAsync(CancellationToken ct)
    {
        s_sb = new StringBuilder();
        s_spawned.Clear(); s_pin.Clear();
        EditorApplication.update -= Pin;
        EditorApplication.update += Pin;
        try
        {
            ParkRoomMonsters();
            Vector3 p = s_player.transform.position;
            Vector3 f = s_player.transform.forward; f.y = 0f; f = f.sqrMagnitude > 0.01f ? f.normalized : Vector3.forward;
            foreach (float d in new[] { 3.5f, 6f })
            {
                var c = await Spawn("Cactus/Cactus", OnMesh(p + f * d), ct);
                if (c == null) continue;
                s_sb.AppendLine($"\n── 선인장 {d}m ──");
                s_sb.AppendLine("  " + Diag(c));
                await WaitReady(c, ct);
                if (d > 5f) ((IDamageable)c).TakeDamage(1f, s_player.gameObject);
                for (int i = 0; i < 10; i++) { s_sb.AppendLine("  " + Diag(c)); await Wait(0.5f, ct); }
                Remove(c);
            }
        }
        catch (OperationCanceledException) { s_sb.AppendLine("취소됨"); }
        catch (Exception e) { s_sb.AppendLine($"✗ 예외: {e.GetType().Name} {e.Message}"); Debug.LogException(e); }
        finally
        {
            EditorApplication.update -= Pin;
            foreach (var m in s_spawned.ToArray()) Remove(m);
            File.WriteAllText(Path.Combine("Temp", "cactus_diag.txt"), s_sb.ToString());
            Debug.Log("[CactusDiag] 완료 — Temp/cactus_diag.txt\n" + s_sb);
        }
    }

    private static readonly string[] CactusStates =
        { "Cactus_IdlePlant", "Cactus_IdleBattle", "Cactus_WalkFWD", "Cactus_RunFWD", "Cactus_Attack01", "Cactus_GetHit" };

    private static string Diag(MonsterBase m)
    {
        var fsm = typeof(MonsterBase).GetField("_fsm", Inst)?.GetValue(m) as MonsterFSM;
        var rt  = Runtime(m);
        m.TryGetComponent<NavMeshAgent>(out var a);
        var an  = m.GetComponentInChildren<Animator>();
        string anim = "-";
        if (an != null && an.runtimeAnimatorController != null)
        {
            int h = an.GetCurrentAnimatorStateInfo(0).shortNameHash;
            anim = "?" + h;
            foreach (var s in CactusStates) if (Animator.StringToHash(s) == h) anim = s;
            anim += $" spd {an.speed:0.##} root {an.applyRootMotion}";
        }
        string agent = a == null ? "없음"
            : $"en {a.enabled} on {a.isOnNavMesh} spd {a.speed:0.##} stop {a.isStopped} vel {a.velocity.magnitude:0.##} path {a.hasPath} rem {(a.hasPath ? a.remainingDistance : -1f):0.#} upd {a.updatePosition}";
        return $"t {Time.time:0.0} · 상태 {fsm?.CurrentType?.Name} · 잠복 {rt?.IsDormant} · 거리 {rt?.DistToPlayer:0.0} · 속도배율 {rt?.SpeedMultiplier:0.##} · 제약 {fsm?.CurrentConstraints} · 에이전트({agent}) · 애니({anim})";
    }

    // ── 도우미 ───────────────────────────────────────────────────
    private static void Head(string t) => s_sb.AppendLine($"\n── {t} ──");
    private static void Check(bool ok, string line) { if (ok) s_pass++; else s_fail++; s_sb.AppendLine((ok ? "✓ " : "✗ ") + line); }
    private static string Yes(bool b) => b ? "예" : "아니오";
    private static UniTask Wait(float s, CancellationToken ct) => UniTask.Delay(TimeSpan.FromSeconds(s), DelayType.DeltaTime, cancellationToken: ct);

    private static MonsterRuntimeData Runtime(MonsterBase m) => typeof(MonsterBase).GetField("_runtime", Inst)?.GetValue(m) as MonsterRuntimeData;
    private static bool Dormant(MonsterBase m) => Runtime(m)?.IsDormant ?? false;

    private static int TopUp()
    {
        var st = s_player.RuntimeStats;
        if (st.Shield > 0) st.AbsorbWithShield(st.Shield);
        st.SetHp(st.MaxHp);
        return st.Hp;
    }

    private static Vector3 OnMesh(Vector3 want)
        => NavMesh.SamplePosition(want, out var hit, 3f, NavMesh.AllAreas) ? hit.position : want;

    /// <summary>방에 있던 몬스터 — 멀리(플레이어에서 14m+) 세우고 기절시켜 실측을 흐리지 않게.</summary>
    private static void ParkRoomMonsters()
    {
        Vector3 p = s_player.transform.position;
        int i = 0;
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
        {
            if (m.IsDead) continue;
            m.HpFloorMin1 = true;
            m.ApplyStun(120f);
            float ang = (i++) * 0.7f;
            Vector3 far = OnMesh(p + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 16f);
            if (Vector3.Distance(far, p) < 12f) far = OnMesh(p + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 20f);
            s_pin[m] = far;
        }
        s_sb.AppendLine($"방 몬스터 {i}마리 멀리 세움(기절)");
    }

    private static void Pin()
    {
        if (!Application.isPlaying) { EditorApplication.update -= Pin; return; }
        foreach (var kv in s_pin)
        {
            var m = kv.Key;
            if (m == null || !m.isActiveAndEnabled) continue;
            if ((m.transform.position - kv.Value).sqrMagnitude < 0.04f) continue;
            if (m.TryGetComponent<NavMeshAgent>(out var a) && a.isActiveAndEnabled && a.isOnNavMesh) a.Warp(kv.Value);
            else m.transform.position = kv.Value;
        }
        // 플레이어가 맞은 횟수(체력 감소)를 대충 센다 — 표시용
    }

    private static async UniTask<MonsterBase> Spawn(string key, Vector3 pos, CancellationToken ct)
    {
        try
        {
            var dir = s_player.transform.position - pos; dir.y = 0f;
            var m = await Managers.ObjectPooler.SpawnAsync<MonsterBase>(key, ObjectPoolerManager.PoolType.Monster, OnMesh(pos),
                                                                        dir.sqrMagnitude > 0.01f ? Quaternion.LookRotation(dir) : Quaternion.identity);
            if (m != null) s_spawned.Add(m);
            else s_sb.AppendLine($"  · {key} 소환 실패(null)");
            return m;
        }
        catch (Exception e) { s_sb.AppendLine($"  · {key} 소환 예외: {e.Message}"); return null; }
    }

    /// <summary>설정 로드(비동기 초기화) · 등장 무적이 끝날 때까지.</summary>
    private static async UniTask WaitReady(MonsterBase m, CancellationToken ct)
    {
        float t0 = Time.realtimeSinceStartup;
        await Wait(1.5f, ct);
        while (Time.realtimeSinceStartup - t0 < 8f && m != null && (m.IsDamageImmuneNow || Runtime(m) == null)) await Wait(0.2f, ct);
    }

    /// <summary>놓아 두고 쫓아오게 한 뒤 이동한 거리 ÷ 시간의 최댓값(0.5초 구간).</summary>
    private static async UniTask<float> MeasureChase(MonsterBase m, float seconds, CancellationToken ct)
    {
        s_pin.Remove(m);
        float best = 0f;
        Vector3 last = m.transform.position;
        float tLast = Time.time, tEnd = Time.time + seconds;
        while (Time.time < tEnd && m != null)
        {
            await Wait(0.5f, ct);
            float dt = Time.time - tLast;
            if (dt <= 0f) continue;
            Vector3 now = m.transform.position;
            Vector3 d = now - last; d.y = 0f;
            best = Mathf.Max(best, d.magnitude / dt);
            last = now; tLast = Time.time;
        }
        return best;
    }

    private static void Remove(MonsterBase m)
    {
        if (m == null) return;
        s_pin.Remove(m);
        s_spawned.Remove(m);
        try { Managers.ObjectPooler.Despawn(m.gameObject); }
        catch (Exception) { UnityEngine.Object.Destroy(m.gameObject); }
    }
}
