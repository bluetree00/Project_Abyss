using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 카메라와 플레이어 사이의 장애물을 반투명 처리한다.
/// 카메라(또는 CinemachineBrain) GameObject에 추가하고
/// Target(플레이어 Transform)을 연결한다.
/// </summary>
public sealed class CameraOcclusionFader : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private LayerMask occlusionMask = ~0;
    [SerializeField, Range(0f, 1f)] private float occludedAlpha = 0.15f;
    [SerializeField] private float fadeSpeed = 6f;
    [SerializeField] private float targetHeightOffset = 1.2f; // 플레이어 허리 높이

    private static readonly int BaseColorId     = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId         = Shader.PropertyToID("_Color");
    private static readonly int SurfaceId       = Shader.PropertyToID("_Surface");

    // 장애물 Renderer당 상태를 추적
    private sealed class FadeEntry
    {
        public Material[] origMaterials;
        public Material[] fadeMaterials;
        public float      currentAlpha = 1f;
        public bool       isOccluding;
    }

    private readonly Dictionary<Renderer, FadeEntry> _tracked = new();
    private readonly List<Renderer>                  _toRemove = new();
    private readonly RaycastHit[]                    _hitBuffer = new RaycastHit[32];

    private Camera _cam;

    //============================================================
    // Lifecycle
    //============================================================

    private void Awake()
    {
        _cam = GetComponent<Camera>();
        if (_cam == null) _cam = Camera.main;
    }

    private void LateUpdate()
    {
        if (_cam == null || target == null) return;

        Vector3 camPos    = _cam.transform.position;
        Vector3 targetPos = target.position + Vector3.up * targetHeightOffset;
        Vector3 dir       = targetPos - camPos;
        float   dist      = dir.magnitude;
        if (dist < 0.1f) return;

        // 이번 프레임 장애물 수집
        int hitCount = Physics.RaycastNonAlloc(camPos, dir.normalized, _hitBuffer, dist, occlusionMask);

        var occluding = new HashSet<Renderer>();
        for (int i = 0; i < hitCount; i++)
        {
            var col = _hitBuffer[i].collider;
            // 플레이어 자신 제외
            if (col.GetComponent<PlayerController>() != null) continue;
            if (col.GetComponentInParent<PlayerController>() != null) continue;

            foreach (var r in col.GetComponentsInChildren<Renderer>())
                occluding.Add(r);
        }

        // 신규 장애물 등록
        foreach (var r in occluding)
        {
            if (r == null) continue;
            if (!_tracked.TryGetValue(r, out _))
                _tracked[r] = BuildEntry(r);
            _tracked[r].isOccluding = true;
        }

        // 알파 갱신
        foreach (var kvp in _tracked)
        {
            var r     = kvp.Key;
            var entry = kvp.Value;

            if (r == null) { _toRemove.Add(r); continue; }

            entry.isOccluding = occluding.Contains(r);
            float targetAlpha = entry.isOccluding ? occludedAlpha : 1f;
            entry.currentAlpha = Mathf.MoveTowards(
                entry.currentAlpha, targetAlpha, fadeSpeed * Time.deltaTime);

            ApplyAlpha(r, entry, entry.currentAlpha);

            // 완전히 복구됐으면 원본 머티리얼로 되돌리고 트래킹 해제
            if (!entry.isOccluding && entry.currentAlpha >= 0.999f)
            {
                r.materials = entry.origMaterials;
                CleanupEntry(entry);
                _toRemove.Add(r);
            }
        }

        foreach (var r in _toRemove)
            _tracked.Remove(r);
        _toRemove.Clear();
    }

    private void OnDestroy()
    {
        foreach (var kvp in _tracked)
        {
            if (kvp.Key != null)
                kvp.Key.materials = kvp.Value.origMaterials;
            CleanupEntry(kvp.Value);
        }
        _tracked.Clear();
    }

    //============================================================
    // 머티리얼 관리
    //============================================================

    private FadeEntry BuildEntry(Renderer r)
    {
        var orig = r.sharedMaterials;
        var fade = new Material[orig.Length];

        for (int i = 0; i < orig.Length; i++)
        {
            fade[i] = new Material(orig[i]);
            // URP Lit/Unlit: Surface Type → Transparent
            if (fade[i].HasProperty(SurfaceId))
            {
                fade[i].SetFloat(SurfaceId, 1f);          // 1 = Transparent
                fade[i].renderQueue = 3000;
                fade[i].SetOverrideTag("RenderType", "Transparent");
            }
        }

        return new FadeEntry
        {
            origMaterials = orig,
            fadeMaterials = fade,
            currentAlpha  = 1f,
        };
    }

    private static void ApplyAlpha(Renderer r, FadeEntry entry, float alpha)
    {
        var mats = r.materials;

        for (int i = 0; i < mats.Length && i < entry.fadeMaterials.Length; i++)
        {
            var mat = entry.fadeMaterials[i];
            if (mat == null) continue;

            if (mat.HasProperty(BaseColorId))
            {
                var c = mat.GetColor(BaseColorId);
                c.a = alpha;
                mat.SetColor(BaseColorId, c);
            }
            else if (mat.HasProperty(ColorId))
            {
                var c = mat.GetColor(ColorId);
                c.a = alpha;
                mat.SetColor(ColorId, c);
            }

            mats[i] = mat;
        }

        r.materials = mats;
    }

    private static void CleanupEntry(FadeEntry entry)
    {
        if (entry?.fadeMaterials == null) return;
        foreach (var m in entry.fadeMaterials)
            if (m != null) Object.Destroy(m);
    }

    //============================================================
    // 런타임에서 Target 설정
    //============================================================

    public void SetTarget(Transform t) => target = t;
}
