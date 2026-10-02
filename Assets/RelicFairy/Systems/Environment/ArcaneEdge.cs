using UnityEngine;

/// <summary>
/// 떠 있는 공간의 <b>마력 결계</b> — 걷는 면 가장자리를 따라 빛나는 룬 띠 · 위로 옅어지는 막 · 아래로 흘러내리는 빛 입자.
/// 09-27 사용자: 「난간 테두리 처리를 잘 — 아니면 신비한 힘으로 떠 있는 느낌」 → 돌 테두리 + 마력 결계(선택).
/// 투명 경계벽(낙하 방지)이 <b>왜 거기 있는지</b>를 보여주는 겉모습일 뿐 충돌은 없다.
/// 경로(<see cref="path"/>)는 이 오브젝트 로컬 좌표 — 프리팹 배율을 그대로 따른다. 시작할 때 한 번 메시를 만들고,
/// 입자는 매 프레임 <see cref="ParticleSystem.Emit(ParticleSystem.EmitParams,int)"/>로 할당 없이 뿌린다.
/// 스테이지 방의 막힌 경계(10-01)는 경로를 비운 프리팹을 찍어 <see cref="SetPath"/>로 방마다 잰 경로를 넣는다.
/// </summary>
public sealed class ArcaneEdge : MonoBehaviour
{
    [Header("경로(로컬)")]
    [SerializeField] private Vector3[] path;
    [SerializeField] private bool loop;

    [Header("룬 띠 — 바닥에 눕힌 빛 선")]
    [SerializeField] private Material bandMaterial;
    [SerializeField] private float bandWidth = 0.22f;
    [SerializeField] private float bandLift = 0.28f;
    [SerializeField] private Color bandColor = new(0.55f, 0.75f, 1f, 0.9f);

    [Header("막 — 위로 옅어지는 결계")]
    [SerializeField] private Material veilMaterial;
    [SerializeField] private float veilHeight = 2.2f;
    [SerializeField] private float veilTile = 6f;
    [SerializeField] private Color veilColor = new(0.45f, 0.55f, 1f, 0.28f);

    [Header("빛 입자 — 가장자리 아래로 흘러내림")]
    [SerializeField] private Material moteMaterial;
    [SerializeField] private float motesPerMeterPerSecond = 0.35f;
    [SerializeField] private float moteFallSpeed = 1.6f;
    [SerializeField] private float moteLifetime = 3.5f;
    [SerializeField] private float moteSize = 0.18f;
    [SerializeField] private Color moteColor = new(0.7f, 0.8f, 1f, 0.8f);

    [Header("등장 — 0이면 바로 보인다(방 경계: 방 조립이 끝날 즈음 은은히 떠오르게)")]
    [SerializeField] private float fadeInDelay;
    [SerializeField] private float fadeInSeconds;

    private LineRenderer _band;
    private Mesh _veilMesh;
    private Color[] _veilColors;       // 짝수 = 아래 줄(알파 있음), 홀수 = 위 줄(알파 0)
    private bool _fading;
    private float _fadeClock;
    private Vector3[] _world;          // 경로 월드 좌표(시작 시 한 번)
    private float[] _cumLen;           // 누적 길이 — 길이에 비례해 입자를 고르게
    private float _totalLen;
    private ParticleSystem _motes;
    private ParticleSystem.EmitParams _emit;
    private float _emitDebt;

    // ── Properties ────────────────────────────────────────────
    public Color BandColor => bandColor;
    public Color VeilColor => veilColor;
    public Color MoteColor => moteColor;

    // ── Lifecycle ─────────────────────────────────────────────

    private void Awake()
    {
        if (path == null || path.Length < 2) { enabled = false; return; }
        Build();
    }

    private void Update()
    {
        if (_fading) TickFadeIn();
        if (_motes == null || _totalLen <= 0f)
        {
            if (!_fading) enabled = false;   // 입자가 없으면 등장이 끝난 뒤 할 일이 없다
            return;
        }
        _emitDebt += motesPerMeterPerSecond * _totalLen * Time.deltaTime;
        int n = (int)_emitDebt;
        if (n <= 0) return;
        _emitDebt -= n;
        for (int i = 0; i < n; i++)
        {
            _emit.position = SampleEdge(Random.value * _totalLen);
            _motes.Emit(_emit, 1);
        }
    }

    private void OnDestroy()
    {
        if (_veilMesh != null) Destroy(_veilMesh);   // 방마다 새로 만드는 메시 — 방과 함께 치운다
    }

    // ── Public Methods ────────────────────────────────────────

    /// <summary>
    /// 런타임 생성용 — 경로(이 오브젝트 로컬)를 받아 바로 만든다. 프리팹은 경로를 비워 두고 겉모습 값만 가진다.
    /// 만든 뒤 한 번만 부를 것(다시 부르면 띠·막이 겹친다).
    /// </summary>
    public void SetPath(Vector3[] localPath, bool isLoop)
    {
        if (localPath == null || localPath.Length < 2) return;
        path = localPath;
        loop = isLoop;
        Build();
    }

    /// <summary>띠 · 막 · 입자 색을 바꾼다 — 거점 포탈이 악몽 모드에서 물든다(10-01). 등장 중이면 등장이 새 색으로 이어 간다.</summary>
    public void SetColors(Color band, Color veil, Color mote)
    {
        bandColor = band;
        veilColor = veil;
        moteColor = mote;
        if (_motes != null) { var m = _motes.main; m.startColor = mote; }
        if (!_fading) ApplyAlpha(1f);
    }

    // ── Private Methods ───────────────────────────────────────

    private void Build()
    {
        BuildWorldPath();
        if (bandMaterial != null) BuildBand();
        if (veilMaterial != null) BuildVeil();
        if (moteMaterial != null) BuildMotes();

        _fading = fadeInSeconds > 0f;
        if (_fading) ApplyAlpha(0f);
        enabled = _fading || _motes != null;
    }

    private void TickFadeIn()
    {
        _fadeClock += Time.deltaTime;
        float k = Mathf.Clamp01((_fadeClock - fadeInDelay) / fadeInSeconds);
        ApplyAlpha(k * k * (3f - 2f * k));
        if (k >= 1f) _fading = false;
    }

    /// <summary>
    /// 띠·막의 세기를 설정값의 k배로 — 둘 다 정점 색이라 재질을 건드리지 않는다.
    /// 색과 알파를 같이 줄인다(가산 혼합 재질은 알파만 줄여서는 안 어두워질 수 있다).
    /// </summary>
    private void ApplyAlpha(float k)
    {
        if (_band != null) _band.startColor = _band.endColor = bandColor * k;
        if (_veilMesh != null)
        {
            Color low = veilColor * k;
            Color top = new(low.r, low.g, low.b, 0f);   // 위 줄 — 색이 바뀌어도 옛 색이 섞이지 않게 같이 바꾼다
            for (int i = 0; i < _veilColors.Length; i += 2) { _veilColors[i] = low; _veilColors[i + 1] = top; }
            _veilMesh.SetColors(_veilColors);
        }
    }

    private void BuildWorldPath()
    {
        int count = path.Length + (loop ? 1 : 0);
        _world = new Vector3[count];
        _cumLen = new float[count];
        for (int i = 0; i < count; i++)
        {
            _world[i] = transform.TransformPoint(path[i % path.Length]);
            if (i > 0) _cumLen[i] = _cumLen[i - 1] + Vector3.Distance(_world[i - 1], _world[i]);
        }
        _totalLen = _cumLen[count - 1];
    }

    private Vector3 SampleEdge(float d)
    {
        for (int i = 1; i < _world.Length; i++)
        {
            if (d > _cumLen[i]) continue;
            float seg = _cumLen[i] - _cumLen[i - 1];
            float t = seg > 0f ? (d - _cumLen[i - 1]) / seg : 0f;
            return Vector3.Lerp(_world[i - 1], _world[i], t);
        }
        return _world[_world.Length - 1];
    }

    /// <summary>룬 띠 — 월드 좌표 LineRenderer, 자식을 눕혀(Z가 위) 바닥에 붙은 띠로.</summary>
    private void BuildBand()
    {
        var go = new GameObject("RuneBand");
        go.transform.SetParent(transform, false);
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.alignment = LineAlignment.TransformZ;
        lr.textureMode = LineTextureMode.Tile;
        lr.sharedMaterial = bandMaterial;
        lr.widthMultiplier = bandWidth;
        lr.startColor = lr.endColor = bandColor;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        var pts = new Vector3[_world.Length];
        for (int i = 0; i < pts.Length; i++) pts[i] = _world[i] + Vector3.up * bandLift * transform.lossyScale.y;
        lr.positionCount = pts.Length;
        lr.SetPositions(pts);
        _band = lr;
    }

    /// <summary>막 — 경로를 따라 세운 양면 띠(아래 v=0 → 위 v=1). 색은 정점 색으로(위로 갈수록 알파 0).</summary>
    private void BuildVeil()
    {
        var go = new GameObject("Veil");
        go.transform.SetParent(transform, false);   // 정점은 월드 점을 이 자식의 로컬로 바꿔 넣는다(배율·회전 무관)

        int n = _world.Length;
        float h = veilHeight * transform.lossyScale.y;
        var v = new Vector3[n * 2];
        var uv = new Vector2[n * 2];
        var col = new Color[n * 2];
        var top = veilColor; top.a = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector3 b = go.transform.InverseTransformPoint(_world[i]);
            Vector3 t = go.transform.InverseTransformPoint(_world[i] + Vector3.up * h);
            v[i * 2] = b; v[i * 2 + 1] = t;
            float u = _cumLen[i] / Mathf.Max(0.01f, veilTile);
            uv[i * 2] = new Vector2(u, 0f); uv[i * 2 + 1] = new Vector2(u, 1f);
            col[i * 2] = veilColor; col[i * 2 + 1] = top;
        }
        var tris = new int[(n - 1) * 12];
        for (int i = 0, k = 0; i < n - 1; i++)
        {
            int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
            tris[k++] = a; tris[k++] = b; tris[k++] = c;  tris[k++] = c; tris[k++] = b; tris[k++] = d;   // 앞
            tris[k++] = a; tris[k++] = c; tris[k++] = b;  tris[k++] = c; tris[k++] = d; tris[k++] = b;   // 뒤
        }
        var mesh = new Mesh { name = "ArcaneVeil" };
        mesh.vertices = v; mesh.uv = uv; mesh.colors = col; mesh.triangles = tris;
        mesh.RecalculateBounds();
        _veilMesh = mesh;
        _veilColors = col;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = veilMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    /// <summary>빛 입자 — 월드 시뮬레이션, 자동 방출 없음(Update에서 경로 길이에 비례해 뿌린다).</summary>
    private void BuildMotes()
    {
        var go = new GameObject("FallingMotes");
        go.transform.SetParent(transform, false);
        _motes = go.AddComponent<ParticleSystem>();
        _motes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = _motes.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = moteLifetime;
        main.startSpeed = 0f;
        main.startSize = moteSize;
        main.startColor = moteColor;
        main.maxParticles = 2000;
        main.gravityModifier = 0f;
        main.playOnAwake = false;

        var emission = _motes.emission;
        emission.enabled = false;
        var shape = _motes.shape;
        shape.enabled = false;

        var vel = _motes.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
        vel.y = new ParticleSystem.MinMaxCurve(-moteFallSpeed, -moteFallSpeed * 0.5f);
        vel.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);

        var fade = _motes.colorOverLifetime;
        fade.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
        fade.color = g;

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = moteMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        _emit = new ParticleSystem.EmitParams { applyShapeToPosition = false };
        _motes.Play();
    }
}
