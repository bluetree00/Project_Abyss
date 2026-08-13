using UnityEngine;

/// <summary>
/// 서비스 방(상점·재련소·정제소) NPC 무대 소품 배치 공용 유틸.
///
/// 기존 문제: NPC 앞/뒤에 고정 반경으로 소품을 뿌리면서 <b>벽을 전혀 보지 않아</b>
/// 매대가 방 가장자리에 붙은 상점에서 소품이 벽 덩어리 안에 박혔다.
/// (상점 NPC는 방 중앙이 아니라 <b>매대 중심</b>에 서기 때문에 벽에 매우 가깝다.)
///
/// 여기서는 배치 후보 지점을 벽(Wall 레이어)과 겹치는지 검사해 안전한 지점만 사용한다.
/// </summary>
public static class ServiceRoomDecorPlacer
{
    private const int   WallLayer     = 8;      // MapBuilder 규약(Wall=8, Ground=3)
    private const float ClearRadius   = 0.75f;  // 소품이 차지한다고 보는 반경(m)
    private const float WallKeepOut   = 1.1f;   // 벽에서 최소 이 정도는 떨어뜨린다
    private const float NpcClearRadius= 2.0f;   // NPC 몸에서 이보다 가까이는 소품을 두지 않는다(관통 방지)
    private const int   AngleTries    = 12;     // 후보 각도 재시도 수
    private const float RadiusShrink  = 0.65f;  // 자리가 없으면 반경을 줄여 재시도

    private static readonly Collider[] _hits = new Collider[8];

    /// <summary>방 블록이 막 생성된 직후 물리 쿼리를 쓰기 전에 1회 호출(콜라이더 등록 보장).</summary>
    public static void SyncPhysics() => Physics.SyncTransforms();

    /// <summary>해당 지점이 벽과 겹치지 않는가.</summary>
    public static bool IsFree(Vector3 pos, float radius = ClearRadius)
    {
        int n = Physics.OverlapSphereNonAlloc(
            pos + Vector3.up * 0.5f, radius, _hits, 1 << WallLayer, QueryTriggerInteraction.Ignore);
        return n == 0;
    }

    /// <summary>
    /// 기준점(NPC) 주변에서 원하는 방향·거리에 가장 가까운 <b>빈 자리</b>를 찾는다.
    /// 각도를 좌우로 흔들어 보고, 그래도 없으면 반경을 줄여 재시도한다. 전부 실패하면 false.
    /// </summary>
    public static bool TryFindSpot(Vector3 origin, Vector3 dir, float radius, float groundY,
                                   out Vector3 result)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
        dir.Normalize();

        for (int pass = 0; pass < 3; pass++)
        {
            float r = radius * Mathf.Pow(RadiusShrink, pass);
            // NPC 관통 방지 — 요청 반경이 처음부터 작아도(카운터 등) 이보다 가까이는 절대 두지 않는다.
            if (r < NpcClearRadius) { if (pass == 0) r = NpcClearRadius; else break; }

            for (int i = 0; i < AngleTries; i++)
            {
                // 0°, +30°, -30°, +60°, -60° … 원하는 방향에서 점점 벌어지며 탐색
                float sign  = (i % 2 == 0) ? 1f : -1f;
                float delta = 30f * ((i + 1) / 2) * sign;
                Vector3 d   = Quaternion.Euler(0f, delta, 0f) * dir;

                Vector3 p = origin + d * r;
                p.y = groundY;
                if (IsFree(p)) { result = p; return true; }
            }
        }

        result = origin;
        return false;
    }

    /// <summary>소품 1개 배치 + 바닥 스냅(피벗이 메시 중심인 Gothic 소품이 뜨는 것 방지).</summary>
    public static GameObject Place(GameObject prefab, Vector3 pos, float yaw, float groundY,
                                   Transform parent, string name = null)
    {
        if (prefab == null) return null;

        var go = Object.Instantiate(prefab, pos, Quaternion.Euler(0f, yaw, 0f), parent);
        if (!string.IsNullOrEmpty(name)) go.name = name;

        var rends = go.GetComponentsInChildren<MeshRenderer>();
        if (rends.Length == 0) return go;

        var b = rends[0].bounds;
        for (int r = 1; r < rends.Length; r++) b.Encapsulate(rends[r].bounds);
        go.transform.position += new Vector3(0f, groundY - b.min.y, 0f);
        return go;
    }

    /// <summary>
    /// grid_csv가 찍어둔 <see cref="ServiceDecorAnchor"/> 자리에 소품을 놓는다.
    /// 앵커가 하나도 없으면 <c>false</c>를 돌려 호출부가 기존 탐색 배치로 폴백하게 한다(회귀 0).
    ///
    /// 매칭 규칙 — <c>Counter</c> 앵커 = <paramref name="prefabs"/>[0],
    /// <c>Prop</c> 앵커 = [1] 이후를 <b>배치 순서대로</b>. 앵커가 남으면 소품을 순환시키지 않고 비운다
    /// (같은 소품이 두 번 서는 것보다 빈 자리가 낫다).
    /// 소품은 모두 NPC를 바라본다 — 무대의 중심이 NPC라는 것을 형태로 알린다.
    /// </summary>
    public static bool TryPlaceFromAnchors(Transform room, GameObject[] prefabs,
                                           Vector3 npcPos, float groundY, string counterName = null)
    {
        if (room == null || prefabs == null || prefabs.Length == 0) return false;

        var anchors = room.GetComponentsInChildren<ServiceDecorAnchor>(true);
        if (anchors == null || anchors.Length == 0) return false;

        int propIndex = 1;   // [0]은 카운터 몫
        for (int i = 0; i < anchors.Length; i++)
        {
            var a = anchors[i];
            if (a == null) continue;

            GameObject prefab;
            string name = null;
            if (a.Kind == ServiceDecorAnchor.Slot.Counter)
            {
                prefab = prefabs[0];
                name = counterName;
            }
            else
            {
                if (propIndex >= prefabs.Length) continue;   // 소품이 모자라면 그 자리는 비운다
                prefab = prefabs[propIndex++];
            }
            if (prefab == null) continue;

            Vector3 pos = a.transform.position;
            pos.y = groundY;

            // 소품이 NPC를 향하도록 — 기존 탐색 경로와 같은 규약.
            Vector3 toNpc = npcPos - pos;
            toNpc.y = 0f;
            float yaw = toNpc.sqrMagnitude > 0.0001f
                ? Mathf.Atan2(toNpc.x, toNpc.z) * Mathf.Rad2Deg
                : a.transform.eulerAngles.y;

            Place(prefab, pos, yaw, groundY, room, name);
        }
        return true;
    }

    /// <summary>NPC가 벽을 등지도록 바라볼 방향을 고른다 — 주변에서 가장 트인 쪽.
    /// (방 블록이 막 생성된 직후 호출되므로 여기서 물리 트랜스폼을 1회 동기화한다.)</summary>
    public static Quaternion ResolveFacing(Vector3 npcPos, Quaternion fallback)
    {
        // 방금 Instantiate된 벽 콜라이더가 쿼리에 잡히도록 보장(autoSyncTransforms=false 대비).
        Physics.SyncTransforms();

        float bestScore = -1f;
        Vector3 best = fallback * Vector3.forward;

        for (int i = 0; i < 12; i++)
        {
            Vector3 d = Quaternion.Euler(0f, i * 30f, 0f) * Vector3.forward;
            // 그 방향으로 얼마나 멀리까지 벽 없이 트여 있는지
            float score = 0f;
            for (float dist = 1.5f; dist <= 6f; dist += 1.5f)
            {
                if (!IsFree(npcPos + d * dist, 0.6f)) break;
                score = dist;
            }
            if (score > bestScore) { bestScore = score; best = d; }
        }

        return bestScore > 0f ? Quaternion.LookRotation(best) : fallback;
    }
}
