using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// T-3 전자기 구체가 발사하는 볼트: 반사 없음, 벽/적 충돌 시 소멸.
/// </summary>
public sealed class ElecBoltAgent : LegendaryProjectileBase
{
    private static readonly string[] s_hitSfx = { SoundKey.Sfx.MonsterHit1, SoundKey.Sfx.MonsterHit2, SoundKey.Sfx.MonsterHit3 };

    public void InitBolt(object owner, ItemEffectContext ctx, float damage, Vector3 dir)
    {
        Init(owner, ctx, damage, 20f, 5f, RuneElement.Electric, dir,
             maxBounces: 0, pierce: false, hitRadius: 0.4f);
    }

    protected override void TryNavMeshBounce() { }

    protected override void OnHitEnemy(GameObject target)
    {
        base.OnHitEnemy(target);
        Managers.Sound?.PlayEffectAsync(s_hitSfx[Random.Range(0, s_hitSfx.Length)]).Forget();
    }

    protected override void OnSpawnHitVfx(Vector3 pos)
    {
        var cat = LegendaryRuntime.Catalog;
        LegendaryRuntime.SpawnVfx(cat?.elecHitVfx, pos, Quaternion.identity, 1.5f, 0.4f);
    }
}
