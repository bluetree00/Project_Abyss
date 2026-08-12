using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// L-3 영원의 성검: 벽 반사 + 적 충돌 시 빛 피해.
/// pierce=true 무한 지속.
/// </summary>
public sealed class LightSwordAgent : LegendaryProjectileBase
{
    private static readonly string[] s_hitSfx = { SoundKey.Sfx.MonsterHit1, SoundKey.Sfx.MonsterHit2, SoundKey.Sfx.MonsterHit3 };

    protected override void TryNavMeshBounce() { } // BoxCollider 벽에서만 반사

    protected override void Move()
    {
        base.Move();
        if (_dir.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(_dir);
    }

    protected override void OnSpawnVfx()
    {
        if (_dir != Vector3.zero)
            transform.rotation = Quaternion.LookRotation(_dir);

        var cat = LegendaryRuntime.Catalog;
        if (cat?.lightSwordVfx != null)
        {
            var vfx = Instantiate(cat.lightSwordVfx, transform);
            vfx.transform.localPosition = Vector3.zero;
            vfx.transform.localScale    = Vector3.one * 1.5f;
        }
    }

    protected override void OnHitEnemy(GameObject target)
    {
        base.OnHitEnemy(target);
        Managers.Sound?.PlayEffectAsync(s_hitSfx[Random.Range(0, s_hitSfx.Length)]).Forget();
    }

    protected override void OnSpawnHitVfx(Vector3 pos)
    {
        var cat = LegendaryRuntime.Catalog;
        if (cat?.lightHitVfx != null)
            LegendaryRuntime.SpawnVfx(cat.lightHitVfx, pos, Quaternion.identity, 2f);
    }
}
