using UnityEngine;

/// <summary>
/// 스켈레톤 광폭화 특수 상태.
/// HP 임계값 이하 도달 시 1회 발동 — 잠깐 멈추며 감지 연출 후 영구적으로 이동속도·공격력 증가.
/// 광폭화 후 머티리얼 색상이 붉게 유지되어 플레이어가 시각적으로 인지할 수 있다.
/// FullLockState 포맷: UnInterruptible | MovementLocked 제약 자동 바인딩.
/// </summary>
public class SkeletonEnrageState : FullLockState<SkeletonEnrageData>
{
    private static readonly int Color07Id = Shader.PropertyToID("_Color07");
    private static readonly int Color08Id = Shader.PropertyToID("_Color08");
    private static readonly Color EnrageColor = new Color(1f, 0.2f, 0.2f);

    private float _timer;

    public SkeletonEnrageState(SkeletonEnrageData data) : base(data) { }

    public override void Enter(LeeMonsterContext ctx)
    {
        ctx.Agent.ResetPath();
        _timer = Data.lockDuration;
        ctx.Animator.CrossFade("Enrage", 0.1f);
        ApplyTint(ctx.Transform, EnrageColor);
    }

    public override void Update(LeeMonsterContext ctx)
    {
        _timer -= Time.deltaTime;
        if (_timer <= 0f)
            ctx.Monster.ChangeState<LeeChaseState>();
    }

    public override void Exit(LeeMonsterContext ctx)
    {
        // 영구 버프 적용 (색상은 광폭화 유지 표시로 리셋하지 않음)
        ctx.Runtime.SpeedMultiplier  = Data.speedMultiplier;
        ctx.Runtime.AttackMultiplier = Data.attackMultiplier;
        ctx.Agent.speed = ctx.Config.stat.moveSpeed * ctx.Runtime.SpeedMultiplier;
    }

    private void ApplyTint(Transform root, Color color)
    {
        var mpb = new MaterialPropertyBlock();
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            r.GetPropertyBlock(mpb);
            if (r.sharedMaterial.HasProperty(Color07Id)) mpb.SetColor(Color07Id, color);
            if (r.sharedMaterial.HasProperty(Color08Id)) mpb.SetColor(Color08Id, color);
            r.SetPropertyBlock(mpb);
        }
    }
}
