using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 카메라~플레이어 사이 오브젝트를 반투명하게 페이드.
///
/// 최적화:
///   - SphereCastNonAlloc + 고정 버퍼 → 매 프레임 GC 제로
///   - MaterialPropertyBlock 캐시 → 프레임당 1개 재사용
///   - toRemove List 재사용 → 힙 할당 없음
///   - 렌더러 캐시 (콜라이더 → Renderer[]) → GetComponentsInChildren 반복 방지
/// </summary>
[RequireComponent(typeof(Camera))]
public class CameraOcclusionFader : MonoBehaviour
{
    // ── Constants ─────────────────────────────────────────────────────────
    private const int MaxHits = 32;
    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");

    // ── SerializeField ────────────────────────────────────────────────────
    [Header("Detection")]
    [SerializeField] private LayerMask occlusionMask = ~0;
    [SerializeField] private float     castRadius    = 0.3f;

    [Header("Fade")]
    [SerializeField, Range(0f, 1f)] private float hiddenAlpha = 0.15f;
    [SerializeField]                private float fadeSpeed    = 8f;

    // ── Private fields ────────────────────────────────────────────────────
    private Transform _playerTransform;

    // NonAlloc 물리 버퍼 — 고정 크기, 재사용
    private readonly RaycastHit[] _hitBuffer = new RaycastHit[MaxHits];

    // 콜라이더 → 렌더러 배열 캐시 (GetComponentsInChildren 반복 방지)
    private readonly Dictionary<Collider, Renderer[]> _rendererCache = new();

    // 페이드 상태 테이블
    private readonly Dictionary<Renderer, FadeState> _fading = new();

    // 이번 프레임 히트 렌더러
    private readonly HashSet<Renderer> _hitThisFrame = new();

    // UpdateFading 내 재사용 리스트
    private readonly List<Renderer> _toRemove = new();

    // 재사용 MaterialPropertyBlock (프레임당 1개) — Awake에서 초기화
    private MaterialPropertyBlock _mpb;

    private class FadeState
    {
        public Material[] originals;
        public Material[] faded;
        public float      currentAlpha;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────

    private void Awake()
    {
        _mpb = new MaterialPropertyBlock();
    }

    private void Start()
    {
        SubscribePlayer();
    }

    private void OnDestroy()
    {
        RestoreAll();
    }

    private void LateUpdate()
    {
        if (_playerTransform == null) return;
        _hitThisFrame.Clear();
        DetectOccluders();
        UpdateFading();
    }

    // ── Public ────────────────────────────────────────────────────────────

    public void SetTarget(Transform player) => _playerTransform = player;

    // ── Private ───────────────────────────────────────────────────────────

    private void SubscribePlayer()
    {
        if (GameRunBootstrapper.Instance?.Run?.Player != null)
        {
            _playerTransform = GameRunBootstrapper.Instance.Run.Player.transform;
            return;
        }
        if (Managers.Player?.PlayerTransform != null)
        {
            _playerTransform = Managers.Player.PlayerTransform;
            return;
        }
        WaitForPlayerAsync().Forget();
    }

    private async Cysharp.Threading.Tasks.UniTaskVoid WaitForPlayerAsync()
    {
        var ct = destroyCancellationToken;
        await Cysharp.Threading.Tasks.UniTask.WaitUntil(
            () => GameRunBootstrapper.Instance?.Run?.Player != null,
            cancellationToken: ct);
        if (GameRunBootstrapper.Instance?.Run?.Player != null)
            _playerTransform = GameRunBootstrapper.Instance.Run.Player.transform;
    }

    private void DetectOccluders()
    {
        var   camPos    = transform.position;
        var   playerPos = _playerTransform.position + Vector3.up * 1f;
        var   dir       = playerPos - camPos;
        float dist      = dir.magnitude;
        if (dist < 0.1f) return;

        // NonAlloc — _hitBuffer 재사용, GC 제로
        int count = Physics.SphereCastNonAlloc(
            camPos, castRadius, dir / dist, _hitBuffer, dist,
            occlusionMask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            var col = _hitBuffer[i].collider;
            if (col == null) continue;
            if (col.gameObject == _playerTransform.gameObject) continue;

            // 렌더러 캐시 조회 (최초 1회만 GetComponentsInChildren 호출)
            if (!_rendererCache.TryGetValue(col, out var renderers))
            {
                renderers = col.GetComponentsInChildren<Renderer>();
                _rendererCache[col] = renderers;
            }

            foreach (var r in renderers)
            {
                if (r == null) continue;
                _hitThisFrame.Add(r);
                if (!_fading.ContainsKey(r))
                    BeginFade(r);
            }
        }
    }

    private void UpdateFading()
    {
        _toRemove.Clear();

        foreach (var kv in _fading)
        {
            var r = kv.Key;
            if (r == null) { _toRemove.Add(r); continue; }

            var   data       = kv.Value;
            bool  shouldHide = _hitThisFrame.Contains(r);
            float target     = shouldHide ? hiddenAlpha : 1f;
            data.currentAlpha = Mathf.MoveTowards(data.currentAlpha, target, fadeSpeed * Time.deltaTime);

            ApplyAlpha(r, data.faded, data.currentAlpha);

            if (!shouldHide && Mathf.Approximately(data.currentAlpha, 1f))
            {
                RestoreRenderer(r, data);
                _toRemove.Add(r);
            }
        }

        for (int i = 0; i < _toRemove.Count; i++)
            _fading.Remove(_toRemove[i]);
    }

    private void BeginFade(Renderer r)
    {
        var origMats  = r.sharedMaterials;
        var fadedMats = new Material[origMats.Length];
        for (int i = 0; i < origMats.Length; i++)
        {
            var copy = new Material(origMats[i]);
            MakeTransparent(copy);
            fadedMats[i] = copy;
        }
        r.materials = fadedMats;

        _fading[r] = new FadeState
        {
            originals    = origMats,
            faded        = fadedMats,
            currentAlpha = 1f,
        };
    }

    // _mpb 재사용 — new MaterialPropertyBlock() 없음
    private void ApplyAlpha(Renderer r, Material[] mats, float alpha)
    {
        foreach (var m in mats)
        {
            if (m == null) continue;
            // _BaseColor 없는 커스텀 ShaderGraph(S_Masking 등) 스킵 — 매 프레임 오류 방지
            if (!m.HasProperty(BaseColorID)) continue;
            var c = m.GetColor(BaseColorID);
            c.a   = alpha;
            m.SetColor(BaseColorID, c);
        }
        // SRP Batcher와 호환되는 방식으로 MPB 적용
        r.GetPropertyBlock(_mpb);
        _mpb.SetFloat("_Surface", 1f);
        r.SetPropertyBlock(_mpb);
    }

    private static void MakeTransparent(Material m)
    {
        m.SetFloat("_Surface",   1f);
        m.SetFloat("_Blend",     0f);
        m.SetFloat("_AlphaClip", 0f);
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        m.SetOverrideTag("RenderType", "Transparent");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.DisableKeyword("_ALPHATEST_ON");
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite",   0);
    }

    private static void RestoreRenderer(Renderer r, FadeState data)
    {
        if (r != null) r.sharedMaterials = data.originals;
        foreach (var m in data.faded)
            if (m != null) Object.Destroy(m);
    }

    private void RestoreAll()
    {
        foreach (var kv in _fading)
            RestoreRenderer(kv.Key, kv.Value);
        _fading.Clear();
        _rendererCache.Clear();
    }
}
