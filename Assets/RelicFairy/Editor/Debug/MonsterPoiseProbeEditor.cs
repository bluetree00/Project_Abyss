#if UNITY_EDITOR
using System;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// [실측 · 플레이 중] 10-11 일반방 긴장감 P2-① · ③ — 공격 확약(위협 · 정예) · 정예 포이즈 · 공격 속도 배율.
/// 테스트 허브에서 런 구조 자동 실측으로 첫 전투방까지 간 뒤 멈추고, 슬라임(일반) · 골렘(희귀) · 늑대인간(정예)을 낸다.
/// 공격 확약 칸은 몬스터를 플레이어 1.5 m 앞에 두고 <b>스스로 공격 상태에 들어갈 때까지 기다려</b> 친다
/// (패턴 공격 상태는 고를 패턴이 없으면 바로 추격으로 돌아가 강제 전환은 믿을 수 없다). 포이즈 칸은 8 m 밖(추격 중)에서 친다.
/// 결과 Temp/monster_poise_probe.txt · 사진 Temp/ui_shots/MP_*.png · 로그 「[MonsterPoise] 끝」.
/// </summary>
public static class MonsterPoiseProbeEditor
{
    private const string AutoRoot = "RelicFairy/Debug/런 구조 자동 실측/";
    private const string OutPath  = "Temp/monster_poise_probe.txt";
    private const string HoldCc   = "probe_hold";
    private const int    TankHp   = 50000;
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private static bool s_armed;
    private static readonly StringBuilder s_sb = new();
    private static int s_pass, s_fail;

    [MenuItem("RelicFairy/Debug/몬스터 공격 예고/10 공격 확약 · 정예 포이즈 (테스트 허브, 플레이 중)")]
    private static void Begin()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[MonsterPoise] 플레이 모드에서만(테스트 허브)."); return; }
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
            if (p == null) { Debug.LogWarning("[MonsterPoise] 런 플레이어가 없다"); return; }
            RunAsync(p, p.GetCancellationTokenOnDestroy()).Forget();
        };
    }

    private static async UniTaskVoid RunAsync(PlayerController p, CancellationToken ct)
    {
        s_sb.Clear().AppendLine("10-11 일반방 긴장감 P2-① — 공격 확약 · 정예 포이즈");
        s_pass = s_fail = 0;
        try
        {
            typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);
            await Wait(5f, ct);   // 방 입장 입력 잠금 · 등장 연출
            // 방 몬스터는 뒤로 치워 붙든다(실측 대상에 끼지 않게)
            var fw = p.transform.forward; fw.y = 0f; fw.Normalize();
            foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            {
                if (m.IsDead) continue;
                m.Status.ApplyCc(HoldCc, 9999f);
                Warp(m, p.transform.position - fw * 14f);
            }
            var r = Vector3.Cross(Vector3.up, fw);
            Vector3 far  = p.transform.position + fw * 8f;
            Vector3 near = p.transform.position + fw * 1.5f;
            var slime = await Spawn("Slime/Slime", far - r * 2.5f, ct);
            var rare  = await Spawn("Golem/Golem", far, ct);
            var elite = await Spawn("Werewolf/Werewolf", far + r * 2.5f, ct);
            await Wait(2.5f, ct);
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 8f && (Immune(slime) || Immune(rare) || Immune(elite))) await UniTask.Yield(ct);
            Line(slime != null && rare != null && elite != null,
                 $"소환 — 일반 {slime?.Grade} · 희귀 {rare?.Grade} · 정예 {elite?.Grade}");
            if (slime == null || rare == null || elite == null) return;
            foreach (var m in new[] { slime, rare, elite }) SetHp(m, TankHp);

            // ① 일반 — 지금처럼 매 타 경직(회귀 없음)
            Warp(rare, far + r * 6f); Warp(elite, far + r * 9f);   // 둘은 멀리 — 플레이어에게 붙어 공격하지 않게
            Warp(slime, far);
            slime.ChangeState<ChaseState>();
            await Hit(p, slime, WeaponActionType.GroundLight, ct);
            Line(Cur(slime) == typeof(GetHitState), $"① 일반 — 맞으면 경직 ({Cur(slime)?.Name})");

            // ② 희귀 — 공격 상태에선 안 끊김 · 밖에선 경직
            Warp(slime, far - r * 6f);
            Warp(rare, near);
            bool atk2 = await WaitState(rare, typeof(AttackState), 6f, ct);
            await Hit(p, rare, WeaponActionType.GroundLight, ct, 0.02f);
            var inAtk = atk2 ? Cur(rare) : null;
            Warp(rare, far + r * 6f);
            await WaitNotState(rare, typeof(AttackState), 3f, ct);
            rare.ChangeState<ChaseState>();
            await Hit(p, rare, WeaponActionType.GroundLight, ct);
            Line(inAtk == typeof(AttackState) && Cur(rare) == typeof(GetHitState),
                 $"② 희귀 — 공격 중 맞아도 {inAtk?.Name} · 공격 밖에서 {Cur(rare)?.Name}");

            // ③ 정예 — 5타는 버티고(줄이 준다) 6타에 휘청 1.5초 · 받는 피해 +25% · 휘청 중 추가 타격은 휘청을 늘리지 않음
            var poise = elite.Poise;
            Line(poise != null, "③ 정예 포이즈가 있다");
            if (poise == null) return;
            Warp(elite, far);
            elite.ChangeState<ChaseState>();
            var fills = new StringBuilder();
            bool stayed = true;
            for (int i = 0; i < 5; i++)
            {
                await Hit(p, elite, WeaponActionType.GroundLight, ct);
                fills.Append($"{poise.Fill01:0.00} ");
                if (Cur(elite) == typeof(GetHitState)) stayed = false;
            }
            if (fills.Length > 0) ScreenCapture.CaptureScreenshot($"Temp/ui_shots/MP_{DateTime.Now:HHmmss}_poise.png");
            await Hit(p, elite, WeaponActionType.GroundLight, ct);
            float timer1 = StateTimer(elite);
            bool amp = elite.HasDamageTakenAmpSlot(MonsterPoise.BreakStatusId);
            var afterBreak = Cur(elite);
            await Wait(0.3f, ct);
            await Hit(p, elite, WeaponActionType.GroundLight, ct);
            float timer2 = StateTimer(elite);
            Line(stayed && afterBreak == typeof(GetHitState) && timer1 > 1.3f && amp && poise.IsBroken && timer2 < timer1,
                 $"③ 정예 — 5타 줄 {fills}· 경직 없음 {stayed} · 6타 {afterBreak?.Name} 휘청 {timer1:0.00}초 · 받는 피해 증폭 {amp} · 휘청 중 추가 타격 뒤 {timer2:0.00}초(늘지 않음)");

            // ④ 회복 — 휘청이 끝나고 2타 → 3초 쉬면 가득
            await Wait(1.4f, ct);
            Warp(elite, far);
            elite.ChangeState<ChaseState>();
            await Hit(p, elite, WeaponActionType.GroundLight, ct);
            await Hit(p, elite, WeaponActionType.GroundLight, ct);
            float mid = poise.Fill01;
            await Wait(3.2f, ct);
            Line(mid < 0.75f && mid > 0.55f && poise.Fill01 > 0.99f, $"④ 회복 — 2타 뒤 줄 {mid:0.00} → 3초 쉬고 {poise.Fill01:0.00}");

            // ⑤ 스킬 타격 2.5배 — 2타 버티고 3타에 휘청
            Warp(elite, far);
            elite.ChangeState<ChaseState>();
            await Hit(p, elite, WeaponActionType.RSkill, ct);
            await Hit(p, elite, WeaponActionType.RSkill, ct);
            var two = Cur(elite);
            float fill2 = poise.Fill01;
            await Hit(p, elite, WeaponActionType.RSkill, ct);
            Line(two != typeof(GetHitState) && Cur(elite) == typeof(GetHitState) && poise.IsBroken,
                 $"⑤ 스킬 — 2타 뒤 줄 {fill2:0.00}({two?.Name}) · 3타 {Cur(elite)?.Name}");

            // ⑥ 정예 공격 확약 — 공격 상태에선 7타를 맞아도 안 끊기고(줄은 빈 채 기다림), 공격이 끝난 뒤 첫 타에 휘청
            await Wait(1.7f, ct);
            HideOthers(slime, rare, elite);
            Warp(elite, near);
            // 진단(10-14): 공격 상태까지 가는 길 — 0.25초마다 상태 · 준비 시계 · 칸 수
            var trail = new StringBuilder();
            float tt0 = Time.realtimeSinceStartup, nextLog = 0f;
            bool atk6 = false;
            while (Time.realtimeSinceStartup - tt0 < 6f)
            {
                float el = Time.realtimeSinceStartup - tt0;
                if (Cur(elite) == typeof(AttackState)) { atk6 = true; break; }
                if (el >= nextLog)
                {
                    nextLog += 0.25f;
                    var dd = elite.transform.position - p.transform.position; dd.y = 0f;
                    trail.Append($"{el:0.00} {Cur(elite)?.Name} t {StateTimer(elite):0.00} 거리 {dd.magnitude:0.0} 근접칸 {MonsterAttackSlots.Count(AttackSlotKind.Melee)}{(el < 0.3f ? " 쥔 몬스터 " + Holders() : "")} | ");
                }
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            s_sb.AppendLine($"   ⑥ 진단 — 공격 상태까지 {(atk6 ? "들어감" : "못 들어감")}: {trail}");
            bool held = atk6;
            for (int i = 0; i < 7; i++)
            {
                await Hit(p, elite, WeaponActionType.GroundLight, ct, 0.02f);
                if (Cur(elite) == typeof(GetHitState)) held = false;
            }
            bool notBroken = !poise.IsBroken;
            await WaitNotState(elite, typeof(AttackState), 3f, ct);
            await Hit(p, elite, WeaponActionType.GroundLight, ct);
            Line(held && notBroken && Cur(elite) == typeof(GetHitState) && poise.IsBroken,
                 $"⑥ 정예 공격 확약 — 공격 중 7타 안 끊김 {held} · 깨지지 않음 {notBroken} · 공격 뒤 첫 타 {Cur(elite)?.Name}");

            // ⑦ 공격 속도 배율(P2-③) — 같은 슬라임의 공격 한 번 길이(공격 상태 들어감 → 나감)가 배율 2에서 절반
            Warp(elite, far + r * 9f); Warp(rare, far + r * 6f);
            HideOthers(slime, rare, elite);
            float d1 = await AttackLength(slime, near, 1f, ct);
            float d2 = await AttackLength(slime, near, 2f, ct);
            slime.SetTraitAttackSpeedMul(1f);
            float ratio = d1 > 0f ? d2 / d1 : -1f;
            // 프레임 오차 보정 — 상태가 끝난 첫 프레임에 나가므로 한 번 길이에 평균 프레임 하나만큼 덧붙는다(느린 에디터 약 20fps)
            float r2 = (d1 - s_lastDt1) > 0f ? (d2 - s_lastDt2) / (d1 - s_lastDt1) : -1f;
            Line(d1 > 0f && d2 > 0f && ((ratio > 0.35f && ratio < 0.65f) || (r2 > 0.35f && r2 < 0.65f)),
                 $"⑦ 공격 속도 배율 — 배율 1 공격 {d1:0.00}초({s_frames1}프레임 · 평균 {s_lastDt1 * 1000f:0}ms) · 배율 2 {d2:0.00}초({s_frames2}프레임 · 평균 {s_lastDt2 * 1000f:0}ms) · 비 {ratio:0.00} · 프레임 하나 뺀 비 {r2:0.00}");
            s_sb.AppendLine("   ⑦ 진단(배율 2 공격 프레임별: 시각 · 쿨다운 · 배율 · 애니 대기 · 첫 사이클 끝 · 애니 상태 · 진행률 · 애니 속도)");
            s_sb.Append(s_diag);
        }
        catch (OperationCanceledException) { s_sb.AppendLine("취소됨"); }
        catch (Exception e) { Line(false, "예외 " + e.Message); Debug.LogException(e); }
        finally
        {
            s_sb.Insert(0, $"통과 {s_pass} · 실패 {s_fail}\n");
            System.IO.File.WriteAllText(OutPath, s_sb.ToString());
            Debug.Log($"[MonsterPoise] 끝 — 통과 {s_pass} · 실패 {s_fail} → {OutPath}");
        }
    }

    private static async UniTask<MonsterBase> Spawn(string key, Vector3 pos, CancellationToken ct)
    {
        if (NavMesh.SamplePosition(pos, out var hit, 4f, NavMesh.AllAreas)) pos = hit.position;
        try { return await Managers.ObjectPooler.SpawnAsync<MonsterBase>(key, ObjectPoolerManager.PoolType.Monster, pos, Quaternion.identity); }
        catch (Exception e) { s_sb.AppendLine($"  (소환 실패 {key}: {e.Message})"); return null; }
    }

    /// <summary>실측 대상 셋 말고 살아 있는 몬스터를 끈다 — 방의 두 번째 파도가 공격 칸을 차지하지 않게(10-14).</summary>
    private static void HideOthers(params MonsterBase[] keep)
    {
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead && Array.IndexOf(keep, m) < 0) m.gameObject.SetActive(false);
    }

    /// <summary>진단(10-14): 지금 공격 칸을 쥔 몬스터 — 이름 · 상태 · CC · 거리.</summary>
    private static string Holders()
    {
        var d = typeof(MonsterAttackSlots).GetField("s_holders", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as System.Collections.IDictionary;
        if (d == null) return "?";
        var sb = new StringBuilder("[");
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        foreach (var k in d.Keys)
        {
            var m = k as MonsterBase;
            if (m == null) { sb.Append("null · "); continue; }
            float dist = player != null ? Vector3.Distance(m.transform.position, player.transform.position) : -1f;
            sb.Append($"{m.name} {Cur(m)?.Name} cc {m.Status.IsCcActive} {dist:0.0}m · ");
        }
        return sb.Append("]").ToString();
    }

    private static async UniTask Hit(PlayerController p, MonsterBase m, WeaponActionType kind, CancellationToken ct, float gap = 0.08f)
    {
        if (m == null || m.IsDead) return;
        CombatDamage.Deal(new CombatDamage.Request
        {
            Target = m.gameObject, BaseDamage = 3f, Owner = p.gameObject, ActionType = kind,
            KnockbackMultiplier = 0f, HitPoint = m.transform.position + Vector3.up, SourcePosition = p.transform.position,
            ProcCoef = kind == WeaponActionType.RSkill ? 2.5f : 1f,   // 규칙 실측 — 무기 계수(10-14 A2)와 떼어 옛 무게로
        });
        await UniTask.Delay(TimeSpan.FromSeconds(gap), DelayType.Realtime, cancellationToken: ct);
    }

    private static readonly StringBuilder s_diag = new();
    private static int s_frames1, s_frames2;
    private static float s_lastDt1, s_lastDt2;

    private static async UniTask<float> AttackLength(MonsterBase m, Vector3 at, float mul, CancellationToken ct)
    {
        m.SetTraitAttackSpeedMul(mul);
        Warp(m, at);
        await WaitNotState(m, typeof(AttackState), 4f, ct);
        if (!await WaitState(m, typeof(AttackState), 8f, ct)) return -1f;
        float t0 = Time.time;
        // 공격 상태 인스턴스(PatternAttackState · 비공개)의 타이머를 프레임마다 적는다 — 배율 2가 덜 빨라지는 원인 찾기
        object st = null;
        if (mul != 1f && typeof(MonsterBase).GetField("_fsm", Inst)?.GetValue(m) is MonsterFSM fsm
            && typeof(MonsterFSM).GetField("_states", Inst)?.GetValue(fsm) is System.Collections.IDictionary dict)
            st = dict[typeof(AttackState)];
        var anim = m.GetComponentInChildren<Animator>();
        int frames = 0; float dtSum = 0f;
        while (Cur(m) == typeof(AttackState) && Time.time - t0 < 8f)
        {
            frames++; dtSum += Time.deltaTime;
            if (st != null)
            {
                var t = st.GetType();
                var info = anim != null ? anim.GetCurrentAnimatorStateInfo(0) : default;
                s_diag.AppendLine($"     {Time.time - t0:0.00} · cd {t.GetField("_cooldownTimer", Inst)?.GetValue(st)} · sp {t.GetField("_speed", Inst)?.GetValue(st)} · wait {t.GetField("_waitForAnimFinish", Inst)?.GetValue(st)} · first {t.GetField("_animFirstCycleDone", Inst)?.GetValue(st)} · anim {info.shortNameHash} · n {info.normalizedTime:0.00} · trans {anim?.IsInTransition(0)} · as {anim?.speed:0.0}");
            }
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        float avg = frames > 0 ? dtSum / frames : 0f;
        if (mul == 1f) { s_frames1 = frames; s_lastDt1 = avg; } else { s_frames2 = frames; s_lastDt2 = avg; }
        return Time.time - t0;
    }

    private static async UniTask<bool> WaitState(MonsterBase m, Type state, float timeout, CancellationToken ct)
    {
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < timeout)
        {
            if (Cur(m) == state) return true;
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        return false;
    }

    private static async UniTask WaitNotState(MonsterBase m, Type state, float timeout, CancellationToken ct)
    {
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < timeout && Cur(m) == state) await UniTask.Yield(PlayerLoopTiming.Update, ct);
    }

    /// <summary>지금 상태의 등록 키 — 패턴 몬스터는 실제 클래스가 PatternAttackState 등이라 키로 되묻는다.</summary>
    private static Type Cur(MonsterBase m)
    {
        if (m.IsInState<AttackState>())      return typeof(AttackState);
        if (m.IsInState<AttackReadyState>()) return typeof(AttackReadyState);
        if (m.IsInState<GetHitState>())      return typeof(GetHitState);
        if (m.IsInState<ChaseState>())       return typeof(ChaseState);
        if (m.IsInState<PatrolState>())      return typeof(PatrolState);
        return (typeof(MonsterBase).GetField("_fsm", Inst)?.GetValue(m) as MonsterFSM)?.CurrentType;
    }

    private static float StateTimer(MonsterBase m)
        => (typeof(MonsterBase).GetField("_runtime", Inst)?.GetValue(m) as MonsterRuntimeData)?.StateTimer ?? -1f;

    private static bool Immune(MonsterBase m) => m != null && m.IsDamageImmuneNow;

    private static void SetHp(MonsterBase m, int hp)
    {
        if (typeof(MonsterBase).GetField("_runtime", Inst)?.GetValue(m) is MonsterRuntimeData rt) rt.CurrentHp = hp;
    }

    private static void Warp(MonsterBase m, Vector3 pos)
    {
        if (NavMesh.SamplePosition(pos, out var hit, 4f, NavMesh.AllAreas)) pos = hit.position;
        if (m.TryGetComponent<NavMeshAgent>(out var ag) && ag.isActiveAndEnabled && ag.isOnNavMesh) ag.Warp(pos);
        else m.transform.position = pos;
    }

    private static void Line(bool ok, string text)
    {
        if (ok) s_pass++; else s_fail++;
        s_sb.AppendLine($"{(ok ? "통과" : "실패")} — {text}");
        Debug.Log($"[MonsterPoise] {(ok ? "통과" : "실패")} {text}");
    }

    private static UniTask Wait(float s, CancellationToken ct)
        => UniTask.Delay(TimeSpan.FromSeconds(s), DelayType.Realtime, cancellationToken: ct);
}
#endif
