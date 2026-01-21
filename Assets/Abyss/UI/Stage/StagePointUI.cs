using System.Collections.Generic;
using UnityEngine;

public class StagePointUI : MonoBehaviour
{
    [Header("Graph")]
    [SerializeField] private int pointId;
    [SerializeField] private List<int> nextPointIds;

    [Header("Stage Request")]
    [SerializeField] private ChapterId chapterId;
    [SerializeField] private StageCategory stageCategory;
    [SerializeField] private NormalRoomCategory normalRoomCategory;

    public int PointId => pointId;
    public IReadOnlyList<int> NextPointIds => nextPointIds;
    public ChapterId ChapterId => chapterId;
    public StageCategory StageCategory => stageCategory;
    public NormalRoomCategory NormalRoomCategory => normalRoomCategory;

    private void OnEnable()
    {
        Managers.GameRun.StagePointManager.Register(
            pointId,
            stageCategory,
            nextPointIds,
            normalRoomCategory
        );
    }


}
