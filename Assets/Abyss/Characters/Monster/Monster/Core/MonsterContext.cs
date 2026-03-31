using UnityEngine;
using UnityEngine.AI;


namespace Abyss.Monster
{
/// <summary>
/// FSM 상태 클래스들이 공유하는 컨텍스트 객체.
/// MonoBehaviour 참조·SO 참조·런타임 데이터를 한 곳에서 접근하기 위한 DI 컨테이너.
/// </summary>
public class MonsterContext
{
    // ── 핵심 참조 ─────────────────────────────────────────
    public MonsterBase           Monster;
    public NavMeshAgent          Agent;
    public Animator              Animator;

    // ── SO ────────────────────────────────────────────────
    public MonsterConfigSO       Config;

    // ── 런타임 ────────────────────────────────────────────
    public MonsterRuntimeData    Runtime;

    // ── 자주 쓰는 데이터 단축 프로퍼티 (기존 코드 호환) ────
    public MonsterStatData      Stat      => Config.stat;
    public MonsterDetectionData Detection => Config.detection;
    public MonsterPatrolData    Patrol    => Config.patrol;
    public MonsterCombatData    Combat    => Config.combat;
    public MonsterAnimationData Animation => Config.animation;

    // ── Transform 단축 ────────────────────────────────────
    public Transform Transform => Monster.transform;
}
}
