using UnityEngine;

/// <summary>
/// 칼날을 감싸는 속성 이펙트(10-26 사용자 「검 크기를 키웠으면 검에 두르는 이펙트를 찾아 반영」 · 대검 R 「속성 검」).
/// 칼날 표면에서 속성 색 입자가 피어오른다 — 재질은 속성 이펙트 목록(<see cref="ElementVfxRegistry"/> 칼날 칸 · _Imported Spells Pack URP 입자),
/// 색은 속성 팔레트(<see cref="ElementPalette"/> 밝은 심지 → 짙은 가장자리), 움직임은 속성마다(불 피어오름 · 얼음 가라앉음 · 번개 튐 …).
/// 무기 메시는 Read/Write가 꺼져 있어(빌드에서 정점을 못 읽음) 칼밑 → 칼끝 얇은 대리 상자를 만들어 모양으로 쓴다.
/// 칼 메시의 자식이고 크기는 계층을 따르므로 칼이 커지면 이펙트도 같이 커진다. 입자는 월드에 남아 휘두르면 궤적을 그린다.
/// 실측 97 ⑨(10-26)에서 뺀 길: INab Character Effects(팩 두 벌 겹침 · 기운 칼에서 입자가 칼 밖) · 속성 몸 이펙트 옮기기(방패 문장 · 연기 덩어리).
/// </summary>
[DisallowMultipleComponent]
public sealed class BladeWrapFx : MonoBehaviour
{
    // ── Constants ─────────────────────────────────────────────────
    private const float WidthRatio = 0.09f, ThickRatio = 0.3f, LingerSeconds = 1.2f;   // 칼날 폭 = 길이 × 0.09 · 두께 = 폭 × 0.3
    private const int   Segments = 6, MaxParticles = 160;

    /// <summary>속성마다 입자 움직임 — 초당 수 · 수명 · 크기 · 위로(월드 m/s) · 흔들림 · 겉면에서 튀어나가는 속도.</summary>
    private readonly struct Motion
    {
        public readonly float Rate, Life, Size, Rise, Noise, Speed;
        public Motion(float rate, float life, float size, float rise, float noise, float speed)
        { Rate = rate; Life = life; Size = size; Rise = rise; Noise = noise; Speed = speed; }
    }

    // ── Static ────────────────────────────────────────────────────
#if UNITY_EDITOR
    /// <summary>실측 97 ⑨ — 후보 재질을 갈아 끼운다(null이면 목록 값).</summary>
    public static Material ProbeMaterial;
    /// <summary>실측 97 ⑨ 진단 — 대리 메시를 보이게 그린다.</summary>
    public static bool ProbeShowProxy;
    /// <summary>실측 97 ⑥-g — 칼날 이펙트가 못 붙는 경우(목록 미로드 · 재질 없음)를 흉내 낸다.</summary>
    public static bool ProbeFail;
#endif

    // ── Private ───────────────────────────────────────────────────
    private Mesh _mesh;
    private ParticleSystem _ps;
    private Transform _blade;
    private bool _released;

    // ── Properties ────────────────────────────────────────────────
    /// <summary>지금 살아 있는 입자 수(실측).</summary>
    public int AliveParticles => _ps != null ? _ps.particleCount : 0;
    /// <summary>대리 메시 칼밑 · 칼끝(칼 메시 로컬) — 실측이 칼 크기와 맞는지 본다.</summary>
    public Vector3 RootLocal { get; private set; }
    public Vector3 TipLocal { get; private set; }
    /// <summary>지금 대리 메시가 덮는 칼 길이(m · 월드).</summary>
    public float WorldLength => _blade != null ? Vector3.Distance(_blade.TransformPoint(RootLocal), _blade.TransformPoint(TipLocal)) : -1f;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void OnDestroy()
    {
        if (_mesh != null) Destroy(_mesh);
    }

    // ── Public Methods ────────────────────────────────────────────
    /// <summary>든 무기 칼날에 속성 이펙트를 두른다. 칼끝 · 칼밑 · 메시 · 목록 칸이 없으면 null.</summary>
    public static BladeWrapFx Attach(WeaponInstance wi, RuneElement element)
    {
#if UNITY_EDITOR
        if (ProbeFail) return null;
#endif
        if (wi == null || wi.tipPoint == null || wi.rootPoint == null) return null;
        var mf = wi.GetComponentInChildren<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return null;
        var mat = ElementVfxPlayer.GetBladeMaterial(element);
#if UNITY_EDITOR
        if (ProbeMaterial != null) mat = ProbeMaterial;
#endif
        if (mat == null) return null;
        var go = new GameObject("BladeWrapFx");
        go.transform.SetParent(mf.transform, false);   // 칼 메시의 자식 — 칼이 커지면 같이 커진다
        var fx = go.AddComponent<BladeWrapFx>();
        fx.Build(mf, wi.rootPoint.position, wi.tipPoint.position, mat, element);
        return fx;
    }

    /// <summary>새 입자를 멈춘다 — 남은 입자는 수명대로 사그라든 뒤 거둔다.</summary>
    public void Release()
    {
        if (_released) return;
        _released = true;
        if (_ps != null) _ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        Destroy(gameObject, LingerSeconds);
    }

    // ── Private Methods ───────────────────────────────────────────
    private void Build(MeshFilter mf, Vector3 rootWorld, Vector3 tipWorld, Material mat, RuneElement element)
    {
        _blade = mf.transform;
        RootLocal = _blade.InverseTransformPoint(rootWorld);
        TipLocal  = _blade.InverseTransformPoint(tipWorld);
        _mesh = BuildProxy(RootLocal, TipLocal, mf.sharedMesh.bounds.size);
#if UNITY_EDITOR
        if (ProbeShowProxy)
        {
            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            gameObject.AddComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = Color.magenta };
        }
#endif
        var m = MotionOf(element);
        _ps = gameObject.AddComponent<ParticleSystem>();
        _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);   // 붙자마자 기본값으로 뿜지 않게

        var main = _ps.main;
        main.duration        = 1f;
        main.loop            = true;
        main.playOnAwake     = false;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(m.Life * 0.7f, m.Life * 1.3f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(m.Speed * 0.5f, m.Speed * 1.5f);
        main.startSize       = new ParticleSystem.MinMaxCurve(m.Size * 0.7f, m.Size * 1.3f);
        main.startRotation   = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor      = Color.white;
        main.simulationSpace = ParticleSystemSimulationSpace.World;    // 휘두르면 입자가 남아 궤적을 그린다
        main.scalingMode     = ParticleSystemScalingMode.Hierarchy;   // 칼 크기(×1.6)를 따른다
        main.maxParticles    = MaxParticles;

        var emission = _ps.emission;
        emission.rateOverTime = m.Rate;

        var shape = _ps.shape;
        shape.enabled       = true;
        shape.shapeType     = ParticleSystemShapeType.Mesh;
        shape.meshShapeType = ParticleSystemMeshShapeType.Triangle;
        shape.mesh          = _mesh;

        var vel = _ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space   = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(0f);
        vel.y = new ParticleSystem.MinMaxCurve(m.Rise);
        vel.z = new ParticleSystem.MinMaxCurve(0f);

        var noise = _ps.noise;
        noise.enabled     = m.Noise > 0f;
        noise.strength    = m.Noise;
        noise.frequency   = 1.5f;
        noise.scrollSpeed = 1f;

        Color bright = ElementPalette.Bright(element), deep = ElementPalette.Deep(element);
        var grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(bright, 0f), new GradientColorKey(deep, 0.6f), new GradientColorKey(deep, 1f) },
                     new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.15f), new GradientAlphaKey(0.6f, 0.6f), new GradientAlphaKey(0f, 1f) });
        var col = _ps.colorOverLifetime;
        col.enabled = true;
        col.color   = grad;

        var size = _ps.sizeOverLifetime;
        size.enabled = true;
        size.size    = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.35f));

        var rd = GetComponent<ParticleSystemRenderer>();
        rd.renderMode     = ParticleSystemRenderMode.Billboard;
        rd.sharedMaterial = mat;
        rd.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rd.receiveShadows = false;

        _ps.Play(false);
    }

    /// <summary>속성마다 움직임 — 불 피어오름 · 얼음 서리가 가라앉음 · 번개 짧게 튐 · 독 느린 안개 · 빛 금빛 티끌 · 어둠 연기.</summary>
    private static Motion MotionOf(RuneElement e) => e switch
    {
        RuneElement.Fire     => new Motion(90f, 0.45f, 0.2f,   0.9f,  0.25f, 0.15f),
        RuneElement.Ice      => new Motion(55f, 0.65f, 0.1f,  -0.15f, 0.1f,  0.05f),
        RuneElement.Electric => new Motion(80f, 0.18f, 0.09f,  0f,    1.0f,  0.4f),
        RuneElement.Grass    => new Motion(40f, 0.8f,  0.26f,  0.2f,  0.2f,  0.05f),
        RuneElement.Light    => new Motion(60f, 0.6f,  0.1f,   0.5f,  0.15f, 0.1f),
        _                    => new Motion(45f, 0.7f,  0.26f,  0.3f,  0.3f,  0.05f),   // 어둠
    };

    /// <summary>
    /// 칼밑 → 칼끝 얇은 상자(길이 방향 <see cref="Segments"/>칸 · 끝 뚜껑 없음). 폭 방향 = 칼 축에 수직인 메시 축 중 더 넓은 쪽(칼날 면) —
    /// 손잡이 · 코등이까지 든 경계 상자라 폭은 길이 비율로 정하고 경계를 넘지 않게만 막는다.
    /// </summary>
    private static Mesh BuildProxy(Vector3 a, Vector3 b, Vector3 bounds)
    {
        Vector3 axis = (b - a).normalized;
        float len = Vector3.Distance(a, b);
        Vector3 wide = Vector3.zero;
        float wideSize = 0f;
        foreach (var (dir, s) in new[] { (Vector3.right, bounds.x), (Vector3.up, bounds.y), (Vector3.forward, bounds.z) })
            if (Mathf.Abs(Vector3.Dot(dir, axis)) < 0.7f && s > wideSize) { wide = dir; wideSize = s; }
        wide = Vector3.ProjectOnPlane(wide, axis).normalized;
        Vector3 thin = Vector3.Cross(axis, wide);
        float hw = Mathf.Min(len * WidthRatio, wideSize) * 0.5f, ht = hw * ThickRatio;

        var verts = new Vector3[(Segments + 1) * 4];
        for (int s = 0; s <= Segments; s++)
        {
            Vector3 c = Vector3.Lerp(a, b, (float)s / Segments);
            verts[s * 4 + 0] = c + wide * hw + thin * ht;
            verts[s * 4 + 1] = c - wide * hw + thin * ht;
            verts[s * 4 + 2] = c - wide * hw - thin * ht;
            verts[s * 4 + 3] = c + wide * hw - thin * ht;
        }
        var tris = new int[Segments * 4 * 6];
        int n = 0;
        for (int s = 0; s < Segments; s++)
            for (int k = 0; k < 4; k++)
            {
                int i0 = s * 4 + k, i1 = s * 4 + (k + 1) % 4, j0 = i0 + 4, j1 = i1 + 4;
                tris[n++] = i0; tris[n++] = j0; tris[n++] = j1;
                tris[n++] = i0; tris[n++] = j1; tris[n++] = i1;
            }
        var mesh = new Mesh { name = "BladeWrapProxy" };
        mesh.vertices = verts;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
