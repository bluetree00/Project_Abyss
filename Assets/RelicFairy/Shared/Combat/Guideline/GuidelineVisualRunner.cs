using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// 가이드라인 비주얼(플레이스홀더) 풀/스포너 런타임 엔진.
///
/// 실제 아트 VFX가 아직 없는 단계에서 "무슨 효과가 어디서 일어났는지"를
/// 단순 프리미티브(구체)·LineRenderer·TMP 라벨 + 색코딩으로 인게임에 표시한다.
/// 외부에서는 정적 파사드 <see cref="GuidelineVisual"/>만 호출하고, 이 클래스는 그 구현부다.
///
/// 구조:
///  • 종류별 풀(구체/라인/라벨) 재사용 — 핫패스 alloc/GC 억제. 동시 표시 상한 + 자동 만료.
///  • 단일 색/도형/수명 스펙은 파사드에서 카테고리별로 결정 → 나중에 진짜 VFX 프리팹을
///    같은 진입점(Spawn*)에 끼우면 그대로 교체된다(스왑 가능 구조).
///  • DDOL 싱글턴, 도메인 리로드 시 정적 상태 리셋.
///
/// 좌표/수명은 unscaledDeltaTime 기반 — HitStop/슬로우(TimeScaleArbiter) 중에도 표시·만료가 진행된다.
/// </summary>
public sealed class GuidelineVisualRunner : MonoBehaviour
{
    // ── 상수 ────────────────────────────────────────────
    private const int   MaxActive    = 256;   // 동시 표시 상한(초과 시 신규 드롭 — 성능 보호)
    private const float DefaultFade   = 0.35f;
    private const int   ConeArcPoints = 12;

    public enum ItemKind { Sphere, Line, Label, Marker }

    private sealed class GVItem
    {
        public ItemKind     kind;
        public GameObject   go;
        public Transform    tf;
        public MeshRenderer sphereMr;     // Sphere/Marker
        public LineRenderer line;         // Line(체인/원뿔)
        public TextMeshPro  label;        // Label/Marker
        public MaterialPropertyBlock mpb;

        public Transform follow;          // null이 아니면 매 프레임 추적
        public Vector3   followOffset;
        public bool      billboard;       // 라벨류 카메라 정렬

        public float   age;
        public float   life;              // <0 이면 영속(명시 해제까지)
        public bool    fadeOut;
        public float   baseAlpha;
        public Color   color;
        public Vector3 baseScale;

        public int    handle;             // 명시 해제용(장판)
        public string markerKey;          // 마커 사전 키((targetId|statusId))
        public string badgeKey;           // 배지 사전 키(상태키)
    }

    // ── 정적 ────────────────────────────────────────────
    private static GuidelineVisualRunner s_instance;
    private static bool s_appQuitting;

    public static GuidelineVisualRunner Instance
    {
        get
        {
            if (s_appQuitting) return null;
            if (s_instance == null)
            {
                var go = new GameObject("[GuidelineVisualRunner]");
                DontDestroyOnLoad(go);
                s_instance = go.AddComponent<GuidelineVisualRunner>();
            }
            return s_instance;
        }
    }

    /// <summary>이미 생성된 인스턴스(없으면 null) — 생성 부작용 없이 조회.</summary>
    public static GuidelineVisualRunner InstanceIfExists => s_instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_instance   = null;
        s_appQuitting = false;
    }

    // ── 필드 ────────────────────────────────────────────
    private readonly List<GVItem> _active = new();
    private readonly Stack<GVItem> _spherePool = new();
    private readonly Stack<GVItem> _linePool   = new();
    private readonly Stack<GVItem> _labelPool  = new();
    private readonly Stack<GVItem> _markerPool = new();
    private readonly Dictionary<string, GVItem> _markers = new();
    private readonly Dictionary<int, GVItem>    _handles = new();
    private readonly Dictionary<string, GVItem> _badges  = new();
    private readonly List<string> _badgeOrder = new();   // 스택 순서

    private Mesh     _sphereMesh;
    private Material _sharedMat;
    private Camera   _cam;
    private int      _handleSeq = 1;
    private readonly Vector3[] _coneBuf = new Vector3[ConeArcPoints + 3];

    // ── 생명주기 ────────────────────────────────────────
    private void Awake()
    {
        EnsureAssets();
        CacheCamera();
    }

    private void OnApplicationQuit() => s_appQuitting = true;

    private void LateUpdate()
    {
        float dt = Time.unscaledDeltaTime;
        if (_cam == null) CacheCamera();

        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var it = _active[i];

            // 추적 대상 소멸/비활성 → 해제
            if (it.follow != null)
            {
                if (it.follow.gameObject == null) { ReleaseAt(i); continue; }
                if (!it.follow.gameObject.activeInHierarchy) { ReleaseAt(i); continue; }
                it.tf.position = it.follow.position + it.followOffset;
            }

            it.age += dt;

            // 토스트(추적 없는 라벨)는 위로 떠오르는 연출
            if (it.kind == ItemKind.Label && it.follow == null)
                it.tf.position += Vector3.up * (dt * 1.4f);

            if (it.billboard && _cam != null)
                it.tf.rotation = Quaternion.LookRotation(_cam.transform.forward, _cam.transform.up);

            if (it.fadeOut && it.life > 0f)
            {
                float a = it.baseAlpha * Mathf.Clamp01(1f - it.age / it.life);
                ApplyAlpha(it, a);
            }

            if (it.life >= 0f && it.age >= it.life)
                ReleaseAt(i);
        }
    }

    // ── 공개 스폰 API (파사드가 호출) ──────────────────────
    /// <summary>즉발/광역 피해 플래시(짧은 수명 구체).</summary>
    public void SpawnFlash(Vector3 pos, Color color, float scale, float life, bool squashed)
    {
        var it = GetSphere();
        if (it == null) return;
        it.tf.position   = pos;
        it.tf.rotation   = Quaternion.identity;
        it.baseScale     = squashed ? new Vector3(scale, scale * 0.08f, scale) : Vector3.one * scale;
        it.tf.localScale = it.baseScale;
        it.follow    = null;
        it.life      = life;
        it.age       = 0f;
        it.fadeOut   = true;
        it.baseAlpha = color.a;
        SetColor(it, color);
        Add(it);
    }

    /// <summary>대상 추적형 장판 디스크(영속, 명시 해제까지). 반환=해제 핸들.</summary>
    public int SpawnField(Transform follow, float radius, Color color)
    {
        var it = GetSphere();
        if (it == null || follow == null) return 0;
        it.follow       = follow;
        it.followOffset = new Vector3(0f, 0.05f, 0f);
        it.baseScale    = new Vector3(radius * 2f, 0.05f, radius * 2f);
        it.tf.localScale = it.baseScale;
        it.tf.position  = follow.position + it.followOffset;
        it.tf.rotation  = Quaternion.identity;
        it.life      = -1f;
        it.age       = 0f;
        it.fadeOut   = false;
        it.baseAlpha = color.a;
        it.handle    = _handleSeq++;
        SetColor(it, color);
        _handles[it.handle] = it;
        Add(it);
        return it.handle;
    }

    public void ReleaseField(int handle)
    {
        if (handle == 0) return;
        if (!_handles.TryGetValue(handle, out var it)) return;
        _handles.Remove(handle);
        int idx = _active.IndexOf(it);
        if (idx >= 0) ReleaseAt(idx);
    }

    /// <summary>적 머리 위 상태이상 마커(구체 + 라벨). (target,statusId)별 1개 — 재부여 시 수명 갱신.</summary>
    public void SpawnMarker(Transform target, string statusId, string label, Color color, float duration)
    {
        if (target == null || string.IsNullOrEmpty(statusId) || duration <= 0f) return;

        string key = target.GetInstanceID() + "|" + statusId;
        if (_markers.TryGetValue(key, out var exist) && exist.go != null && exist.go.activeSelf)
        {
            // 갱신: 수명 리셋(연타/top-up).
            exist.age  = 0f;
            exist.life = duration;
            return;
        }

        var it = GetMarker();
        if (it == null) return;
        it.follow       = target;
        it.followOffset = new Vector3(0f, 2.2f, 0f);
        it.baseScale    = Vector3.one;             // 자식(구체 0.3 / 라벨)에 고정 스케일 — 라벨 가독 유지
        it.tf.localScale = it.baseScale;
        it.tf.position  = target.position + it.followOffset;
        it.life      = duration;
        it.age       = 0f;
        it.fadeOut   = false;
        it.billboard = true;
        it.baseAlpha = color.a;
        it.markerKey = key;
        SetColor(it, color);
        if (it.label != null)
        {
            it.label.text  = label;
            it.label.color = new Color(color.r, color.g, color.b, 1f);
        }
        _markers[key] = it;
        Add(it);
    }

    /// <summary>두 점 사이 라인(체인/볼트).</summary>
    public void SpawnLine(Vector3 a, Vector3 b, Color color, float life)
    {
        var it = GetLine();
        if (it == null) return;
        it.line.positionCount = 2;
        it.line.SetPosition(0, a);
        it.line.SetPosition(1, b);
        it.line.startColor = color;
        it.line.endColor   = color;
        it.follow    = null;
        it.life      = life;
        it.age       = 0f;
        it.fadeOut   = true;
        it.baseAlpha = color.a;
        it.color     = color;
        Add(it);
    }

    /// <summary>원뿔(부채꼴) 윤곽 라인. origin에서 forward 기준 ±halfAngle, 길이 range.</summary>
    public void SpawnCone(Vector3 origin, Vector3 forward, float range, float halfAngleDeg, Color color, float life)
    {
        var it = GetLine();
        if (it == null) return;

        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
        forward.Normalize();

        _coneBuf[0] = origin;
        for (int i = 0; i <= ConeArcPoints; i++)
        {
            float t = (float)i / ConeArcPoints;          // 0..1
            float ang = Mathf.Lerp(-halfAngleDeg, halfAngleDeg, t);
            Vector3 dir = Quaternion.AngleAxis(ang, Vector3.up) * forward;
            _coneBuf[i + 1] = origin + dir * range;
        }
        // 마지막에 origin으로 닫기(부채꼴 윤곽)
        int count = ConeArcPoints + 3;
        _coneBuf[count - 1] = origin;

        it.line.positionCount = count;
        it.line.SetPositions(_coneBuf);
        it.line.startColor = color;
        it.line.endColor   = color;
        it.follow    = null;
        it.life      = life;
        it.age       = 0f;
        it.fadeOut   = true;
        it.baseAlpha = color.a;
        it.color     = color;
        Add(it);
    }

    /// <summary>위로 떠오르며 사라지는 플로팅 라벨(서약/유물/스킬/리소스 토스트).</summary>
    public void SpawnToast(Vector3 pos, string label, Color color, float life)
    {
        var it = GetLabel();
        if (it == null) return;
        it.tf.position   = pos;
        it.follow    = null;
        it.life      = life;
        it.age       = 0f;
        it.fadeOut   = true;
        it.billboard = true;
        it.baseAlpha = 1f;
        if (it.label != null)
        {
            it.label.text  = label;
            it.label.color = new Color(color.r, color.g, color.b, 1f);
            it.label.fontSize = 4f;
        }
        // 살짝 위로 띄움
        it.followOffset = Vector3.zero;
        Add(it);
        // 토스트는 위로 떠오르는 연출만 LateUpdate에서 처리하지 않으므로 시작 위치만 잡고 fade로 처리.
    }

    /// <summary>플레이어 머리 위 지속 배지(상태키별 1개). 같은 키 재호출 = 텍스트/색 갱신. 영속(ClearBadge까지).</summary>
    public void SetBadge(Transform anchor, string key, string text, Color color)
    {
        if (anchor == null || string.IsNullOrEmpty(key)) return;

        if (_badges.TryGetValue(key, out var it) && it.go != null && it.go.activeSelf)
        {
            it.follow = anchor;
            if (it.label != null)
            {
                it.label.text  = text;
                it.label.color = new Color(color.r, color.g, color.b, 1f);
                it.label.alpha = 1f;
            }
            return;
        }

        it = GetLabel();
        it.follow    = anchor;
        it.billboard = true;
        it.life      = -1f;
        it.age       = 0f;
        it.fadeOut   = false;
        it.baseAlpha = 1f;
        it.badgeKey  = key;
        it.tf.position = anchor.position;
        if (it.label != null)
        {
            it.label.text  = text;
            it.label.color = new Color(color.r, color.g, color.b, 1f);
            it.label.alpha = 1f;
            it.label.fontSize = 3.4f;
        }
        _badges[key] = it;
        _badgeOrder.Add(key);
        Add(it);
        RestackBadges();
    }

    public void ClearBadge(string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        if (!_badges.TryGetValue(key, out var it)) return;
        int idx = _active.IndexOf(it);
        if (idx >= 0) ReleaseAt(idx);
        else { _badges.Remove(key); _badgeOrder.Remove(key); }
        RestackBadges();
    }

    private void RestackBadges()
    {
        int n = 0;
        for (int i = 0; i < _badgeOrder.Count; i++)
        {
            if (!_badges.TryGetValue(_badgeOrder[i], out var it)) continue;
            it.followOffset = new Vector3(0f, 3.0f + n * 0.55f, 0f);   // 머리 위로 스택
            n++;
        }
    }

    public void ClearAll()
    {
        for (int i = _active.Count - 1; i >= 0; i--) ReleaseAt(i);
        _markers.Clear();
        _handles.Clear();
        _badges.Clear();
        _badgeOrder.Clear();
    }

    // ── 내부: 풀 관리 ────────────────────────────────────
    private void Add(GVItem it)
    {
        it.go.SetActive(true);
        _active.Add(it);

        // 상한 초과 시 가장 오래된 비추적/비영속 아이템부터 정리.
        if (_active.Count > MaxActive)
        {
            for (int i = 0; i < _active.Count; i++)
            {
                if (_active[i].life >= 0f && _active[i] != it) { ReleaseAt(i); break; }
            }
        }
    }

    private void ReleaseAt(int index)
    {
        if (index < 0 || index >= _active.Count) return;
        var it = _active[index];
        _active.RemoveAt(index);

        if (!string.IsNullOrEmpty(it.markerKey))
        {
            if (_markers.TryGetValue(it.markerKey, out var m) && m == it) _markers.Remove(it.markerKey);
            it.markerKey = null;
        }
        if (it.handle != 0)
        {
            _handles.Remove(it.handle);
            it.handle = 0;
        }
        if (!string.IsNullOrEmpty(it.badgeKey))
        {
            if (_badges.TryGetValue(it.badgeKey, out var b) && b == it) _badges.Remove(it.badgeKey);
            _badgeOrder.Remove(it.badgeKey);
            it.badgeKey = null;
        }

        it.follow = null;
        if (it.go != null) it.go.SetActive(false);

        switch (it.kind)
        {
            case ItemKind.Sphere: _spherePool.Push(it); break;
            case ItemKind.Line:   _linePool.Push(it);   break;
            case ItemKind.Label:  _labelPool.Push(it);  break;
            case ItemKind.Marker: _markerPool.Push(it); break;
        }
    }

    private GVItem GetSphere() => _spherePool.Count > 0 ? _spherePool.Pop() : CreateSphere();
    private GVItem GetMarker() => _markerPool.Count > 0 ? _markerPool.Pop() : CreateMarker();
    private GVItem GetLine()   => _linePool.Count   > 0 ? _linePool.Pop()   : CreateLine();
    private GVItem GetLabel()  => _labelPool.Count  > 0 ? _labelPool.Pop()  : CreateLabel();

    // ── 내부: 아이템 생성 ────────────────────────────────
    private GVItem CreateSphere()
    {
        var go = new GameObject("gv_sphere");
        go.transform.SetParent(transform, false);
        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = _sphereMesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial    = _sharedMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows    = false;
        go.SetActive(false);
        return new GVItem { kind = ItemKind.Sphere, go = go, tf = go.transform, sphereMr = mr, mpb = new MaterialPropertyBlock() };
    }

    private GVItem CreateLine()
    {
        var go = new GameObject("gv_line");
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial  = _sharedMat;          // 색은 startColor/endColor(정점색)로 — 머티리얼 공유
        lr.widthMultiplier = 0.12f;
        lr.numCapVertices  = 2;
        lr.useWorldSpace   = true;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows    = false;
        lr.alignment         = LineAlignment.View;
        go.SetActive(false);
        return new GVItem { kind = ItemKind.Line, go = go, tf = go.transform, line = lr, mpb = new MaterialPropertyBlock() };
    }

    private GVItem CreateLabel()
    {
        var go = new GameObject("gv_label");
        go.transform.SetParent(transform, false);
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize  = 4f;
        tmp.rectTransform.sizeDelta = new Vector2(6f, 2f);
        go.SetActive(false);
        return new GVItem { kind = ItemKind.Label, go = go, tf = go.transform, label = tmp, mpb = new MaterialPropertyBlock() };
    }

    private GVItem CreateMarker()
    {
        // 루트(추적/빌보드) + 구체 자식 + 라벨 자식
        var go = new GameObject("gv_marker");
        go.transform.SetParent(transform, false);

        var sphere = new GameObject("dot");
        sphere.transform.SetParent(go.transform, false);
        var mf = sphere.AddComponent<MeshFilter>();
        mf.sharedMesh = _sphereMesh;
        var mr = sphere.AddComponent<MeshRenderer>();
        mr.sharedMaterial    = _sharedMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows    = false;
        sphere.transform.localScale = Vector3.one * 0.3f;
        sphere.transform.localPosition = Vector3.zero;

        var labelGo = new GameObject("txt");
        labelGo.transform.SetParent(go.transform, false);
        var tmp = labelGo.AddComponent<TextMeshPro>();
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize  = 3f;
        tmp.rectTransform.sizeDelta = new Vector2(6f, 2f);
        labelGo.transform.localPosition = new Vector3(0f, 0.6f, 0f);

        go.SetActive(false);
        return new GVItem { kind = ItemKind.Marker, go = go, tf = go.transform, sphereMr = mr, label = tmp, mpb = new MaterialPropertyBlock() };
    }

    // ── 내부: 색/알파 ────────────────────────────────────
    private void SetColor(GVItem it, Color c)
    {
        it.color = c;
        if (it.sphereMr != null)
        {
            it.mpb.Clear();
            it.mpb.SetColor(ColorProp, c);
            it.sphereMr.SetPropertyBlock(it.mpb);
        }
        if (it.line != null)
        {
            it.line.startColor = c;
            it.line.endColor   = c;
        }
    }

    private void ApplyAlpha(GVItem it, float a)
    {
        var c = it.color; c.a = a;
        if (it.sphereMr != null)
        {
            it.mpb.Clear();
            it.mpb.SetColor(ColorProp, c);
            it.sphereMr.SetPropertyBlock(it.mpb);
        }
        if (it.line != null)
        {
            it.line.startColor = c;
            it.line.endColor   = c;
        }
        if (it.label != null)
            it.label.alpha = a;
    }

    // ── 내부: 에셋/카메라 ────────────────────────────────
    private static int s_colorProp = -1;
    private static int ColorProp
    {
        get { if (s_colorProp < 0) s_colorProp = Shader.PropertyToID("_Color"); return s_colorProp; }
    }

    private void EnsureAssets()
    {
        if (_sphereMesh == null)
        {
            var temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var src  = temp.GetComponent<MeshFilter>().sharedMesh;
            // 내장 셰어드 메시는 수정 금지 → 복사본에 흰색 정점색을 넣어 Sprites/Default 곱셈에서 _Color가 보이게 한다.
            _sphereMesh = Instantiate(src);
            var cols = new Color32[_sphereMesh.vertexCount];
            for (int i = 0; i < cols.Length; i++) cols[i] = new Color32(255, 255, 255, 255);
            _sphereMesh.colors32 = cols;
            Destroy(temp);
        }
        if (_sharedMat == null)
        {
            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            _sharedMat = new Material(sh) { name = "gv_shared" };
            _sharedMat.renderQueue = 3100;   // 투명 위에 오버레이
        }
    }

    private void CacheCamera()
    {
        _cam = Camera.main;
        if (_cam == null)
        {
            var any = Camera.allCamerasCount > 0 ? Camera.allCameras[0] : null;
            _cam = any;
        }
    }
}
