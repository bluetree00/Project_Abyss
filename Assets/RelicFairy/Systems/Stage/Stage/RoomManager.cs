using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// STAGEDATA(rooms) 로드 + 인덱싱 + 필터 + 가중치 랜덤 선택
/// </summary>
public sealed class RoomManager
{
    private readonly Dictionary<string, RoomData> _byId = new();
    private readonly Dictionary<RoomCategory, List<RoomData>> _byCategory = new();

    public bool IsInitialized { get; private set; }

    public async UniTask InitializeAsync(string addressableKey, Func<string, UniTask<TextAsset>> loader)
    {
        IsInitialized = false;
        _byId.Clear();
        _byCategory.Clear();

        TextAsset textAsset = null;
        try
        {
            textAsset = await loader(addressableKey);
        }
        catch (Exception e)
        {
            Debug.LogError($"[RoomManager] Failed to load rooms: {e.Message}");
            return;
        }

        if (textAsset == null)
        {
            Debug.LogError("[RoomManager] Rooms TextAsset is null");
            return;
        }

        var root = JsonUtility.FromJson<RoomDataRoot>(textAsset.text);
        if (root?.rooms == null || root.rooms.Count == 0)
        {
            Debug.LogError("[RoomManager] rooms is empty");
            return;
        }

        foreach (var room in root.rooms)
        {
            if (room == null || string.IsNullOrWhiteSpace(room.roomId))
                continue;

            _byId[room.roomId] = room;

            var cat = RoomCategoryUtil.Parse(room.category);
            if (cat == RoomCategory.Unknown)
            {
                Debug.LogWarning($"[RoomManager] Unknown category. roomId={room.roomId}, raw='{room.category}'");
                continue;
            }

            if (!_byCategory.TryGetValue(cat, out var list))
            {
                list = new List<RoomData>();
                _byCategory.Add(cat, list);
            }
            list.Add(room);
        }

        IsInitialized = true;
        Debug.Log($"[RoomManager] Loaded rooms: {_byId.Count}");
    }

    public RoomData GetById(string roomId)
        => _byId.TryGetValue(roomId, out var room) ? room : null;

    /// <summary>
    /// category + 옵션 조건으로 후보군을 만든 뒤 weight 기반으로 1개 선택
    /// </summary>
    public RoomData Pick(
        RoomCategory category,
        int? minDifficulty = null,
        int? maxDifficulty = null,
        IEnumerable<string> requiredTags = null)
    {
        if (!IsInitialized)
        {
            Debug.LogError("[RoomManager] Not initialized");
            return null;
        }

        if (!_byCategory.TryGetValue(category, out var list) || list.Count == 0)
            return null;

        IEnumerable<RoomData> q = list;

        if (minDifficulty.HasValue) q = q.Where(r => r.difficulty >= minDifficulty.Value);
        if (maxDifficulty.HasValue) q = q.Where(r => r.difficulty <= maxDifficulty.Value);

        if (requiredTags != null)
        {
            var tagSet = requiredTags
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (tagSet.Count > 0)
            {
                q = q.Where(r =>
                    r.tags != null &&
                    tagSet.All(rt => r.tags.Any(t => string.Equals(t, rt, StringComparison.OrdinalIgnoreCase)))
                );
            }
        }

        var candidates = q.Where(r => r.weight > 0).ToList();
        if (candidates.Count == 0)
            return null;

        return WeightedPick(candidates);
    }

    private static RoomData WeightedPick(List<RoomData> rooms)
    {
        int total = 0;
        for (int i = 0; i < rooms.Count; i++)
            total += rooms[i].weight;

        if (total <= 0)
            return rooms[UnityEngine.Random.Range(0, rooms.Count)];

        int roll = UnityEngine.Random.Range(0, total);
        int acc = 0;

        for (int i = 0; i < rooms.Count; i++)
        {
            acc += rooms[i].weight;
            if (roll < acc)
                return rooms[i];
        }

        return rooms[^1];
    }


    public bool HasCandidates(
    RoomCategory category,
    int? minDifficulty = null,
    int? maxDifficulty = null,
    IEnumerable<string> requiredTags = null)
    {
        if (!IsInitialized) return false;
        if (!_byCategory.TryGetValue(category, out var list) || list.Count == 0) return false;

        IEnumerable<RoomData> q = list;

        if (minDifficulty.HasValue) q = q.Where(r => r.difficulty >= minDifficulty.Value);
        if (maxDifficulty.HasValue) q = q.Where(r => r.difficulty <= maxDifficulty.Value);

        if (requiredTags != null)
        {
            var tagSet = requiredTags
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (tagSet.Count > 0)
            {
                q = q.Where(r =>
                    r.tags != null &&
                    tagSet.All(rt => r.tags.Any(t => string.Equals(t, rt, StringComparison.OrdinalIgnoreCase)))
                );
            }
        }

        // weight > 0 후보가 있는지만 체크
        return q.Any(r => r.weight > 0);
    }


}
