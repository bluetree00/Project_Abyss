using System.Collections.Generic;
using UnityEngine;

public class StagePointUI : MonoBehaviour
{
    [Header("Graph Config (Inspector 입력)")]
    [SerializeField] private int pointId;
    [SerializeField] private StageCategory stageCategory;
    [SerializeField] private List<int> nextPointIds = new();

    [Header("Normal 노드 룸 카테고리(Inspector에서 결정)")]
    [SerializeField] private NormalRoomCategory normalRoomCategory = NormalRoomCategory.Battle;

    [Header("필터(선택) - -1이면 미사용")]
    [SerializeField] private int minDifficulty = -1;
    [SerializeField] private int maxDifficulty = -1;
    [SerializeField] private List<string> requiredTags = new();

    [Header("Resolved (Runtime - Inspector 확인용)")]
    [SerializeField] private string resolvedRoomId;
    [SerializeField] private string resolvedRoomName;
    [SerializeField] private string resolvedRoomCategory;
    [SerializeField] private int resolvedDifficulty;
    [SerializeField] private string resolvedPrefab;
    [SerializeField] private string resolvedTags;

    private StagePointManager _mgr;
    private RoomManager _roomMgr;

    public void Register(StagePointManager mgr, RoomManager roomMgr)
    {
        _mgr = mgr;
        _roomMgr = roomMgr;

        mgr.Register(
            pointId: pointId,
            stageCategory: stageCategory,
            nextPointIds: nextPointIds,
            normalRoomCategory: normalRoomCategory,
            minDifficulty: (minDifficulty >= 0) ? (int?)minDifficulty : null,
            maxDifficulty: (maxDifficulty >= 0) ? (int?)maxDifficulty : null,
            requiredTags: (requiredTags != null && requiredTags.Count > 0) ? requiredTags : null
        );

        mgr.OnPointResolved -= HandleResolved;
        mgr.OnPointResolved += HandleResolved;
    }

    private void OnDisable()
    {
        if (_mgr != null)
            _mgr.OnPointResolved -= HandleResolved;
    }

    private void HandleResolved(int resolvedPointId, string roomId)
    {
        if (resolvedPointId != pointId) return;

        resolvedRoomId = roomId;

        var room = _roomMgr?.GetById(roomId);
        if (room == null)
        {
            resolvedRoomName = "(not found)";
            resolvedRoomCategory = "";
            resolvedDifficulty = 0;
            resolvedPrefab = "";
            resolvedTags = "";
            return;
        }

        resolvedRoomName = room.name;
        resolvedRoomCategory = room.category;
        resolvedDifficulty = room.difficulty;
        resolvedPrefab = room.prefab;
        resolvedTags = (room.tags == null) ? "" : string.Join(", ", room.tags);
    }
}
