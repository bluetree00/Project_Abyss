#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// [실측 · 플레이 중] 10-11 일반방 긴장감 P2-② — 동시 공격 상한.
/// 테스트 허브 첫 전투방(Ch1 · 근접 상한 2)에서 방 몬스터를 치우고 슬라임 8마리를 플레이어 둘레 4 m에 낸다(플레이어 무적).
/// 10초 동안 매 프레임 공격 준비 · 공격 상태 수를 센다 — 최대 ≤ 상한 · 평균 ≥ 1.2(다 같이 쉬는 빈틈 없음) ·
/// 돌아가며 공격한 수 ≥ 5(한 마리가 쥐지 않음) · 기다리는 몬스터의 평균 속도 > 0.3(멈춰 구경하지 않음).
/// 결과 Temp/monster_slot_probe.txt · 사진 Temp/ui_shots/MS_*.png · 로그 「[MonsterSlot] 끝」.
/// </summary>
public static class MonsterSlotProbeEditor
{
    private const string AutoRoot = "RelicFairy/Debug/런 구조 자동 실측/";
    private const string OutPath  = "Temp/monster_slot_probe.txt";
    private const string HoldCc   = "probe_hold";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static bool s_armed;

    [MenuItem("RelicFairy/Debug/몬스터 공격 예고/11 동시 공격 상한 (테스트 허브, 플레이 중)")]
    private static void Begin()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[MonsterSlot] 플레이 모드에서만(테스트 허브)."); return; }
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
            if (p == null) { Debug.LogWarning("[MonsterSlot] 런 플레이어가 없다"); return; }
            RunAsync(p, p.GetCancellationTokenOnDestroy()).Forget();
        };
    }

    private static async UniTaskVoid RunAsync(PlayerController p, CancellationToken ct)
    {
        var sb = new StringBuilder("10-11 일반방 긴장감 P2-② — 동시 공격 상한\n");
        int pass = 0, fail = 0;
        void Line(bool ok, string t) { if (ok) pass++; else fail++; sb.AppendLine($"{(ok ? "통과" : "실패")} — {t}"); Debug.Log($"[MonsterSlot] {(ok ? "통과" : "실패")} {t}"); }
        try
        {
            typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);
            await Wait(5f, ct);
            var fw = p.transform.forward; fw.y = 0f; fw.Normalize();
            foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            {
                if (m.IsDead) continue;
                m.Status.ApplyCc(HoldCc, 9999f);
                Warp(m, p.transform.position - fw * 18f);
            }
            var ms = new List<MonsterBase>();
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 2f / 8f;
                var pos = p.transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 4f;
                if (NavMesh.SamplePosition(pos, out var hit, 3f, NavMesh.AllAreas)) pos = hit.position;
                try
                {
                    var m = await Managers.ObjectPooler.SpawnAsync<MonsterBase>("Slime/Slime", ObjectPoolerManager.PoolType.Monster, pos, Quaternion.identity);
                    if (m != null) ms.Add(m);
                }
                catch (Exception e) { sb.AppendLine("  (소환 실패 " + e.Message + ")"); }
            }
            await Wait(2.5f, ct);
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 8f && ms.Exists(m => m != null && m.IsDamageImmuneNow)) await UniTask.Yield(ct);
            foreach (var m in ms)
                if (typeof(MonsterBase).GetField("_runtime", Inst)?.GetValue(m) is MonsterRuntimeData rt) rt.CurrentHp = 50000;
            int cap = MonsterAttackSlots.MeleeCap;
            Line(ms.Count == 8, $"슬라임 {ms.Count}마리 · 근접 상한 {cap}");

            int frames = 0, maxN = 0, maxSlots = 0;
            double sum = 0, waitSpeed = 0;
            int waitN = 0, zeroFrames = 0;
            var attackers = new HashSet<int>();
            var lastStart = new Dictionary<int, float>();   // 공격 상태에 들어간 시각(P1-6 공격 주기)
            var wasAtk = new HashSet<int>();
            float minInterval = 99f;
            bool shot = false;
            t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 10f)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                frames++;
                int n = 0;
                foreach (var m in ms)
                {
                    if (m == null || m.IsDead) continue;
                    bool atk = m.IsInState<AttackReadyState>() || m.IsInState<AttackState>();
                    int id = m.GetInstanceID();
                    bool inAtk = m.IsInState<AttackState>();
                    if (inAtk && !wasAtk.Contains(id))
                    {
                        if (lastStart.TryGetValue(id, out var prev)) minInterval = Mathf.Min(minInterval, Time.time - prev);
                        lastStart[id] = Time.time;
                    }
                    if (inAtk) wasAtk.Add(id); else wasAtk.Remove(id);
                    if (atk) { n++; attackers.Add(id); continue; }
                    var d = m.transform.position - p.transform.position; d.y = 0f;
                    if (d.magnitude < 6f && m.TryGetComponent<NavMeshAgent>(out var ag) && ag.isActiveAndEnabled)
                    { waitSpeed += ag.velocity.magnitude; waitN++; }
                }
                sum += n;
                if (n == 0) zeroFrames++;
                maxN = Mathf.Max(maxN, n);
                maxSlots = Mathf.Max(maxSlots, MonsterAttackSlots.Count(AttackSlotKind.Melee));
                if (!shot && Time.realtimeSinceStartup - t0 > 5f) { shot = true; ScreenCapture.CaptureScreenshot($"Temp/ui_shots/MS_{DateTime.Now:HHmmss}_circle.png"); }
            }
            double mean = frames > 0 ? sum / frames : 0;
            double ws = waitN > 0 ? waitSpeed / waitN : 0;
            Line(maxN <= cap, $"동시 공격(준비 + 공격) 최대 {maxN} ≤ 상한 {cap} · 칸 최대 {maxSlots}");
            Line(mean >= 1.2, $"평균 동시 공격 {mean:0.00}(≥ 1.2 — 다 같이 쉬는 빈틈 없음) · 아무도 안 노린 프레임 {zeroFrames}/{frames}");
            Line(attackers.Count >= 5, $"10초 동안 돌아가며 공격한 슬라임 {attackers.Count}/8(≥ 5 — 한 마리가 쥐지 않음)");
            Line(ws > 0.3, $"기다리는 슬라임 평균 속도 {ws:0.00} m/s(> 0.3 — 멈춰 구경하지 않음)");
            Line(minInterval >= 1.2f * 0.95f, $"슬라임 한 마리의 공격 시작 간격 최소 {(minInterval > 90f ? -1f : minInterval):0.00}초(≥ 1.2 — 잡몹 공격 주기 · 10-13 P1-6)");

            // ② 원거리 물러남(P3-⑥) — 악마 마법사를 1.5 m 옆에 두면 공격 거리 절반(최소 2.5 m) 밖으로 물러난다
            foreach (var m in ms) if (m != null && !m.IsDead) { m.Status.ApplyCc(HoldCc, 9999f); Warp(m, p.transform.position - fw * 18f); }
            await Wait(0.5f, ct);
            MonsterBase mage = null;
            try { mage = await Managers.ObjectPooler.SpawnAsync<MonsterBase>("EvilMage/EvilMage", ObjectPoolerManager.PoolType.Monster, p.transform.position + fw * 1.5f, Quaternion.identity); }
            catch (Exception e) { sb.AppendLine("  (악마 마법사 소환 실패 " + e.Message + ")"); }
            if (mage != null)
            {
                await Wait(2.5f, ct);
                float tm = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - tm < 8f && mage.IsDamageImmuneNow) await UniTask.Yield(ct);
                if (typeof(MonsterBase).GetField("_runtime", Inst)?.GetValue(mage) is MonsterRuntimeData mrt) mrt.CurrentHp = 50000;
                Warp(mage, p.transform.position + fw * 1.5f);
                float d0 = Flat(mage.transform.position - p.transform.position).magnitude;
                float far = d0; bool mageAttacked = false;
                tm = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - tm < 5f)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    far = Mathf.Max(far, Flat(mage.transform.position - p.transform.position).magnitude);
                    mageAttacked |= mage.IsInState<AttackState>();
                }
                Line(far >= 2.5f, $"악마 마법사(원거리만) 1.5 m 옆에서 {d0:0.0} → 최대 {far:0.0} m(≥ 2.5 — 물러나 거리를 지킴 · P3-⑥) · 공격 {mageAttacked}");
                mage.Status.ApplyCc(HoldCc, 9999f); Warp(mage, p.transform.position - fw * 18f);
            }
            else Line(false, "악마 마법사 소환");

            // ③ 정예 큰 공격 겹침 — 게(정예 · Spin = 둘레 8칸)가 큰 공격 중일 때 다른 근접(슬라임)이 새로 공격 준비에 들어가지 않는다
            MonsterBase crab = null;
            try { crab = await Managers.ObjectPooler.SpawnAsync<MonsterBase>("CrabMonster/CrabMonster", ObjectPoolerManager.PoolType.Monster, p.transform.position + fw * 1.5f, Quaternion.identity); }
            catch (Exception e) { sb.AppendLine("  (게 소환 실패 " + e.Message + ")"); }
            if (crab != null)
            {
                await Wait(2.5f, ct);
                float tc = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - tc < 8f && crab.IsDamageImmuneNow) await UniTask.Yield(ct);
                if (typeof(MonsterBase).GetField("_runtime", Inst)?.GetValue(crab) is MonsterRuntimeData crt) crt.CurrentHp = 50000;
                var near = new List<MonsterBase>();
                for (int i = 0; i < 3 && i < ms.Count; i++)
                {
                    var m = ms[i]; if (m == null || m.IsDead) continue;
                    m.Status.ClearCc(HoldCc);
                    float a = i * Mathf.PI * 2f / 3f;
                    Warp(m, p.transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 3f);
                    near.Add(m);
                }
                // 큰 공격을 고르게 한다(무작위라 14초 동안 한 번도 안 할 때가 있었다 · ⑩) — 공격 상태의 패턴 진행에 Spin을 넣고 공격 상태로
                ForcePattern(crab, "Spin");
                int bigFrames = 0, violations = 0;
                var slimeReady = new HashSet<int>();
                foreach (var m in near) if (m.IsInState<AttackReadyState>() || m.IsInState<AttackState>()) slimeReady.Add(m.GetInstanceID());
                tc = Time.realtimeSinceStartup;
                float nextForce = Time.realtimeSinceStartup + 5f;
                while (Time.realtimeSinceStartup - tc < 14f)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    bool big = IsBig(crab);
                    if (big) bigFrames++;
                    if (Time.realtimeSinceStartup > nextForce && !crab.IsInState<AttackState>()) { ForcePattern(crab, "Spin"); nextForce = Time.realtimeSinceStartup + 5f; }
                    foreach (var m in near)
                    {
                        if (m == null || m.IsDead) continue;
                        int id = m.GetInstanceID();
                        bool now = m.IsInState<AttackReadyState>() || m.IsInState<AttackState>();
                        if (now && !slimeReady.Contains(id) && big) violations++;   // 큰 공격 중에 새로 들어옴
                        if (now) slimeReady.Add(id); else slimeReady.Remove(id);
                    }
                }
                Line(bigFrames > 0 && violations == 0, $"게(정예) 큰 공격 {bigFrames}프레임 동안 슬라임이 새로 공격 준비에 들어간 수 {violations}(= 0 — 정예 큰 공격 겹침 막기){(bigFrames == 0 ? " · ⚠ 14초 동안 큰 공격 안 함(무작위) — 판정 못 함" : "")}");
            }
            else Line(false, "게 소환");
        }
        catch (OperationCanceledException) { sb.AppendLine("취소됨"); }
        catch (Exception e) { Line(false, "예외 " + e.Message); Debug.LogException(e); }
        finally
        {
            sb.Insert(0, $"통과 {pass} · 실패 {fail}\n");
            System.IO.File.WriteAllText(OutPath, sb.ToString());
            Debug.Log($"[MonsterSlot] 끝 — 통과 {pass} · 실패 {fail} → {OutPath}");
        }
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    /// <summary>지금 정예 큰 공격 중인지 — 공격 상태 인스턴스의 _big(PatternAttackOverrideSO · 10-13).</summary>
    private static bool IsBig(MonsterBase m)
    {
        var fsm = typeof(MonsterBase).GetField("_fsm", Inst)?.GetValue(m);
        var cur = fsm != null ? typeof(MonsterFSM).GetField("_current", Inst)?.GetValue(fsm) : null;
        return cur != null && cur.GetType().GetField("_big", Inst)?.GetValue(cur) is bool b && b;
    }

    /// <summary>패턴 몬스터에게 이름으로 패턴을 고르게 하고 바로 공격 상태로 보낸다(측정용).</summary>
    private static void ForcePattern(MonsterBase m, string pattern)
    {
        var fsm = typeof(MonsterBase).GetField("_fsm", Inst)?.GetValue(m);
        if (fsm == null || !(typeof(MonsterFSM).GetField("_states", Inst)?.GetValue(fsm) is System.Collections.IDictionary states)) return;
        if (!(states[typeof(AttackState)] is object atk)) return;
        var owner = atk.GetType().GetField("_owner", Inst)?.GetValue(atk) as PatternAttackOverrideSO;
        var rt = atk.GetType().GetField("_runtime", Inst)?.GetValue(atk);
        int idx = owner != null ? owner.patterns.FindIndex(x => x.name == pattern) : -1;
        if (rt == null || idx < 0) return;
        rt.GetType().GetProperty("SelectedPatternIndex", Inst)?.SetValue(rt, idx);
        rt.GetType().GetField("SelectedPatternIndex", Inst)?.SetValue(rt, idx);
        m.ChangeState<AttackState>();
    }

    private static void Warp(MonsterBase m, Vector3 pos)
    {
        if (NavMesh.SamplePosition(pos, out var hit, 4f, NavMesh.AllAreas)) pos = hit.position;
        if (m.TryGetComponent<NavMeshAgent>(out var ag) && ag.isActiveAndEnabled && ag.isOnNavMesh) ag.Warp(pos);
        else m.transform.position = pos;
    }

    private static UniTask Wait(float s, CancellationToken ct)
        => UniTask.Delay(TimeSpan.FromSeconds(s), DelayType.Realtime, cancellationToken: ct);
}
#endif
