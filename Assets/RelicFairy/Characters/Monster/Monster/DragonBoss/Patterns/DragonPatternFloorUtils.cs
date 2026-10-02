using UnityEngine;

namespace RelicFairy.Monster
{
    internal static class DragonPatternFloorUtils
    {
        private const float SampleHeight = 50f;
        private const float SampleDepth  = 100f;

        private static int s_groundLayerMask = -1;

        /// <summary>바닥(Ground 레이어)만 — 착지점 레이캐스트용. 마스크가 없으면 공중 화룡 몸 · 트리거 위에 떨어졌다(10-01 감사).</summary>
        internal static int GroundMask
        {
            get
            {
                if (s_groundLayerMask < 0)
                    s_groundLayerMask = 1 << LayerMask.NameToLayer("Ground");
                return s_groundLayerMask;
            }
        }

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

        /// <summary>벽 안면 = 바닥 상자 가장자리에서 이만큼 안쪽(Ch2 아레나 실측: 상자 ±32.5 · 벽 안면 ±31.5, 10-02).</summary>
        internal const float WallInset = 1f;

        /// <summary>
        /// 수평 위치를 「벽 안면 − 몸 반경」 안으로 끌어들인다(10-03 개선 1-3). 공중 · 패턴 이동은 transform을 직접 옮겨
        /// 아레나 가드가 닿지 않고, 경계가 바닥 상자(벽보다 1 m 바깥) 기준인 데다 몸 크기를 안 빼 머리 · 날개가 벽 너머로 나갔다.
        /// 아주 작은 바닥이면 가장자리에서 반폭의 90 %까지만 줄인다. Y는 건드리지 않는다.
        /// </summary>
        internal static Vector3 ClampInsideWalls(Vector3 pos, float bodyRadius)
        {
            Bounds b  = GetRoomFloorBoundsXZ();
            float  m  = WallInset + Mathf.Max(0f, bodyRadius);
            float  mx = Mathf.Min(m, b.extents.x * 0.9f);
            float  mz = Mathf.Min(m, b.extents.z * 0.9f);
            pos.x = Mathf.Clamp(pos.x, b.min.x + mx, b.max.x - mx);
            pos.z = Mathf.Clamp(pos.z, b.min.z + mz, b.max.z - mz);
            return pos;
        }

        /// <summary>화룡 몸 반경(<see cref="DragonBossMonster.BodyRadius"/>) — 화룡이 아니면 0(벽 안면까지만).</summary>
        internal static float BodyRadiusOf(MonsterContext ctx)
            => ctx.Monster is DragonBossMonster dragon ? dragon.BodyRadius : 0f;

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
        /// Transform Y를 SpawnPosition.y(최초 정상 착지 높이)로 스냅한 뒤 NavMesh 복구를 시도한다.
        /// SpawnPosition.y는 초기 착지 시 정상 동작이 확인된 높이이므로
        /// Raycast 오차나 애니메이션 드리프트로 인한 NavMesh 이탈을 방지한다.
        /// </summary>
        internal static void SnapToFloorAndRestoreAgent(MonsterContext ctx)
        {
            Vector3 pos = ctx.Transform.position;
            pos.y = ctx.Runtime.SpawnPosition.y;
            ctx.Transform.position = PullInsideArena(ctx, pos);

            EnsureAgentOnNavMesh(ctx);
        }

        /// <summary>
        /// 바닥 높이의 <paramref name="pos"/>가 아레나 밖(벽 너머 · 외딴 NavMesh 조각)이면 안쪽 자리를 돌려준다(09-30).
        /// 공중 패턴이 벽 밖에서 끝나면 높이만 맞춘 채 그 자리에 내려앉았고, 뒤이은 복구는 가장 가까운 NavMesh(벽 밖 조각 포함)에 붙였다.
        /// </summary>
        internal static Vector3 PullInsideArena(MonsterContext ctx, Vector3 pos)
        {
            if (!BossArenaGuard.TryPullInside(pos, ctx.Runtime.SpawnPosition.y, ctx.Runtime.PlayerTarget,
                                              DragonBossRoomContext.WorldCenter, ctx.Agent, out var inside))
                return pos;
            Debug.LogWarning($"[BossArenaGuard] 화룡 아레나 밖 {pos} → 안쪽 {inside}", ctx.Monster);
            return inside;
        }

        // 단계적으로 넓혀가며 시도할 NavMesh 검색 반경 — 한 번 실패해도 절대 포기하지 않는다.
        private static readonly float[] s_navMeshSearchRadii = { 5f, 15f, 40f, 100f };

        /// <summary>
        /// 드래곤이 Floor 위에서 절대 멈춰서지 않도록 보장하는 NavMesh 복구 루틴.
        /// 현재 위치 기준으로 검색 반경을 단계적으로 넓혀 시도하고, 그래도 실패하면
        /// 룸 중앙(항상 NavMesh가 존재해야 하는 기준점)으로 강제 복귀시킨다.
        /// 이 마지노선까지 실패하면 NavMesh 베이크 자체가 문제이므로 에러 로그로 표면화한다.
        /// </summary>
        /// <returns>Agent가 NavMesh 위에 정상 복구되었는지 여부.</returns>
        internal static bool EnsureAgentOnNavMesh(MonsterContext ctx)
        {
            if (ctx.Agent == null) return false;
            if (!ctx.Agent.enabled) ctx.Agent.enabled = true;

            // 밖(벽 너머 · 외딴 조각)이면 먼저 안쪽으로 — 아래 「가장 가까운 NavMesh」는 벽 밖 조각에도 붙는다(09-30)
            Vector3 pos = PullInsideArena(ctx, ctx.Transform.position);
            if (pos != ctx.Transform.position)
            {
                ctx.Transform.position = pos;
                ctx.Agent.Warp(pos);
            }
            if (ctx.Agent.isOnNavMesh) return true;

            foreach (float radius in s_navMeshSearchRadii)
            {
                if (!UnityEngine.AI.NavMesh.SamplePosition(pos, out var hit, radius, UnityEngine.AI.NavMesh.AllAreas))
                    continue;

                ctx.Transform.position = hit.position;
                ctx.Agent.Warp(hit.position);
                if (ctx.Agent.isOnNavMesh) return true;
            }

            // 마지노선: 룸 중앙은 항상 NavMesh가 깔려 있어야 하는 기준점 — 여기서도 실패하면 더 이상 코드로 복구 불가.
            Vector3 roomCenter = DragonBossRoomContext.WorldCenter;
            if (UnityEngine.AI.NavMesh.SamplePosition(roomCenter, out var centerHit, 50f, UnityEngine.AI.NavMesh.AllAreas))
            {
                ctx.Transform.position = centerHit.position;
                ctx.Agent.Warp(centerHit.position);
                if (ctx.Agent.isOnNavMesh) return true;
            }

            Debug.LogError($"[DragonBoss] NavMesh 복구 완전 실패 — pos={pos}, roomCenter={roomCenter}. " +
                            "해당 보스룸의 NavMesh 베이크 또는 Floor 경계 설정을 확인해야 합니다.");
            return false;
        }
    }
}
