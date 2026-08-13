using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// D-3 어둠의 낫: 칼날 피벗 회전으로 곡선 비행 + 관통 + 어둠 피해.
/// 방향(_dir)에 일정 각속도를 적용해 호형 경로를 만들고,
/// 벽 반사 시 곡선 방향 반전. NavMesh 경계는 BoxCollider만 사용.
/// </summary>
public sealed class DarkScytheAgent : LegendaryProjectileBase
{
    private static readonly string[] s_hitSfx = { SoundKey.Sfx.MonsterHit1, SoundKey.Sfx.MonsterHit2, SoundKey.Sfx.MonsterHit3 };

    private const float TurnRate = 80f;        // degrees/sec — 경로 곡률
    private const float SpinRate = 360f;       // degrees/sec — 메쉬 자전 속도
    private const float OrbitFlipMin = 3f;     // 궤도 방향 전환 최소 주기
    private const float OrbitFlipMax = 6f;     // 궤도 방향 전환 최대 주기

    private float _spinAngle;
    private float _turnSign;                   // +1 or -1 (곡선 좌/우 방향)
    private float _orbitFlipTimer;
    private Transform _meshVisual;

    public void InitScythe(object owner, ItemEffectContext ctx, float damage,
                           float speed, Vector3 startDir, float turnSign)
    {
        Init(owner, ctx, damage, speed, 9999f, RuneElement.Dark, startDir,
             maxBounces: 99999, pierce: true, hitRadius: 0.8f);
        _turnSign = turnSign;
        _orbitFlipTimer = Random.Range(OrbitFlipMin, OrbitFlipMax);
    }

    protected override void TryNavMeshBounce() { } // BoxCollider 벽에서만 반사

    protected override void Move()
    {
        // 가끔 궤도 방향 전환
        _orbitFlipTimer -= Time.deltaTime;
        if (_orbitFlipTimer <= 0f)
        {
            _turnSign = -_turnSign;
            _orbitFlipTimer = Random.Range(OrbitFlipMin, OrbitFlipMax);
        }

        // 진행 방향을 서서히 회전 → 호형 곡선 경로
        _dir = Quaternion.Euler(0f, TurnRate * _turnSign * Time.deltaTime, 0f) * _dir;
        _dir.y = 0f;
        _dir.Normalize();

        transform.position += _dir * _speed * Time.deltaTime;

        // 낫 메쉬 Y축 자전 (수평면에서 스핀)
        _spinAngle += SpinRate * Time.deltaTime;
        if (_meshVisual != null)
            _meshVisual.localRotation = Quaternion.Euler(0f, _spinAngle, 0f);
    }

    protected override void OnBounced(Vector3 hitPoint)
    {
        _turnSign = -_turnSign; // 반사 시 곡선 방향 반전
    }

    protected override void OnSpawnVfx()
    {
        var cat = LegendaryRuntime.Catalog;
        if (cat?.darkScytheVfx == null) return;

        _meshVisual = new GameObject("ScytheVisual").transform;
        _meshVisual.SetParent(transform, false);
        _meshVisual.localPosition = Vector3.zero;

        var mesh = Object.Instantiate(cat.darkScytheVfx, _meshVisual);
        mesh.transform.localPosition = Vector3.zero;
        mesh.transform.localScale = Vector3.one * 0.6f; // 프리팹 0.25 → 0.6으로 교체 (약 2.4배)
    }

    protected override void OnHitEnemy(GameObject target)
    {
        base.OnHitEnemy(target);
        Managers.Sound?.PlayEffectAsync(s_hitSfx[Random.Range(0, s_hitSfx.Length)]).Forget();
    }

    protected override void OnSpawnHitVfx(Vector3 pos)
    {
        var cat = LegendaryRuntime.Catalog;
        if (cat?.darkHitVfx != null)
            LegendaryRuntime.SpawnVfx(cat.darkHitVfx, pos, Quaternion.identity, 2f);
    }
}
