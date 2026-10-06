using System.Collections.Generic;
using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 형 발동 피해 도우미 — 직선(풍절) · 원형(내려찍기). 적중 처리 안에서 불리므로 DealSynergyDamage만 쓴다(재귀 방지).
/// 맞힌 적 수를 돌려준다.
/// </summary>
public static class FormStrike
{
    /// <summary>from에서 dir로 length m, 폭 ±halfWidth 안의 살아 있는 적에게 damage.</summary>
    public static int Line(PlayerController p, Vector3 from, Vector3 dir, float length, float halfWidth, float damage, List<MonsterBase> buf)
    {
        dir.y = 0f;
        if (p == null || dir.sqrMagnitude < 1e-4f || length <= 0f) return 0;
        dir.Normalize();
        Vector3 to = from + dir * length;
        int n = CombatQuery.GetNearbyEnemies((from + to) * 0.5f, length * 0.5f + halfWidth + 0.5f, p.gameObject, 32, buf);
        int hits = 0;
        for (int i = 0; i < n; i++)
        {
            var e = buf[i];
            if (e == null || e.IsDead || DistToSegmentXZ(e.transform.position, from, to) > halfWidth + 0.4f) continue;
            CombatQuery.DealSynergyDamage(e, damage, p.gameObject);
            hits++;
        }
        return hits;
    }

    /// <summary>두 갈래 직선(쌍풍) — 어느 갈래에 들었든 적마다 한 번만 맞는다(넓게 — 두 번 치는 것은 되감는 바람의 몫).</summary>
    public static int Lines(PlayerController p, Vector3 from, Vector3 dirA, Vector3 dirB, float length, float halfWidth, float damage, List<MonsterBase> buf)
    {
        dirA.y = 0f; dirB.y = 0f;
        if (p == null || dirA.sqrMagnitude < 1e-4f || dirB.sqrMagnitude < 1e-4f || length <= 0f) return 0;
        Vector3 toA = from + dirA.normalized * length, toB = from + dirB.normalized * length;
        int n = CombatQuery.GetNearbyEnemies(from, length + halfWidth + 0.5f, p.gameObject, 32, buf);
        int hits = 0;
        for (int i = 0; i < n; i++)
        {
            var e = buf[i];
            if (e == null || e.IsDead) continue;
            Vector3 at = e.transform.position;
            if (DistToSegmentXZ(at, from, toA) > halfWidth + 0.4f && DistToSegmentXZ(at, from, toB) > halfWidth + 0.4f) continue;
            CombatQuery.DealSynergyDamage(e, damage, p.gameObject);
            hits++;
        }
        return hits;
    }

    /// <summary>center 반경 radius 안의 적에게 damage. stun > 0이면 보스 제외 경직, ampPct > 0이면 경직한 적 받피증.</summary>
    public static int Radius(PlayerController p, Vector3 center, float radius, float damage, List<MonsterBase> buf,
                             float stun = 0f, float ampPct = 0f, float ampSeconds = 0f)
    {
        if (p == null || radius <= 0f) return 0;
        int n = CombatQuery.GetNearbyEnemies(center, radius, p.gameObject, 32, buf);
        int hits = 0;
        for (int i = 0; i < n; i++)
        {
            var e = buf[i];
            if (e == null || e.IsDead) continue;
            CombatQuery.DealSynergyDamage(e, damage, p.gameObject);
            hits++;
            if (stun <= 0f || e.IsDead || e.Grade == MonsterGrade.Boss) continue;   // 보스는 경직 없이 피해만
            e.ApplyStun(stun);
            if (ampPct > 0f) e.ApplyDamageTakenAmp(ampPct, ampSeconds, "form_crush");
        }
        return hits;
    }

    public static void PlayVfx(string key, Vector3 at, Vector3 forward, float scale = 1f)
    {
        if (!string.IsNullOrEmpty(key)) RelicStateVfx.PlayOneShot(key, at, scale, forward);
    }

    public static float DistToSegmentXZ(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector2 P = new(p.x, p.z), A = new(a.x, a.z), B = new(b.x, b.z);
        Vector2 ab = B - A;
        float t = ab.sqrMagnitude > 0.0001f ? Mathf.Clamp01(Vector2.Dot(P - A, ab) / ab.sqrMagnitude) : 0f;
        return Vector2.Distance(P, A + ab * t);
    }
}
