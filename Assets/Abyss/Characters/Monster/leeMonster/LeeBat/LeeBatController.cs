using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// BatPADefault용 컨트롤러.
///
/// 상태 흐름:
///   Idle ──(감지)──► Chase ──(공격 범위)──► AttackReady ──► Attack ──► Chase
///                      │
///                 (스폰 거리 초과)
///                      ▼
///                   Patrol(귀환) ──(도착)──► Idle
///
/// MonsterState.Patrol 슬롯을 "귀환" 용도로 재사용.
/// </summary>
public class LeeBatController : LeeMonsterFSMBase, IReturnableMonster
{
    [Header("귀환 설정")]
    [Tooltip("스폰 지점에서 이 거리를 넘으면 추적을 포기하고 귀환한다.")]
    [SerializeField] private float returnDistance  = 15f;
    [Tooltip("스폰 지점 도착 판정 거리.")]
    [SerializeField] private float arrivalDistance = 1.2f;

    public Vector3 SpawnPoint      { get; private set; }
    public float   ReturnDistance  => returnDistance;
    public float   ArrivalDistance => arrivalDistance;

    public override Define.MonsterType Type => Define.MonsterType.Bat;
    protected override int MonsterId        => Define.GetMonsterId(Type);

    // Bat.controller 실제 State 이름에 맞게 매핑
    public override string AnimIdle        => "IdleNormal";
    public override string AnimMove        => "FlyFWD";
    public override string AnimAttackReady => "IdleBattle";
    public override string AnimAttack      => "Attack01";
    public override string AnimGetHit      => "GetHit";
    public override string AnimDie         => "Die";

    protected override async UniTask InitAsync()
    {
        // 스폰 위치를 InitializeFSM 이전에 기록해야 State에서 참조 가능
        SpawnPoint = transform.position;
        await base.InitAsync();
    }

    protected override void InitializeFSM()
    {
        RegisterState(MonsterState.Idle,        new LeeIdleState());
        RegisterState(MonsterState.Patrol,      new LeeReturnState());       // Patrol = 귀환
        RegisterState(MonsterState.Chase,       new LeeReturnChaseState());
        RegisterState(MonsterState.AttackReady, new LeeAttackReadyState(
            Define.AttackStyle.Melee,
            Define.AttackPurpose.Normal01));
        RegisterState(MonsterState.Attack,      new LeeAttackState());
        RegisterState(MonsterState.GetHit,      new LeeGetHitState(duration: 0.6f));
        RegisterState(MonsterState.Die,         new LeeDieState(destroyDelay: 2f));
    }
}
