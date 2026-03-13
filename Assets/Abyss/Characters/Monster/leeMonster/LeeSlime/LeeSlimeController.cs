using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 슬라임 컨트롤러.
///
/// LeeBatController와 동일한 FSM 구조를 사용하며 애니메이션 State 이름만 다르다.
/// 프리팹 구성:
///   - NavMeshAgent
///   - Animator (슬라임 Animator Controller 할당)
///   - LeeSlimeController
///   - LeeAnimationEventReceiver
///
/// 슬라임 Animator Controller의 State 이름에 맞게 Anim* 프로퍼티를 수정한다.
/// </summary>
public class LeeSlimeController : LeeMonsterFSMBase, IReturnableMonster
{
    [Header("귀환 설정")]
    [Tooltip("스폰 지점에서 이 거리를 넘으면 추적을 포기하고 귀환한다.")]
    [SerializeField] private float returnDistance  = 15f;
    [Tooltip("스폰 지점 도착 판정 거리.")]
    [SerializeField] private float arrivalDistance = 1.2f;

    public Vector3 SpawnPoint      { get; private set; }
    public float   ReturnDistance  => returnDistance;
    public float   ArrivalDistance => arrivalDistance;

    public override Define.MonsterType Type => Define.MonsterType.Slime;
    protected override int MonsterId        => Define.GetMonsterId(Type);

    // 슬라임 Animator Controller State 이름 (FSMMonster/Slime 상태 파일 기준)
    // Idle·Walk는 동일 블렌드 트리 State("MoveBlend")를 사용한다.
    public override string AnimIdle        => "MoveBlend";
    public override string AnimMove        => "MoveBlend";
    public override string AnimAttackReady => "AttackReady";
    public override string AnimAttack      => "Attack02";
    public override string AnimGetHit      => "GetHit";
    public override string AnimDie         => "Die";

    protected override async UniTask InitAsync()
    {
        SpawnPoint = transform.position;
        await base.InitAsync();
    }

    protected override void InitializeFSM()
    {
        RegisterState(MonsterState.Idle,        new LeeIdleState());
        RegisterState(MonsterState.Patrol,      new LeeReturnState());
        RegisterState(MonsterState.Chase,       new LeeReturnChaseState());
        RegisterState(MonsterState.AttackReady, new LeeAttackReadyState(
            Define.AttackStyle.Melee,
            Define.AttackPurpose.Normal01));
        RegisterState(MonsterState.Attack,      new LeeAttackState());
        RegisterState(MonsterState.GetHit,      new LeeGetHitState(duration: 0.6f));
        RegisterState(MonsterState.Die,         new LeeDieState(destroyDelay: 2f));
    }
}
