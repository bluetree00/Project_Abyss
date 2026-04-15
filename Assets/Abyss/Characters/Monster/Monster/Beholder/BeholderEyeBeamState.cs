using Abyss.Monster;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 비홀더 눈 빔 특수 상태.
/// HP 60% 이하일 때 반복 발동 — Charge → Fire 두 단계로 구성.
/// Charge: 차지 애니 재생 후 Fire 단계로 전환.
/// Fire: 틱 단위로 범위 데미지, 종료 후 AttackReadyState 복귀.
/// MovementLockedState 포맷: 이동만 잠금, 방해 가능.
/// </summary>
public class BeholderEyeBeamState : MovementLockedState<BeholderEyeBeamData>
{
    private enum Phase { Charge, Fire }

    private Phase _phase;
    private float _chargeTimer;
    private float _beamTimer;
    private float _tickTimer;
    private GameObject _beamVfxInstance;

    public BeholderEyeBeamState(BeholderEyeBeamData data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        ctx.Agent.ResetPath();
        ctx.Agent.velocity = Vector3.zero;

        _phase       = Phase.Charge;
        _chargeTimer = Data.chargeDuration;
        _tickTimer   = 0f;

        if (ctx.Animator != null && !string.IsNullOrEmpty(Data.chargeStateName))
            ctx.Animator.CrossFade(Data.chargeStateName, 0.1f);
    }

    public override void Update(MonsterContext ctx)
    {
        switch (_phase)
        {
            case Phase.Charge:
                _chargeTimer -= Time.deltaTime;
                if (_chargeTimer <= 0f)
                {
                    _phase     = Phase.Fire;
                    _beamTimer = Data.beamDuration;
                    _tickTimer = 0f;
                    if (ctx.Animator != null && !string.IsNullOrEmpty(Data.beamStateName))
                        ctx.Animator.CrossFade(Data.beamStateName, 0.1f);
                    if (Data.beamVfxPrefab != null)
                        _beamVfxInstance = Object.Instantiate(Data.beamVfxPrefab, ctx.Transform.position, ctx.Transform.rotation, ctx.Transform);
                }
                break;

            case Phase.Fire:
                _tickTimer  -= Time.deltaTime;
                _beamTimer  -= Time.deltaTime;

                if (_tickTimer <= 0f)
                {
                    _tickTimer = Data.damageTick;
                    ApplyAreaDamage(ctx, Data.beamRadius, Data.beamDamage);
                }

                if (_beamTimer <= 0f)
                {
                    if (_beamVfxInstance != null)
                    {
                        Object.Destroy(_beamVfxInstance, 0.5f);
                        _beamVfxInstance = null;
                    }
                    ctx.Monster.ChangeState<AttackReadyState>();
                }
                break;
        }
    }

    public override void Exit(MonsterContext ctx) { }

    private static void ApplyAreaDamage(MonsterContext ctx, float radius, float damage)
    {
        var selfDamageable = ctx.Monster as IDamageable;
        foreach (var col in Physics.OverlapSphere(ctx.Transform.position, radius))
        {
            if (col.transform.IsChildOf(ctx.Transform)) continue;

            var player = col.GetComponent<PlayerController>()
                      ?? col.GetComponentInParent<PlayerController>();
            if (player != null)
            {
                player.TakeDamage((int)damage);
                Vector3 knockDir = (player.transform.position - ctx.Transform.position).normalized;
                knockDir.y = 0.2f;
                player.ApplyKnockback(knockDir.normalized * ctx.Stat.knockbackForce);
                continue;
            }

            var dmg = col.GetComponent<IDamageable>()
                   ?? col.GetComponentInParent<IDamageable>();
            if (dmg != null && dmg != selfDamageable)
                dmg.TakeDamage((int)damage, ctx.Monster.gameObject, 0f);
        }
    }
}
