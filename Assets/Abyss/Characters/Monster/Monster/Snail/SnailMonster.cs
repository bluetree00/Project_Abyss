using UnityEngine;


namespace Abyss.Monster
{
/// <summary>
/// 달팽이 몬스터.
/// 특수 상태: 전투 중 일정 주기마다 이동 멈추고 받는 데미지 50% 감소 (SnailShellState).
///
/// 구현 방식: ChaseState / AttackReadyState / AttackState 를 각각 파생해
/// 전투 상태 내부에서 쿨다운(_shellCooldown)을 직접 관리한다.
/// 세 파생 상태가 SlimeMonster 인스턴스의 _shellCooldown 을 공유한다.
/// </summary>
public class SnailMonster : MonsterBase
{
    public const string PrefabAddress = "Snail/Snail";
    protected override string ConfigAddress    => "Snail/SnailConfig";
    protected override string DataAddress      => "Snail/SnailData";
    protected override float  HPBarHeadOffset  => 0.7f;

    private float _shellCooldown;

    protected override void OnEnable()
    {
        base.OnEnable();
        _shellCooldown = GetShellInterval() * Random.Range(0.3f, 1.0f);
    }

    protected override void RegisterStates()
    {
        base.RegisterStates();
        _fsm.RegisterAs<ChaseState>(new SnailChaseState(this));
        _fsm.RegisterAs<AttackReadyState>(new SnailAttackReadyState(this));
        _fsm.RegisterAs<AttackState>(new SnailAttackState(this));
    }

    // ── 쿨다운 공유 메서드 ────────────────────────────────
    /// <summary>전투 상태 파생 클래스들이 매 프레임 호출한다.</summary>
    private void TickShell(MonsterContext ctx)
    {
        _shellCooldown -= Time.deltaTime;
        if (_shellCooldown <= 0f)
        {
            var state = GetSpecialState(0);
            if (state != null)
            {
                _shellCooldown = GetShellInterval();
                ctx.Monster.ChangeState(state);
            }
        }
    }

    internal float GetShellInterval()
        => _config?.specialState0 is SnailShellData d ? d.interval : 15f;

    // ── 전투 상태 파생 ────────────────────────────────────
    private class SnailChaseState : ChaseState
    {
        private readonly SnailMonster _owner;
        public SnailChaseState(SnailMonster owner) => _owner = owner;

        public override void Update(MonsterContext ctx)
        {
            _owner.TickShell(ctx);
            if (ctx.Monster.IsInSpecialState) return; // 특수 상태 진입 시 Chase 로직 스킵
            base.Update(ctx);
        }
    }

    private class SnailAttackReadyState : AttackReadyState
    {
        private readonly SnailMonster _owner;
        public SnailAttackReadyState(SnailMonster owner) => _owner = owner;

        public override void Update(MonsterContext ctx)
        {
            _owner.TickShell(ctx);
            if (ctx.Monster.IsInSpecialState) return;
            base.Update(ctx);
        }
    }

    private class SnailAttackState : AttackState
    {
        private readonly SnailMonster _owner;
        public SnailAttackState(SnailMonster owner) => _owner = owner;

        public override void Update(MonsterContext ctx)
        {
            _owner.TickShell(ctx);
            if (ctx.Monster.IsInSpecialState) return;
            base.Update(ctx);
        }
    }
}
}
