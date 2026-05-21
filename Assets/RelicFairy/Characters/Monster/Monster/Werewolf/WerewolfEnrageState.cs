using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 웨어울프 광폭화 특수 상태.
/// HP 40% 이하 도달 시 1회 발동 — 제자리에서 포효하며 붉은 Tint 적용.
/// 포효 종료 후 영구적으로 이동속도·공격력 증가 (Tint는 유지).
/// FullLockState 포맷: UnInterruptible | MovementLocked 제약 자동 바인딩.
/// </summary>
public class WerewolfEnrageState : FullLockState<WerewolfEnrageData>
{
    private static readonly int Color07Id = Shader.PropertyToID("_Color07");
    private static readonly int Color08Id = Shader.PropertyToID("_Color08");
    private static readonly Color EnrageColor = new Color(1f, 0.15f, 0.15f);

    private float _timer;

    public WerewolfEnrageState(WerewolfEnrageData data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        ctx.Agent.ResetPath();
        _timer = Data.lockDuration;

        if (ctx.Animator != null && !string.IsNullOrEmpty(Data.enrageStateName))
            ctx.Animator.CrossFade(Data.enrageStateName, 0.1f);

        Data.SpawnVFX(ctx.Transform);
        ApplyTint(ctx.Transform, EnrageColor);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer -= Time.deltaTime;
        if (_timer <= 0f)
            ctx.Monster.ChangeState<ChaseState>();
    }

    public override void Exit(MonsterContext ctx)
    {
        // 영구 버프 적용 (붉은 Tint는 광폭화 상태 유지 표시로 리셋하지 않음)
        ctx.Runtime.SpeedMultiplier  = Data.speedMultiplier;
        ctx.Runtime.AttackMultiplier = Data.attackMultiplier;
        ctx.Agent.speed = ctx.Config.stat.moveSpeed * ctx.Runtime.SpeedMultiplier;
    }

    private static void ApplyTint(Transform root, Color color)
    {
        var mpb = new MaterialPropertyBlock();
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r.sharedMaterial == null) continue;
            r.GetPropertyBlock(mpb);
            if (r.sharedMaterial.HasProperty(Color07Id)) mpb.SetColor(Color07Id, color);
            if (r.sharedMaterial.HasProperty(Color08Id)) mpb.SetColor(Color08Id, color);
            r.SetPropertyBlock(mpb);
        }
    }
}
