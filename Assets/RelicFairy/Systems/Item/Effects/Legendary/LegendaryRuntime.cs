using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 레전드리 룬 투사체 에이전트 생명주기 관리 + VFX 카탈로그 보관.
/// AppBootstrapper에 컴포넌트로 부착. [SerializeField] 카탈로그를 Inspector에서 연결.
/// EnsureExists()는 이미 씬에 존재하는 인스턴스를 반환하므로 중복 생성되지 않는다.
/// </summary>
public class LegendaryRuntime : MonoBehaviour
{
    // URP는 GrabPass를 지원하지 않는다. 이 셰이더로 그리면 화면이 하얗게 덮이고 `_GrabTexture` 오류가 매 프레임 찍힌다
    // (에디터에선 씬 뷰가 같은 오류를 내 Error Pause로 플레이가 멈춘다 — 09-19 「심연 잠식 뒤 타격 0」의 원인).
    // 이펙트 팩 원본은 다른 곳도 같이 쓰므로 고치지 않고, 띄울 때 왜곡 조각만 끈다.
    private static readonly HashSet<string> GrabPassShaders = new()
    {
        "GAPH Custom Shader/Distortion Effect", "Hovl/Particles/Distortion", "Hovl/Particles/BlendDistort",
    };

    // 풀링 #7(11-02) — 전설 이펙트 전용 풀. BossEffectPool과 사본을 나누지 않는다: 같은 프리팹을 쓰는 리치 쪽이 사본의 입자 크기 모드 · 반복 · 색을 바꿔 둔다.
    private const int FxPoolCap = 16;   // 프리팹당 쉬는 사본 상한 — 넘치면 지운다
    private static readonly Dictionary<GameObject, Stack<GameObject>> s_fxPool = new();
    private static readonly Dictionary<GameObject, bool> s_poolable = new();
    private static readonly List<ParticleSystem> s_psBuf = new();
    private static readonly List<TrailRenderer> s_trailBuf = new();

    public static LegendaryRuntime Instance { get; private set; }
    public static LegendaryVfxCatalog Catalog => Instance?._catalog;

    [SerializeField] private LegendaryVfxCatalog _catalog;

    private readonly List<GameObject> _agents = new();
    private readonly List<PooledFx> _fxLive = new();

    private struct PooledFx { public GameObject go; public GameObject prefab; public float releaseAt; }

    public static void EnsureExists()
    {
        if (Instance != null) return;
        var go = new GameObject("[LegendaryRuntime]");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<LegendaryRuntime>();
        Instance.LoadCatalogAsync().Forget();
    }

    private const string CatalogKey = "LegendaryVfxCatalog";

    // 씬 컴포넌트로 _catalog가 이미 연결된 경우 스킵. 코드로 만든 경우(EnsureExists) Addressables로 불러온다 —
    // 예전엔 에디터 전용 AssetDatabase 분기뿐이라 빌드에서 카탈로그가 늘 비어, 레전드리 룬 이펙트가 하나도 안 보였다(10-09 출시 점검).
    // 에디터도 같은 길을 타서 빌드와 다르게 보이지 않게 한다.
    private async UniTaskVoid LoadCatalogAsync()
    {
        if (_catalog != null) return;
        try
        {
            var cat = await Managers.AddressableManager.TryLoadAssetAsync<LegendaryVfxCatalog>(CatalogKey);
            if (this == null || _catalog != null) return;
            _catalog = cat;
            if (cat == null) Debug.LogError($"[LegendaryRuntime] 이펙트 카탈로그 「{CatalogKey}」를 못 불렀다 — 레전드리 룬 이펙트가 안 보인다");
        }
        catch (System.OperationCanceledException) { }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    // 풀에서 빌린 이펙트를 수명이 다하면 돌려준다(Destroy(go, 수명)과 같은 게임 시간)
    private void Update()
    {
        for (int i = _fxLive.Count - 1; i >= 0; i--)
        {
            var fx = _fxLive[i];
            if (fx.go != null && Time.time < fx.releaseAt) continue;
            _fxLive.RemoveAt(i);
            ReturnFx(fx.go, fx.prefab);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void TrackAgent(GameObject agent)
    {
        if (agent != null) _agents.Add(agent);
    }

    public void RemoveAgent(GameObject agent) => _agents.Remove(agent);

    /// <summary>소유자 오브젝트가 일치하는 에이전트만 파괴.</summary>
    public void ClearAgentsOf(object owner)
    {
        for (int i = _agents.Count - 1; i >= 0; i--)
        {
            var a = _agents[i];
            if (a == null) { _agents.RemoveAt(i); continue; }
            if (a.TryGetComponent<LegendaryProjectileBase>(out var proj) && proj.Owner == owner)
            {
                _agents.RemoveAt(i);
                Destroy(a);
            }
            else if (a.TryGetComponent<IceZoneField>(out var zone) && zone.Owner == owner)
            {
                _agents.RemoveAt(i);
                Destroy(a);
            }
            else if (a.TryGetComponent<LegendGroundField>(out var ground) && ground.Owner == owner)
            {
                _agents.RemoveAt(i);
                Destroy(a);
            }
            else if (a.TryGetComponent<LegendOrbitRing>(out var ring) && ring.Owner == owner)
            {
                _agents.RemoveAt(i);
                Destroy(a);
            }
        }
    }

    public void ClearAll()
    {
        foreach (var a in _agents)
            if (a != null) Destroy(a);
        _agents.Clear();
    }

    /// <summary>한 번 재생 후 자동 파괴되는 VFX 스폰. 플레이어 충돌 방지를 위해 Collider를 비활성화한다.</summary>
    /// <param name="yScale">0 이상이면 Y축 스케일만 별도 지정 (scale은 XZ에만 적용).</param>
    public static void SpawnVfx(GameObject prefab, Vector3 pos, Quaternion rot, float lifetime = 3f, float scale = 1f, float alpha = 1f, float yScale = -1f, Transform follow = null)
    {
        if (prefab == null) return;
        alpha *= PlayerFxCalm.Alpha;   // 예고가 떠 있으면 옅게(10-30)
        var ls = Vector3.one * scale;
        if (yScale >= 0f) ls.y = yScale;

        // 풀링 #7(11-02 실측: 매번 만들기 0.13~2.4 ms · 재사용 0.02~0.09 ms) — 따라붙지 않고 · 알파를 안 칠하고 · 스크립트 없는 것만 풀로.
        // 알파 칠은 사본 색을 바꾸고, follow는 부모와 함께 지워지고, 팩 스크립트는 재사용하면 그림이 달라진다(10-29 문서 7절).
        if (follow == null && Mathf.Approximately(alpha, 1f) && Instance != null && IsPoolable(prefab))
        {
            Instance._fxLive.Add(new PooledFx { go = RentFx(prefab, pos, rot, ls), prefab = prefab, releaseAt = Time.time + lifetime });
            return;
        }

        var vfx = Instantiate(prefab, pos, rot);
        DisableGrabPassRenderers(vfx);
        PlayerFxCalm.LowerSorting(vfx);   // 보스 예고 아래로(10-30)
        vfx.transform.localScale = ls;
        if (follow != null) vfx.transform.SetParent(follow, true);   // 판정이 따라가는 광역 — 그림도 붙인다(10-13 이펙트 = 판정)
        if (!Mathf.Approximately(alpha, 1f))
        {
            foreach (var ps in vfx.GetComponentsInChildren<ParticleSystem>())
            {
                var main = ps.main;
                var sc = main.startColor;
                if (sc.mode == ParticleSystemGradientMode.Color)
                {
                    var c = sc.color;
                    c.a *= alpha;
                    main.startColor = c;
                }
            }
        }
        foreach (var col in vfx.GetComponentsInChildren<Collider>())
            col.enabled = false;
        Destroy(vfx, lifetime);
    }

    /// <summary>GrabPass 왜곡 조각(URP 미지원)만 끈다 — 나머지 파티클은 그대로 그려진다.</summary>
    public static void DisableGrabPassRenderers(GameObject vfx)
    {
        if (vfx == null) return;
        foreach (var r in vfx.GetComponentsInChildren<Renderer>(true))
        {
            var m = r.sharedMaterial;
            if (m != null && m.shader != null && GrabPassShaders.Contains(m.shader.name)) r.enabled = false;
        }
    }

    /// <summary>현재 씬 NavMesh 삼각분할을 기반으로 맵 내 임의 위치를 반환한다. 투사체/필드 배치용.</summary>
    /// <remarks>NavMesh 경계에서 최소 1.5m 이상 안쪽인 점만 반환해 Floor 밖 생성을 방지한다.</remarks>
    public static Vector3 GetRandomNavPoint(float y)
    {
        var tri = NavMesh.CalculateTriangulation();
        if (tri.vertices.Length == 0) return Vector3.up * y;
        for (int i = 0; i < 40; i++)
        {
            Vector3 v = tri.vertices[Random.Range(0, tri.vertices.Length)];
            v.y = y;
            if (!NavMesh.SamplePosition(v, out NavMeshHit hit, 2f, NavMesh.AllAreas)) continue;
            // 경계에서 1.5m 이상 안쪽인 점만 사용
            if (NavMesh.FindClosestEdge(hit.position, out NavMeshHit edge, NavMesh.AllAreas)
                && edge.distance >= 1.5f)
                return new Vector3(hit.position.x, y, hit.position.z);
        }
        return Vector3.up * y;
    }

    /// <summary>단일기용: target 주변 여러 각도에서 수렴하는 VFX를 스폰한다.</summary>
    /// <param name="elevationVariance">수직 방향 랜덤 범위(도). 0이면 수평만. 예: 40f → ±40도 대각선 혼합.</param>
    public static void SpawnConvergingVfx(GameObject prefab, Vector3 targetPos, float lifetime, int count = 4, float offset = 2f, float scale = 1f, float alpha = 1f, float elevationVariance = 0f)
    {
        if (prefab == null) return;
        for (int i = 0; i < count; i++)
        {
            float yaw = i * 360f / count + Random.Range(-15f, 15f);
            float pitch = elevationVariance > 0f ? Random.Range(-elevationVariance, -elevationVariance * 0.3f) : 0f;
            Vector3 spawnPos = targetPos + Quaternion.Euler(pitch, yaw, 0) * Vector3.forward * offset;
            Vector3 dir = targetPos - spawnPos;
            Quaternion rot = dir.sqrMagnitude > 0.01f ? Quaternion.LookRotation(dir) : Quaternion.identity;
            SpawnVfx(prefab, spawnPos, rot, lifetime, scale, alpha);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetFxPool()
    {
        s_fxPool.Clear();
        s_poolable.Clear();
    }

    /// <summary>스크립트 없는(입자만 있는) 프리팹인가 — 재사용해도 그림이 같다(프리팹당 한 번 검사).</summary>
    private static bool IsPoolable(GameObject prefab)
    {
        if (!s_poolable.TryGetValue(prefab, out bool ok))
        {
            ok = prefab.GetComponentsInChildren<MonoBehaviour>(true).Length == 0;
            s_poolable[prefab] = ok;
        }
        return ok;
    }

    /// <summary>쉬는 사본을 꺼내거나(씬과 함께 지워진 것은 버린다) 새로 만들어 처음부터 다시 튼다. 손질(왜곡 끔 · 정렬 낮춤 · 충돌 끔)은 만들 때 한 번.</summary>
    private static GameObject RentFx(GameObject prefab, Vector3 pos, Quaternion rot, Vector3 scale)
    {
        GameObject go = null;
        if (s_fxPool.TryGetValue(prefab, out var stack))
            while (go == null && stack.Count > 0) go = stack.Pop();
        if (go == null)
        {
            go = Instantiate(prefab, pos, rot);
            DisableGrabPassRenderers(go);
            PlayerFxCalm.LowerSorting(go);   // 보스 예고 아래로(10-30)
            foreach (var col in go.GetComponentsInChildren<Collider>())
                col.enabled = false;
        }
        go.transform.SetPositionAndRotation(pos, rot);
        go.transform.localScale = scale;
        go.SetActive(true);
        go.GetComponentsInChildren(s_trailBuf);
        for (int i = 0; i < s_trailBuf.Count; i++) s_trailBuf[i].Clear();
        s_trailBuf.Clear();
        go.GetComponentsInChildren(s_psBuf);
        for (int i = 0; i < s_psBuf.Count; i++) { s_psBuf[i].Clear(true); s_psBuf[i].Play(true); }
        s_psBuf.Clear();
        return go;
    }

    private static void ReturnFx(GameObject go, GameObject prefab)
    {
        if (go == null) return;
        if (!s_fxPool.TryGetValue(prefab, out var stack)) s_fxPool[prefab] = stack = new Stack<GameObject>();
        if (stack.Count >= FxPoolCap) { Destroy(go); return; }
        go.SetActive(false);
        stack.Push(go);
    }
}
