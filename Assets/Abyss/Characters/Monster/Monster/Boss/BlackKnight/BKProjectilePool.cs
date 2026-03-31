using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// BKBossProjectile 오브젝트 풀.
/// BlackKnightBoss.OnInitialized 에서 생성, BKScatterShotState 가 참조.
/// </summary>
public class BKProjectilePool
{
    private readonly GameObject                _prefab;
    private readonly Transform                 _container;
    private readonly Queue<BKBossProjectile>   _idle = new();
    private readonly HashSet<BKBossProjectile> _active = new();

    /// <param name="container">풀 오브젝트를 묶어 놓을 부모 Transform (null 허용)</param>
    public BKProjectilePool(GameObject prefab, int initialSize, Transform container = null)
    {
        _prefab    = prefab;
        _container = container;
        for (int i = 0; i < initialSize; i++)
            Enqueue(CreateNew());
    }

    // ── 풀에서 꺼내기 ─────────────────────────────────────
    public BKBossProjectile Get(Vector3 position, Quaternion rotation)
    {
        BKBossProjectile proj = _idle.Count > 0 ? _idle.Dequeue() : CreateNew();
        proj.transform.SetParent(null);  // 월드 공간으로 분리 → 보스 회전 영향 차단
        proj.transform.SetPositionAndRotation(position, rotation);

        // Rigidbody constraints 초기화 (잔류 FreezeAll 방지)
        foreach (var rb in proj.GetComponentsInChildren<Rigidbody>(true))
            rb.constraints = RigidbodyConstraints.None;

        // TrailRenderer: SetActive 이전에 Clear → 활성화 첫 프레임에 이전 궤적이 렌더되는 것 방지
        foreach (var tr in proj.GetComponentsInChildren<TrailRenderer>(true))
            tr.Clear();

        proj.gameObject.SetActive(true);
        _active.Add(proj);
        return proj;
    }

    // ── 풀에 반납 ─────────────────────────────────────────
    public void Return(BKBossProjectile proj)
    {
        if (!_active.Remove(proj)) return;
        Enqueue(proj);
    }

    // ── 활성 투사체 전체 강제 반납 (보스 재사용 시) ──────
    public void RecycleAll()
    {
        foreach (var proj in _active)
        {
            if (proj != null) Enqueue(proj);
        }
        _active.Clear();
    }

    // ── 풀 전체 해제 (보스 파괴 시) ─────────────────────
    public void Dispose()
    {
        _active.Clear(); // 이미 파괴 중이면 Enqueue 하지 않음
        while (_idle.Count > 0)
        {
            var p = _idle.Dequeue();
            if (p != null) Object.Destroy(p.gameObject);
        }
    }

    // ─────────────────────────────────────────────────────
    private BKBossProjectile CreateNew()
    {
        var go = Object.Instantiate(_prefab, _container);
        go.name = _prefab.name;
        go.SetActive(false);

        // Hovl Studio 컴포넌트 전체 비활성화
        // HS_ProjectileMover: OnCollisionEnter 에서 rb.constraints = FreezeAll 을 걸어
        //   끝부분에서 투사체가 휘는 원인이 됨 → Destroy 로 완전 제거
        foreach (var mover in go.GetComponentsInChildren<HS_ProjectileMover>(true))
            Object.Destroy(mover);

        // HS_Poolable: rejoinMode=Auto + delayBeforeAutoRejoin=1 → SetActive 후 1초만에 강제 비활성화
        foreach (var poolable in go.GetComponentsInChildren<CGT.Pooling.HS_Poolable>(true))
            poolable.enabled = false;

        // Rigidbody: 루트 및 자식 모두 kinematic + gravity off
        // BKBossProjectile 이 transform 을 직접 이동하므로 물리 시뮬레이션 충돌 방지
        foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true))
        {
            rb.isKinematic  = true;
            rb.useGravity   = false;
        }

        // 볼륨 절반
        foreach (var src in go.GetComponentsInChildren<AudioSource>(true))
            src.volume *= 0.5f;

        var proj = go.GetComponent<BKBossProjectile>() ?? go.AddComponent<BKBossProjectile>();
        proj.Pool = this;
        return proj;
    }

    private void Enqueue(BKBossProjectile proj)
    {
        proj.gameObject.SetActive(false);
        proj.transform.SetParent(_container);  // 풀 컨테이너로 복귀
        _idle.Enqueue(proj);
    }
}
