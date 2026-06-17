using UnityEngine;

namespace RelicFairy.Monster
{
    internal static class DragonPatternFloorUtils
    {
        private const float SampleHeight = 50f;
        private const float SampleDepth  = 100f;

        private static int s_groundLayerMask = -1;

        /// <summary>
        /// xzPos 위치 바로 위에서 Raycast를 내려 실제 바닥 Y를 반환한다.
        /// 충돌이 없으면 fallbackY를 반환한다.
        /// Ground 레이어만 검사 — 플레이어/몬스터 콜라이더가 그 위치에 있어도 바닥 높이를 정확히 구한다.
        /// </summary>
        internal static float GetFloorY(Vector3 xzPos, float fallbackY)
        {
            if (s_groundLayerMask < 0)
                s_groundLayerMask = 1 << LayerMask.NameToLayer("Ground");

            var origin = new Vector3(xzPos.x, fallbackY + SampleHeight, xzPos.z);
            return Physics.Raycast(origin, Vector3.down, out RaycastHit hit, SampleDepth, s_groundLayerMask)
                ? hit.point.y
                : fallbackY;
        }

        /// <summary>현재 룸의 바닥 영역(XZ)을 DragonBossRoomContext 기준으로 반환한다.</summary>
        internal static Bounds GetRoomFloorBoundsXZ()
        {
            Vector3 center = DragonBossRoomContext.WorldCenter;
            Vector3 size = new Vector3(
                DragonBossRoomContext.Width  * DragonBossRoomContext.CellSize,
                0f,
                DragonBossRoomContext.Height * DragonBossRoomContext.CellSize);
            return new Bounds(center, size);
        }

        /// <summary>
        /// 룸 바닥의 가로+세로 합 — DistanceToFloorEdge의 fallback으로 사용하면
        /// 어떤 방향이든 실제 바닥 경계 거리가 fallback보다 작아 항상 바닥 끝까지의 거리가 반환된다.
        /// </summary>
        internal static float GetRoomMaxExtent()
        {
            Bounds bounds = GetRoomFloorBoundsXZ();
            return bounds.size.x + bounds.size.z;
        }

        /// <summary>origin에서 direction(수평) 방향으로 룸 바닥 경계까지의 거리. 경계를 못 찾으면 fallback.</summary>
        internal static float DistanceToFloorEdge(Vector3 origin, Vector3 direction, float fallback)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return fallback;
            direction.Normalize();

            Bounds bounds = GetRoomFloorBoundsXZ();
            float result = fallback;

            if (Mathf.Abs(direction.x) > 0.0001f)
            {
                float boundX = direction.x > 0f ? bounds.max.x : bounds.min.x;
                float t = (boundX - origin.x) / direction.x;
                if (t > 0f) result = Mathf.Min(result, t);
            }
            if (Mathf.Abs(direction.z) > 0.0001f)
            {
                float boundZ = direction.z > 0f ? bounds.max.z : bounds.min.z;
                float t = (boundZ - origin.z) / direction.z;
                if (t > 0f) result = Mathf.Min(result, t);
            }
            return result;
        }

        /// <summary>씬의 BoxCollider 중 referencePos를 포함하는 가장 적합한 바닥 영역(XZ)을 찾는다. 못 찾으면 fallback 정사각형.</summary>
        internal static Bounds ResolveArenaBoundsXZ(Vector3 referencePos, float fallbackHalfSize)
        {
            float fallbackSide = Mathf.Max(1f, fallbackHalfSize * 2f);
            var fallback = new Bounds(referencePos, new Vector3(fallbackSide, 0f, fallbackSide));

            float bestScore = float.NegativeInfinity;
            Bounds best = fallback;

            foreach (var collider in Object.FindObjectsOfType<BoxCollider>())
            {
                if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy)
                    continue;

                Bounds bounds = collider.bounds;
                if (!ContainsXZ(bounds, referencePos))
                    continue;

                Vector3 size = bounds.size;
                float side = Mathf.Min(size.x, size.z);
                if (side <= 1f)
                    continue;

                float squareness = 1f - Mathf.Clamp01(Mathf.Abs(size.x - size.z) / Mathf.Max(size.x, size.z));
                float namePriority = GetArenaNamePriority(collider.name);
                float areaPenalty = Mathf.Clamp(side / 200f, 0f, 1f);
                float score = namePriority + squareness - areaPenalty;
                if (score <= bestScore)
                    continue;

                bestScore = score;
                best = bounds;
            }

            return best;
        }

        private static bool ContainsXZ(Bounds bounds, Vector3 point)
        {
            return point.x >= bounds.min.x && point.x <= bounds.max.x
                && point.z >= bounds.min.z && point.z <= bounds.max.z;
        }

        private static float GetArenaNamePriority(string name)
        {
            if (string.IsNullOrEmpty(name))
                return 0f;

            string lower = name.ToLowerInvariant();
            if (lower.Contains("safefloor"))
                return 3f;
            if (lower.Contains("bossfloor"))
                return 2.25f;
            if (lower.Contains("boss") && lower.Contains("floor"))
                return 1.5f;
            if (lower.Contains("floor"))
                return 1f;
            return 0f;
        }

        /// <summary>
        /// 공중 패턴 종료 후 지상 복귀 시 모든 패턴에서 공통으로 사용한다.
        /// Transform Y를 SpawnPosition.y(최초 정상 착지 높이)로 스냅한 뒤
        /// NavMesh.SamplePosition으로 가장 가까운 NavMesh 지점에 Agent를 Warp한다.
        /// SpawnPosition.y는 초기 착지 시 정상 동작이 확인된 높이이므로
        /// Raycast 오차나 애니메이션 드리프트로 인한 NavMesh 이탈을 방지한다.
        /// </summary>
        internal static void SnapToFloorAndRestoreAgent(MonsterContext ctx)
        {
            Vector3 pos = ctx.Transform.position;
            pos.y = ctx.Runtime.SpawnPosition.y;
            ctx.Transform.position = pos;

            if (ctx.Agent == null) return;
            if (!ctx.Agent.enabled) ctx.Agent.enabled = true;
            if (!ctx.Agent.isOnNavMesh
                && UnityEngine.AI.NavMesh.SamplePosition(pos, out var hit, 5f, UnityEngine.AI.NavMesh.AllAreas))
            {
                ctx.Agent.Warp(hit.position);
            }
        }
    }
}
