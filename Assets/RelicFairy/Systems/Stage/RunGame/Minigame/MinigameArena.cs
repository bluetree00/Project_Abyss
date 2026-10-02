using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 놀이판 구하기 — 방 가운데 · 쓸 수 있는 반경 · 설 수 있는 자리.
/// 방 가운데 = 방 오브젝트 위치(미니맵과 같은 기준). 반경 = 가운데에서 가장 가까운 NavMesh 가장자리(벽 · 막힌 장식)까지 − 여유.
/// NavMesh 질의는 에이전트 종류를 밝힌 필터로만 한다(두 종류로 따로 구워진다 — 10-01 함정).
/// </summary>
public static class MinigameArena
{
    private const float EdgeMargin = 1.2f;   // 가장자리에서 이만큼 안쪽까지만 놀이판
    private const float MinRadius  = 3f;
    private const float SampleUp   = 4f;

    private static readonly NavMeshQueryFilter s_filter = new NavMeshQueryFilter { agentTypeID = 0, areaMask = NavMesh.AllAreas };

    public static NavMeshQueryFilter Filter => s_filter;

    /// <summary>
    /// 놀이판을 정한다. <paramref name="roomCenter"/>(방 오브젝트 위치)에서 가까운 걸을 수 있는 면 → 가운데.
    /// 못 찾으면 플레이어 자리. 반경은 <paramref name="want"/>를 넘지 않고, 가장자리까지 거리 − 여유로 줄인다(최소 3 m).
    /// </summary>
    public static void Resolve(Vector3 roomCenter, Vector3 playerPos, float want, out Vector3 center, out float radius)
    {
        var probe = new Vector3(roomCenter.x, playerPos.y, roomCenter.z);
        center = NavMesh.SamplePosition(probe, out var hit, SampleUp, s_filter) ? hit.position : playerPos;

        radius = want;
        if (NavMesh.FindClosestEdge(center, out var edge, s_filter))
            radius = Mathf.Clamp(edge.distance - EdgeMargin, MinRadius, want);
    }

    /// <summary><paramref name="p"/>에서 가까운 걸을 수 있는 면(반경 <paramref name="snap"/> 안). 없으면 false.</summary>
    public static bool Snap(Vector3 p, float snap, out Vector3 onMesh)
    {
        if (NavMesh.SamplePosition(p, out var hit, snap, s_filter)) { onMesh = hit.position; return true; }
        onMesh = p;
        return false;
    }

    /// <summary>
    /// 가운데 둘레 반경 <paramref name="ring"/>에 자리 <paramref name="count"/>개(<paramref name="yaw0"/>도부터 고르게).
    /// 자리마다 걸을 수 있는 면에 붙이고, 못 붙이면 반경을 줄여 다시 — 끝내 못 구한 자리는 뺀다.
    /// </summary>
    public static List<Vector3> RingSpots(Vector3 center, float ring, int count, float yaw0)
    {
        var spots = new List<Vector3>(count);
        for (int i = 0; i < count; i++)
        {
            float yaw = yaw0 + 360f * i / count;
            Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            for (float r = ring; r >= MinRadius * 0.5f; r -= 0.75f)
            {
                if (Snap(center + dir * r, 0.6f, out var p) && IsLinked(center, p))
                {
                    spots.Add(p);
                    break;
                }
            }
        }
        return spots;
    }

    /// <summary>가운데에서 그 자리까지 걸어서 닿는가.</summary>
    public static bool IsLinked(Vector3 from, Vector3 to)
    {
        var path = new NavMeshPath();
        return NavMesh.CalculatePath(from, to, s_filter, path) && path.status == NavMeshPathStatus.PathComplete;
    }

    /// <summary>놀이판 안 무작위 자리(가운데에서 <paramref name="minR"/>~<paramref name="maxR"/>, 걸을 수 있는 면). 못 구하면 가운데.</summary>
    public static Vector3 RandomSpot(System.Random rng, Vector3 center, float minR, float maxR)
    {
        for (int tries = 0; tries < 12; tries++)
        {
            float yaw = (float)rng.NextDouble() * 360f;
            float r   = Mathf.Lerp(minR, maxR, (float)rng.NextDouble());
            Vector3 p = center + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * r;
            if (Snap(p, 0.8f, out var onMesh)) return onMesh;
        }
        return center;
    }
}
