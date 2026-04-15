using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// 달팽이 — 전투 중 방어막 오버라이드.
/// Chase / AttackReady / AttackState 를 교체하여 쿨다운마다 SnailShellState(specialState0) 로 진입한다.
/// 세 상태가 SharedTimer 를 공유하므로 어느 전투 상태에서든 쿨다운이 함께 감소한다.
/// </summary>
[CreateAssetMenu(fileName = "SnailShellOverride", menuName = "Lee/Monster/StateOverrides/SnailShell")]
public class SnailShellOverrideSO : MonsterStateOverrideSO
{
    [Header("발동 주기 (초)")]
    public float interval = 7f;

    [Header("초기 쿨다운 분산 — 여러 마리 동시 발동 방지")]
    [Range(0f, 1f)] public float initialCooldownMin = 0.3f;
    [Range(0f, 1f)] public float initialCooldownMax = 1.0f;

    public override void RegisterOverrides(MonsterFSM fsm, MonsterBase monster)
    {
        var timer = new SharedTimer(monster, this);
        fsm.RegisterAs<ChaseState>(new ShellChaseState(timer));
        fsm.RegisterAs<AttackReadyState>(new ShellAttackReadyState(timer));
        fsm.RegisterAs<AttackState>(new ShellAttackState(timer));
        monster.RegisterOnEnabledCallback(timer.Randomize);
    }

    // ── 공유 쿨다운 타이머 ────────────────────────────────────
    private class SharedTimer
    {
        private readonly MonsterBase       _owner;
        private readonly SnailShellOverrideSO _data;
        private float _cooldown;

        public SharedTimer(MonsterBase owner, SnailShellOverrideSO data)
        {
            _owner = owner;
            _data  = data;
            Randomize();
        }

        public void Randomize()
            => _cooldown = _data.interval * Random.Range(_data.initialCooldownMin, _data.initialCooldownMax);

        /// <summary>매 프레임 전투 상태에서 호출. 특수 상태로 전환됐으면 true 반환.</summary>
        public bool Tick(MonsterContext ctx)
        {
            _cooldown -= Time.deltaTime;
            if (_cooldown > 0f) return false;

            var special = _owner.GetSpecialState(0);
            if (special == null) return false;

            _cooldown = _data.interval;
            ctx.Monster.ChangeState(special);
            return true;
        }
    }

    // ── 오버라이드 상태들 ─────────────────────────────────────
    private class ShellChaseState : ChaseState
    {
        private readonly SharedTimer _timer;
        public ShellChaseState(SharedTimer t) => _timer = t;

        public override void Update(MonsterContext ctx)
        {
            if (_timer.Tick(ctx)) return;
            base.Update(ctx);
        }
    }

    private class ShellAttackReadyState : AttackReadyState
    {
        private readonly SharedTimer _timer;
        public ShellAttackReadyState(SharedTimer t) => _timer = t;

        public override void Update(MonsterContext ctx)
        {
            if (_timer.Tick(ctx)) return;
            base.Update(ctx);
        }
    }

    private class ShellAttackState : AttackState
    {
        private readonly SharedTimer _timer;
        public ShellAttackState(SharedTimer t) => _timer = t;

        public override void Update(MonsterContext ctx)
        {
            if (_timer.Tick(ctx)) return;
            base.Update(ctx);
        }
    }
}
}
