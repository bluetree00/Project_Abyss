using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// S4-1 「하늘 → 탑 안」(10-03 설계 「모든 챕터에서 던전 안」 · 구현 설계 §1-1).
/// 런 씬에서 하늘(스카이박스)을 끄고 게임 카메라 배경을 탑 안 어둠으로 바꾼 뒤, 플레이어를 따라가는
/// <b>탑 벽 원통</b>(안쪽을 보는 반지름 120 m · 지금 방 벽과 같은 재질을 어둡게)과 아래 <b>구덩이 바닥</b>을 세운다.
/// 떠 있는 방 가장자리 · 먼 벽 너머 · 통로 틈으로 보이던 하늘이 「탑 안의 먼 벽과 아래로 꺼진 어둠」이 된다.
/// <para>원통은 방마다 다시 짓지 않는다 — 반지름이 어느 방보다 커서 플레이어를 따라 수평으로 옮기기만 한다(스카이박스처럼 먼 배경).
/// 카메라는 늘 방 벽보다 낮다(10-02 실측: 카메라 4.8~5.4 m · 벽 7~11 m) — 원통은 벽 너머 · 가장자리 너머에서만 보인다.</para>
/// <see cref="GameRunBootstrapper"/>가 Awake에서 붙인다.
/// </summary>
public sealed class DungeonSpaceDirector : MonoBehaviour
{
    // ── Constants ─────────────────────────────────────────────────
    private const float ShellRadius     = 120f;
    private const float ShellAbove      = 60f;    // 바닥 위로
    private const float ShellBelow      = 90f;    // 바닥 아래로 = 구덩이 깊이
    private const int   ShellSegments   = 64;
    private const float TextureTile     = 4f;     // 탑 벽 무늬 한 칸(m) — 방 벽 블록 크기와 비슷하게
    private const float FollowRate      = 4f;     // 높이 따라가기(1/초)
    private const float WallProbeRadius = 30f;
    private const float WallWaitSeconds = 20f;    // 대기방 짓기를 기다리는 상한

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId     = Shader.PropertyToID("_Color");
    private static readonly Collider[] s_wallHits = new Collider[16];
    private static DungeonSpaceDirector s_current;

    // ── Private ───────────────────────────────────────────────────
    private DungeonSpaceSetSO.Entry _entry;
    private Transform  _shell;
    private Mesh       _shellMesh;
    private Mesh       _pitMesh;
    private Material   _skyBefore;
    private bool       _skyHidden;
    private Camera     _cam;
    private CameraClearFlags _camFlagsBefore;
    private Color      _camBgBefore;
    private float      _groundY;
    private bool       _groundReady;
    private Material   _shaftMaterial;
    private bool       _ready;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Start() => InitAsync(this.GetCancellationTokenOnDestroy()).Forget();

    private void LateUpdate()
    {
        if (_shell == null) return;
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        if (player == null) return;
        Vector3 p = player.transform.position;
        if (!_groundReady) { _groundY = p.y; _groundReady = true; }
        _groundY = Mathf.Lerp(_groundY, p.y, 1f - Mathf.Exp(-FollowRate * Time.unscaledDeltaTime));
        _shell.position = new Vector3(p.x, _groundY, p.z);
    }

    private void OnDestroy()
    {
        if (_skyHidden) RenderSettings.skybox = _skyBefore;
        if (_cam != null && _skyHidden) { _cam.clearFlags = _camFlagsBefore; _cam.backgroundColor = _camBgBefore; }
        if (s_current == this) s_current = null;
        if (_shell != null) Destroy(_shell.gameObject);
        if (_shellMesh != null) Destroy(_shellMesh);
        if (_pitMesh != null) Destroy(_pitMesh);
    }

    // ── Public Methods ────────────────────────────────────────────
    /// <summary>S4-3 빛줄기 재질 · 챕터 빛깔 — 세트를 읽기 전(첫 방 짓기와 겹칠 때)이면 false(그 방은 빛줄기 없이).</summary>
    public static bool TryGetShaftStyle(out Material material, out Color color)
    {
        var d = s_current;
        bool ok = d != null && d._ready && d._shaftMaterial != null;
        material = ok ? d._shaftMaterial : null;
        color    = ok ? d._entry.shaftColor : default;
        return ok;
    }

    // ── Private Methods ───────────────────────────────────────────
    private async UniTaskVoid InitAsync(CancellationToken ct)
    {
        try
        {
            await UniTask.WaitUntil(() => GameRunBootstrapper.Instance?.Run?.Player != null && GameCameraController.Instance != null,
                                    cancellationToken: ct);
            var am = Managers.AddressableManager;
            var set = am != null ? await am.TryLoadAssetAsync<DungeonSpaceSetSO>(DungeonSpaceSetSO.Address) : null;
            ct.ThrowIfCancellationRequested();

            var chapter = GameRunBootstrapper.Instance.Run.CurrentChapter;
            if (set == null || !set.TryGet(chapter, out _entry))
            {
                Debug.LogWarning($"[DungeonSpace] 세트 · {chapter} 항목 없음 — 하늘 그대로({DungeonSpaceSetSO.Address})");
                return;
            }
            HideSky();
            _shaftMaterial = set.ShaftMaterial;
            _ready = true;
            s_current = this;

            // 탑 벽 재질 = 지금 방 벽 재질 — 대기방이 지어질 때까지 기다린다.
            Material wall = null;
            float t0 = Time.unscaledTime;
            while (wall == null && Time.unscaledTime - t0 < WallWaitSeconds)
            {
                wall = FindWallMaterial();
                if (wall == null) await UniTask.Delay(500, ignoreTimeScale: true, cancellationToken: ct);
            }
            if (wall == null) { Debug.LogWarning("[DungeonSpace] 벽 재질을 못 찾음 — 탑 벽 없이 어둠만"); return; }
            BuildShell(wall);
            int marks = PlaceSpawnMarks();
            Debug.Log($"[DungeonSpace] {chapter} — 하늘 끔 · 탑 벽 재질 {wall.name} · 도착 마법진 {marks}");
        }
        catch (OperationCanceledException) { }
    }

    private void HideSky()
    {
        _skyBefore = RenderSettings.skybox;
        RenderSettings.skybox = null;
        _cam = GameCameraController.Instance.GetComponent<Camera>();
        if (_cam != null)
        {
            _camFlagsBefore = _cam.clearFlags;
            _camBgBefore    = _cam.backgroundColor;
            _cam.clearFlags      = CameraClearFlags.SolidColor;
            _cam.backgroundColor = _entry.voidColor;
        }
        _skyHidden = true;
    }

    /// <summary>
    /// 플레이어 가까운 벽 콜라이더의 렌더러 재질(방 벽 블록). 벽 레이어가 없는 방(Ch1 보스 대기방 등 — 10-02 실측 「못 찾음」)은
    /// 바닥(Ground) 재질로 대신한다 — 같은 챕터 돌이라 탑 안 먼 벽으로 어색하지 않다. 둘 다 없으면 null.
    /// </summary>
    private static Material FindWallMaterial()
    {
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        if (player == null) return null;
        return FindLayerMaterial(player.transform.position, LayerMask.GetMask("Wall"))
            ?? FindLayerMaterial(player.transform.position, LayerMask.GetMask("Ground"));
    }

    private static Material FindLayerMaterial(Vector3 at, int mask)
    {
        int n = Physics.OverlapSphereNonAlloc(at, WallProbeRadius, s_wallHits, mask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var r = s_wallHits[i].GetComponentInParent<MeshRenderer>();
            if (r != null && r.sharedMaterial != null) return r.sharedMaterial;
        }
        return null;
    }

    /// <summary>
    /// 대기방 스폰 칸(<c>~PlayerSpawnMark</c>, MapBuilder가 남김)에 챕터 도착 마법진을 붙인다 — 공용 회색 구 대신(10-02 사용자).
    /// 대기방이 지어진 뒤(탑 벽 재질을 찾은 뒤) 한 번 부른다. 반환 = 붙인 수.
    /// </summary>
    private int PlaceSpawnMarks()
    {
        if (_entry.spawnMark == null) return 0;
        int n = 0;
        foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (t.name != "~PlayerSpawnMark" || t.childCount > 0) continue;
            var fx = Instantiate(_entry.spawnMark, t);
            fx.name = "~ArrivalCircle";
            fx.transform.localPosition = Vector3.zero;
            fx.transform.localScale = Vector3.one * (_entry.spawnMarkScale > 0f ? _entry.spawnMarkScale : 1f);
            n++;
        }
        return n;
    }

    private void BuildShell(Material wall)
    {
        var root = new GameObject("@DungeonShell").transform;
        _shell = root;

        _shellMesh = BuildCylinderMesh();
        AddMeshObject(root, "TowerWall", _shellMesh, wall, _entry.shellTint);

        _pitMesh = BuildDiskMesh(-ShellBelow);
        AddMeshObject(root, "PitFloor", _pitMesh, wall, _entry.pitTint);
    }

    private static void AddMeshObject(Transform parent, string name, Mesh mesh, Material mat, Color tint)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial    = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows    = false;
        var mpb = new MaterialPropertyBlock();
        mpb.SetColor(BaseColorId, tint);
        mpb.SetColor(ColorId, tint);
        r.SetPropertyBlock(mpb);
    }

    /// <summary>안쪽을 보는 열린 원통(뚜껑 없음). 무늬는 <see cref="TextureTile"/> m마다 한 번.</summary>
    private static Mesh BuildCylinderMesh()
    {
        int cols = ShellSegments + 1;
        var v  = new Vector3[cols * 2];
        var nm = new Vector3[cols * 2];
        var uv = new Vector2[cols * 2];
        float circ = 2f * Mathf.PI * ShellRadius;
        for (int i = 0; i < cols; i++)
        {
            float a = i / (float)ShellSegments * Mathf.PI * 2f;
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            v[i]        = dir * ShellRadius + Vector3.up * -ShellBelow;
            v[i + cols] = dir * ShellRadius + Vector3.up * ShellAbove;
            nm[i] = nm[i + cols] = -dir;   // 안쪽
            float u = i / (float)ShellSegments * circ / TextureTile;
            uv[i]        = new Vector2(u, -ShellBelow / TextureTile);
            uv[i + cols] = new Vector2(u,  ShellAbove / TextureTile);
        }
        var tri = new int[ShellSegments * 6];
        for (int i = 0, t = 0; i < ShellSegments; i++)
        {
            int a = i, b = i + 1, c = i + cols, d = i + 1 + cols;
            // 안쪽(중심)에서 볼 때 시계 방향 = 앞면 — 각이 늘면 안에서 보아 왼쪽으로 간다
            tri[t++] = a; tri[t++] = b; tri[t++] = c;
            tri[t++] = b; tri[t++] = d; tri[t++] = c;
        }
        var mesh = new Mesh { name = "DungeonShell_Wall" };
        mesh.vertices = v; mesh.normals = nm; mesh.uv = uv; mesh.triangles = tri;
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>원통 바닥을 막는 원판(위를 본다) — 구덩이 바닥.</summary>
    private static Mesh BuildDiskMesh(float y)
    {
        int n = ShellSegments;
        var v  = new Vector3[n + 1];
        var nm = new Vector3[n + 1];
        var uv = new Vector2[n + 1];
        v[0] = new Vector3(0f, y, 0f); nm[0] = Vector3.up; uv[0] = Vector2.zero;
        for (int i = 0; i < n; i++)
        {
            float a = i / (float)n * Mathf.PI * 2f;
            v[i + 1]  = new Vector3(Mathf.Cos(a) * ShellRadius, y, Mathf.Sin(a) * ShellRadius);
            nm[i + 1] = Vector3.up;
            uv[i + 1] = new Vector2(v[i + 1].x, v[i + 1].z) / TextureTile;
        }
        var tri = new int[n * 3];
        for (int i = 0, t = 0; i < n; i++)
        {
            tri[t++] = 0; tri[t++] = 1 + (i + 1) % n; tri[t++] = 1 + i;   // 위에서 보이게
        }
        var mesh = new Mesh { name = "DungeonShell_Pit" };
        mesh.vertices = v; mesh.normals = nm; mesh.uv = uv; mesh.triangles = tri;
        mesh.RecalculateBounds();
        return mesh;
    }
}
