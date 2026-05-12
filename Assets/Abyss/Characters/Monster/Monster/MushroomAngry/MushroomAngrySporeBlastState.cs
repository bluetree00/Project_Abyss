using Abyss.Monster;
using UnityEngine;

/// <summary>
/// 성난 버섯 포자 폭발 특수 상태.
/// HP 50% 이하일 때 반복 발동 — 진입 즉시 범위 포자 폭발 + 이펙트 후 ChaseState 복귀.
/// UnInterruptibleState 포맷: 방해 불가, 이동 가능.
/// </summary>
public class MushroomAngrySporeBlastState : UnInterruptibleState<MushroomAngrySporeData>
{
    private float _timer;

    public MushroomAngrySporeBlastState(MushroomAngrySporeData data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        ctx.Agent.ResetPath();

        _timer = Data.sporeDuration;

        if (ctx.Animator != null && !string.IsNullOrEmpty(Data.sporeStateName))
            ctx.Animator.CrossFade(Data.sporeStateName, 0.1f);

        SpawnSporeEffect(ctx);
        ApplyAreaDamage(ctx, Data.sporeRadius, Data.sporeDamage);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer -= Time.deltaTime;
        if (_timer <= 0f)
            ctx.Monster.ChangeState<ChaseState>();
    }

    public override void Exit(MonsterContext ctx) { }

    // ── 포자 이펙트 ───────────────────────────────────────

    private void SpawnSporeEffect(MonsterContext ctx)
    {
        if (Data.sporeEffectPrefab == null) return;

        var obj = Object.Instantiate(Data.sporeEffectPrefab,
                                     ctx.Transform.position, Quaternion.identity);
        obj.transform.localScale = Vector3.one * Data.sporeEffectScale;
        var ps = obj.GetComponent<ParticleSystem>() ?? obj.GetComponentInChildren<ParticleSystem>();
        float lifetime = ps != null ? ps.main.duration + 0.5f : 3f;
        Object.Destroy(obj, lifetime);
    }

    // ── 범위 데미지 ───────────────────────────────────────

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
                player.ApplyKnockback(
                    (player.transform.position - ctx.Transform.position).normalized * 3f, 0.3f);
                continue;
            }

            var dmg = col.GetComponent<IDamageable>()
                   ?? col.GetComponentInParent<IDamageable>();
            if (dmg != null && dmg != selfDamageable)
                dmg.TakeDamage((int)damage, ctx.Monster.gameObject, 0f);
        }
    }
}
