using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 6속성 실제 VFX 재생 단일 진입점(정적 파사드 + DDOL 런타임).
///
/// <see cref="ElementVfxRegistry"/>(Addressable "ElementVfxRegistry")를 1회 로드해
/// 속성별 프리팹을 프리팹 단위로 풀링 재생한다. 가이드라인 플레이스홀더와 달리 실제 게임 표시라
/// 릴리스 빌드에서도 동작한다.
///
///  • <see cref="PlayBurst"/> — 위치에 즉발 1회(자동 회수).
///  • <see cref="AttachAura"/> — 대상에 상태 지속 오라(대상+속성별 1개, 재부여 시 수명 갱신). 반환=해제 핸들.
///  • <see cref="ReleaseAura"/> — 핸들로 즉시 해제(만료 전 상태 소멸 시).
///
/// 수명/추적은 unscaledDeltaTime 기반 — HitStop/슬로우 중에도 표시·만료가 진행된다.
/// </summary>
public sealed class ElementVfxPlayer : MonoBehaviour
{
    private const string RegistryKey = "ElementVfxRegistry";
    private const float  BurstTtl    = 2.0f;   // 버스트 프리팹 자동 회수(초)
    private const float  AreaFrom    = 1.01f;  // 이 반경을 넘으면 「광역」 칸을 쓴다(그 아래는 점 타격)

    private sealed class VfxItem
    {
        public GameObject go;
        public Transform  tf;
        public GameObject prefab;      // 회수 시 풀 키
        public Transform  follow;      // null이 아니면 매 프레임 추적
        public Vector3    followOffset;
        public float      age;
        public float      life;        // <0 이면 영속(명시 해제까지)
        public int        handle;
        public string     auraKey;     // (targetId|element) — 대상별 1개 갱신용
    }

    /// <summary>체인/빔 — 프리팹이 아닌 절차적 지그재그 LineRenderer(두 점 연결). 짧은 수명 후 페이드.</summary>
    private sealed class Beam
    {
        public GameObject   go;
        public LineRenderer lr;
        public Color        color;
        public float        baseAlpha;
        public float        age;
        public float        life;
    }

    // ── 정적 ────────────────────────────────────────────
    private static ElementVfxPlayer s_instance;
    private static bool s_appQuitting;

    private static ElementVfxPlayer Instance
    {
        get
        {
            if (s_appQuitting) return null;
            if (s_instance == null)
            {
                var go = new GameObject("[ElementVfxPlayer]");
                DontDestroyOnLoad(go);
                s_instance = go.AddComponent<ElementVfxPlayer>();
            }
            return s_instance;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_instance    = null;
        s_appQuitting = false;
    }

    /// <summary>공명 재구성 등 대량 재활성 중 발동 버스트 스팸을 막는 가드(RuneEffectDispatcher가 제어).</summary>
    public static bool SuppressBursts { get; set; }

    /// <summary>
    /// 재생기를 미리 만들어 목록 로드를 시작한다. 재생기는 처음 불릴 때 생기고 목록을 비동기로 읽으므로,
    /// 그 전에 부른 <b>첫 이펙트는 목록이 없어 버려졌다</b>(10-01 실측) — 런이 시작될 때 한 번 불러 둔다.
    /// </summary>
    public static void Warmup() { _ = Instance; }

    // ── 정적 파사드 ─────────────────────────────────────
    /// <summary>
    /// 속성 즉발 이펙트를 위치에 1회 재생. radiusScale = 실제 판정 반경(m).
    /// 반경이 1을 넘으면 <b>광역</b> 칸을 그 반경에 맞춰 키워 쓰고(보이는 범위 = 맞는 범위), 1 이하면 <b>점 타격</b> 칸을 쓴다.
    /// atFeet = 넘긴 위치가 발밑(대상 transform)인가 — 참이면 타격 이펙트를 몸 높이로 올린다. 맞은 점을 넘길 땐 false.
    /// </summary>
    public static void PlayBurst(RuneElement element, Vector3 pos, float radiusScale = 1f, bool atFeet = true)
    {
        if (SuppressBursts) return;
        var inst = Instance; if (inst == null) return;
        inst.DoPlayBurst(element, pos, radiusScale, atFeet);
    }

    /// <summary>단계 발동 표시 — 플레이어 자리에 속성 문장 1회.</summary>
    public static void PlayActivation(RuneElement element, Vector3 pos)
    {
        if (SuppressBursts) return;
        var inst = Instance; if (inst == null) return;
        if (inst._registry == null) return;
        var prefab = inst._registry.GetBurst(element);
        if (prefab != null) inst.DoPlayPrefab(prefab, pos, 1f, BurstTtl);
    }

    /// <summary>장판(GroundField)에 영역 이펙트를 부착. scale = 장판 반경(m). 반환=해제 핸들(0=실패).</summary>
    public static int AttachAura(RuneElement element, Transform target, float duration, float scale = 1f)
    {
        var inst = Instance; if (inst == null) return 0;
        return inst.DoAttachAura(element, target, duration, scale, body: false);
    }

    /// <summary>
    /// 적 <b>몸</b>에 상태 이펙트를 부착(화상/독/빙결 등). 바닥 원반이 아니라 캐릭터 이펙트라
    /// "밟으면 아픈 장판"으로 오독되지 않는다. (대상,속성)별 1개 — 재부여 시 수명 갱신.
    /// </summary>
    public static int AttachStatus(RuneElement element, Transform target, float duration)
    {
        var inst = Instance; if (inst == null) return 0;
        return inst.DoAttachAura(element, target, duration, 1f, body: true);
    }

    /// <summary>
    /// 속성과 무관한 프리팹을 위치에 1회 재생(서약 효과 등). 속성 버스트와 같은 풀·수명 규칙을 쓴다.
    /// scale = 프리팹 배율, life = 회수 시각(초, unscaled).
    /// </summary>
    public static void PlayPrefab(GameObject prefab, Vector3 pos, float scale = 1f, float life = BurstTtl)
    {
        if (prefab == null) return;
        var inst = Instance; if (inst == null) return;
        inst.DoPlayPrefab(prefab, pos, scale, life);
    }

    /// <summary>
    /// 속성과 무관한 프리팹을 대상에 부착(대상+프리팹별 1개, 재부착 시 수명 갱신). 반환=해제 핸들(0=실패).
    /// </summary>
    public static int AttachPrefab(GameObject prefab, Transform target, float duration, float scale = 1f)
    {
        if (prefab == null || target == null) return 0;
        var inst = Instance; if (inst == null) return 0;
        return inst.DoAttachPrefab(prefab, target, duration, scale,
                                   target.GetInstanceID() + "|p" + prefab.GetInstanceID());
    }

    /// <summary>핸들로 오라 즉시 해제(만료 전 상태 소멸 시).</summary>
    public static void ReleaseAura(int handle)
    {
        if (handle == 0 || s_instance == null) return;
        s_instance.DoReleaseAura(handle);
    }

    /// <summary>두 점을 잇는 속성 빔(체인 낙뢰 등). 지그재그 아크로 그려 짧게 페이드. from→to 동일점이면 무동작.</summary>
    public static void PlayBeam(RuneElement element, Vector3 from, Vector3 to, float life = 0.16f)
    {
        var inst = Instance; if (inst == null) return;
        inst.DoPlayBeam(element, from, to, life);
    }

    // ── 필드 ────────────────────────────────────────────
    private ElementVfxRegistry _registry;
    private bool _loading;

    private readonly List<VfxItem> _active = new();
    private readonly Dictionary<GameObject, Stack<VfxItem>> _pools = new();
    private readonly Dictionary<int, VfxItem>    _handles = new();
    private readonly Dictionary<string, VfxItem> _auras   = new();
    private int _handleSeq = 1;

    private readonly List<Beam>  _activeBeams = new();
    private readonly Stack<Beam> _beamPool    = new();
    private Material _beamMat;

    // ── 생명주기 ────────────────────────────────────────
    private void Awake() => EnsureRegistryAsync().Forget();

    private void OnApplicationQuit() => s_appQuitting = true;

    private void LateUpdate()
    {
        float dt = Time.unscaledDeltaTime;
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var it = _active[i];

            if (it.follow != null)
            {
                if (!it.follow.gameObject.activeInHierarchy) { ReleaseAt(i); continue; }
                it.tf.position = it.follow.position + it.followOffset;
            }

            it.age += dt;
            if (it.life >= 0f && it.age >= it.life) ReleaseAt(i);
        }

        // 빔(체인 아크) — 수명 동안 페이드 후 회수
        for (int i = _activeBeams.Count - 1; i >= 0; i--)
        {
            var b = _activeBeams[i];
            b.age += dt;
            float k = 1f - b.age / b.life;
            if (k <= 0f) { ReleaseBeamAt(i); continue; }
            var c = b.color; c.a = b.baseAlpha * k;
            b.lr.startColor = c; b.lr.endColor = c;
        }
    }

    // ── 로드 ────────────────────────────────────────────
    private async UniTaskVoid EnsureRegistryAsync()
    {
        if (_registry != null || _loading) return;
        _loading = true;
        try
        {
            _registry = await Managers.AddressableManager.TryLoadAssetAsync<ElementVfxRegistry>(RegistryKey);
            if (_registry == null)
                Debug.LogWarning($"[ElementVfxPlayer] '{RegistryKey}' Addressable 로드 실패 — 속성 VFX 미표시");
        }
        finally { _loading = false; }
    }

    // ── 구현 ────────────────────────────────────────────
    private void DoPlayBurst(RuneElement element, Vector3 pos, float radiusScale, bool atFeet)
    {
        if (_registry == null) return;

        if (radiusScale > AreaFrom)
        {
            var area = _registry.GetArea(element, out float nominal, out float life);
            if (area != null)
            {
                DoPlayPrefab(area, pos, radiusScale / nominal, life > 0f ? life : BurstTtl);
                return;
            }
        }

        var prefab = _registry.GetImpact(element, out float lift);
        if (prefab == null) return;
        if (atFeet) pos.y += lift;
        DoPlayPrefab(prefab, pos, Mathf.Max(0.1f, radiusScale), BurstTtl);
    }

    private void DoPlayPrefab(GameObject prefab, Vector3 pos, float scale, float life)
    {
        var it = Spawn(prefab, pos, Mathf.Max(0.05f, scale));
        if (it == null) return;
        it.follow = null;
        it.life   = life;
        it.age    = 0f;
    }

    private int DoAttachAura(RuneElement element, Transform target, float duration, float scale, bool body)
    {
        if (_registry == null || target == null || duration <= 0f) return 0;

        // 몸 상태(b)와 바닥 장판(f)은 같은 트랜스폼에 공존할 수 있어 키를 분리한다.
        string key = target.GetInstanceID() + "|" + (int)element + (body ? "b" : "f");
        // 몸 상태는 몸 한가운데에 — 발밑(루트)에 두면 나는 몬스터 아래 바닥에서 탔다(10-01 실측).
        if (body) return DoAttachPrefab(_registry.GetStatusBody(element), target, duration, scale, key, BodyCenterOffset(target));

        // 장판: scale = 장판 반경(m) → 프리팹 공칭 반경으로 나눠 배율을 낸다.
        var aura = _registry.GetAura(element, out float nominal);
        return DoAttachPrefab(aura, target, duration, scale / nominal, key);
    }

    private int DoAttachPrefab(GameObject prefab, Transform target, float duration, float scale, string key,
                               Vector3 followOffset = default)
    {
        if (prefab == null || target == null || duration <= 0f) return 0;

        if (_auras.TryGetValue(key, out var exist) && exist.go != null && exist.go.activeSelf)
        {
            exist.age  = 0f;                       // 재부여 = 수명 갱신(top-up)
            exist.life = Mathf.Max(exist.life, duration);
            return exist.handle;
        }

        var it = Spawn(prefab, target.position + followOffset, scale);
        if (it == null) return 0;
        it.follow       = target;
        it.followOffset = followOffset;
        it.life         = duration;
        it.age          = 0f;
        it.handle       = _handleSeq++;
        it.auraKey      = key;
        _handles[it.handle] = it;
        _auras[key]         = it;
        return it.handle;
    }

    /// <summary>
    /// 루트(발밑) → 몸 한가운데 높이. 몸 콜라이더(트리거 아님)의 중심 높이를 쓴다 — 나는 몬스터는 몸이 루트보다 높이 떠 있다.
    /// 부착할 때 한 번만 부른다(프레임마다 아님).
    /// </summary>
    private static Vector3 BodyCenterOffset(Transform target)
    {
        foreach (var col in target.GetComponentsInChildren<Collider>())
        {
            if (col == null || col.isTrigger || !col.enabled) continue;
            return new Vector3(0f, Mathf.Clamp(col.bounds.center.y - target.position.y, 0f, 3f), 0f);
        }
        return Vector3.zero;
    }

    private void DoReleaseAura(int handle)
    {
        if (!_handles.TryGetValue(handle, out var it)) return;
        int idx = _active.IndexOf(it);
        if (idx >= 0) ReleaseAt(idx);
    }

    // ── 풀 ──────────────────────────────────────────────
    private VfxItem Spawn(GameObject prefab, Vector3 pos, float scale)
    {
        VfxItem it = null;
        if (_pools.TryGetValue(prefab, out var stack) && stack.Count > 0)
            it = stack.Pop();

        if (it == null)
        {
            var go = Instantiate(prefab, transform);
            it = new VfxItem { go = go, tf = go.transform, prefab = prefab };
            UseHierarchyScale(go.transform);
        }

        it.tf.SetPositionAndRotation(pos, prefab.transform.rotation);
        it.tf.localScale = prefab.transform.localScale * scale;   // 장판 반경 대응(버스트=1)
        it.handle  = 0;
        it.auraKey = null;
        it.go.SetActive(true);
        RestartParticles(it.tf);
        _active.Add(it);
        return it;
    }

    private void ReleaseAt(int index)
    {
        if (index < 0 || index >= _active.Count) return;
        var it = _active[index];
        _active.RemoveAt(index);

        if (it.handle != 0) { _handles.Remove(it.handle); it.handle = 0; }
        if (!string.IsNullOrEmpty(it.auraKey))
        {
            if (_auras.TryGetValue(it.auraKey, out var a) && a == it) _auras.Remove(it.auraKey);
            it.auraKey = null;
        }
        it.follow = null;
        if (it.go != null) it.go.SetActive(false);

        if (!_pools.TryGetValue(it.prefab, out var stack))
        {
            stack = new Stack<VfxItem>();
            _pools[it.prefab] = stack;
        }
        stack.Push(it);
    }

    // ── 빔(체인 아크) ────────────────────────────────────
    private void DoPlayBeam(RuneElement element, Vector3 from, Vector3 to, float life)
    {
        if ((to - from).sqrMagnitude < 0.01f) return;

        var b = _beamPool.Count > 0 ? _beamPool.Pop() : CreateBeam();
        BuildJaggedLine(b.lr, from, to);

        Color c = BeamColor(element);
        b.lr.startColor = c; b.lr.endColor = c;
        b.color     = c;
        b.baseAlpha = c.a;
        b.age       = 0f;
        b.life      = Mathf.Max(0.02f, life);
        b.go.SetActive(true);
        _activeBeams.Add(b);
    }

    private void ReleaseBeamAt(int index)
    {
        if (index < 0 || index >= _activeBeams.Count) return;
        var b = _activeBeams[index];
        _activeBeams.RemoveAt(index);
        if (b.go != null) b.go.SetActive(false);
        _beamPool.Push(b);
    }

    private Beam CreateBeam()
    {
        if (_beamMat == null)
        {
            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            _beamMat = new Material(sh) { name = "elemvfx_beam" };
            _beamMat.renderQueue = 3200;   // 투명 위 오버레이
        }

        var go = new GameObject("elem_beam");
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial    = _beamMat;
        lr.useWorldSpace     = true;
        lr.widthMultiplier   = 0.14f;
        lr.numCapVertices    = 2;
        lr.numCornerVertices = 2;
        lr.alignment         = LineAlignment.View;
        lr.textureMode       = LineTextureMode.Stretch;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows    = false;
        go.SetActive(false);
        return new Beam { go = go, lr = lr };
    }

    // 두 점을 지그재그(번개 아크)로 잇는다. 중간 정점을 수직/측면으로 무작위 오프셋.
    private static void BuildJaggedLine(LineRenderer lr, Vector3 a, Vector3 b)
    {
        const int SEG = 6;
        Vector3 dir  = b - a;
        float   dist = dir.magnitude;
        Vector3 fwd  = dist > 0.001f ? dir / dist : Vector3.forward;
        Vector3 side = Vector3.Cross(fwd, Vector3.up);
        if (side.sqrMagnitude < 0.0001f) side = Vector3.right; else side.Normalize();
        Vector3 vert = Vector3.Cross(fwd, side).normalized;

        float jag = Mathf.Clamp(dist * 0.12f, 0.1f, 0.6f);
        lr.positionCount = SEG + 1;
        for (int i = 0; i <= SEG; i++)
        {
            float t = (float)i / SEG;
            Vector3 p = a + dir * t;
            if (i != 0 && i != SEG)   // 끝점은 정확히 물리게, 중간만 흔든다
                p += side * ((Random.value * 2f - 1f) * jag) + vert * ((Random.value * 2f - 1f) * jag);
            lr.SetPosition(i, p);
        }
    }

    // 속성 색은 ElementPalette 단일 출처를 쓴다(빔/데미지 텍스트/아이콘 일관).
    private static Color BeamColor(RuneElement e) => ElementPalette.Core(e);

    private static readonly List<ParticleSystem> s_psBuf = new();

    /// <summary>
    /// 뿌리 크기(판정 반경)를 입자까지 전달한다. 속성 이펙트 프리팹의 입자계는 전부 「자기 트랜스폼 크기만」 따르는 설정이라
    /// 뿌리를 키워도 입자는 그대로고 <b>자식 자리만 배율만큼 벌어져 조각이 흩어졌다</b>(10-01 실측: 작열 · 빙하 · 광폭발 · 장판).
    /// 프리팹 안 트랜스폼 크기가 전부 1이라 배율 1에서는 모습이 달라지지 않는다. 인스턴스에만 건다(프리팹 에셋은 그대로).
    /// </summary>
    private static void UseHierarchyScale(Transform root)
    {
        root.GetComponentsInChildren(true, s_psBuf);
        for (int i = 0; i < s_psBuf.Count; i++)
        {
            var main = s_psBuf[i].main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        }
        s_psBuf.Clear();
    }

    private static void RestartParticles(Transform root)
    {
        root.GetComponentsInChildren(true, s_psBuf);
        for (int i = 0; i < s_psBuf.Count; i++)
        {
            var ps = s_psBuf[i];
            ps.Clear(true);
            ps.Play(true);
        }
        s_psBuf.Clear();
    }
}
