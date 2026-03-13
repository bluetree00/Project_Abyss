using UnityEngine;

/// <summary>
/// 스폰 지점으로 귀환하는 몬스터가 구현하는 인터페이스.
/// LeeBatChaseState / LeeBatReturnState 등 귀환 State가 이 인터페이스를 통해 데이터에 접근한다.
/// </summary>
public interface IReturnableMonster
{
    Vector3 SpawnPoint      { get; }
    float   ReturnDistance  { get; }
    float   ArrivalDistance { get; }
}
