using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
public enum AttackSlotKind { Melee, Ranged }

/// <summary>
/// 동시 공격 상한(10-11 일반방 긴장감 P2-②) — 플레이어를 동시에 노리는(공격 준비 · 공격 · 특수 공격) 몬스터 수를 제한한다.
/// 근접 Ch1 2 · Ch2부터 3 · 원거리 2. 칸을 못 잡은 몬스터는 플레이어 둘레를 돌며 기다린다(멈춰 구경하지 않게).
///
/// 줄 서기 — 칸이 비면 오래 기다린 몬스터가 먼저 들어온다. 공격 하나를 끝낸 몬스터는 칸을 놓고 줄 맨 뒤에 다시 선다
/// (한 마리가 계속 쥐면 나머지가 둘러서서 구경만 한다 — 배틀 서클 실패담).
/// 보스는 세지 않는다. 쥔 몬스터가 죽거나 꺼지거나 공격 밖 상태로 가면 다음 조회에서 저절로 풀린다.
/// 정예 접두어(f9)의 돌진은 근접 칸, 박격 · 포박 원 예고는 원거리 칸을 쓴다.
/// </summary>
public static class MonsterAttackSlots
{
    public const int   RangedCap  = 2;
    private const float WaitStale = 0.6f;   // 이만큼 다시 묻지 않은 대기자는 줄에서 뺀다(멀어졌거나 다른 일을 한다)
    private const float ApproachGrace = 1.5f;   // 칸을 잡고 다가가는 동안(추격 상태)엔 칸을 지킨다

    private static readonly Dictionary<MonsterBase, (AttackSlotKind kind, float at)> s_holders = new();
    private static readonly Dictionary<MonsterBase, (float since, float lastAsk, AttackSlotKind kind)> s_waiters = new();
    private static readonly List<MonsterBase> s_drop = new();
    private static MonsterBase s_exclusive;   // 정예 큰 공격 중 — 이 동안 다른 근접은 칸을 못 잡는다(10-13 「정예 큰 공격 겹침 막기」)

    /// <summary>근접 동시 상한 — Ch1 2 · Ch2부터 3.</summary>
    public static int MeleeCap
    {
        get
        {
            var run = GameRunBootstrapper.Instance?.Run;
            int ch = run != null ? (int)run.CurrentChapter : 1;
            return ch <= 1 ? 2 : 3;
        }
    }

    /// <summary>공격 칸을 잡는다. 이미 쥐고 있으면 true. 못 잡으면 줄에 선다(false).</summary>
    public static bool TryAcquire(MonsterBase m, AttackSlotKind kind)
    {
        if (m == null) return false;
        if (m.Grade == MonsterGrade.Boss) return true;
        if (s_holders.TryGetValue(m, out var held) && held.kind == kind) return true;
        Prune();
        float now = Time.time;
        float mySince = s_waiters.TryGetValue(m, out var me) && me.kind == kind ? me.since : now;

        bool free = Count(kind) < Cap(kind) && !(kind == AttackSlotKind.Melee && s_exclusive != null && s_exclusive != m);
        // 정예가 먼저 · 같은 급이면 오래 기다린 몬스터가 먼저 — 잡몹 둘이 칸을 돌려 쓰는 동안 정예가 5~7초 둘레만 돌았다(10-14 정예 점검)
        bool elite = m.Grade == MonsterGrade.Elite;
        if (free)
            foreach (var kv in s_waiters)
            {
                if (kv.Key == null || kv.Key == m || kv.Value.kind != kind) continue;
                bool otherElite = kv.Key.Grade == MonsterGrade.Elite;
                if ((otherElite && !elite) || (otherElite == elite && kv.Value.since < mySince)) { free = false; break; }
            }

        if (!free)
        {
            s_waiters[m] = (mySince, now, kind);
            return false;
        }
        s_waiters.Remove(m);
        s_holders[m] = (kind, now);
        return true;
    }

    /// <summary>정예 큰 공격을 시작한다 — 끝날 때까지 다른 몬스터는 근접 칸을 새로 못 잡는다(이미 쥔 공격은 그대로).</summary>
    public static void BeginExclusive(MonsterBase m) { if (m != null && m.Grade != MonsterGrade.Boss) s_exclusive = m; }

    /// <summary>정예 큰 공격이 끝났다(끊김 · 사망 포함).</summary>
    public static void EndExclusive(MonsterBase m) { if (s_exclusive == m) s_exclusive = null; }

    /// <summary>칸을 놓는다(공격 하나가 끝났을 때).</summary>
    public static void Release(MonsterBase m)
    {
        if (m != null) s_holders.Remove(m);
    }

    /// <summary>지금 그 종류 칸을 쥔 수.</summary>
    public static int Count(AttackSlotKind kind)
    {
        int n = 0;
        foreach (var kv in s_holders) if (kv.Value.kind == kind) n++;
        return n;
    }

    private static int Cap(AttackSlotKind kind) => kind == AttackSlotKind.Melee ? MeleeCap : RangedCap;

    private static void Prune()
    {
        if (s_exclusive != null && (s_exclusive.IsDead || !s_exclusive.isActiveAndEnabled || !s_exclusive.IsInState<AttackState>()))
            s_exclusive = null;   // Exit를 못 거친 경우(꺼짐 · 사망)도 다음 조회에서 풀린다
        s_drop.Clear();
        float now = Time.time;
        foreach (var kv in s_holders)
        {
            var m = kv.Key;
            // 빙결 · 기절 · 석화로 묶인 몬스터는 상태가 공격 준비로 남아도 공격하지 않는다 — 칸을 쥔 채 다른 몬스터를 막지 않게(10-14)
            bool attacking = m != null && !m.Status.IsCcActive
                          && (m.IsInState<AttackReadyState>() || m.IsInState<AttackState>() || m.IsInSpecialState);
            if (m == null || m.IsDead || !m.isActiveAndEnabled || (!attacking && now - kv.Value.at > ApproachGrace))
                s_drop.Add(m);
        }
        foreach (var m in s_drop) s_holders.Remove(m);

        s_drop.Clear();
        foreach (var kv in s_waiters)
            if (kv.Key == null || kv.Key.IsDead || !kv.Key.isActiveAndEnabled || now - kv.Value.lastAsk > WaitStale)
                s_drop.Add(kv.Key);
        foreach (var m in s_drop) s_waiters.Remove(m);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_holders.Clear();
        s_waiters.Clear();
        s_exclusive = null;
    }
}
}
