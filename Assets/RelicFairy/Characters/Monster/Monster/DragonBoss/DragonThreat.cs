using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 화룡 위협 공용 규칙(10-14 사용자 「챕터2 보스 패턴이 위협적이지 않다 — 메테오 양 · 크기 · 범위 불규칙 · 플레이어를 노리는 메테오 · 돌진 · 근접도 위협적으로」).
///   · 플레이어 예측 — 지금 속도(평면 · 6 m/s 상한)로 몇 초 뒤 자리. 예고는 그 자리에 그리고 그때 굳힌다(예고 = 판정 · 멈추거나 꺾으면 피한다).
///   · 메테오 모양 — 반경 r칸 원, 바깥 테두리 칸 일부를 빼서 매번 모양이 다르다. 크기 셋(1 · 2 · 3) + 노림(2) · 큰 메테오(4).
///   · 피해 — 기본 한 방(공격력 × 패턴 배율, 지금 390)이 플레이어 최대 HP의 41~60%라 피해를 올리지 않고 맞을 확률로 위협을 키운다.
///     크기별 0.8 ~ 1.4배(큰 메테오가 상한 — 한 방에 죽지 않게).
/// </summary>
public static class DragonThreat
{
    public const float MaxPlayerSpeed = 6f;

    // 메테오 크기 — 반경(칸) · 뽑을 몫 · 피해 배율
    public static readonly int[]   ScatterRadius = { 1, 2, 3 };
    public static readonly float[] ScatterWeight = { 0.55f, 0.35f, 0.10f };
    public static readonly float[] ScatterDamage = { 0.8f, 1.0f, 1.2f };
    public const int   AimedRadius = 2;
    public const float AimedDamage = 1.0f;
    public const int   BigRadius   = 4;
    public const float BigDamage   = 1.4f;
    public const float EdgeDrop    = 0.3f;    // 바깥 테두리 칸을 뺄 확률
    public const float MeteorReachAtOne = 4.8f; // 운석 이펙트(Meteor.prefab) 배율 1의 보이는 반경(충격파까지 · 10-14 실측 2.14 / 4.81 m) — 예전 배율 1은 3×3칸(반폭 1.5 m)의 1.4~3.2배였다

    /// <summary>플레이어 평면 속도(m/s, 6 상한) — 몸의 Rigidbody 기준. 없으면 0.</summary>
    public static Vector3 PlayerVelocity(MonsterContext ctx)
    {
        var t = ctx?.Runtime?.PlayerTarget;
        if (t == null || !t.TryGetComponent<Rigidbody>(out var rb)) return Vector3.zero;
        Vector3 v = rb.linearVelocity; v.y = 0f;
        return v.sqrMagnitude > MaxPlayerSpeed * MaxPlayerSpeed ? v.normalized * MaxPlayerSpeed : v;
    }

    /// <summary>플레이어의 <paramref name="seconds"/>초 뒤 자리(지금 속도 그대로) — 높이는 지금 그대로.</summary>
    public static Vector3 PredictPlayer(MonsterContext ctx, float seconds)
    {
        var t = ctx?.Runtime?.PlayerTarget;
        if (t == null) return ctx != null ? ctx.Transform.position : Vector3.zero;
        return t.position + PlayerVelocity(ctx) * Mathf.Max(0f, seconds);
    }

    /// <summary>흩뿌림 크기 뽑기 — 반경(칸)과 몫 순번(피해 배율 칸).</summary>
    public static int PickScatterRadius(out int tier)
    {
        float v = Random.value;
        for (tier = 0; tier < ScatterRadius.Length - 1; tier++)
        {
            if (v < ScatterWeight[tier]) break;
            v -= ScatterWeight[tier];
        }
        return ScatterRadius[tier];
    }

    /// <summary>반경 r칸 원 — 바깥 테두리 칸을 <paramref name="drop"/> 확률로 뺀다. 방 안쪽 칸만.</summary>
    public static List<Vector2Int> Footprint(Vector2Int c, int r, float drop)
    {
        var cells = new List<Vector2Int>((2 * r + 1) * (2 * r + 1));
        float outer = (r + 0.4f) * (r + 0.4f), inner = (r - 0.6f) * (r - 0.6f);
        for (int dx = -r; dx <= r; dx++)
        for (int dz = -r; dz <= r; dz++)
        {
            int d2 = dx * dx + dz * dz;
            if (d2 > outer) continue;
            if (d2 > inner && d2 > 0 && Random.value < drop) continue;
            int x = c.x + dx, z = c.y + dz;
            if (DragonBossRoomContext.IsInterior(x, z)) cells.Add(new Vector2Int(x, z));
        }
        return cells;
    }

    /// <summary>운석 이펙트 배율 — 보이는 반경이 판정 반폭(r + 0.5칸)에 맞게(반경 1칸 0.31 · 2칸 0.52 · 3칸 0.73 · 4칸 0.94).</summary>
    public static float MeteorScale(int r) => (r + 0.5f) * DragonBossRoomContext.CellSize / MeteorReachAtOne;

    // 운석끼리 겹쳐 같은 순간 여러 번 맞으면(맞은 뒤 무적이 없다) 2초에 1994 — 최대 HP를 넘었다(10-14 실측). 운석 피해는 이만큼 사이를 둔다
    public const float MeteorHitGap = 0.6f;
    private static float s_meteorHitAt = -99f;

    /// <summary>운석 한 대를 넣어도 되는가 — 직전 운석 피해에서 <see cref="MeteorHitGap"/>초가 지났으면 true(그리고 지금을 적는다).</summary>
    public static bool TryMeteorHit()
    {
        if (Time.time - s_meteorHitAt < MeteorHitGap) return false;
        s_meteorHitAt = Time.time;
        return true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => s_meteorHitAt = -99f;

    public static Vector2Int ClampInterior(Vector2Int c)
        => new(Mathf.Clamp(c.x, 1, DragonBossRoomContext.Width - 2), Mathf.Clamp(c.y, 1, DragonBossRoomContext.Height - 2));
}
}
