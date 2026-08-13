using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 레전드리 룬 투사체 에이전트 생명주기 관리 + VFX 카탈로그 보관.
/// AppBootstrapper에 컴포넌트로 부착. [SerializeField] 카탈로그를 Inspector에서 연결.
/// EnsureExists()는 이미 씬에 존재하는 인스턴스를 반환하므로 중복 생성되지 않는다.
/// </summary>
public class LegendaryRuntime : MonoBehaviour
{
    public static LegendaryRuntime Instance { get; private set; }
    public static LegendaryVfxCatalog Catalog => Instance?._catalog;

    [SerializeField] private LegendaryVfxCatalog _catalog;

    private readonly List<GameObject> _agents = new();

    public static void EnsureExists()
    {
        if (Instance != null) return;
        var go = new GameObject("[LegendaryRuntime]");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<LegendaryRuntime>();
        Instance.TryLoadCatalogFallback();
    }

    // 씬 컴포넌트로 _catalog가 이미 연결된 경우 스킵.
    // 에디터 플레이 모드에서 EnsureExists()로 생성된 경우 AssetDatabase로 폴백.
    private void TryLoadCatalogFallback()
    {
        if (_catalog != null) return;
#if UNITY_EDITOR
        _catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<LegendaryVfxCatalog>(
            "Assets/RelicFairy/Systems/Item/Effects/Legendary/LegendaryVfxCatalog.asset");
#endif
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
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
    public static void SpawnVfx(GameObject prefab, Vector3 pos, Quaternion rot, float lifetime = 3f, float scale = 1f, float alpha = 1f, float yScale = -1f)
    {
        if (prefab == null) return;
        var vfx = Instantiate(prefab, pos, rot);
        var ls = Vector3.one * scale;
        if (yScale >= 0f) ls.y = yScale;
        vfx.transform.localScale = ls;
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
}
