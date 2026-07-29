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

    // ── 정적 파사드 ─────────────────────────────────────
    /// <summary>속성 즉발 버스트를 위치에 1회 재생. radiusScale=실제 판정 반경(광역이면 그 반경으로 크기 일치). 기본 1=점 타격.</summary>
    public static void PlayBurst(RuneElement element, Vector3 pos, float radiusScale = 1f)
    {
        if (SuppressBursts) return;
        var inst = Instance; if (inst == null) return;
        inst.DoPlayBurst(element, pos, radiusScale);
    }

    /// <summary>장판(GroundField)에 바닥 원반 오라를 부착. scale=장판 반경. 반환=해제 핸들(0=실패).</summary>
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
    private void DoPlayBurst(RuneElement element, Vector3 pos, float radiusScale)
    {
        if (_registry == null) return;
        var prefab = _registry.GetBurst(element);
        if (prefab == null) return;

        var it = Spawn(prefab, pos, Mathf.Max(0.1f, radiusScale));
        if (it == null) return;
        it.follow = null;
        it.life   = BurstTtl;
        it.age    = 0f;
    }

    private int DoAttachAura(RuneElement element, Transform target, float duration, float scale, bool body)
    {
        if (_registry == null || target == null || duration <= 0f) return 0;

        // 몸 상태(b)와 바닥 장판(f)은 같은 트랜스폼에 공존할 수 있어 키를 분리한다.
        string key = target.GetInstanceID() + "|" + (int)element + (body ? "b" : "f");
        if (_auras.TryGetValue(key, out var exist) && exist.go != null && exist.go.activeSelf)
        {
            exist.age  = 0f;                       // 재부여 = 수명 갱신(top-up)
            exist.life = Mathf.Max(exist.life, duration);
            return exist.handle;
        }

        var prefab = body ? _registry.GetStatusBody(element) : _registry.GetAura(element);
        if (prefab == null) return 0;

        var it = Spawn(prefab, target.position, scale);
        if (it == null) return 0;
        it.follow       = target;
        it.followOffset = Vector3.zero;
        it.life         = duration;
        it.age          = 0f;
        it.handle       = _handleSeq++;
        it.auraKey      = key;
        _handles[it.handle] = it;
        _auras[key]         = it;
        return it.handle;
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
