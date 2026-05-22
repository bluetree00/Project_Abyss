using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 스펙터 위상 이동 특수 상태.
/// HP 임계값 이하 도달 시 1회 발동 — 연한 파랑 유령 색상으로 투명화하여 phaseDuration 동안
/// 자유롭게 이동한 뒤 ChaseState 로 복귀한다.
/// InvincibleState 포맷: 위상 중 데미지 완전 차단.
/// </summary>
public class SpecterPhaseState : InvincibleState<SpecterPhaseData>
{
    private static readonly int Color07Id = Shader.PropertyToID("_Color07");
    private static readonly int Color08Id = Shader.PropertyToID("_Color08");
    private static readonly Color GhostColor = new Color(0.5f, 0.8f, 1.0f);

    private float      _timer;
    private Renderer[] _renderers;

    public SpecterPhaseState(SpecterPhaseData data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _timer     = Data.phaseDuration;
        _renderers = ctx.Transform.GetComponentsInChildren<Renderer>(true);

        ctx.Agent.ResetPath();
        ctx.Animator?.CrossFade(Data.phaseStateName, 0.1f);
        Data.SpawnVFX(ctx.Transform);
        ApplyTint(ctx.Transform, GhostColor);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer -= Time.deltaTime;
        if (_timer <= 0f)
        {
            ClearTint(_renderers);
            ctx.Monster.ChangeState<ChaseState>();
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        ClearTint(_renderers);
    }

    private void ApplyTint(Transform root, Color color)
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

    private static void ClearTint(Renderer[] renderers)
    {
        if (renderers == null) return;
        var mpb = new MaterialPropertyBlock();
        foreach (var r in renderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(mpb);
            mpb.Clear();
            r.SetPropertyBlock(mpb);
        }
    }
}
