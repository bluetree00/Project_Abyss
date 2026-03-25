using UnityEngine;

/// <summary>배회 방식 열거형.</summary>
public enum PatrolType
{
    /// <summary>스폰 기준 좌우 왕복</summary>
    Horizontal,
    /// <summary>스폰 기준 앞뒤 왕복</summary>
    Vertical,
    /// <summary>스폰 기준 랜덤 방향 이동</summary>
    Random,
}

/// <summary>
/// 몬스터 배회 패턴 SO.
/// </summary>
[CreateAssetMenu(fileName = "MonsterPatrol", menuName = "Abyss/Monster/PatrolSO")]
public class MonsterPatrolSO : ScriptableObject
{
    [Tooltip("배회 패턴 종류")]
    public PatrolType patrolType       = PatrolType.Horizontal;

    [Tooltip("스폰 위치 기준 배회 반경 (m)")]
    public float         patrolRange      = 3f;

    [Tooltip("배회 중 이동 속도 (m/s). 0이면 MonsterStatSO.moveSpeed 사용.")]
    public float         patrolSpeed      = 1.5f;

    [Tooltip("웨이포인트 도착 후 다음 이동 전 대기 시간 (s)")]
    public float         waypointWaitTime = 0.5f;
}
