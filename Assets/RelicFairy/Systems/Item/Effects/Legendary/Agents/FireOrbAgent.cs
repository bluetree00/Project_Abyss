using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// F-3 화염 오브: 벽 반사 + 이동 궤적 화염 trail + 적 충돌 시 피해.
/// </summary>
public sealed class FireOrbAgent : LegendaryProjectileBase
{
    private static readonly string[] s_hitSfx = { SoundKey.Sfx.MonsterHit1, SoundKey.Sfx.MonsterHit2, SoundKey.Sfx.MonsterHit3 };

    private float _trailTimer;
    private const float TrailInterval = 0.25f;


    protected override void TryNavMeshBounce() { } // BoxCollider 벽에서만 반사

    protected override void OnSpawnVfx()
    {
        var cat = LegendaryRuntime.Catalog;
        if (cat?.fireOrbVfx != null)
        {
            var vfx = Instantiate(cat.fireOrbVfx, transform);
            vfx.transform.localPosition = Vector3.zero;
            vfx.transform.localScale = Vector3.one * 1.5f;
            foreach (var col in vfx.GetComponentsInChildren<Collider>(true))
                col.enabled = false;
        }
    }

    protected override void Update()
    {
        base.Update();
        SpawnTrail();
    }

    private void SpawnTrail()
    {
        _trailTimer += Time.deltaTime;
        if (_trailTimer < TrailInterval) return;
        _trailTimer = 0f;

        var cat = LegendaryRuntime.Catalog;
        if (cat?.fireAoeVfx == null) return;

        var pos = transform.position;
        int groundMask = LayerMask.GetMask("Ground");
        if (Physics.Raycast(pos + Vector3.up * 0.5f, Vector3.down, out var hit, 5f, groundMask))
            pos.y = hit.point.y + 0.02f;

        var t = Instantiate(cat.fireAoeVfx, pos, Quaternion.identity);
        t.transform.localScale = new Vector3(0.15f, 0.01f, 0.15f);
        Destroy(t, 1.5f);
    }

    protected override void OnHitEnemy(GameObject target)
    {
        base.OnHitEnemy(target);
        Managers.Sound?.PlayEffectAsync(s_hitSfx[Random.Range(0, s_hitSfx.Length)]).Forget();
    }

    protected override void OnSpawnHitVfx(Vector3 pos)
    {
        var cat = LegendaryRuntime.Catalog;
        if (cat?.fireOrbHitVfx != null)
            LegendaryRuntime.SpawnVfx(cat.fireOrbHitVfx, pos, Quaternion.identity, 2f);
    }
}
