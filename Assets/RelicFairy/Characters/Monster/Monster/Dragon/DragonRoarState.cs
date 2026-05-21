using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 드래곤 포효 특수 상태.
/// HP 50% 이하 도달 시 1회 발동 — 제자리에서 포효하며 무적 + 주변 넉백 충격파.
/// 포효 종료 후 분노 추격 속도 부스트.
/// InvincibleState 포맷: TakeDamage 완전 차단.
/// </summary>
public class DragonRoarState : InvincibleState<DragonRoarData>
{
    private float _timer;

    public DragonRoarState(DragonRoarData data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _timer = Data.roarDuration;

        ctx.Agent.ResetPath();
        ctx.Agent.velocity = Vector3.zero;

        if (ctx.Animator != null && !string.IsNullOrEmpty(Data.roarStateName))
            ctx.Animator.CrossFade(Data.roarStateName, 0.1f);

        Data.SpawnVFX(ctx.Transform);
        ApplyRoarBlast(ctx);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer -= Time.deltaTime;
        if (_timer <= 0f)
            ctx.Monster.ChangeState<ChaseState>();
    }

    public override void Exit(MonsterContext ctx)
    {
        ctx.Runtime.SpeedMultiplier = Data.rageSpeedMultiplier;
        ctx.Agent.speed = ctx.Config.stat.moveSpeed * Data.rageSpeedMultiplier;
        (ctx.Monster as DragonMonster)?.StartRageChase(Data.rageChaseDuration);
    }

    private void ApplyRoarBlast(MonsterContext ctx)
    {
        var selfDamageable = ctx.Monster as IDamageable;
        var colliders = Physics.OverlapSphere(ctx.Transform.position, Data.knockbackRadius);
        foreach (var col in colliders)
        {
            if (col.transform.IsChildOf(ctx.Transform)) continue;

            Vector3 rawDir = col.transform.position - ctx.Transform.position;
            rawDir.y = 0f;
            if (rawDir.sqrMagnitude < 0.001f) rawDir = ctx.Transform.forward;
            Vector3 blastDir = (rawDir.normalized + Vector3.up * 0.4f).normalized;

            var player = col.GetComponent<PlayerController>()
                      ?? col.GetComponentInParent<PlayerController>();
            if (player != null)
            {
                player.ApplyKnockback(blastDir * Data.knockbackForce * 3f, 0.4f);
                continue;
            }

            var damageable = col.GetComponent<IDamageable>()
                          ?? col.GetComponentInParent<IDamageable>();
            if (damageable != null && damageable != selfDamageable)
            {
                damageable.TakeDamage((int)Data.knockbackDamage, ctx.Monster.gameObject, Data.knockbackForce);
                continue;
            }

            var rb = col.GetComponent<Rigidbody>()
                  ?? col.GetComponentInParent<Rigidbody>();
            if (rb != null && !rb.isKinematic)
                rb.AddForce(blastDir * Data.knockbackForce * 3f, ForceMode.Impulse);
        }
    }
}
