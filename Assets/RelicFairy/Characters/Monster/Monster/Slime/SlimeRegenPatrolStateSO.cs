using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 슬라임 — 배회 중 일정 주기마다 HP 회복 특수 상태로 진입하도록 PatrolState를 교체하는 SO.
///
/// MonsterConfigSO.stateOverrides 리스트에 이 SO를 추가하면
/// PatrolState 하나만 커스텀 버전으로 덮어씌운다.
/// </summary>
[CreateAssetMenu(fileName = "SlimeRegenPatrolState",
                 menuName  = "Lee/Monster/States/Patrol/SlimeRegen")]
public class SlimeRegenPatrolStateSO : MonsterStateOverrideSO
{
    [Header("발동 주기 (초)")]
    public float interval = 8f;

    [Header("초기 쿨다운 분산 — 여러 마리 동시 발동 방지")]
    [Range(0f, 1f)] public float initialCooldownMin = 0.3f;
    [Range(0f, 1f)] public float initialCooldownMax = 1.0f;

    [Header("회복 발동 조건")]
    [Range(0f, 1f)]
    [Tooltip("HP 비율이 이 값 이하일 때만 회복 발동. 1.0 = 항상 발동.")]
    public float regenHpThreshold = 0.8f;

    [Tooltip("발동할 특수 상태의 인덱스 (specialStates 리스트 기준)")]
    public int specialStateIndex = 0;

    public override void RegisterOverrides(MonsterFSM fsm, MonsterBase monster)
    {
        var state = new RegenPatrolState(monster, this);
        monster.RegisterOnEnabledCallback(state.ResetCooldown);
        fsm.RegisterAs<PatrolState>(state);
    }

    // ── 오버라이드 상태 ────────────────────────────────────────
    private class RegenPatrolState : PatrolState
    {
        private readonly MonsterBase             _owner;
        private readonly SlimeRegenPatrolStateSO _data;
        private float _cooldown;

        public RegenPatrolState(MonsterBase owner, SlimeRegenPatrolStateSO data)
        {
            _owner = owner;
            _data  = data;
        }

        /// <summary>풀 재사용 시 MonsterBase.OnEnable 에서 호출.</summary>
        public void ResetCooldown()
            => _cooldown = _data.interval * Random.Range(_data.initialCooldownMin, _data.initialCooldownMax);

        public override void Enter(MonsterContext ctx)
        {
            base.Enter(ctx);
            if (_cooldown <= 0f) ResetCooldown();
        }

        public override void Update(MonsterContext ctx)
        {
            _cooldown -= Time.deltaTime;
            if (_cooldown <= 0f)
            {
                _cooldown = _data.interval;

                int   maxHp   = ctx.Stat.maxHp;
                float hpRatio = maxHp > 0 ? (float)ctx.Runtime.CurrentHp / maxHp : 1f;
                if (hpRatio <= _data.regenHpThreshold)
                {
                    var special = _owner.GetSpecialState(_data.specialStateIndex);
                    if (special != null)
                    {
                        ctx.Monster.ChangeState(special);
                        return;
                    }
                }
            }
            base.Update(ctx);
        }
    }
}
}
