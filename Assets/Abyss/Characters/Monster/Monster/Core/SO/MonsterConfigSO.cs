using System;
using System.Collections.Generic;
using Abyss.Monster;
using UnityEngine;


/// <summary>몬스터 등급.</summary>
public enum MonsterGrade { Normal, Elite, Boss }

/// <summary>
/// 몬스터 마스터 설정 SO.
/// 순수 데이터(스탯·감지·이동·전투·애니메이션)는 MonsterStatData 등 인라인 클래스로 보관.
/// 거동(상태 팩토리 SO, 특수 상태, 오버라이드)은 여기서 참조한다.
/// Addressables 주소 하나만 알면 몬스터 전체 설정을 로드할 수 있음.
/// </summary>
[CreateAssetMenu(fileName = "MonsterConfig", menuName = "Abyss/Monster/ConfigSO")]
public class MonsterConfigSO : ScriptableObject
{
    [Header("기본 정보")]
    public string       monsterName;
    public MonsterGrade grade = MonsterGrade.Normal;

    [Header("레이어")]
    [Tooltip("플레이어 레이어 마스크 (감지 및 공격 판정에 공통 사용)")]
    public LayerMask playerLayer;

    // ── 인라인 데이터 ─────────────────────────────────────
    [Header("데이터 — 수치/애니메이션 (JSON으로 덮어쓰기 가능)")]
    public MonsterStatData      stat      = new MonsterStatData();
    public MonsterDetectionData detection = new MonsterDetectionData();
    public MonsterPatrolData    patrol    = new MonsterPatrolData();
    public MonsterCombatData    combat    = new MonsterCombatData();
    public MonsterAnimationData animation = new MonsterAnimationData();
    public MonsterElementalData elemental = new MonsterElementalData();

    // ── 특수 상태 ─────────────────────────────────────────
    [Header("특수 상태 (조건 + 상태 SO 쌍으로 구성)")]
    [Tooltip("특수 상태 목록. 조건이 있으면 OnDamageTaken 시 평가. 비어있으면 코드에서 직접 발동.")]
    public List<SpecialStateEntry> specialStates = new();

    // ── 공용 상태 커스텀 슬롯 ────────────────────────────────────
    [Header("공용 상태 커스텀 (null = 기본 상태 사용)")]
    [Tooltip("null이면 기본 공용 상태를 사용한다.\n" +
             "파생 SO (예: BKChaseStateSO)를 설정하면 해당 SO가 생성하는 커스텀 상태로 교체된다.\n\n" +
             "SO 계층: ChaseStateSO → BKChaseStateSO\n" +
             "상태 계층: ChaseState  → OrbitalChaseState (SO 내부 클래스)\n\n" +
             "이것이 진정한 모듈화: SO를 상속하고, 그 안에 공용 상태를 상속한 클래스를 보유한다.")]
    public ChaseStateSO       chaseState;
    public PatrolStateSO      patrolState;
    public AttackReadyStateSO attackReadyState;
    public AttackStateSO      attackState;
    public GetHitStateSO      getHitState;
    public DieStateSO         dieState;

    // ── 복합 행동 오버라이드 (다중 상태 공유 로직이 필요한 경우에만) ─
    [Header("복합 행동 오버라이드 (다중 상태가 하나의 런타임 객체를 공유해야 할 때만 사용)")]
    [Tooltip("예: SnailShell처럼 Chase/AttackReady/Attack 세 상태가 하나의 쿨다운 타이머를 공유하는 경우.\n" +
             "단순 상태 교체는 위의 개별 슬롯을 사용한다.")]
    public List<MonsterStateOverrideSO> stateOverrides;
}

/// <summary>
/// 특수 상태 하나의 설정 엔트리.
/// conditions 가 비어있으면 OnDamageTaken 에서 자동 평가되지 않음 (코드에서 직접 발동).
/// conditions 가 있으면 피격 시 ALL 조건 충족 여부를 검사한다.
/// </summary>
[Serializable]
public class SpecialStateEntry
{
    [Tooltip("AND 조건 목록. 비어있으면 자동 평가 안 함 — 코드(PatrolState 등)에서 직접 발동.\n" +
             "MonsterHpConditionSO 등 MonsterConditionSO 파생 Asset 을 드래그. SO 내 수치는 인라인 표시.")]
    public List<MonsterConditionSO> conditions = new();

    [Tooltip("특수 상태를 생성할 데이터 SO")]
    public SpecialStateDataBase state;

    [Tooltip("true 면 한 번만 발동. false 면 조건 충족 시 매 피격마다 발동.")]
    public bool oneShot = true;
}
