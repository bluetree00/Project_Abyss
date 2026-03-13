using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// MonsterFSMDefinitionSO를 읽어 동작하는 범용 몬스터 컨트롤러.
/// 새 몬스터를 추가할 때 이 컴포넌트를 Prefab에 붙이고 SO만 만들면 된다.
/// </summary>
public class LeeGenericMonsterController : LeeMonsterFSMBase, IReturnableMonster
{
    [SerializeField] private MonsterFSMDefinitionSO definition;

    // ── 기본 정보 ─────────────────────────────────────────────────────
    public override Define.MonsterType Type => definition != null ? definition.monsterType : Define.MonsterType.Bat;
    protected override int MonsterId        => Define.GetMonsterId(Type);

    // ── 애니메이션 State 이름 (SO에서 읽음) ───────────────────────────
    public override string AnimIdle        => definition != null ? definition.animIdle        : base.AnimIdle;
    public override string AnimMove        => definition != null ? definition.animMove        : base.AnimMove;
    public override string AnimAttackReady => definition != null ? definition.animAttackReady : base.AnimAttackReady;
    public override string AnimAttack      => definition != null ? definition.animAttack      : base.AnimAttack;
    public override string AnimGetHit      => definition != null ? definition.animGetHit      : base.AnimGetHit;
    public override string AnimDie         => definition != null ? definition.animDie         : base.AnimDie;

    // ── IReturnableMonster ────────────────────────────────────────────
    public Vector3 SpawnPoint      { get; private set; }
    public float   ReturnDistance  => definition != null ? definition.returnDistance  : 15f;
    public float   ArrivalDistance => definition != null ? definition.arrivalDistance : 1.2f;

    // ── 초기화 ────────────────────────────────────────────────────────
    protected override async UniTask InitAsync()
    {
        SpawnPoint = transform.position;
        await base.InitAsync();
    }

    protected override void InitializeFSM()
    {
        if (definition == null)
        {
            Debug.LogError($"[{name}] MonsterFSMDefinitionSO가 할당되지 않았습니다.");
            return;
        }

        RegisterState(MonsterState.Idle, new LeeIdleState(definition.idleDuration));

        // 귀환 여부에 따라 Chase / Patrol 상태 선택
        if (definition.useReturnBehavior)
        {
            RegisterState(MonsterState.Chase,  new LeeReturnChaseState()); // 귀환 거리 체크 포함
            RegisterState(MonsterState.Patrol, new LeeReturnState());     // 스폰 지점으로 귀환
        }
        else
        {
            RegisterState(MonsterState.Chase,  new LeeChaseState());
            RegisterState(MonsterState.Patrol, new LeePatrolState());
        }

        RegisterState(MonsterState.AttackReady, new LeeAttackReadyState(
            definition.attackStyle,
            definition.attackPurpose));
        RegisterState(MonsterState.Attack,  new LeeAttackState());
        RegisterState(MonsterState.GetHit,  new LeeGetHitState(definition.getHitDuration));
        RegisterState(MonsterState.Die,     new LeeDieState(definition.destroyDelay));
    }
}
