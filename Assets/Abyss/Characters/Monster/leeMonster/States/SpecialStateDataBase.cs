using UnityEngine;

/// <summary>
/// 특수 상태 데이터 베이스 ScriptableObject.
/// 모든 특수 상태 데이터는 이를 상속해 독립 .asset 파일로 생성된 뒤
/// MonsterConfigSO 의 제약 타입별 슬롯에 드래그하여 참조한다.
///
/// CreateState() 를 구현해 자신에 맞는 특수 상태 인스턴스를 반환한다.
/// LeeMonsterBase 가 초기화 시 자동으로 호출한다.
/// </summary>
public abstract class SpecialStateDataBase : ScriptableObject
{
    /// <summary>이 데이터에 대응하는 특수 상태 인스턴스를 생성해 반환한다.</summary>
    public abstract LeeSpecialStateBase CreateState();
}
