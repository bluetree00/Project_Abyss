using System;
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

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// 몬스터 데이터 — 순수 데이터 [Serializable] 클래스.
// MonsterConfigSO 에 직접 임베드되므로 별도 .asset 파일 불필요.
// MonsterJsonData.ApplyToConfig 로 JSON 서버 값을 덮어쓸 수 있다.
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

/// <summary>몬스터 수치 데이터.</summary>
[Serializable]
public class MonsterStatData
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

    [Header("공격 형태 (없으면 기본 구체 판정 사용)")]
    [Tooltip("공격 판정 형태 SO. 비워두면 attackRadius 기반 구체 판정.")]
    public MonsterAttackShapeSO attackShape;
}

/// <summary>몬스터 감지·추격 포기 데이터.</summary>
[Serializable]
public class MonsterDetectionData
{
    [Tooltip("플레이어 인식 거리 (m)")]
    public float detectionRange   = 5f;
    [Tooltip("이 거리를 초과하면 추격 포기 후 배회로 복귀 (m). detectionRange 보다 크게 설정.")]
    public float chaseGiveUpRange = 8f;
}

/// <summary>몬스터 배회 패턴 데이터.</summary>
[Serializable]
public class MonsterPatrolData
{
    [Tooltip("배회 패턴 종류")]
    public PatrolType patrolType      = PatrolType.Horizontal;
    [Tooltip("스폰 위치 기준 배회 반경 (m)")]
    public float      patrolRange     = 3f;
    [Tooltip("배회 중 이동 속도 (m/s). 0이면 MonsterStatData.moveSpeed 사용.")]
    public float      patrolSpeed     = 1.5f;
    [Tooltip("웨이포인트 도착 후 다음 이동 전 대기 시간 (s)")]
    public float      waypointWaitTime = 0.5f;
}

/// <summary>공격 타이밍·판정 레이어 데이터.</summary>
[Serializable]
public class MonsterCombatData
{
    [Tooltip("공격 애니메이션 시작 후 실제 데미지 판정까지 지연 시간 (s). " +
             "애니메이션 이벤트로 데미지를 주는 경우 0으로 설정.")]
    public float     damageApplyDelay = 0.4f;
    [Tooltip("공격 히트 판정 대상 레이어 (Player 레이어 설정)")]
    public LayerMask targetLayer;
}

/// <summary>몬스터 애니메이션 설정 데이터.</summary>
[Serializable]
public class MonsterAnimationData
{
    [Header("Animator Controller")]
    [Tooltip("Addressables 에 등록된 AnimatorOverrideController 주소.\n" +
             "비어있으면 프리팹 Animator 에 붙은 Controller 를 그대로 사용.")]
    public string animatorControllerAddress;

    [Header("CrossFade 상태 이름")]
    public string idleStateName        = "Idle_Normal";
    public string patrolStateName      = "MoveBlend";
    public string chaseStateName       = "MoveBlend";
    public string attackReadyStateName = "AttackReady";

    [Header("Trigger 파라미터 이름")]
    public string attackTrigger  = "Attack01";
    public string getHitTrigger  = "GetHit";
    public string dieTrigger     = "Die";
    public string detectTrigger  = "SenseSomething";

    [Header("블렌드 파라미터")]
    [Tooltip("이동 속도 Float 파라미터. 사용 안 하면 비워두기.")]
    public string speedParam     = "";
    [Tooltip("이동 속도 파라미터 댐핑 시간 (s). 0 = 즉시.")]
    public float  speedDampTime  = 0.1f;

    [Header("전환 설정")]
    [Tooltip("CrossFade 전환 시간 (s)")]
    public float  crossFadeDuration = 0.15f;
}
