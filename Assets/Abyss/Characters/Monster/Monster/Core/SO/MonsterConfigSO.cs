using Abyss.Monster;
using UnityEngine;

/// <summary>몬스터 등급.</summary>
public enum MonsterGrade { Normal, Elite, Boss }

/// <summary>
/// 몬스터 마스터 설정 SO.
/// 이 하나의 SO가 모든 서브 SO를 참조한다.
/// Addressables 주소 하나만 알면 몬스터 전체 설정을 로드할 수 있음.
/// </summary>
[CreateAssetMenu(fileName = "MonsterConfig", menuName = "Lee/Monster/ConfigSO")]
public class MonsterConfigSO : ScriptableObject
{
    [Header("기본 정보")]
    public string       monsterName;
    public MonsterGrade grade = MonsterGrade.Normal;

    [Header("레이어")]
    [Tooltip("플레이어 레이어 마스크 (감지 및 공격 판정에 공통 사용)")]
    public LayerMask playerLayer;

    [Header("SO 모듈 — 각 항목은 별도 SO로 분리 관리")]
    public MonsterStatSO      stat;
    public MonsterDetectionSO detection;
    public MonsterPatrolSO    patrol;
    public MonsterCombatSO    combat;
    public MonsterAnimationSO animation;

    [Header("특수 상태 데이터 (없으면 비워둠)")]
    public SpecialStateDataBase specialState0;
    public SpecialStateDataBase specialState1;
    public SpecialStateDataBase specialState2;
    public SpecialStateDataBase specialState3;
}
