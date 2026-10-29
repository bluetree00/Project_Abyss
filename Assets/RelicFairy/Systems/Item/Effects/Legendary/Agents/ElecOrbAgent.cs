using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// T-3 전자기 구체: 벽 반사 + 적 충돌 시 전기 폭발 피해.
/// 개별 3초 쿨다운으로 가장 가까운 적에게 전기 볼트 발사.
/// </summary>
public sealed class ElecOrbAgent : LegendaryProjectileBase
{
    private static readonly string[] s_hitSfx = { SoundKey.Sfx.MonsterHit1, SoundKey.Sfx.MonsterHit2, SoundKey.Sfx.MonsterHit3 };

    private float _explosionMult;
    private float _boltTimer;
    private const float BoltInterval = 3f;
    private readonly List<GameObject> _boltBuf = new(2);

    /// <summary>true면 3초마다 볼트, false면 <see cref="FireBoltNow"/>로만(전자기 구체 = 스킬 적중 때 · 10-14 R3).</summary>
    public bool TimedBolts { get; set; } = true;

    public void InitElec(object owner, ItemEffectContext ctx, float damage, float explosionMult,
                         float speed, float lifetime, Vector3 startDir)
    {
        Init(owner, ctx, damage, speed, lifetime, RuneElement.Electric, startDir,
             maxBounces: 99999, pierce: true, hitRadius: 0.55f);
        _explosionMult = explosionMult;
        _boltTimer = BoltInterval; // 첫 발사 즉시 대기
    }

    protected override void Update()
    {
        base.Update();
        if (!TimedBolts) return;
        _boltTimer -= Time.deltaTime;
        if (_boltTimer <= 0f)
        {
            _boltTimer = BoltInterval;
            TryFireBolt();
        }
    }

    /// <summary>가장 가까운 적에게 볼트 한 발(스킬 적중 때).</summary>
    public void FireBoltNow() => TryFireBolt();

    private void TryFireBolt()
    {
        if (_ctx?.Player == null) return;
        int n = CombatQuery.GetNearbyDamageables(transform.position, 30f, _ctx.Player.gameObject, 1, _boltBuf);
        if (n == 0) return;

        Vector3 toTarget = _boltBuf[0].transform.position - transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.01f) return;

        var go = new GameObject("ElecBolt");
        go.transform.position = transform.position;
        var bolt = go.AddComponent<ElecBoltAgent>();
        bolt.InitBolt(Owner, _ctx, CurrentDamage, toTarget.normalized);
    }

    protected override void TryNavMeshBounce() { } // BoxCollider 벽에서만 반사

    protected override void OnSpawnVfx()
    {
        var cat = LegendaryRuntime.Catalog;
        if (cat?.elecBallVfx != null)
        {
            var vfx = Instantiate(cat.elecBallVfx, transform);
            vfx.transform.localPosition = Vector3.zero;
            vfx.transform.localScale    = Vector3.one * 1.5f;
        }
    }

    private readonly List<GameObject> _blastBuffer = new(8);

    protected override void OnHitEnemy(GameObject target)
    {
        float hit = CurrentDamage;
        float explosionDmg = hit * _explosionMult;

        CombatQuery.DealSynergyDamage(target, hit, _ctx?.Player?.gameObject, element: RuneElement.Electric);

        // 폭발은 따로 담는다 — 적 감지 루프(DetectEnemies)가 _hitBuffer를 도는 중이라 덮어쓰면 범위 밖 예외 · 안 닿은 적 직격(10-24)
        int n = CombatQuery.GetNearbyDamageables(transform.position, 2f, target, 8, _blastBuffer);
        for (int i = 0; i < n; i++)
            CombatQuery.DealSynergyDamage(_blastBuffer[i], explosionDmg, _ctx?.Player?.gameObject, element: RuneElement.Electric);

        OnSpawnHitVfx(target.transform.position);
        Managers.Sound?.PlayEffectAsync(s_hitSfx[Random.Range(0, s_hitSfx.Length)]).Forget();
    }

    protected override void OnSpawnHitVfx(Vector3 pos)
    {
        var cat = LegendaryRuntime.Catalog;
        if (cat?.elecOrbHitVfx != null)
            LegendaryRuntime.SpawnVfx(cat.elecOrbHitVfx, pos, Quaternion.identity, 2f, 3f);   // 폭발 판정 2 m — 배율 1은 0.6 m로 보였다(10-18 실측 68)
    }
}
