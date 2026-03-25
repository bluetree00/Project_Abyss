using Abyss.Monster;
using UnityEngine;

/// <summary>
/// 골렘 포효 특수 상태.
/// HP가 임계값 이하로 떨어지면 한 번 발동 — 제자리에서 포효하며 무적 + 금색 펄스.
/// 포효 시작 시 주변 플레이어·몬스터를 radial 넉백으로 날려버린다.
/// 포효 종료 후 분노 추격 속도 부스트.
/// InvincibleState 포맷: Invincible 제약 자동 바인딩.
/// </summary>
public class GolemRoarState : InvincibleState<GolemRoarData>
{
    private static readonly int Color07Id = Shader.PropertyToID("_Color07");
    private static readonly int Color08Id = Shader.PropertyToID("_Color08");
    private static readonly Color InvincibleColor = new Color(1f, 0.85f, 0.2f);

    private float      _timer;
    private Renderer[] _renderers;

    public GolemRoarState(GolemRoarData data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _timer     = Data.roarDuration;
        _renderers = ctx.Transform.GetComponentsInChildren<Renderer>(true);

        // 제자리 정지
        ctx.Agent.ResetPath();
        ctx.Agent.velocity = Vector3.zero;

        if (ctx.Animator != null && !string.IsNullOrEmpty(Data.roarStateName))
            ctx.Animator.CrossFade(Data.roarStateName, 0.1f);

        // 포효 충격파 — 주변 오브젝트 날리기
        ApplyRoarBlast(ctx);
    }

    public override void Update(MonsterContext ctx)
    {
        // 무적 펄스 (흰색 ↔ 금색)
        float pulse = Mathf.Abs(Mathf.Sin(Time.time * 4f));
        ApplyTint(_renderers, Color.Lerp(Color.white, InvincibleColor, pulse));

        _timer -= Time.deltaTime;
        if (_timer <= 0f)
            ctx.Monster.ChangeState<ChaseState>();
    }

    public override void Exit(MonsterContext ctx)
    {
        ClearTint(_renderers);
        // 포효 종료 → 분노 추격 속도 부스트
        ctx.Runtime.SpeedMultiplier = Data.rageSpeedMultiplier;
        ctx.Agent.speed = ctx.Config.stat.moveSpeed * Data.rageSpeedMultiplier;
        (ctx.Monster as GolemMonster)?.StartRageChase(Data.rageChaseDuration);
    }

    // ── 충격파 ────────────────────────────────────────────────

    private void ApplyRoarBlast(MonsterContext ctx)
    {
        var colliders = Physics.OverlapSphere(ctx.Transform.position, Data.knockbackRadius);
        foreach (var col in colliders)
        {
            // 자기 자신(골렘) 스킵
            if (col.transform.IsChildOf(ctx.Transform) || col.transform == ctx.Transform)
                continue;

            Vector3 rawDir = col.transform.position - ctx.Transform.position;
            rawDir.y = 0f;
            if (rawDir.sqrMagnitude < 0.001f) rawDir = ctx.Transform.forward;
            Vector3 blastDir = (rawDir.normalized + Vector3.up * 0.4f).normalized;

            // 플레이어 — IDamageable 미구현이므로 PlayerController 직접 처리
            var player = col.GetComponent<PlayerController>()
                      ?? col.GetComponentInParent<PlayerController>();
            if (player != null)
            {
                player.TakeDamage((int)Data.knockbackDamage);
                player.Rigid?.AddForce(blastDir * Data.knockbackForce * 3f, ForceMode.Impulse);
                continue;
            }

            // IDamageable (다른 몬스터 등)
            var damageable = col.GetComponent<IDamageable>()
                          ?? col.GetComponentInParent<IDamageable>();
            if (damageable != null)
            {
                damageable.TakeDamage(Data.knockbackDamage, ctx.Monster.gameObject, Data.knockbackForce);
                continue;
            }

            // 순수 물리 오브젝트
            var rb = col.GetComponent<Rigidbody>()
                  ?? col.GetComponentInParent<Rigidbody>();
            if (rb == null || rb.isKinematic) continue;
            rb.AddForce(blastDir * Data.knockbackForce * 3f, ForceMode.Impulse);
        }
    }

    // ── 비주얼 ────────────────────────────────────────────────

    private static void ApplyTint(Renderer[] renderers, Color color)
    {
        if (renderers == null) return;
        var mpb = new MaterialPropertyBlock();
        foreach (var r in renderers)
        {
            r.GetPropertyBlock(mpb);
            if (r.sharedMaterial != null)
            {
                if (r.sharedMaterial.HasProperty(Color07Id)) mpb.SetColor(Color07Id, color);
                if (r.sharedMaterial.HasProperty(Color08Id)) mpb.SetColor(Color08Id, color);
            }
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
