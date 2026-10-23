using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// F-3 화염 오브: 벽 반사 + 적 충돌 시 피해. 불타는 바닥 꼬리는 피해 없는 「불바닥」으로 읽혀 없앴다(10-18 이펙트 점검 · 이펙트 남발 금지).
/// </summary>
public sealed class FireOrbAgent : LegendaryProjectileBase
{
    private static readonly string[] s_hitSfx = { SoundKey.Sfx.MonsterHit1, SoundKey.Sfx.MonsterHit2, SoundKey.Sfx.MonsterHit3 };

    protected override void TryNavMeshBounce() { } // BoxCollider 벽에서만 반사

    // ── 궤도(10-14 R3 「화염 오브 = 기동」) — 플레이어 둘레를 돈다 ──
    private Transform _orbitCenter;
    private float _orbitRadius, _orbitAngle, _orbitDegPerSec;

    /// <summary>플레이어 둘레를 돈다 — 반지름 · 시작 각(도) · 초당 각속도(도).</summary>
    public void SetOrbit(Transform center, float radius, float startDeg, float degPerSec)
    {
        _orbitCenter = center;
        _orbitRadius = radius;
        _orbitAngle  = startDeg;
        _orbitDegPerSec = degPerSec;
    }

    protected override bool TryBounce() => _orbitCenter == null && base.TryBounce();

    protected override void Move()
    {
        if (_orbitCenter == null) { base.Move(); return; }
        _orbitAngle += _orbitDegPerSec * Time.deltaTime;
        float rad = _orbitAngle * Mathf.Deg2Rad;
        Vector3 c = _orbitCenter.position;
        transform.position = new Vector3(c.x + Mathf.Cos(rad) * _orbitRadius, transform.position.y, c.z + Mathf.Sin(rad) * _orbitRadius);
    }

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

    protected override void OnHitEnemy(GameObject target)
    {
        base.OnHitEnemy(target);
        Managers.Sound?.PlayEffectAsync(s_hitSfx[Random.Range(0, s_hitSfx.Length)]).Forget();
    }

    protected override void OnSpawnHitVfx(Vector3 pos)
    {
        var cat = LegendaryRuntime.Catalog;
        if (cat?.fireOrbHitVfx != null)
            LegendaryRuntime.SpawnVfx(cat.fireOrbHitVfx, pos, Quaternion.identity, 2f, 0.19f);   // 대상 타격 ≈ 1 m(10-22 B2 실측 83: 노바 배율 1 ≈ 5.2 m)
    }
}
