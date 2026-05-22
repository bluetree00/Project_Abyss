using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 데몬킹 폭발 특수 상태.
/// HP 40% 이하 도달 시 1회 발동 — 무적 상태로 주변 대폭발 즉시 실행 + 보라/붉은 Tint.
/// 폭발 종료 후 분노 추격 속도 부스트, Tint 제거.
/// InvincibleState 포맷: TakeDamage 완전 차단.
/// </summary>
public class DemonKingExplosionState : InvincibleState<DemonKingExplosionData>
{
    private static readonly int Color07Id = Shader.PropertyToID("_Color07");
    private static readonly int Color08Id = Shader.PropertyToID("_Color08");
    private static readonly Color ExplosionColor = new Color(0.8f, 0.1f, 0.8f);

    private float      _timer;
    private Renderer[] _renderers;

    public DemonKingExplosionState(DemonKingExplosionData data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _timer     = Data.explosionDuration;
        _renderers = ctx.Transform.GetComponentsInChildren<Renderer>(true);

        ctx.Agent.ResetPath();
        ctx.Agent.velocity = Vector3.zero;

        if (ctx.Animator != null && !string.IsNullOrEmpty(Data.explosionStateName))
            ctx.Animator.CrossFade(Data.explosionStateName, 0.1f);

        Data.SpawnVFX(ctx.Transform);
        ApplyTint(_renderers, ExplosionColor);
        ApplyAreaDamage(ctx, Data.explosionRadius, Data.explosionDamage, Data.knockbackForce);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer -= Time.deltaTime;
        if (_timer <= 0f)
            ctx.Monster.ChangeState<ChaseState>();
    }

    public override void Exit(MonsterContext ctx)
    {
        ClearTint(_renderers);
        ctx.Runtime.SpeedMultiplier = Data.rageSpeedMultiplier;
        ctx.Agent.speed = ctx.Config.stat.moveSpeed * Data.rageSpeedMultiplier;
        (ctx.Monster as DemonKingMonster)?.StartRageChase(Data.rageChaseDuration);
    }

    private static void ApplyAreaDamage(MonsterContext ctx, float radius, float damage, float knockbackForce)
    {
        var selfDamageable = ctx.Monster as IDamageable;
        foreach (var col in Physics.OverlapSphere(ctx.Transform.position, radius))
        {
            if (col.transform.IsChildOf(ctx.Transform)) continue;

            Vector3 rawDir = col.transform.position - ctx.Transform.position;
            rawDir.y = 0f;
            if (rawDir.sqrMagnitude < 0.001f) rawDir = ctx.Transform.forward;
            Vector3 blastDir = (rawDir.normalized + Vector3.up * 0.3f).normalized;

            var player = col.GetComponent<PlayerController>()
                      ?? col.GetComponentInParent<PlayerController>();
            if (player != null)
            {
                player.ApplyKnockback(blastDir * knockbackForce, 0.4f);
                continue;
            }

            var dmg = col.GetComponent<IDamageable>()
                   ?? col.GetComponentInParent<IDamageable>();
            if (dmg != null && dmg != selfDamageable)
                dmg.TakeDamage((int)damage, ctx.Monster.gameObject, knockbackForce);
        }
    }

    private static void ApplyTint(Renderer[] renderers, Color color)
    {
        if (renderers == null) return;
        var mpb = new MaterialPropertyBlock();
        foreach (var r in renderers)
        {
            if (r.sharedMaterial == null) continue;
            r.GetPropertyBlock(mpb);
            if (r.sharedMaterial.HasProperty(Color07Id)) mpb.SetColor(Color07Id, color);
            if (r.sharedMaterial.HasProperty(Color08Id)) mpb.SetColor(Color08Id, color);
            r.SetPropertyBlock(mpb);
        }
    }

    private static void ClearTint(Renderer[] renderers)
    {
        if (renderers == null) return;
        var mpb = new MaterialPropertyBlock();
        foreach (var r in renderers)
        {
            r.GetPropertyBlock(mpb);
            mpb.Clear();
            r.SetPropertyBlock(mpb);
        }
    }
}
