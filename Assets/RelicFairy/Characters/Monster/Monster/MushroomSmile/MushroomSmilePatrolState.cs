using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 머쉬룸스마일 전용 패트롤 오버라이드 상태.
/// 기본 상태로 식물 위장을 유지하며 이동하지 않는다.
/// 플레이어가 activateRange 이내로 접근하면 폭발 애니메이션 → 딜레이 후 범위 데미지 + 이펙트 → DieState.
/// MushroomSmileMonster.TakeDamage 오버라이드로 외부 피격은 전부 차단(무적).
/// </summary>
public class MushroomSmilePatrolState : PatrolState
{
    private readonly MushroomSmileTrapData _data;
    private bool  _triggered;
    private float _delayTimer;

    public MushroomSmilePatrolState(MushroomSmileTrapData data)
    {
        _data = data;
    }

    public override void Enter(MonsterContext ctx)
    {
        if (ctx.Agent.isActiveAndEnabled && ctx.Agent.isOnNavMesh)
            ctx.Agent.ResetPath();
        ctx.Agent.speed = 0f;

        ctx.Animator?.CrossFade(_data.idleStateName, 0.1f);
        _triggered  = false;
        _delayTimer = 0f;
    }

    public override void Update(MonsterContext ctx)
    {
        if (!_triggered)
        {
            if (ctx.Runtime.PlayerTarget == null) return;

            float dist = Vector3.Distance(ctx.Transform.position, ctx.Runtime.PlayerTarget.position);
            if (dist <= _data.activateRange)
            {
                ctx.Animator?.CrossFade(_data.burstStateName, 0.1f);
                _triggered  = true;
                _delayTimer = _data.burstDelay;
            }
        }
        else
        {
            _delayTimer -= Time.deltaTime;
            if (_delayTimer <= 0f)
            {
                SpawnExplosionEffect(ctx);
                ApplyAreaDamage(ctx, _data.burstRadius, _data.burstDamage);
                ctx.Monster.ChangeState<DieState>();
            }
        }
    }

    public override void Exit(MonsterContext ctx) { }

    // ── 폭발 이펙트 ────────────────────────────────────────

    private void SpawnExplosionEffect(MonsterContext ctx)
    {
        if (_data.explosionEffectPrefab == null) return;

        var obj = Object.Instantiate(_data.explosionEffectPrefab,
                                     ctx.Transform.position, Quaternion.identity);
        var ps = obj.GetComponent<ParticleSystem>() ?? obj.GetComponentInChildren<ParticleSystem>();
        float lifetime = ps != null ? ps.main.duration + 0.5f : 3f;
        Object.Destroy(obj, lifetime);
    }

    // ── 범위 데미지 ────────────────────────────────────────

    private static void ApplyAreaDamage(MonsterContext ctx, float radius, float damage)
    {
        var self = ctx.Monster as IDamageable;
        foreach (var col in Physics.OverlapSphere(ctx.Transform.position, radius))
        {
            if (col.transform.IsChildOf(ctx.Transform)) continue;

            var player = col.GetComponent<PlayerController>()
                      ?? col.GetComponentInParent<PlayerController>();
            if (player != null)
            {
                player.TakeDamage((int)damage);
                player.ApplyKnockback(
                    (player.transform.position - ctx.Transform.position).normalized * 4f, 0.4f);
                continue;
            }

            var dmg = col.GetComponent<IDamageable>()
                   ?? col.GetComponentInParent<IDamageable>();
            if (dmg != null && dmg != self)
                dmg.TakeDamage((int)damage, ctx.Monster.gameObject, 2f);
        }
    }
}
