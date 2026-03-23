using UnityEngine;

/// <summary>
/// 달팽이 중갑 방어 특수 상태.
/// 일정 주기마다 발동 — 이동 멈추고 일정 시간 받는 데미지 50% 감소 후 복귀.
/// 방어 중 머티리얼 색상이 파랗게 변해 플레이어가 시각적으로 인지할 수 있다.
/// 피격 시 경직(GetHit)으로 끊긴다.
/// MovementLockedState 포맷: MovementLocked 제약 자동 바인딩.
/// </summary>
public class SnailShellState : MovementLockedState<SnailShellData>
{
    private static readonly int Color07Id = Shader.PropertyToID("_Color07");
    private static readonly int Color08Id = Shader.PropertyToID("_Color08");
    private static readonly Color ShellColor = new Color(0.4f, 0.7f, 1f);

    private float      _timer;
    private Renderer[] _renderers;

    public SnailShellState(SnailShellData data) : base(data) { }

    public override void Enter(LeeMonsterContext ctx)
    {
        ctx.Agent.ResetPath();
        _timer = Data.duration;
        ctx.Runtime.DamageMultiplier = Data.damageMultiplier;
        ctx.Animator.CrossFade("Defend", 0.15f);

        _renderers = ctx.Transform.GetComponentsInChildren<Renderer>(true);
        ApplyTint(_renderers, ShellColor);
    }

    public override void Update(LeeMonsterContext ctx)
    {
        _timer -= Time.deltaTime;
        if (_timer <= 0f)
            ctx.Monster.ChangeState<LeeChaseState>();
    }

    public override void Exit(LeeMonsterContext ctx)
    {
        ctx.Runtime.DamageMultiplier = 1f;
        ClearTint(_renderers);
    }

    private void ApplyTint(Renderer[] renderers, Color color)
    {
        var mpb = new MaterialPropertyBlock();
        foreach (var r in renderers)
        {
            r.GetPropertyBlock(mpb);
            if (r.sharedMaterial.HasProperty(Color07Id)) mpb.SetColor(Color07Id, color);
            if (r.sharedMaterial.HasProperty(Color08Id)) mpb.SetColor(Color08Id, color);
            r.SetPropertyBlock(mpb);
        }
    }

    private static void ClearTint(Renderer[] renderers)
    {
        if (renderers == null) return;
        foreach (var r in renderers)
            r.SetPropertyBlock(null);
    }
}
