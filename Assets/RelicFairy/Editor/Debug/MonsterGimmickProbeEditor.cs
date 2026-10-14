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
/// [실측 · 플레이 중] 10-11 일반방 긴장감 P1-7 — 골렘 포효 · 비홀더 눈 빔 예고 손질 + 연결.
/// 테스트 허브 첫 전투방에서 방 몬스터를 치우고, 골렘 · 비홀더를 각각 체력 임계 바로 위로 맞춘 뒤 한 대 쳐 특수 상태를 연다.
/// 옆에 슬라임 하나를 두어 아군 피해가 없는지 본다. 플레이어는 무적(받은 피해는 OnDamageResolved 원래 피해로 센다).
/// 결과 Temp/monster_gimmick_probe.txt · 사진 Temp/ui_shots/MG7_*.png · 로그 「[MonsterGimmick] 끝」.
/// </summary>
public static class MonsterGimmickProbeEditor
{
    private const string AutoRoot = "RelicFairy/Debug/런 구조 자동 실측/";
    private const string OutPath  = "Temp/monster_gimmick_probe.txt";
    private const string HoldCc   = "probe_hold";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static bool s_armed;

    [MenuItem("RelicFairy/Debug/몬스터 공격 예고/12 골렘 포효 · 비홀더 눈 빔 예고 (테스트 허브, 플레이 중)")]
    private static void Begin()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[MonsterGimmick] 플레이 모드에서만(테스트 허브)."); return; }
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
            if (p == null) { Debug.LogWarning("[MonsterGimmick] 런 플레이어가 없다"); return; }
            RunAsync(p, p.GetCancellationTokenOnDestroy()).Forget();
        };
    }

    private static async UniTaskVoid RunAsync(PlayerController p, CancellationToken ct)
    {
        var sb = new StringBuilder("10-11 일반방 긴장감 P1-7 — 골렘 포효 · 비홀더 눈 빔\n");
        int pass = 0, fail = 0;
        void Line(bool ok, string t) { if (ok) pass++; else fail++; sb.AppendLine($"{(ok ? "통과" : "실패")} — {t}"); Debug.Log($"[MonsterGimmick] {(ok ? "통과" : "실패")} {t}"); }
        int beamHits = 0, beamFromBeholder = 0;
        var others = new List<string>();   // 비홀더가 아닌 공격자(10-13 — 6번 중 1번이 누구였나)
        MonsterBase beholderRef = null;
        void OnPlayerDamage(GameObject attacker, int raw, int final, PlayerController.DamageOutcome o)
        {
            if (beholderRef == null || raw <= 0) return;
            beamHits++;
            if (attacker != null && attacker.transform.root == beholderRef.transform.root) beamFromBeholder++;
            else others.Add(attacker != null ? $"{attacker.name}(root {attacker.transform.root.name})" : "null");
        }
        try
        {
            typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);
            await Wait(5f, ct);
            var fw = p.transform.forward; fw.y = 0f; fw.Normalize();
            var r = Vector3.Cross(Vector3.up, fw);
            foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            {
                if (m.IsDead) continue;
                m.Status.ApplyCc(HoldCc, 9999f);
                Warp(m, p.transform.position - fw * 18f);
            }

            // ── 골렘 포효 ──
            var golem = await Spawn("Golem/Golem", p.transform.position + fw * 3f, ct);
            var ally  = await Spawn("Slime/Slime", p.transform.position + fw * 3f + r * 2.5f, ct);
            await WaitReady(ct, golem, ally);
            if (golem == null || ally == null) { Line(false, "골렘 · 슬라임 소환"); return; }
            ally.Status.ApplyCc(HoldCc, 9999f);
            SetHp(golem, Mathf.FloorToInt(golem.EffectiveMaxHp * 0.56f) + 1);   // 임계(56%) 바로 위 — 한 대(최소 1)에 넘는다
            SetHp(ally, 50000);
            int allyHp0 = ally.CurrentHp;
            string stamp = DateTime.Now.ToString("HHmmss");
            Hit(p, golem);   // 임계 아래로 — 포효
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
            bool roar = golem.IsInSpecialState;
            bool circle = GameObject.Find("[MonsterGroundWarning.Circle]") != null;
            float t0 = Time.time;
            object st0 = CurrentState(golem);   // 포효 시작 프레임과 t0 사이 — 남은 예고 타이머로 시작 시각을 되돌린다(에디터 프레임이 길면 한 프레임이 0.1초를 넘는다)
            float remain0 = st0?.GetType().GetField("_blastTimer", Inst)?.GetValue(st0) is float rf ? rf : -1f;
            await Wait(0.25f, ct);
            ScreenCapture.CaptureScreenshot($"Temp/ui_shots/MG7_{stamp}_golem_telegraph.png");
            int hpA = golem.CurrentHp;
            Hit(p, golem);   // 예고 중 — 무적
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
            bool invTele = golem.CurrentHp == hpA;
            // 충격파 시각 — 상태 인스턴스의 _blasted가 켜지는 순간
            float tBlast = -1f;
            object st = CurrentState(golem);
            while (Time.time - t0 < 2.5f && golem.IsInSpecialState)
            {
                if (st != null && st.GetType().GetField("_blasted", Inst)?.GetValue(st) is bool b && b) { tBlast = Time.time - t0; break; }
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            await Wait(0.2f, ct);
            int hpB = golem.CurrentHp;
            Hit(p, golem);   // 충격파 뒤 — 피해가 들어가고 포효는 안 끊김
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
            bool dmgAfter = golem.CurrentHp < hpB && golem.IsInSpecialState;
            Line(roar && circle, $"골렘 — 체력 56% 아래 한 대에 포효 {roar} · 원 외곽선 {circle}");
            float fromEnter = remain0 >= 0f ? tBlast + (0.6f - remain0) : tBlast;   // 포효 시작부터(예고 상수 0.6 − t0의 남은 시간)
            Line(invTele && fromEnter >= 0.55f, $"골렘 — 예고 중 무적 {invTele} · 충격파 포효 시작부터 {fromEnter:0.00}초(t0부터 {tBlast:0.00} · t0에 남은 예고 {remain0:0.00}) (≥ 0.55 — 예전 0초)");
            Line(dmgAfter, $"골렘 — 충격파 뒤 피해 들어감 · 포효 안 끊김 {dmgAfter}");
            Line(ally.CurrentHp == allyHp0, $"골렘 — 옆 슬라임(2.5 m) 피해 없음 ({allyHp0} → {ally.CurrentHp})");
            await Wait(2f, ct);
            Warp(golem, p.transform.position - fw * 18f - r * 4f); golem.Status.ApplyCc(HoldCc, 9999f);

            // ── 비홀더 눈 빔 ──
            var beholder = await Spawn("Beholder/Beholder", p.transform.position + fw * 2.5f, ct);
            await WaitReady(ct, beholder);
            if (beholder == null) { Line(false, "비홀더 소환"); return; }
            Warp(ally, beholder.transform.position + r * 2f);
            SetHp(beholder, Mathf.FloorToInt(beholder.EffectiveMaxHp * 0.6f) + 1);   // 임계(60%) 바로 위
            allyHp0 = ally.CurrentHp;
            beholderRef = beholder;
            p.OnDamageResolved += OnPlayerDamage;
            stamp = DateTime.Now.ToString("HHmmss");
            Hit(p, beholder);   // 임계 아래로 — 눈 빔
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
            bool beam = beholder.IsInSpecialState;
            bool circle2 = GameObject.Find("[MonsterGroundWarning.Circle]") != null;
            await Wait(0.4f, ct);
            ScreenCapture.CaptureScreenshot($"Temp/ui_shots/MG7_{stamp}_beholder_charge.png");
            await Wait(0.9f, ct);
            ScreenCapture.CaptureScreenshot($"Temp/ui_shots/MG7_{stamp}_beholder_fire.png");
            float tb = Time.time;
            while (Time.time - tb < 3f && beholder.IsInSpecialState) await UniTask.Yield(PlayerLoopTiming.Update, ct);
            p.OnDamageResolved -= OnPlayerDamage;
            Line(beam && circle2, $"비홀더 — 체력 60% 아래 한 대에 눈 빔 {beam} · 충전 원 외곽선 {circle2}");
            Line(beamHits >= 3 && beamFromBeholder == beamHits, $"비홀더 — 플레이어 피격 {beamHits}번 · 공격자 = 비홀더 {beamFromBeholder}번{(others.Count > 0 ? " · 다른 공격자 " + string.Join(", ", others) : "")}");
            Line(ally.CurrentHp == allyHp0, $"비홀더 — 옆 슬라임(2 m) 피해 없음 ({allyHp0} → {ally.CurrentHp})");
        }
        catch (OperationCanceledException) { sb.AppendLine("취소됨"); }
        catch (Exception e) { Line(false, "예외 " + e.Message); Debug.LogException(e); }
        finally
        {
            p.OnDamageResolved -= OnPlayerDamage;
            sb.Insert(0, $"통과 {pass} · 실패 {fail}\n");
            System.IO.File.WriteAllText(OutPath, sb.ToString());
            Debug.Log($"[MonsterGimmick] 끝 — 통과 {pass} · 실패 {fail} → {OutPath}");
        }
    }

    private static object CurrentState(MonsterBase m)
    {
        var fsm = typeof(MonsterBase).GetField("_fsm", Inst)?.GetValue(m);
        return fsm != null ? typeof(MonsterFSM).GetField("_current", Inst)?.GetValue(fsm) : null;
    }

    private static async UniTask<MonsterBase> Spawn(string key, Vector3 pos, CancellationToken ct)
    {
        if (NavMesh.SamplePosition(pos, out var hit, 4f, NavMesh.AllAreas)) pos = hit.position;
        try { return await Managers.ObjectPooler.SpawnAsync<MonsterBase>(key, ObjectPoolerManager.PoolType.Monster, pos, Quaternion.identity); }
        catch (Exception) { return null; }
    }

    private static async UniTask WaitReady(CancellationToken ct, params MonsterBase[] ms)
    {
        await Wait(2.5f, ct);
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 8f && Array.Exists(ms, m => m != null && m.IsDamageImmuneNow)) await UniTask.Yield(ct);
    }

    private static void Hit(PlayerController p, MonsterBase m)
    {
        if (m == null || m.IsDead) return;
        CombatDamage.Deal(new CombatDamage.Request
        {
            Target = m.gameObject, BaseDamage = 3f, Owner = p.gameObject, ActionType = WeaponActionType.GroundLight,
            KnockbackMultiplier = 0f, HitPoint = m.transform.position + Vector3.up, SourcePosition = p.transform.position,
        });
    }

    private static void SetHp(MonsterBase m, int hp)
    {
        if (typeof(MonsterBase).GetField("_runtime", Inst)?.GetValue(m) is MonsterRuntimeData rt) rt.CurrentHp = hp;
    }

    private static void Warp(MonsterBase m, Vector3 pos)
    {
        if (m == null) return;
        if (NavMesh.SamplePosition(pos, out var hit, 4f, NavMesh.AllAreas)) pos = hit.position;
        if (m.TryGetComponent<NavMeshAgent>(out var ag) && ag.isActiveAndEnabled && ag.isOnNavMesh) ag.Warp(pos);
        else m.transform.position = pos;
    }

    private static UniTask Wait(float s, CancellationToken ct)
        => UniTask.Delay(TimeSpan.FromSeconds(s), DelayType.Realtime, cancellationToken: ct);
}
#endif
