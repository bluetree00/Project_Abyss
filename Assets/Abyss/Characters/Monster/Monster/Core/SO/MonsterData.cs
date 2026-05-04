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

    [Header("원소 속성 (공격 원소 / 네이티브 원소)")]
    [Tooltip("몬스터가 가진 원소 속성. 공격 부여 원소 및 저항/약점 판정에 활용.")]
    public ElementType nativeElement = ElementType.None;

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

/// <summary>사망 시 드롭 데이터. 현재는 골드 코인만 지원. coinMax == 0 이면 드롭 없음.</summary>
[Serializable]
public class MonsterDropData
{
    [Header("골드 코인 드롭")]
    [Tooltip("드롭할 코인 최소 개수 (inclusive)")]
    [Min(0)] public int coinMin = 5;
    [Tooltip("드롭할 코인 최대 개수 (inclusive)")]
    [Min(0)] public int coinMax = 8;
    [Tooltip("코인 1개당 지급 골드")]
    [Min(1)] public int coinValue = 1;
}

/// <summary>몬스터 애니메이션 설정 데이터.</summary>
[Serializable]
public class MonsterAnimationData
{
    [Header("상태 이름 (공용 Base Controller 기준 — 프리팹 Animator에 직접 세팅)")]
    public string idleStateName        = "Idle";
    public string patrolStateName      = "Walk";
    public string chaseStateName       = "Run";
    public string attackReadyStateName = "AttackReady";
    public string attackStateName      = "Attack";
    public string getHitStateName      = "GetHit";
    public string dieStateName         = "Die";

    [Header("트리거 / ThirdParty 컨트롤러 상태 이름")]
    [Tooltip("공격 상태 이름 또는 트리거. ThirdParty 컨트롤러 사용 시 실제 상태명 입력 (예: BattleBee_Attack01)")]
    public string attackTrigger  = "";
    [Tooltip("피격 상태 이름 또는 트리거.")]
    public string getHitTrigger  = "";
    [Tooltip("사망 상태 이름 또는 트리거.")]
    public string dieTrigger     = "";
    [Tooltip("감지 상태 이름 또는 트리거.")]
    public string detectTrigger  = "";

    [Header("블렌드 파라미터")]
    [Tooltip("이동 속도 Float 파라미터. 사용 안 하면 비워두기.")]
    public string speedParam     = "";
    [Tooltip("이동 속도 파라미터 댐핑 시간 (s). 0 = 즉시.")]
    public float  speedDampTime  = 0.1f;

    [Header("전환 설정")]
    [Tooltip("CrossFade 전환 시간 (s)")]
    public float  crossFadeDuration = 0.15f;
}
