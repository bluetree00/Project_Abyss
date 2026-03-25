using UnityEngine;

/// <summary>
/// 몬스터의 수치 데이터 SO.
/// HP / 방어 / 공격 / 이동속도 / 사정거리 / 공격 범위 / 공격속도 등을 보관.
/// </summary>
[CreateAssetMenu(fileName = "MonsterStat", menuName = "Lee/Monster/StatSO")]
public class MonsterStatSO : ScriptableObject
{
    [Header("기본 스탯")]
    public int   maxHp         = 10;
    public float defense       = 1f;
    public float attackPower   = 10f;
    public float moveSpeed     = 3f;

    [Header("공격 수치")]
    [Tooltip("공격 준비를 시작하는 거리 (m)")]
    public float attackRange   = 2f;

    [Tooltip("실제 히트 판정 반경 (m)")]
    public float attackRadius  = 1f;

    [Tooltip("공격 횟수 / 초")]
    public float attackRate    = 1f;

    [Tooltip("공격 상태 진입 후 실제 공격까지의 대기 시간 (s)")]
    public float attackDelay   = 1f;

    [Tooltip("플레이어에게 가할 넉백 힘")]
    public float knockbackForce = 5f;
}
