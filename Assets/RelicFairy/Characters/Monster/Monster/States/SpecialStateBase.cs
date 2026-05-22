

namespace RelicFairy.Monster
{
/// <summary>
/// 특수 상태 최상위 추상 베이스.
/// 제약 선언만 담당하며, 실제 제약 적용은 MonsterFSM.CurrentConstraints를 통해
/// MonsterBase에서 중앙 처리한다.
/// </summary>
public abstract class SpecialStateBase : IMonsterState
{
    public abstract SpecialStateConstraint Constraints { get; }

    public abstract void Enter (MonsterContext ctx);
    public abstract void Update(MonsterContext ctx);
    public abstract void Exit  (MonsterContext ctx);
}

// ── 제약 포맷 레이어 ────────────────────────────────────────────────────────────
// 특수 상태는 아래 포맷 중 하나를 상속해 제약을 자동 바인딩한다.
// 새 제약 조합이 필요하면 포맷을 추가하면 된다.

/// <summary>중단 불가 (피격 상태 전환 차단, 데미지는 들어옴)</summary>
public abstract class UnInterruptibleState<TData> : SpecialStateBase
    where TData : SpecialStateDataBase
{
    public override SpecialStateConstraint Constraints => SpecialStateConstraint.UnInterruptible;
    protected readonly TData Data;
    protected UnInterruptibleState(TData data) { Data = data; }
}

/// <summary>이동 잠금 (NavMeshAgent 정지)</summary>
public abstract class MovementLockedState<TData> : SpecialStateBase
    where TData : SpecialStateDataBase
{
    public override SpecialStateConstraint Constraints => SpecialStateConstraint.MovementLocked;
    protected readonly TData Data;
    protected MovementLockedState(TData data) { Data = data; }
}

/// <summary>완전 잠금 (중단 불가 + 이동 잠금)</summary>
public abstract class FullLockState<TData> : SpecialStateBase
    where TData : SpecialStateDataBase
{
    public override SpecialStateConstraint Constraints =>
        SpecialStateConstraint.UnInterruptible | SpecialStateConstraint.MovementLocked;
    protected readonly TData Data;
    protected FullLockState(TData data) { Data = data; }
}

/// <summary>무적 (TakeDamage 자체 무시, 이동은 허용)</summary>
public abstract class InvincibleState<TData> : SpecialStateBase
    where TData : SpecialStateDataBase
{
    public override SpecialStateConstraint Constraints =>
        SpecialStateConstraint.Invincible;
    protected readonly TData Data;
    protected InvincibleState(TData data) { Data = data; }
}
}
