using Abyss.Monster;
using UnityEngine;

/// <summary>
/// BT 리프 노드 — Enrage (각성).
/// FullLockState: 실행 중 피격 차단.
///
/// HP 40% 이하 최초 진입 시 1회 발동.
/// 효과:
///   • Blackboard.AttackSpeedMult += enrageSpeedBonus  (Backstep·DashSlash 속도 증가)
///   • MonsterRuntime.AttackMultiplier *= enrageDamageMult (전체 데미지 증폭)
///   • 보스 렌더러에 붉은 색조 적용
///   • 각성 모션 + 포효 SFX
/// 흐름:
///   ① WindUp  (enrageWindUp 초) — 느린 자세 (연출용 정지)
///   ② Flash   (0.15 초) — 렌더러 색조 순간 점멸
///   ③ Roar    (enrageRoarDuration 초) — 포효 자세 유지
///   ④ ChaseState 복귀
/// </summary>
public class BKEnrageState : FullLockState<BKEnragePatternSO>
{
    private readonly BossAttackBlackboard _bb;

    private enum Phase { WindUp, Flash, Roar, Done }

    private Phase     _phase;
    private float     _timer;
    private Renderer[] _renderers;
    private bool      _statsApplied;

    private static readonly int Color07Id = Shader.PropertyToID("_Color07");
    private static readonly int Color08Id = Shader.PropertyToID("_Color08");

    public BKEnrageState(BKEnragePatternSO data, BossAttackBlackboard bb) : base(data)
    {
        _bb = bb;
    }

    // ── BT 조건 ──────────────────────────────────────────
    public bool CanExecute(MonsterContext ctx) => !_bb.HasEnraged;

    // ── FSM Enter/Update/Exit ────────────────────────────
    public override void Enter(MonsterContext ctx)
    {
        _renderers   = ctx.Transform.GetComponentsInChildren<Renderer>(true);
        _statsApplied = false;

        ctx.Agent.ResetPath();
        FacePlayer(ctx);

        _bb.AudioPool?.Play(ctx.Transform.position, Data.enrageSfx, 0.8f);
        ctx.Animator?.CrossFade(Data.enrageAnimState, 0.05f);

        _phase = Phase.WindUp;
        _timer = Data.enrageWindUp;
    }

    public override void Update(MonsterContext ctx)
    {
        _timer -= Time.deltaTime;

        switch (_phase)
        {
            // ── 정지 (연출) ───────────────────────────────
            case Phase.WindUp:
                if (_timer <= 0f)
                {
                    // 능력치 즉시 증폭
                    if (!_statsApplied)
                    {
                        _statsApplied = true;
                        ApplyEnrageBuffs(ctx);
                    }

                    // 순간 점멸
                    SetTint(_renderers, Color.red);
                    _phase = Phase.Flash;
                    _timer = 0.15f;
                }
                break;

            // ── 색조 점멸 ────────────────────────────────
            case Phase.Flash:
                if (_timer <= 0f)
                {
                    // 점멸 색 유지 (각성 틴트)
                    SetTint(_renderers, Data.enrageTint);
                    _phase = Phase.Roar;
                    _timer = Data.enrageRoarDuration;
                }
                break;

            // ── 포효 자세 ────────────────────────────────
            case Phase.Roar:
                if (_timer <= 0f)
                    _phase = Phase.Done;
                break;

            case Phase.Done:
                ctx.Monster.ChangeState<ChaseState>();
                break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        // 능력치 미적용 비정상 종료 대비
        if (!_statsApplied)
            ApplyEnrageBuffs(ctx);
    }

    // ── 각성 버프 ─────────────────────────────────────────
    private void ApplyEnrageBuffs(MonsterContext ctx)
    {
        _bb.HasEnraged      = true;
        _bb.AttackSpeedMult = Mathf.Max(_bb.AttackSpeedMult, Data.enrageSpeedMult);
        ctx.Runtime.AttackMultiplier *= Data.enrageDamageMult;
    }

    // ── 색조 ──────────────────────────────────────────────
    private static void SetTint(Renderer[] renderers, Color tint)
    {
        if (renderers == null) return;
        var mpb = new MaterialPropertyBlock();
        foreach (var r in renderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(mpb);
            if (r.sharedMaterial.HasProperty(Color07Id)) mpb.SetColor(Color07Id, tint);
            if (r.sharedMaterial.HasProperty(Color08Id)) mpb.SetColor(Color08Id, tint);
            r.SetPropertyBlock(mpb);
        }
    }

    private static void FacePlayer(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;
        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
            ctx.Transform.rotation = Quaternion.LookRotation(dir);
    }
}
