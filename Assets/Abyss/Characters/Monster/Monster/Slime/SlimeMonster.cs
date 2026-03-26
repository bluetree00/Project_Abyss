using UnityEngine;


namespace Abyss.Monster
{
/// <summary>
/// 외눈슬라임 몬스터.
/// 특수 상태: 배회 중 일정 주기마다 이동을 멈추고 HP를 회복한다 (SlimeRegenState).
///
/// 구현 방식: SlimePatrolState 가 PatrolState 를 상속해 쿨다운을 직접 관리한다.
/// Update() 에서 전체 특수 상태 체크 없이 배회 상태 내부에서만 처리된다.
/// </summary>
public class SlimeMonster : MonsterBase
{
    public const string PrefabAddress = "Slime/Slime";
    protected override string ConfigAddress    => "Slime/SlimeConfig";
    protected override string DataAddress      => "Slime/SlimeData";
    protected override float  HPBarHeadOffset  => 0.7f;

    private float _regenCooldown;

    protected override void OnEnable()
    {
        base.OnEnable();
        // 여러 마리가 동시에 발동하지 않도록 초기 쿨다운을 30~100% 범위에서 랜덤 분산
        _regenCooldown = GetRegenInterval() * Random.Range(0.3f, 1.0f);
    }

    protected override void RegisterStates()
    {
        base.RegisterStates();
        _fsm.RegisterAs<PatrolState>(new SlimePatrolState(this));
    }

    internal float GetRegenInterval()
        => _config?.specialState0 is SlimeRegenData d ? d.interval : 20f;

    // ── 배회 상태 파생 ────────────────────────────────────
    private class SlimePatrolState : PatrolState
    {
        private readonly SlimeMonster _owner;

        public SlimePatrolState(SlimeMonster owner) => _owner = owner;

        public override void Update(MonsterContext ctx)
        {
            // 쿨다운 감소 → 만료 시 회복 특수 상태 진입
            _owner._regenCooldown -= Time.deltaTime;
            if (_owner._regenCooldown <= 0f)
            {
                var state = _owner.GetSpecialState(0);
                if (state != null)
                {
                    _owner._regenCooldown = _owner.GetRegenInterval();
                    ctx.Monster.ChangeState(state);
                    return; // 특수 상태 진입 — 이후 배회 로직 스킵
                }
            }

            base.Update(ctx);
        }
    }
}
}
