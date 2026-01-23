using System.Collections.Generic;
using UnityEngine;

public class StagePointUI : MonoBehaviour
{
    [SerializeField] private bool isStartPoint;
    [SerializeField] int pointId;
    [SerializeField] List<int> nextPointIds;
    [SerializeField] StageCategory stageCategory;
    [SerializeField] NormalRoomCategory normalRoomCategory;

    public void Register(StagePointManager manager)
    {
        manager.Register(
            pointId,
            stageCategory,
            nextPointIds,
            normalRoomCategory
        );
    }
}
