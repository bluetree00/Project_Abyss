using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;

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

    // ─────────────────────────────────────────────────────────
    // 클릭 (Button 컴포넌트의 OnClick 또는 코드에서 직접 호출)
    // ─────────────────────────────────────────────────────────
    public void OnPointClicked()
    {
        Debug.Log($"[StagePointUI] Clicked pointId={pointId}");

        var run = GameRunBootstrapper.Instance?.Run;
        if (run == null || !run.IsRunning)
        {
            Debug.LogWarning($"[StagePointUI] pointId={pointId} → run null or not running. run={run}, IsRunning={run?.IsRunning}");
            return;
        }
        if (run.StagePointManager == null)
        {
            Debug.LogWarning($"[StagePointUI] pointId={pointId} → StagePointManager is null");
            return;
        }
        if (!run.StagePointManager.CanMove(pointId))
        {
            var cur = run.StagePointManager.CurrentPointId;
            Debug.LogWarning($"[StagePointUI] pointId={pointId} → CanMove=false  currentPointId={cur}");
            return;
        }

        Debug.Log($"[StagePointUI] pointId={pointId} → RequestMoveTo 호출");
        if (TransitionOverlay.Instance != null)
            TransitionOverlay.Instance.PlayAsync(() => run.RequestMoveTo(pointId)).Forget();
        else
            run.RequestMoveTo(pointId);
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
