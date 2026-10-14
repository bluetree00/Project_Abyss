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

    private const float PhaseFlank = 3.5f;   // 위상 중 플레이어 옆으로 돌아 들어간다 — 예전엔 길을 지우고 제자리에서 달렸다(10-14 정예 애니 점검)

    private float      _timer;
    private Renderer[] _renderers;

    public SpecterPhaseState(SpecterPhaseData data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _timer     = Data.phaseDuration;
        _renderers = ctx.Transform.GetComponentsInChildren<Renderer>(true);

        PickFlank(ctx);
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
            return;
        }
        if (ctx.Agent.isOnNavMesh && !ctx.Agent.pathPending && ctx.Agent.remainingDistance < 0.4f) PickFlank(ctx);
    }

    // 플레이어를 축으로 90° 옆(3.5 m) — 닿으면 반대편으로
    private static void PickFlank(MonsterContext ctx)
    {
        if (!ctx.Agent.isOnNavMesh) return;
        var target = ctx.Runtime.PlayerTarget;
        if (target == null) { ctx.Agent.ResetPath(); return; }
        Vector3 p = target.position;
        Vector3 from = ctx.Transform.position - p; from.y = 0f;
        if (from.sqrMagnitude < 0.01f) from = -ctx.Transform.forward;
        float side = Random.value < 0.5f ? 90f : -90f;
        Vector3 want = p + Quaternion.Euler(0f, side, 0f) * from.normalized * PhaseFlank;
        if (!UnityEngine.AI.NavMesh.SamplePosition(want, out var hit, 2f, ctx.Agent.areaMask))
        {
            want = p + Quaternion.Euler(0f, -side, 0f) * from.normalized * PhaseFlank;
            if (!UnityEngine.AI.NavMesh.SamplePosition(want, out hit, 2f, ctx.Agent.areaMask)) { ctx.Agent.ResetPath(); return; }
        }
        ctx.Agent.isStopped = false;
        ctx.Agent.SetDestination(hit.position);
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
