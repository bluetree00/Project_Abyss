using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed class StagePointManager
{
    public ChapterId CurrentChapter { get; private set; }

    private readonly Dictionary<int, StagePointContext> _contexts = new();
    public IReadOnlyDictionary<int, StagePointContext> Contexts => _contexts;

    private RoomManager _roomManager;

    public int CurrentPointId { get; private set; } = -1;

    // (pointId, resolvedRoomId)
    public event Action<int, string> OnPointResolved;

    public void Initialize(ChapterId chapter, RoomManager roomManager)
    {
        CurrentChapter = chapter;
        _roomManager = roomManager;
        _contexts.Clear();
        CurrentPointId = -1;
    }

    // ---------- Registration ----------
    public StagePointContext Register(
        int pointId,
        StageCategory stageCategory,
        IReadOnlyList<int> nextPointIds,
        NormalRoomCategory normalRoomCategory = NormalRoomCategory.Random,
        int? minDifficulty = null,
        int? maxDifficulty = null,
        IReadOnlyList<string> requiredTags = null)
    {
        if (_contexts.ContainsKey(pointId))
        {
            Debug.LogWarning($"[StagePointManager] Duplicate pointId: {pointId}");
            return _contexts[pointId];
        }

        var ctx = new StagePointContext(pointId, stageCategory, nextPointIds, normalRoomCategory);
        ctx.SetFilters(minDifficulty, maxDifficulty, requiredTags);

        _contexts.Add(pointId, ctx);
        return ctx;
    }

    public StagePointContext GetContext(int pointId)
        => _contexts.TryGetValue(pointId, out var ctx) ? ctx : null;

    // ---------- Start ----------
    public StagePointContext GetStartPoint()
        => _contexts.Values.FirstOrDefault(c => c.StageCategory == StageCategory.Start);

    public void SetStartAsCurrent()
    {
        var start = GetStartPoint();
        if (start == null)
        {
            Debug.LogError("[StagePointManager] Start point not found");
            return;
        }

        if (start.State == StagePointState.Locked)
            start.SetState(StagePointState.Available);

        // Start도 데이터 기반 Resolve
        Resolve(start);

        CurrentPointId = start.PointId;
        MarkVisited(CurrentPointId);
        UnlockNextPoints(CurrentPointId);
    }

    // ---------- Resolve ----------
    public void ResolveAll()
    {
        foreach (var ctx in _contexts.Values)
            Resolve(ctx);
    }

    public void Resolve(StagePointContext ctx)
    {
        if (ctx == null || ctx.IsResolved)
            return;

        if (_roomManager == null || !_roomManager.IsInitialized)
        {
            Debug.LogError("[StagePointManager] Resolve failed: RoomManager not ready");
            return;
        }

        RoomData picked = null;

        switch (ctx.StageCategory)
        {
            case StageCategory.Start:
            {
                picked = _roomManager.Pick(RoomCategory.Start, ctx.MinDifficulty, ctx.MaxDifficulty, ctx.RequiredTags);
                break;
            }

            case StageCategory.Boss:
            {
                picked = _roomManager.Pick(RoomCategory.Boss, ctx.MinDifficulty, ctx.MaxDifficulty, ctx.RequiredTags);
                break;
            }

            case StageCategory.Normal:
            {
                // ✅ 개선: Random은 "가능한 카테고리만" 골라서 선택 (필터 고려)
                RoomCategory cat =
                    (ctx.NormalRoomCategory == NormalRoomCategory.Random)
                        ? PickRandomCategoryThatHasCandidates(ctx)
                        : ResolveNormalCategoryNonRandom(ctx.NormalRoomCategory);

                if (cat == RoomCategory.Unknown)
                {
                    Debug.LogError(
                        $"[StagePointManager] Normal Resolve failed (no valid category). " +
                        $"pointId={ctx.PointId}, normal={ctx.NormalRoomCategory}, " +
                        $"min={ctx.MinDifficulty?.ToString() ?? "null"}, max={ctx.MaxDifficulty?.ToString() ?? "null"}, " +
                        $"tags={(ctx.RequiredTags == null ? "null" : string.Join(",", ctx.RequiredTags))}"
                    );
                    return;
                }

                picked = _roomManager.Pick(cat, ctx.MinDifficulty, ctx.MaxDifficulty, ctx.RequiredTags);
                break;
            }
        }

        var roomId = picked?.roomId;
        if (string.IsNullOrEmpty(roomId))
        {
            Debug.LogError(
                $"[StagePointManager] Resolve failed. " +
                $"pointId={ctx.PointId}, stage={ctx.StageCategory}, normal={ctx.NormalRoomCategory}, " +
                $"min={ctx.MinDifficulty?.ToString() ?? "null"}, max={ctx.MaxDifficulty?.ToString() ?? "null"}, " +
                $"tags={(ctx.RequiredTags == null ? "null" : string.Join(",", ctx.RequiredTags))}"
            );
            return;
        }

        ctx.SetResolvedRoomId(roomId);
        OnPointResolved?.Invoke(ctx.PointId, roomId);
    }

    // ---------- Category Resolve (Improved) ----------

    /// <summary>
    /// Random이 아닐 때: 단순 매핑(이름이 통일되어 있으므로 1:1)
    /// </summary>
    private static RoomCategory ResolveNormalCategoryNonRandom(NormalRoomCategory normal)
    {
        return normal switch
        {
            NormalRoomCategory.Battle => RoomCategory.Battle,
            NormalRoomCategory.Elite  => RoomCategory.Elite,
            NormalRoomCategory.Event  => RoomCategory.Event,
            NormalRoomCategory.Shop   => RoomCategory.Shop,
            _ => RoomCategory.Unknown
        };
    }

    /// <summary>
    /// Random일 때: (difficulty/tags 필터를 고려해) 후보가 있는 카테고리만 모아 그 중 랜덤 선택
    /// - 기존 문제: Random으로 Event/Shop을 뽑았는데 minDifficulty=1이면 후보 0개 → Resolve 실패
    /// - 해결: "후보가 있는 카테고리"에서만 Random
    /// </summary>
    private RoomCategory PickRandomCategoryThatHasCandidates(StagePointContext ctx)
    {
        // 필요하면 여기서 Random 풀을 조정(예: Elite 제외 등) 가능
        RoomCategory[] pool =
        {
            RoomCategory.Battle,
            RoomCategory.Elite,
            RoomCategory.Event,
            RoomCategory.Shop
        };

        var valid = new List<RoomCategory>(pool.Length);

        for (int i = 0; i < pool.Length; i++)
        {
            var cat = pool[i];

            // "이 카테고리 + 이 필터"로 뽑을 수 있는 방이 존재하는지 검사
            var test = _roomManager.Pick(cat, ctx.MinDifficulty, ctx.MaxDifficulty, ctx.RequiredTags);
            if (test != null)
                valid.Add(cat);
        }

        if (valid.Count == 0)
            return RoomCategory.Unknown;

        return valid[UnityEngine.Random.Range(0, valid.Count)];
    }

    // ---------- Movement / State ----------
    public bool CanMove(int targetPointId)
    {
        if (CurrentPointId < 0) return false;

        var cur = GetContext(CurrentPointId);
        if (cur == null) return false;

        if (!cur.NextPointIds.Contains(targetPointId))
            return false;

        var target = GetContext(targetPointId);
        if (target == null) return false;

        return target.State == StagePointState.Available;
    }

    public IReadOnlyList<int> GetAvailableNextPoints()
    {
        if (CurrentPointId < 0) return Array.Empty<int>();

        var cur = GetContext(CurrentPointId);
        if (cur == null) return Array.Empty<int>();

        return cur.NextPointIds
            .Where(id => _contexts.TryGetValue(id, out var c) && c.State == StagePointState.Available)
            .ToList();
    }

    public bool TryMoveTo(int targetPointId)
    {
        if (!CanMove(targetPointId))
            return false;

        CurrentPointId = targetPointId;

        MarkVisited(CurrentPointId);
        UnlockNextPoints(CurrentPointId);

        return true;
    }

    private void MarkVisited(int pointId)
    {
        if (!_contexts.TryGetValue(pointId, out var ctx))
            return;

        if (ctx.State == StagePointState.Available)
            ctx.SetState(StagePointState.Visited);

        // 방문 시점에 Resolve 보장
        Resolve(ctx);
    }

    public void MarkCleared(int pointId)
    {
        if (!_contexts.TryGetValue(pointId, out var ctx))
            return;

        ctx.SetState(StagePointState.Cleared);
        UnlockNextPoints(pointId);
    }

    private void UnlockNextPoints(int fromPointId)
    {
        if (!_contexts.TryGetValue(fromPointId, out var from))
            return;

        foreach (var nextId in from.NextPointIds)
        {
            if (_contexts.TryGetValue(nextId, out var next))
            {
                if (next.State == StagePointState.Locked)
                    next.SetState(StagePointState.Available);
            }
        }
    }
}
