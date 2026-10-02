using UnityEngine;
using UnityEngine.AI;

namespace RelicFairy.Monster
{
/// <summary>
/// 보스 아레나 가드 — 보스가 맵 밖(벽 너머 · 걸을 수 있는 면 밖 · 벽 위 같은 외딴 NavMesh 조각)에 서 있으면 안쪽 자리를 구해 준다
/// (09-30 사용자 「밖에 벗어나면 안쪽으로 넣어 줘야 한다 — 보스도 맵 밖으로 벗어날 수 있다」).
///
/// 「안쪽」 = 기준점(플레이어 · 아레나 중심)에서 <b>NavMesh 길이 이어지는 곳</b>.
/// 바닥 상자 사각형은 벽보다 넓을 수 있어(Ch1 모서리) 기준으로 쓰지 않는다. 보스는 키네마틱이라 벽에 막히지 않고,
/// NavMesh는 런타임에 모든 콜라이더에서 구워져 벽 밖 · 벽 위에도 조각이 생긴다 — 「가장 가까운 NavMesh로 붙이기」는 그 조각에 내려앉을 수 있다.
/// 판정은 기준점 둘 중 하나에만 이어져도 안쪽이다(플레이어가 잠깐 외딴 자리에 서 있어도 보스를 끌어오지 않게).
/// 되돌릴 자리는 <b>아레나 중심과 이어진 곳</b>만 쓴다 — 플레이어가 벽 밖 주머니에 서 있을 때 그쪽 조각으로 끌려갔다(10-01 실측: Ch1 모서리).
///
/// NavMesh는 에이전트 종류마다 따로 구워진다 — 모든 질의에 <b>그 보스의 종류</b>를 밝힌다.
/// 종류를 안 밝힌 질의(areaMask 오버로드)는 다른 종류의 면을 섞어 봐서 발밑 면까지 「길이 끊김」으로 판정한다(10-01 e0 실측).
/// </summary>
public static class BossArenaGuard
{
    // ── Constants ──────────────────────────────────────────────
    private const float AnchorSample = 6f;     // 기준점을 NavMesh에 붙이는 반경
    private const float FootSample   = 2f;     // 보스 발밑 — 이 안에 걸을 수 있는 면이 없으면 밖
    private const float PullBack     = 1.5f;   // 이어진 면의 끝에서 안쪽으로 물리는 거리
    private static readonly float[] NearRadii = { 3f, 6f, 12f };

    private static NavMeshPath s_path;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => s_path = null;

    // ── Public Methods ─────────────────────────────────────────

    /// <summary>주기 확인용 — <paramref name="interval"/>초마다 true.</summary>
    public static bool Due(ref float timer, float dt, float interval = 1f)
    {
        timer += dt;
        if (timer < interval) return false;
        timer = 0f;
        return true;
    }

    /// <summary>
    /// 보스가 밖에 서 있으면 안쪽으로 옮긴다(에이전트가 켜져 있으면 Warp). 옮겼으면 true.
    /// 패턴이 없고 지상에 있을 때만 부를 것 — 일부 패턴은 일부러 밖을 지난다(화룡 브레스 쓸기).
    /// </summary>
    public static bool ReturnInside(Transform body, NavMeshAgent agent, Transform player, Vector3 arenaCenter)
    {
        if (body == null) return false;
        Vector3 pos = body.position;
        if (!TryPullInside(pos, pos.y, player, arenaCenter, agent, out var inside)) return false;

        Debug.LogWarning($"[BossArenaGuard] {body.name} 아레나 밖 {pos} → 안쪽 {inside}", body);
        body.position = inside;
        if (agent != null && agent.enabled) agent.Warp(inside);
        return true;
    }

    /// <summary>
    /// <paramref name="pos"/>(XZ, 바닥 높이 <paramref name="floorY"/>)가 밖이면 가장 가까운 안쪽 자리를 준다.
    /// 안쪽이거나 잴 수 없으면(기준점 둘 다 NavMesh에 못 붙음) false — 건드리지 않는다.
    /// <paramref name="agent"/> = 그 보스의 에이전트(꺼져 있어도 된다 — 종류만 읽는다).
    /// </summary>
    public static bool TryPullInside(Vector3 pos, float floorY, Transform player, Vector3 arenaCenter, NavMeshAgent agent,
                                     out Vector3 inside)
    {
        inside = pos;
        pos.y  = floorY;
        var filter = new NavMeshQueryFilter { agentTypeID = agent != null ? agent.agentTypeID : 0, areaMask = NavMesh.AllAreas };

        NavMeshHit a = default, b = default;
        bool hasA = player != null && NavMesh.SamplePosition(player.position, out a, AnchorSample, filter);
        bool hasB = NavMesh.SamplePosition(arenaCenter, out b, AnchorSample, filter);
        if (!hasA && !hasB) return false;

        // 발밑에 면이 있고 기준점과 이어져 있으면 안쪽
        if (NavMesh.SamplePosition(pos, out var foot, FootSample, filter)
            && Linked(foot.position, hasA, a.position, hasB, b.position, filter))
            return false;

        // 되돌릴 자리의 기준 — 아레나 중심이 먼저(없을 때만 플레이어)
        Vector3 anchor = hasB ? b.position : a.position;

        // ① 가까운 면 가운데 기준과 이어진 곳
        foreach (float radius in NearRadii)
        {
            if (!NavMesh.SamplePosition(pos, out var near, radius, filter)) continue;
            if (!PathComplete(anchor, near.position, filter)) continue;
            inside = near.position;
            return true;
        }

        // ② 기준에서 보스 쪽으로 곧게 — 이어진 면이 끝나는 자리에서 조금 안쪽
        NavMesh.Raycast(anchor, pos, out var edge, filter);
        Vector3 p    = edge.position;
        Vector3 back = anchor - p;
        back.y = 0f;
        p = back.sqrMagnitude > PullBack * PullBack ? p + back.normalized * PullBack : anchor;
        inside = NavMesh.SamplePosition(p, out var snap, FootSample, filter) ? snap.position : anchor;
        return true;
    }

    // ── Private Methods ────────────────────────────────────────

    private static bool Linked(Vector3 point, bool hasA, Vector3 a, bool hasB, Vector3 b, NavMeshQueryFilter filter)
        => (hasA && PathComplete(a, point, filter)) || (hasB && PathComplete(b, point, filter));

    private static bool PathComplete(Vector3 from, Vector3 to, NavMeshQueryFilter filter)
    {
        s_path ??= new NavMeshPath();
        return NavMesh.CalculatePath(from, to, filter, s_path) && s_path.status == NavMeshPathStatus.PathComplete;
    }
}
}
