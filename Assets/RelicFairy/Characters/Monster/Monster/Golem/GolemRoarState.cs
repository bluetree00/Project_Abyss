using RelicFairy.Monster;
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
    private const float BlastTelegraph = 0.6f;   // 충격파 예고(원 외곽선 · 머리 위 반짝임) — 예전엔 들어가자마자 터져 피할 수 없었다(10-11 P1-7)
    private float _blastTimer;
    private bool  _blasted;

    /// <summary>예고 동안만 무적 — 충격파 뒤엔 피해가 들어간다(포효는 끝까지 하고 끊기지 않는다).</summary>
    public override SpecialStateConstraint Constraints
        => _blasted ? SpecialStateConstraint.UnInterruptible : SpecialStateConstraint.Invincible;

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

        Data.SpawnVFX(ctx.Transform);

        // 포효 충격파 — 예고(판정과 같은 원 외곽선 + 머리 위 반짝임) 뒤에 날린다
        _blasted    = false;
        _blastTimer = BlastTelegraph;
        MonsterGroundWarning.SpawnCircleOutline(ctx.Transform.position, Data.knockbackRadius, BlastTelegraph, PatternAttackOverrideSO.WarningOutlineColor);
        ctx.Monster.NotifyAttackIncoming(BlastTelegraph);
    }

    public override void Update(MonsterContext ctx)
    {
        if (!_blasted)
        {
            // 무적 펄스 (흰색 ↔ 금색) — 예고 동안만
            float pulse = Mathf.Abs(Mathf.Sin(Time.time * 4f));
            ApplyTint(_renderers, Color.Lerp(Color.white, InvincibleColor, pulse));
            _blastTimer -= Time.deltaTime;
            ctx.Monster.NotifyAttackIncoming(_blastTimer);
            if (_blastTimer <= 0f)
            {
                _blasted = true;
                ClearTint(_renderers);
                ctx.Monster.ResetAttackGlint();
                ApplyRoarBlast(ctx);
            }
        }

        _timer -= Time.deltaTime;
        if (_timer <= 0f)
            ctx.Monster.ChangeState<ChaseState>();
    }

    public override void Exit(MonsterContext ctx)
    {
        ClearTint(_renderers);
        ctx.Monster.ResetAttackGlint();
        // 포효 종료 → 분노 추격 속도 부스트
        ctx.Runtime.SpeedMultiplier = Data.rageSpeedMultiplier;
        ctx.Agent.speed = ctx.Config.stat.moveSpeed * Data.rageSpeedMultiplier;
        (ctx.Monster as GolemMonster)?.StartRageChase(Data.rageChaseDuration);
    }

    // ── 충격파 ────────────────────────────────────────────────

    private void ApplyRoarBlast(MonsterContext ctx)
    {
        var selfDamageable = ctx.Monster as IDamageable;
        var colliders = Physics.OverlapSphere(ctx.Transform.position, Data.knockbackRadius);
        foreach (var col in colliders)
        {
            // 자기 자신(골렘) 스킵 — transform 계층 + IDamageable 이중 체크
            if (col.transform.IsChildOf(ctx.Transform)) continue;

            Vector3 rawDir = col.transform.position - ctx.Transform.position;
            rawDir.y = 0f;
            if (rawDir.sqrMagnitude < 0.001f) rawDir = ctx.Transform.forward;
            Vector3 blastDir = (rawDir.normalized + Vector3.up * 0.4f).normalized;

            // 플레이어 — IDamageable 미구현이므로 PlayerController 직접 처리
            var player = col.GetComponent<PlayerController>()
                      ?? col.GetComponentInParent<PlayerController>();
            if (player != null)
            {
                player.ApplyKnockback(blastDir * Data.knockbackForce * 3f, 0.4f);
                continue;
            }

            // IDamageable (다른 몬스터 등) — 자기 자신 제외
            var damageable = col.GetComponent<IDamageable>()
                          ?? col.GetComponentInParent<IDamageable>();
            if (damageable != null)
            {
                if (damageable == selfDamageable) continue;
                if (damageable is MonsterBase) continue;   // 다른 몬스터는 다치지 않는다(아군 피해 제거 · 10-11 P1-7)
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
