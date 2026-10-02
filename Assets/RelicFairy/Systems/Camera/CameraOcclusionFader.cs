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
    private const float NearMinHeight = 1.5f;    // 근거리 판정은 이만큼 높은 구조물만(촛대 · 잔해 · 보상 오브젝트 같은 작은 것은 빼고)
    private const float NearHoldSeconds = 0.35f; // 근거리로 흐린 것은 이만큼 붙잡는다 — 캡슐 경계에서 들락날락 깜박이지 않게
    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
    private static readonly int BaseMapID   = Shader.PropertyToID("_BaseMap");
    private static readonly int BumpMapID   = Shader.PropertyToID("_BumpMap");

    // 원본 → 페이드용 URP Lit로 옮길 때 찾아볼 프로퍼티 이름들(앞에서부터 먼저 맞는 것 1개).
    //
    // 이 프로젝트의 석재·목재는 대부분 커스텀 ShaderGraph라 URP 표준 이름을 쓰지 않는다:
    //   · Gothic_Interior(봉인 석문 4종) = _BaseColorMap / _BaseColor
    //   · Leartes S_Masking(숲 봉인문 뿌리) = _Base, 색 프로퍼티 없음
    // _BaseMap만 보면 전부 실패해 텍스처 없는 흰 판으로 페이드된다.
    private static readonly string[] TextureProps = { "_BaseMap", "_BaseColorMap", "_MainTex", "_Base", "_Albedo" };
    private static readonly string[] ColorProps   = { "_BaseColor", "_Color", "_BaseTintColor", "_MainColor" };
    private static readonly string[] NormalProps  = { "_BumpMap", "_NormalMap", "_Normal" };

    // ── SerializeField ────────────────────────────────────────────────────
    [Header("Detection")]
    [SerializeField] private LayerMask occlusionMask = ~0;
    [SerializeField] private float     castRadius    = 0.3f;
    [Tooltip("카메라 앞 근거리 판정 — 카메라에서 플레이어 쪽으로 이 거리(m)까지(플레이어까지의 절반을 넘지 않음). 0 = 끔")]
    [SerializeField] private float     nearDistance  = 0f;
    [Tooltip("카메라 앞 근거리 판정 반경(m) — 카메라→플레이어 선 위가 아니어도 카메라 바로 옆 큰 구조물(회랑 기둥)을 잡는다")]
    [SerializeField] private float     nearRadius    = 2.5f;

    [Header("Fade")]
    [SerializeField, Range(0f, 1f)] private float hiddenAlpha = 0.15f;
    [Tooltip("카메라 앞 근거리로 흐린 것의 알파 — 화면 가장자리를 크게 덮는 구조물이라 더 옅게(밝게 떠서 눈을 끌지 않게)")]
    [SerializeField, Range(0f, 1f)] private float nearHiddenAlpha = 0.06f;
    [SerializeField]                private float fadeSpeed    = 8f;

    // ── Private fields ────────────────────────────────────────────────────
    private Transform _playerTransform;

    // NonAlloc 물리 버퍼 — 고정 크기, 재사용
    private readonly RaycastHit[] _hitBuffer = new RaycastHit[MaxHits];
    private readonly Collider[]   _nearBuffer = new Collider[MaxHits];
    private readonly Dictionary<Renderer, float> _nearHoldUntil = new();

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

    // 페이드용 투명 셰이더 (URP Lit). 원본이 Opaque 전용 ShaderGraph라도 확실히 반투명 처리하기 위해
    // 원본 클론 대신 이 셰이더로 스왑한다. Awake에서 1회 캐싱.
    private Shader _fadeShader;

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
        _fadeShader = Shader.Find("Universal Render Pipeline/Lit");
    }

    private void OnEnable()
    {
        // 디졸브가 원본을 기억할 때 이 페이더가 씌운 임시 재질 대신 진짜 원본을 받게 한다(10-01)
        DissolveEffect.OriginalMaterialsResolver = ResolveOriginals;
    }

    private void OnDisable()
    {
        if (DissolveEffect.OriginalMaterialsResolver == (System.Func<Renderer, Material[]>)ResolveOriginals)
            DissolveEffect.OriginalMaterialsResolver = null;
    }

    private void Start()
    {
        // 플레이어 (재)스폰 시마다 타깃 갱신 — 허브 유물 재스폰·전투 존 재스폰에서 이전 타깃이 파괴되므로.
        if (Managers.Player != null) Managers.Player.OnPlayerSpawned += SetTarget;
        SubscribePlayer();
    }

    private void OnDestroy()
    {
        if (Managers.Player != null) Managers.Player.OnPlayerSpawned -= SetTarget;
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
            () => GameRunBootstrapper.Instance?.Run?.Player != null
               || Managers.Player?.PlayerTransform != null,
            cancellationToken: ct);
        if (GameRunBootstrapper.Instance?.Run?.Player != null)
            _playerTransform = GameRunBootstrapper.Instance.Run.Player.transform;
        else if (Managers.Player?.PlayerTransform != null)
            _playerTransform = Managers.Player.PlayerTransform;
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
            MarkOccluder(_hitBuffer[i].collider);

        // 카메라 앞 근거리 — 카메라 바로 옆 큰 구조물은 플레이어와 한 줄이 아니어도 화면 한쪽을 통째로 가린다
        // (10-01 f5 · 09-25 G21: 기사 아레나 가장자리에서 회랑 기둥이 화면 35~40%). 플레이어까지의 절반에서 멈춰 바닥은 안 닿는다.
        if (nearDistance > 0f)
        {
            float   reach = Mathf.Min(nearDistance, dist * 0.5f);
            Vector3 end   = camPos + dir / dist * reach;
            int near = Physics.OverlapCapsuleNonAlloc(camPos, end, nearRadius, _nearBuffer, occlusionMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < near; i++)
            {
                var col = _nearBuffer[i];
                // 움직이는 것(리지드바디) · 낮은 것은 빼고 큰 고정 구조물(기둥 · 벽)만
                if (col == null || col.attachedRigidbody != null || col.bounds.size.y < NearMinHeight) continue;
                MarkOccluder(col, true);
            }
        }
    }

    private void MarkOccluder(Collider col, bool near = false)
    {
        if (col == null) return;
        if (col.gameObject == _playerTransform.gameObject) return;

        // 렌더러 캐시 조회 (최초 1회만 GetComponentsInChildren 호출)
        if (!_rendererCache.TryGetValue(col, out var renderers))
        {
            renderers = col.GetComponentsInChildren<Renderer>();
            _rendererCache[col] = renderers;
        }

        foreach (var r in renderers)
        {
            if (r == null) continue;
            // 꺼진 렌더러(방을 짓는 동안 숨긴 블록) · 디졸브 중인 렌더러는 잡지 않는다 — 그 순간의 재질(디졸브 임시 재질)을
            // 원본으로 기억하거나, 곧 시작할 입장 디졸브가 이 페이더의 임시 재질을 원본으로 기억하면 되돌릴 때 빈 재질(마젠타)이 된다.
            // (10-01 Ch1 상점·정제소 — 입장 연출이 없어 벽이 숨김 · 디졸브 중일 때 카메라와 플레이어 사이에 들었다)
            if (!r.enabled || DissolveEffect.IsDissolving(r)) continue;
            _hitThisFrame.Add(r);
            if (near) _nearHoldUntil[r] = Time.unscaledTime + NearHoldSeconds;
            if (!_fading.ContainsKey(r))
                BeginFade(r);
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

            // 디졸브가 이 렌더러를 잡아 갔다 — 디졸브는 우리 원본(ResolveOriginals)을 기억했으니 되돌리지 않고 손을 뗀다
            if (DissolveEffect.IsDissolving(r))
            {
                foreach (var m in data.faded) if (m != null) Object.Destroy(m);
                _toRemove.Add(r);
                continue;
            }

            bool  shouldHide = _hitThisFrame.Contains(r)
                            || (_nearHoldUntil.TryGetValue(r, out float holdUntil) && Time.unscaledTime < holdUntil);
            float target     = shouldHide ? (_nearHoldUntil.ContainsKey(r) ? nearHiddenAlpha : hiddenAlpha) : 1f;
            data.currentAlpha = Mathf.MoveTowards(data.currentAlpha, target, fadeSpeed * Time.deltaTime);

            ApplyAlpha(r, data.faded, data.currentAlpha);

            if (!shouldHide && Mathf.Approximately(data.currentAlpha, 1f))
            {
                RestoreRenderer(r, data);
                _toRemove.Add(r);
            }
        }

        for (int i = 0; i < _toRemove.Count; i++)
        {
            _fading.Remove(_toRemove[i]);
            _nearHoldUntil.Remove(_toRemove[i]);
        }
    }

    private void BeginFade(Renderer r)
    {
        var origMats  = r.sharedMaterials;
        var fadedMats = new Material[origMats.Length];
        for (int i = 0; i < origMats.Length; i++)
        {
            var orig = origMats[i];

            // 원본을 클론해 _Surface만 토글하면 Opaque 전용 ShaderGraph(예: BaseCamp 석재
            // S_OpaqueORMWorldAlign)는 투명 패스가 컴파일돼 있지 않아 무시된다(불투명 그대로).
            // → URP Lit(투명 패스 내장)로 스왑하고 베이스맵/색/노멀만 복사해 어떤 셰이더든 확실히 반투명화.
            Material copy;
            if (_fadeShader != null)
            {
                copy = new Material(_fadeShader);
                if (orig != null)
                {
                    CopyFirstTexture(orig, copy, TextureProps, BaseMapID);
                    CopyFirstTexture(orig, copy, NormalProps,  BumpMapID);
                    CopyFirstColor(orig, copy, ColorProps, BaseColorID);
                }
            }
            else
            {
                // URP Lit 미발견 시 기존 경로 폴백(원본 클론 + _Surface 토글)
                copy = orig != null ? new Material(orig) : null;
            }

            if (copy != null) MakeTransparent(copy);
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

    /// <summary>후보 중 원본이 가진 첫 텍스처를 페이드 머티리얼의 dstId로 옮긴다. 없으면 아무것도 안 한다.</summary>
    private static void CopyFirstTexture(Material orig, Material copy, string[] candidates, int dstId)
    {
        if (!copy.HasProperty(dstId)) return;
        for (int i = 0; i < candidates.Length; i++)
        {
            if (!orig.HasProperty(candidates[i])) continue;
            var tex = orig.GetTexture(candidates[i]);
            if (tex == null) continue;
            copy.SetTexture(dstId, tex);
            return;
        }
    }

    /// <summary>후보 중 원본이 가진 첫 색을 옮긴다. 알파는 페이드가 매 프레임 덮어쓰므로 무시한다.</summary>
    private static void CopyFirstColor(Material orig, Material copy, string[] candidates, int dstId)
    {
        if (!copy.HasProperty(dstId)) return;
        for (int i = 0; i < candidates.Length; i++)
        {
            if (!orig.HasProperty(candidates[i])) continue;
            copy.SetColor(dstId, orig.GetColor(candidates[i]));
            return;
        }
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
        _mpb ??= new MaterialPropertyBlock();
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

        // 새 URP Lit의 기본 매끄러움 0.5가 직사광을 되비춰 흐린 기둥이 번들거렸다(10-01 f5) — 거칠게.
        // 키워드(정반사 · 환경 반사 끄기)는 쓰지 않는다: 새 변형이라 에디터에선 컴파일 동안 청록 대체 셰이더로 뜨고, 빌드에선 걸러질 수 있다.
        m.SetFloat("_Smoothness", 0f);
    }

    /// <summary>디졸브가 묻는다 — 이 페이더가 임시 재질을 씌운 렌더러면 그 원본, 아니면 null.</summary>
    private Material[] ResolveOriginals(Renderer r)
        => r != null && _fading.TryGetValue(r, out var data) ? data.originals : null;

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
