using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using OccaSoftware.Buto.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// S4-1 「하늘 → 탑 안」(10-03 설계 「모든 챕터에서 던전 안」 · 구현 설계 §1-1).
/// 런 씬에서 하늘(스카이박스)을 끄고 게임 카메라 배경을 탑 안 어둠으로 바꾼 뒤, 플레이어를 따라가는
/// <b>탑 벽 원통</b>(안쪽을 보는 반지름 120 m · 지금 방 벽과 같은 재질을 어둡게)과 아래 <b>구덩이 바닥</b>을 세운다.
/// 떠 있는 방 가장자리 · 먼 벽 너머 · 통로 틈으로 보이던 하늘이 「탑 안의 먼 벽과 아래로 꺼진 어둠」이 된다.
/// <para>원통은 방마다 다시 짓지 않는다 — 반지름이 어느 방보다 커서 플레이어를 따라 수평으로 옮기기만 한다(스카이박스처럼 먼 배경).
/// 카메라는 늘 방 벽보다 낮다(10-02 실측: 카메라 4.8~5.4 m · 벽 7~11 m) — 원통은 벽 너머 · 가장자리 너머에서만 보인다.</para>
/// <see cref="GameRunBootstrapper"/>가 Awake에서 붙인다.
/// <para>10-12 챕터 공기 — 세트의 직사광 색 · 시기 색(해방기 · 악몽 모드) · 분위기 입자 1종도 여기서 입힌다.
/// 안개 <b>밀도</b>는 건드리지 않는다(방 입장 <see cref="EntranceFogVeil"/>이 밀도를 붙잡았다 되돌린다) — 밀도는 씬 값, 색만 바꾼다.</para>
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
    // 10-12 챕터 공기 — 시기 색이 각 층을 미는 몫(안개 = 세기 그대로 · 나머지는 덜)
    private const float NightmareEraBoost = 1.4f;
    private const float EraAmbientShare   = 0.6f;
    private const float EraSunShare       = 0.35f;
    private const float EraShadowShare    = 0.5f;   // 그림자 색(Split Toning) — 회색 = 그대로
    // 10-12 2차 — 1차는 안개 · 환경광 · 해 · 그림자만 밀어 화면을 지배하는 초록(방 조명 29개 · Buto 숲빛 · 챕터 색 필터)이 그대로였다(83 촬영)
    private const float EraRoomLightShare = 1f;     // 방 · 회랑 · 층계 조명(LightTint) — 바닥 색을 가장 크게 정한다
    private const float EraButoShare      = 1f;     // Buto 빛깔(챕터 프로파일 값에서)
    private const float EraFilterShare    = 0.35f;  // 화면 색 필터(챕터 프로파일 값에서) — 밝기 그대로 색조만
    private const float NightmareVignette = 0.40f;  // 기본 톤 0.33보다 조금 조인다
    private const int   EraVolumePriority = 2;      // 기본 톤 0 · 챕터 1 위, 피격 9 · 회피 1000 아래
    private const float AmbientFxHeight   = 3f;     // 플레이어 발밑 기준 입자 상자 높이

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId     = Shader.PropertyToID("_Color");
    private static readonly Collider[] s_wallHits = new Collider[16];
    private static DungeonSpaceDirector s_current;
    private static readonly List<Light> s_pendingRoomLights = new();   // 세트를 읽기 전에 지어진 방 조명(첫 대기방)

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
    private GameObject    _eraVolume;
    private VolumeProfile _eraProfile;
    private float         _eraK;
    private Color         _eraTint;

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
        s_pendingRoomLights.Clear();
        if (_shell != null) Destroy(_shell.gameObject);
        if (_shellMesh != null) Destroy(_shellMesh);
        if (_pitMesh != null) Destroy(_pitMesh);
        // 직사광 · 안개 · 환경광 색은 되돌리지 않는다 — 씬 소유라 씬이 다시 열리면 원래 값이고, 언로드 도중 되돌리면 다음 씬에 섞일 수 있다.
        if (_eraVolume != null) Destroy(_eraVolume);
        if (_eraProfile != null) Destroy(_eraProfile);
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

    /// <summary>
    /// 10-12 2차 — 방 조명(<see cref="LightTint.ApplyTo"/> = 방 벽 · 중앙 · 회랑 · 층계)에 시기 색을 입힌다.
    /// 세트를 읽기 전(첫 대기방)에 지어진 조명은 모아 뒀다가 세트를 읽은 뒤 한 번에 입힌다. 봉인기면 아무것도 안 한다.
    /// </summary>
    public static void TintRoomLight(Light light)
    {
        if (light == null || !Application.isPlaying) return;   // 편집 모드 방 미리보기(RoomPreviewBuilder)는 모으지 않는다
        var d = s_current;
        if (d == null || !d._ready) { s_pendingRoomLights.Add(light); return; }
        d.RetintRoomLight(light);
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
            ApplyChapterAir();
            _shaftMaterial = set.ShaftMaterial;
            _ready = true;
            s_current = this;
            FlushPendingRoomLights();

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
            Debug.Log($"[DungeonSpace] {chapter} — 하늘 끔 · 탑 벽 재질 {wall.name} · 도착 마법진 {marks} · 시기 {StoryProgress.Era} 색 {EraStrength():0.00}");
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
    /// 10-12 챕터 공기 — 세트의 직사광 색을 입히고, 해방기 · 악몽 모드면 안개 · 환경광 · 직사광 · 카메라 배경 색을 시기 색조로 민다(밝기 유지)
    /// + 그림자 색(Split Toning) · 악몽 비네트를 런타임 볼륨으로 얹는다. 안개 밀도는 씬 값 그대로(<see cref="EntranceFogVeil"/>과 경합 없음).
    /// </summary>
    private void ApplyChapterAir()
    {
        var sun = FindSun();
        if (sun != null && _entry.sunColor.a > 0f) sun.color = Opaque(_entry.sunColor);

        float k = EraStrength();
        if (k <= 0f) return;
        Color tint = Opaque(_entry.eraTint);
        _eraK    = k;
        _eraTint = tint;
        RenderSettings.fogColor     = Retint(RenderSettings.fogColor, tint, k);
        RenderSettings.ambientLight = Retint(RenderSettings.ambientLight, tint, k * EraAmbientShare);
        if (sun != null) sun.color = Retint(sun.color, tint, k * EraSunShare);
        if (_cam != null) _cam.backgroundColor = Retint(_cam.backgroundColor, tint, k);
        BuildEraVolume(tint, k, StoryProgress.Era == StoryEra.NightmareMode);
    }

    /// <summary>시기 색 세기 — 봉인기 0 · 해방기 = 세트 알파 · 악몽 모드 = 알파 × <see cref="NightmareEraBoost"/>.</summary>
    private float EraStrength()
    {
        var era = StoryProgress.Era;
        if (era == StoryEra.Sealed || _entry.eraTint.a <= 0f) return 0f;
        return Mathf.Clamp01(_entry.eraTint.a * (era == StoryEra.NightmareMode ? NightmareEraBoost : 1f));
    }

    /// <summary>
    /// 시기 런타임 볼륨(우선순위 2) — 그림자 색 + <b>화면 색 필터 · Buto 빛깔은 지금 챕터 값에서 시기 색조로</b> 민 값을 덮는다
    /// (우선순위가 높은 볼륨은 값을 「대체」하므로 챕터 값을 볼륨 스택에서 읽어 출발점으로 쓴다 — 볼륨을 만들기 전에 읽는다).
    /// </summary>
    private void BuildEraVolume(Color tint, float k, bool nightmare)
    {
        var urp   = _cam != null ? _cam.GetUniversalAdditionalCameraData() : null;
        var stack = urp != null && urp.volumeStack != null ? urp.volumeStack : VolumeManager.instance.stack;
        var chapterAdjust = stack.GetComponent<ColorAdjustments>();
        var chapterFog    = stack.GetComponent<ButoVolumetricFog>();

        _eraProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        _eraProfile.name = "EraTone(런타임)";
        _eraProfile.Add<SplitToning>(false).shadows.Override(Color.Lerp(Color.grey, tint, k * EraShadowShare));
        Color filter = chapterAdjust != null ? chapterAdjust.colorFilter.value : Color.white;
        _eraProfile.Add<ColorAdjustments>(false).colorFilter.Override(Retint(filter, tint, k * EraFilterShare));
        if (chapterFog != null && chapterFog.colorInfluence.value > 0.5f)   // 빛깔 영향이 꺼진 챕터(직사광 따라감)는 그대로 둔다
            _eraProfile.Add<ButoVolumetricFog>(false).litColor.Override(Retint(chapterFog.litColor.value, tint, k * EraButoShare));
        if (nightmare) _eraProfile.Add<Vignette>(false).intensity.Override(NightmareVignette);

        _eraVolume = new GameObject("@EraTone");
        _eraVolume.transform.SetParent(transform, false);
        var volume = _eraVolume.AddComponent<Volume>();
        volume.isGlobal      = true;
        volume.priority      = EraVolumePriority;
        volume.sharedProfile = _eraProfile;
    }

    private void RetintRoomLight(Light light)
    {
        if (_eraK <= 0f) return;
        light.color = Retint(light.color, _eraTint, _eraK * EraRoomLightShare);
    }

    /// <summary>세트를 읽기 전에 지어진 방 조명(첫 대기방) — 시기 색을 입히고 비운다(봉인기여도 비운다).</summary>
    private void FlushPendingRoomLights()
    {
        foreach (var light in s_pendingRoomLights)
            if (light != null) RetintRoomLight(light);
        s_pendingRoomLights.Clear();
    }

    /// <summary>씬 직사광 — RenderSettings.sun이 비어 있으면(Ch1~4 씬 전부 m_Sun 0) 켜진 첫 Directional.</summary>
    private static Light FindSun()
    {
        if (RenderSettings.sun != null) return RenderSettings.sun;
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional && l.isActiveAndEnabled) return l;
        return null;
    }

    /// <summary>색조만 tint 쪽으로 k만큼 — 밝기(Rec.709 휘도)는 원래 색 그대로 둔다(챕터 밝기 밴드 유지).</summary>
    private static Color Retint(Color c, Color tint, float k)
    {
        float lt = Luma(tint);
        Color target = lt > 1e-4f ? tint * (Luma(c) / lt) : c;
        target.a = c.a;
        return Color.Lerp(c, target, k);
    }

    private static float Luma(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

    private static Color Opaque(Color c) { c.a = 1f; return c; }

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

        SpawnAmbientFx(root);
    }

    /// <summary>
    /// 10-12 분위기 입자 1종 — 원통 뿌리(플레이어를 따라감)에 붙이고 시뮬레이션을 월드 공간으로 돌려,
    /// 상자는 플레이어와 같이 움직이되 이미 난 입자는 제자리에 흩날리게 한다. 방출 배율은 세트 값(0 = 원본).
    /// </summary>
    private void SpawnAmbientFx(Transform root)
    {
        if (_entry.ambientFx == null) return;
        var fx = Instantiate(_entry.ambientFx, root);
        fx.name = "~ChapterAir";
        fx.transform.localPosition = Vector3.up * AmbientFxHeight;
        foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            if (_entry.ambientFxRate > 0f)
            {
                var emission = ps.emission;
                emission.rateOverTimeMultiplier *= _entry.ambientFxRate;
            }
        }
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
