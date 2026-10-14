using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 샐러맨더 화염 브레스 특수 상태.
/// HP 70% 이하일 때 반복 발동 — 정면 Cone 범위에 틱 데미지.
/// MovementLockedState 포맷: 이동만 잠금, 방해 가능.
/// </summary>
public class SalamanderFlameBreathState : MovementLockedState<SalamanderBreathData>
{
    private const float BreathLead = 0.6f;   // 예고 — 불 혀가 판정 끝 가까이 닿는 때(10-13 이펙트 = 판정)

    private float _breathTimer;
    private float _tickTimer;
    private MonsterTelegraph _telegraph;
    private GameObject _vfx;

    public SalamanderFlameBreathState(SalamanderBreathData data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        ctx.Agent.ResetPath();
        ctx.Agent.velocity = Vector3.zero;

        _breathTimer = Data.breathDuration + BreathLead;
        _tickTimer   = BreathLead;

        if (ctx.Animator != null && !string.IsNullOrEmpty(Data.breathStateName))
            ctx.Animator.CrossFade(Data.breathStateName, 0.1f);

        _vfx = Data.SpawnVFX(ctx.Transform);
        _telegraph = MonsterTelegraph.Cone(ctx.Transform.position, ctx.Transform.forward, Data.breathRadius, Data.breathAngle * 0.5f, BreathLead, MonsterTelegraph.Heavy);
    }

    public override void Update(MonsterContext ctx)
    {
        _tickTimer   -= Time.deltaTime;
        _breathTimer -= Time.deltaTime;

        if (_tickTimer <= 0f)
        {
            _tickTimer = Data.damageTick;
            _telegraph = null;   // 첫 피해까지 그렸다
            ApplyConeDamage(ctx);
        }

        if (_breathTimer <= 0f)
            ctx.Monster.ChangeState<ChaseState>();
    }

    public override void Exit(MonsterContext ctx)
    {
        if (_telegraph != null) _telegraph.Cancel();   // 첫 피해 전에 끊겼다 — 헛예고를 지운다
        _telegraph = null;
        // 끊기거나 끝나면 불 뿜기를 멈춘다(남은 불꽃은 제 수명대로 사그라든다 · 오브젝트는 SpawnVFX가 지운다)
        if (_vfx != null && _vfx.TryGetComponent<ParticleSystem>(out var ps)) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        _vfx = null;
    }

    private void ApplyConeDamage(MonsterContext ctx)
    {
        var selfDamageable = ctx.Monster as IDamageable;
        var forward        = ctx.Transform.forward;
        var origin         = ctx.Transform.position;
        var halfAngle      = Data.breathAngle * 0.5f;

        bool playerHit = false;
        foreach (var col in Physics.OverlapSphere(origin, Data.breathRadius))
        {
            if (col.transform.IsChildOf(ctx.Transform)) continue;

            Vector3 dir = (col.transform.position - origin);
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f) continue;

            if (Vector3.Angle(forward, dir.normalized) > halfAngle) continue;

            var player = col.GetComponent<PlayerController>()
                      ?? col.GetComponentInParent<PlayerController>();
            if (player != null)
            {
                // 예전엔 넉백 0만 걸고 피해를 주지 않았다(플레이어가 맞아도 무해) — 성난 버섯 포자와 같은 방식으로 준다.
                if (playerHit) continue;
                playerHit = true;
                player.TakeDamage(Mathf.RoundToInt(Data.breathDamage * ctx.Runtime.AttackMultiplier), ctx.Transform.gameObject);
                player.ApplyKnockback((player.transform.position - ctx.Transform.position).normalized * 3f, 0.3f);
                continue;
            }

            var dmg = col.GetComponent<IDamageable>()
                   ?? col.GetComponentInParent<IDamageable>();
            if (dmg != null && dmg != selfDamageable)
                dmg.TakeDamage((int)Data.breathDamage, ctx.Monster.gameObject, 0f);
        }
    }
}
