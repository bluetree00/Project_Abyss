using UnityEngine;

/// <summary>
/// 몬스터 FSM 전체 설정을 담는 ScriptableObject.
/// LeeGenericMonsterController에 할당하면 코드 수정 없이 새 몬스터를 추가할 수 있다.
/// </summary>
[CreateAssetMenu(fileName = "MonsterFSMDefinition", menuName = "Abyss/Monster/FSM Definition")]
public class MonsterFSMDefinitionSO : ScriptableObject
{
    [Header("몬스터 정보")]
    public Define.MonsterType monsterType;

    [Header("Animator State 이름 — Animator Controller와 정확히 일치해야 함")]
    public string animIdle        = "Idle";
    public string animMove        = "Move";
    public string animAttackReady = "AttackReady";
    public string animAttack      = "Attack";
    public string animGetHit      = "GetHit";
    public string animDie         = "Die";

    [Header("상태별 설정")]
    [Tooltip("Idle 유지 시간(초)")]
    public float idleDuration   = 3f;
    [Tooltip("피격 경직 시간(초)")]
    public float getHitDuration = 0.6f;
    [Tooltip("사망 후 오브젝트 파괴 대기 시간(초). 0이면 자동 파괴 안 함")]
    public float destroyDelay   = 2f;

    [Header("공격 설정")]
    public Define.AttackStyle   attackStyle   = Define.AttackStyle.Melee;
    public Define.AttackPurpose attackPurpose = Define.AttackPurpose.Normal01;

    [Header("귀환 행동 — 스폰 지점으로 돌아오는 몬스터(박쥐 등)에 사용")]
    [Tooltip("true면 Patrol 슬롯을 귀환 상태로 사용. false면 일반 순찰")]
    public bool  useReturnBehavior = false;
    [Tooltip("스폰 지점에서 이 거리를 넘으면 귀환 시작")]
    public float returnDistance    = 15f;
    [Tooltip("스폰 지점 도착 판정 거리")]
    public float arrivalDistance   = 1.2f;
}
