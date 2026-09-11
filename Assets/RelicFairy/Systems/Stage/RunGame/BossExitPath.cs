using System;
using UnityEngine;

/// <summary>
/// 보스방 클리어 시 다음 챕터 게이트(<see cref="ChapterGate"/>)를 <b>아레나 안쪽 출구 자리</b>에 세운다.
///
/// 예전엔 출구 마커에서 아레나 밖으로 18~19m 다리를 깔고 그 끝에 게이트를 세웠다. 절차 생성 방은 옆 방·지형이
/// 바로 붙어 있어 다리가 맵을 뚫었고, 마커가 없으면 바닥 경계 추정으로 엉뚱한 곳에, 아레나가 없거나 계산이 실패하면
/// 호출측이 넘긴 <b>보스 사망 위치</b>에 게이트가 났다. 게이트는 순간이동 장치라 다리가 없어도 성립하므로
/// 다리를 없애고 위치 규칙만 남긴다:
///   1) Next_Ch 마커 → 2) Exit 마커 : 마커에서 아레나 중심 쪽으로 <see cref="GateInset"/>만큼 물러난 자리(벽 앞, 바닥 위)
///   3) 마커 없음                     : 아레나 중심에서 입구(PlayerSpawn) 반대편으로 <see cref="FallbackDist"/>
///   4) 아레나 없음(격자 보스방)       : 방 중심(호출측이 방 루트 위치를 넘긴다 — 보스 위치가 아니다)
/// Y는 Ground 레이캐스트로 바닥에 스냅한다. 벽은 건드리지 않는다 — 구멍 뒤는 허공이라 떨어진다.
/// </summary>
public static class BossExitPath
{
    // ── Constants ──────────────────────────────────────────────
    /// <summary>출구 마커(벽 위치)에서 아레나 안쪽으로 물러나는 거리. 게이트 폭 3.5m가 벽에 파묻히지 않게.</summary>
    private const float GateInset    = 3.0f;
    /// <summary>마커가 없을 때 아레나 중심에서 보스 뒤편으로 나가는 거리 — 보스 시체·중앙 연출과 겹치지 않게.</summary>
    private const float FallbackDist = 6.0f;
    /// <summary>바닥 레이어 — MapBuilder가 방 바닥에 쓰는 값과 동일.</summary>
    private const int   GroundLayer  = 3;

    // ── Public Methods ─────────────────────────────────────────
    /// <summary>보스방 클리어 시 호출. 게이트를 세우고 그 위치를 돌려준다(카메라 연출용).</summary>
    public static Vector3 Spawn(Vector3 roomCenter, Transform arena)
    {
        Vector3 pos = ResolveGatePosition(roomCenter, arena);
        pos.y = SnapToGroundY(pos, arena);
        ChapterGate.Spawn(pos);
        Debug.Log($"[BossExitPath] 챕터 게이트 배치 {pos} (arena={(arena != null ? arena.name : "없음")})");
        return pos;
    }

    // ── Private Methods ────────────────────────────────────────
    private static Vector3 ResolveGatePosition(Vector3 roomCenter, Transform arena)
    {
        if (arena == null) return roomCenter;

        Vector3 center = arena.position;
        Transform marker = FindMarker(arena, "Next_Ch") ?? FindMarker(arena, "Exit");
        if (marker != null)
        {
            // 마커는 벽 위치다 — 중심 쪽으로 물러나 바닥 위에 선다. 마커가 중심과 겹치면 마커 정면의 반대로.
            Vector3 inward = Flatten(center - marker.position);
            if (inward.sqrMagnitude < 0.01f) inward = -Flatten(marker.forward);
            if (inward.sqrMagnitude < 0.01f) inward = Vector3.back;
            return marker.position + inward.normalized * GateInset;
        }

        // 마커 없음 — 입구 반대편(보스 뒤편). 입구도 없으면 아레나 정면.
        var spawn = arena.Find("PlayerSpawn");
        Vector3 dir = spawn != null ? Flatten(center - spawn.position) : Flatten(arena.forward);
        if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
        Debug.LogWarning("[BossExitPath] 출구 마커(Next_Ch/Exit) 없음 — 입구 반대편에 게이트를 세운다");
        return center + dir.normalized * FallbackDist;
    }

    /// <summary>이름이 prefix로 시작하는 자식(비활성 포함)을 찾는다.</summary>
    private static Transform FindMarker(Transform arena, string prefix)
    {
        var all = arena.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
            if (all[i] != arena && all[i].name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return all[i];
        return null;
    }

    /// <summary>XZ에서 Ground 레이어를 내리쏴 걷는 바닥 Y를 구한다. 마커 Y가 바닥 아래일 수 있어 높은 지점에서 쏜다.
    /// 실패 시 아레나 Floor 렌더러 상단, 그마저 없으면 입력 Y.</summary>
    private static float SnapToGroundY(Vector3 pos, Transform arena)
    {
        int mask = 1 << GroundLayer;
        var origin = new Vector3(pos.x, pos.y + 100f, pos.z);
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 200f, mask, QueryTriggerInteraction.Collide))
            return hit.point.y;

        if (arena != null)
        {
            foreach (Transform child in arena)
            {
                if (child.name.IndexOf("Floor", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (child.TryGetComponent<Renderer>(out var rend)) return rend.bounds.max.y;
            }
        }
        return pos.y;
    }

    private static Vector3 Flatten(Vector3 v)
    {
        v.y = 0f;
        return v;
    }
}
