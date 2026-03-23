using UnityEngine;

/// <summary>
/// 스켈레톤 몬스터.
/// 특수 상태: HP 50% 이하 도달 시 1회 광폭화 — 이후 영구적으로 이동속도·공격력 증가 (SkeletonEnrageState).
/// </summary>
public class SkeletonMonster : LeeMonsterBase
{
    public const string PrefabAddress = "Skeleton/Skeleton";
    protected override string ConfigAddress    => "Skeleton/SkeletonConfig";
    protected override string DataAddress      => "Skeleton/SkeletonData";
    protected override float  HPBarHeadOffset  => 0.7f;

    private bool _hasEnraged;

    protected override void OnEnable()
    {
        _hasEnraged = false;
        ResetTint();
        base.OnEnable();
    }

    private static readonly int Color07Id = Shader.PropertyToID("_Color07");
    private static readonly int Color08Id = Shader.PropertyToID("_Color08");

    private void ResetTint()
    {
        var mpb = new MaterialPropertyBlock();
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            r.GetPropertyBlock(mpb);
            if (r.sharedMaterial.HasProperty(Color07Id)) mpb.SetColor(Color07Id, Color.white);
            if (r.sharedMaterial.HasProperty(Color08Id)) mpb.SetColor(Color08Id, Color.white);
            r.SetPropertyBlock(mpb);
        }
    }

    public override ILeeMonsterState TryGetSpecialState(LeeMonsterContext ctx)
    {
        var state = GetSpecialState(0);
        if (state == null || _hasEnraged) return null;
        if (_config.specialState0 is not SkeletonEnrageData enrageData) return null;

        float hpRatio = (float)ctx.Runtime.CurrentHp / ctx.Config.stat.maxHp;
        if (hpRatio <= enrageData.hpThreshold)
        {
            _hasEnraged = true;
            return state;
        }
        return null;
    }
}
