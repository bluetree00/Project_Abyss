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
    public const float MeteorReachAtOne = 4.8f; // 운석 이펙트(Meteor.prefab) 배율 1의 땅 효과 보이는 반경(충격파까지 · 10-14 실측 2.14 / 4.81 m)

    // 10-19 사용자 「맨 처음 쓰던 메테오 리소스가 좋았다 — 키워서 더 높은 곳에서 떨어져 웅장하게」(10-18 몸통/착탄 분리는 걷음):
    // Meteor.prefab 하나 — 떨어지는 불덩이(뿌리 입자)와 꼬리는 크게(배율만큼 더 높은 곳에서 · 0.4초 그대로),
    // 땅 효과 자식만 판정 크기(MeteorScale)로 되돌려 「예고 = 판정」을 지킨다.
    public const float BodyScale    = 1.6f;   // 흩뿌림 불덩이(예전 판정 크기 0.31~0.94)
    public const float BigBodyScale = 2.4f;   // 노림 · 큰 메테오
    private static readonly HashSet<string> TrailParts = new() { "SubFire", "SubSmoke", "SubSparks" };   // 뿌리(불덩이)의 꼬리 — 땅 효과가 아니다
    private const float SkyPartY      = 1f;     // 로컬 높이가 이보다 높은 자식 = 하늘 부분(출발 섬광 Glow · Flare — 출발점 (0, 7, −4))
    private const float NativeFallTime = 0.4f;  // Meteor.prefab 불덩이 입자 수명(배율과 무관)
    private static readonly List<ParticleSystem> s_meteorPs = new();

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

    /// <summary>
    /// 운석 하나를 착탄 자리에 띄운다 — 불덩이(뿌리 입자)와 꼬리는 <paramref name="bodyScale"/>(클수록 더 높은 곳에서 떨어진다),
    /// 땅 효과 자식(분화구 · 충격파 · 불 · 연기 · 파편)은 <paramref name="groundScale"/>(판정 크기). 풀 인스턴스라 매번 다시 맞춘다.
    /// </summary>
    public static GameObject SpawnMeteor(GameObject prefab, Vector3 land, float bodyScale, float groundScale,
                                         float fallSeconds = NativeFallTime, bool fromCameraFar = true)
    {
        if (prefab == null) return null;
        // 출발점(로컬 −Z 위)이 카메라 먼 쪽 = 화면 위에서 들어오게(identity면 늘 월드 −Z에서 왔다 · 10-19 01 조사)
        Quaternion rot = Quaternion.identity;
        var cam = fromCameraFar ? Camera.main : null;
        if (cam != null) { Vector3 f = cam.transform.forward; f.y = 0f; if (f.sqrMagnitude > 0.01f) rot = Quaternion.LookRotation(-f.normalized); }
        var go = BossEffectPool.Spawn(prefab, land, rot);
        if (go == null) return null;
        go.transform.localScale = Vector3.one * bodyScale;
        float k = groundScale / Mathf.Max(0.01f, bodyScale);
        foreach (Transform c in go.transform)
            c.localScale = TrailParts.Contains(c.name) || c.localPosition.y > SkyPartY ? Vector3.one : Vector3.one * k;
        // 보이는 착탄 = 피해 순간 — 불덩이 수명 0.4초를 낙하 시간(ImpactDelay)에 맞춰 재생 속도를 늦춘다(땅 효과도 그만큼 무겁게)
        float speed = NativeFallTime / Mathf.Max(NativeFallTime, fallSeconds);
        go.GetComponentsInChildren(true, s_meteorPs);
        foreach (var ps in s_meteorPs) { var m = ps.main; m.simulationSpeed = speed; }
        s_meteorPs.Clear();
        return go;
    }

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
