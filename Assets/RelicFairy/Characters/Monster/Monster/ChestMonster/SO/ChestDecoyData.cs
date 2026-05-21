using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 체스트 몬스터 위장 특수 상태 데이터 SO.
/// 플레이어 접근 전까지 상자처럼 무적 대기 상태를 유지한다.
/// ChestMonsterConfig 의 specialStates 리스트에 추가한다.
/// </summary>
[CreateAssetMenu(fileName = "ChestDecoyData", menuName = "Lee/Monster/Special/ChestDecoyData")]
public class ChestDecoyData : SpecialStateDataBase
{
    [Tooltip("대기 중 재생할 애니메이션 스테이트 이름")]
    public string idleStateName = "IdleChest";

    [Tooltip("전투 개시까지의 감지 거리 (m)")]
    public float activateRange = 3.0f;

    public override SpecialStateBase CreateState() => new ChestIdleDecoyState(this);
}
