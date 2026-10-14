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
    // 궤적 불꽃 풀 — 0.25초마다 Instantiate · 1.5초 뒤 Destroy(구체 5개면 초당 20개)를 칸 6개 돌려쓰기로(10-11 톤 2차 V-10)
    private const int   TrailSlots    = 6;      // 1.5초 수명 ÷ 0.25초 간격
    private readonly GameObject[]       _trail   = new GameObject[TrailSlots];
    private readonly ParticleSystem[][] _trailPs = new ParticleSystem[TrailSlots][];
    private int _trailNext;


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
        if (IsIdle) return;   // 비전투 방 — 구체를 숨긴 동안 궤적도 남기지 않는다

        var cat = LegendaryRuntime.Catalog;
        if (cat?.fireAoeVfx == null) return;

        var pos = transform.position;
        int groundMask = LayerMask.GetMask("Ground");
        if (Physics.Raycast(pos + Vector3.up * 0.5f, Vector3.down, out var hit, 5f, groundMask))
            pos.y = hit.point.y + 0.02f;

        int k = _trailNext;
        _trailNext = (_trailNext + 1) % TrailSlots;
        var t = _trail[k];
        if (t == null)
        {
            t = Instantiate(cat.fireAoeVfx, pos, Quaternion.identity);
            LegendaryRuntime.DisableGrabPassRenderers(t);   // 궤적마다 띄우는 화염 — 왜곡 조각은 URP에서 화면을 덮는다
            t.transform.localScale = new Vector3(0.15f, 0.01f, 0.15f);
            _trail[k]   = t;
            _trailPs[k] = t.GetComponentsInChildren<ParticleSystem>(true);
            return;
        }
        t.transform.position = pos;   // 한 바퀴(1.5초) 전 칸 — 다 탄 불꽃을 이 자리에서 다시 피운다
        var systems = _trailPs[k];
        for (int i = 0; i < systems.Length; i++)
        {
            systems[i].Clear(true);
            systems[i].Play(true);
        }
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        for (int i = 0; i < _trail.Length; i++)
            if (_trail[i] != null) Destroy(_trail[i]);
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
