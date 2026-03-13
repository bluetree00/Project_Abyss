/// <summary>
/// LeeMonsterFSMBase 사용 예시.
///
/// 새 몬스터를 추가할 때는 이 파일을 복사해서 아래 3가지만 수정한다.
///   1. 클래스 이름
///   2. Type / MonsterId
///   3. InitializeFSM() 내부 — 상태 교체 or 추가
///
/// 공통 상태(LeeIdleState 등)를 그대로 쓰거나,
/// 몬스터 전용 로직이 필요하면 LeeMonsterStateBase를 상속한 새 클래스를 만들어 끼우면 된다.
/// </summary>
public class LeeExampleMonster : LeeMonsterFSMBase
{
    // ── 필수 구현 ─────────────────────────────────────────────────────

    public override Define.MonsterType Type => Define.MonsterType.Slime; // 몬스터 종류 지정
    protected override int MonsterId        => Define.GetMonsterId(Type);

    protected override void InitializeFSM()
    {
        // 공통 상태를 그대로 사용하는 경우
        RegisterState(MonsterState.Idle,        new LeeIdleState(idleDuration: 2f));
        RegisterState(MonsterState.Patrol,      new LeePatrolState());
        RegisterState(MonsterState.Chase,       new LeeChaseState());
        RegisterState(MonsterState.AttackReady, new LeeAttackReadyState(
            Define.AttackStyle.Melee,           // ← 공격 스타일 주입 (하드코딩 제거)
            Define.AttackPurpose.Normal01));
        RegisterState(MonsterState.Attack,      new LeeAttackState());
        RegisterState(MonsterState.Die,         new LeeDieState(destroyDelay: 2.5f));

        // 특정 상태만 전용 로직으로 교체하고 싶다면:
        // RegisterState(MonsterState.Chase, new MyCustomChaseState());
    }
}
