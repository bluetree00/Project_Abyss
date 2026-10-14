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
/// [실측 · 플레이 중] 10-13 「이펙트 = 판정」 — 새 예고 4종이 실제 전투에서 뜨고 지워지는지(자동 실측은 처치가 즉사라 공격 장면이 없었다).
/// 살라만더 브레스(체력 50% · 예고 0.6초 뒤 첫 피해) · 비숍 반격(체력 50% · 막기 동안 원) · 웃는 버섯 덫(다가가면 원) · 요정 박쥐 근접(원).
/// 테스트 허브 첫 전투방에서 방 몬스터를 치우고 한 마리씩 부른다. 플레이어는 무적(받은 피해는 OnDamageResolved 원래 피해로 센다).
/// 결과 Temp/monster_telegraph_live.txt · 로그 「[TelegraphLive] 끝」.
/// </summary>
public static class MonsterTelegraphLiveProbeEditor
{
    private const string AutoRoot = "RelicFairy/Debug/런 구조 자동 실측/";
    private const string OutPath  = "Temp/monster_telegraph_live.txt";
    private const string HoldCc   = "probe_hold";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static bool s_armed;

    [MenuItem("RelicFairy/Debug/몬스터 공격 예고/13 새 예고 4종 실전 — 브레스 · 비숍 · 웃는 버섯 · 요정 박쥐 (테스트 허브, 플레이 중)")]
    private static void Begin()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[TelegraphLive] 플레이 모드에서만(테스트 허브)."); return; }
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
            if (p == null) { Debug.LogWarning("[TelegraphLive] 런 플레이어가 없다"); return; }
            RunAsync(p, p.GetCancellationTokenOnDestroy()).Forget();
        };
    }

    private static async UniTaskVoid RunAsync(PlayerController p, CancellationToken ct)
    {
        var sb = new StringBuilder("10-13 새 예고 4종 실전 — 예고가 뜨고(공격 전) 지워지는지 · 첫 피해 시각\n");
        int pass = 0, fail = 0;
        void Line(bool ok, string t) { if (ok) pass++; else fail++; sb.AppendLine($"{(ok ? "통과" : "실패")} — {t}"); Debug.Log($"[TelegraphLive] {(ok ? "통과" : "실패")} {t}"); }
        MonsterBase watch = null; float firstHit = -1f, tStart = 0f;
        void OnPlayerDamage(GameObject attacker, int raw, int final, PlayerController.DamageOutcome o)
        {
            if (watch == null || raw <= 0 || firstHit >= 0f) return;
            // 브레스 · 반격 · 덫 폭발은 공격자 없이 TakeDamage(int)로 준다 — 다른 몬스터는 묶어 18 m 밖에 두었으니 공격자 없음도 이 몬스터 몫
            if (attacker == null || attacker.transform.root == watch.transform.root) firstHit = Time.time - tStart;
        }
        try
        {
            typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);
            p.OnDamageResolved += OnPlayerDamage;
            await Wait(5f, ct);
            var fw = p.transform.forward; fw.y = 0f; fw.Normalize();
            foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            {
                if (m.IsDead) continue;
                m.Status.ApplyCc(HoldCc, 9999f);
                Warp(m, p.transform.position - fw * 18f);
            }
            await Wait(0.5f, ct);
            int baseTele = Tele();

            // ── 살라만더 브레스 — 체력 50% 아래 한 대 ──
            var sal = await Spawn("Salamander/Salamander", p.transform.position + fw * 2.5f, ct);
            await WaitReady(ct, sal);
            if (sal == null) Line(false, "살라만더 소환");
            else
            {
                Face(sal, p.transform.position);
                SetHp(sal, Mathf.FloorToInt(sal.EffectiveMaxHp * 0.5f) + 1);
                watch = sal; firstHit = -1f; tStart = Time.time;
                Hit(p, sal);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                bool inBreath = sal.IsInSpecialState;
                int tele = Tele() - baseTele;
                while (Time.time - tStart < 4f && sal.IsInSpecialState) await UniTask.Yield(PlayerLoopTiming.Update, ct);
                await Wait(0.3f, ct);
                int teleAfter = Tele() - baseTele;
                Line(inBreath && tele >= 1, $"살라만더 — 브레스 들어감 {inBreath} · 들어간 순간 예고 {tele}개");
                Line(firstHit >= 0.5f, $"살라만더 — 첫 피해 {firstHit:0.00}초 뒤(예고 0.6초 · 예전 0초)");
                Line(teleAfter == 0, $"살라만더 — 끝난 뒤 남은 예고 {teleAfter}개");
                Park(sal, p, fw);
            }

            // ── 비숍 반격 — 체력 50% 아래 한 대 → 막기(2.5초) → 반격 3 m ──
            var bishop = await Spawn("BishopKnight/BishopKnight", p.transform.position + fw * 2f, ct);
            await WaitReady(ct, bishop);
            if (bishop == null) Line(false, "비숍 소환");
            else
            {
                SetHp(bishop, Mathf.FloorToInt(bishop.EffectiveMaxHp * 0.5f) + 1);
                watch = bishop; firstHit = -1f; tStart = Time.time;
                Hit(p, bishop);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                bool guard = bishop.IsInSpecialState;
                int tele = Tele() - baseTele;
                while (Time.time - tStart < 5f && bishop.IsInSpecialState) await UniTask.Yield(PlayerLoopTiming.Update, ct);
                await Wait(0.3f, ct);
                int teleAfter = Tele() - baseTele;
                Line(guard && tele >= 1, $"비숍 — 막기 들어감 {guard} · 들어간 순간 예고 {tele}개");
                Line(firstHit >= 2f, $"비숍 — 반격 피해 {firstHit:0.00}초 뒤(막기 2.5초 · 그동안 예고)");
                Line(teleAfter == 0, $"비숍 — 끝난 뒤 남은 예고 {teleAfter}개");
                Park(bishop, p, fw);
            }

            // ── 웃는 버섯 덫 — 다가가면 0.4초 뒤 4 m 폭발 ──
            var mush = await Spawn("MushroomSmile/MushroomSmile", p.transform.position + fw * 1.5f, ct);
            if (mush == null) Line(false, "웃는 버섯 소환");
            else
            {
                watch = mush; firstHit = -1f; tStart = Time.time;
                int seen = 0;
                while (Time.time - tStart < 8f && firstHit < 0f) { seen = Mathf.Max(seen, Tele() - baseTele); await UniTask.Yield(PlayerLoopTiming.Update, ct); }
                await Wait(0.5f, ct);
                int teleAfter = Tele() - baseTele;
                Line(seen >= 1, $"웃는 버섯 — 덫이 터지기 전 예고 {seen}개(폭발 피해 {firstHit:0.00}초)");
                Line(teleAfter == 0, $"웃는 버섯 — 터진 뒤 남은 예고 {teleAfter}개");
                if (mush != null && !mush.IsDead) Park(mush, p, fw);
            }

            // ── 요정 박쥐 근접 — 원 예고 · 피해 전 ──
            var bat = await Spawn("FairyBat/FairyBat", p.transform.position + fw * 1.5f, ct);
            await WaitReady(ct, bat);
            if (bat == null) Line(false, "요정 박쥐 소환");
            else
            {
                SetHp(bat, 50000);
                watch = bat; firstHit = -1f; tStart = Time.time;
                int seen = 0; float seenAt = -1f;
                while (Time.time - tStart < 12f && firstHit < 0f)
                {
                    int n = Tele() - baseTele;
                    if (n > seen) { seen = n; if (seenAt < 0f) seenAt = Time.time - tStart; }
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
                await Wait(0.5f, ct);
                int teleAfter = Tele() - baseTele;
                bool melee = firstHit >= 0f;
                Line(!melee || (seen >= 1 && seenAt <= firstHit), $"요정 박쥐 — 첫 피해 {firstHit:0.00}초 · 그 전 예고 {seen}개({seenAt:0.00}초)(음파였으면 예고 없음이 맞다)");
                Line(teleAfter == 0, $"요정 박쥐 — 공격 뒤 남은 예고 {teleAfter}개");
                Park(bat, p, fw);
            }
        }
        catch (OperationCanceledException) { sb.AppendLine("취소됨"); }
        catch (Exception e) { Line(false, "예외 " + e.Message); Debug.LogException(e); }
        finally
        {
            p.OnDamageResolved -= OnPlayerDamage;
            sb.Insert(0, $"통과 {pass} · 실패 {fail}\n");
            System.IO.File.WriteAllText(OutPath, sb.ToString());
            Debug.Log($"[TelegraphLive] 끝 — 통과 {pass} · 실패 {fail} → {OutPath}");
        }
    }

    private static int Tele() => UnityEngine.Object.FindObjectsByType<MonsterTelegraph>(FindObjectsSortMode.None).Length;

    private static void Park(MonsterBase m, PlayerController p, Vector3 fw)
    {
        if (m == null || m.IsDead) return;
        m.Status.ApplyCc(HoldCc, 9999f);
        Warp(m, p.transform.position - fw * 18f);
    }

    private static void Face(MonsterBase m, Vector3 at)
    {
        var d = at - m.transform.position; d.y = 0f;
        if (d.sqrMagnitude > 1e-4f) m.transform.rotation = Quaternion.LookRotation(d.normalized);
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
