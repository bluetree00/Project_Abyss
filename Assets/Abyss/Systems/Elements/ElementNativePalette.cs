using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Polyart의 PAMaskTint 셰이더 등 컬러 팔레트 기반 셰이더의 모든 Color 프로퍼티를
/// 원소 색 하나로 통일해서 주입하는 컴포넌트.
///
/// ShaderGraph로 만든 셰이더는 프로퍼티 ReferenceName이 랜덤 ID(예: Color_E64BA0E)로
/// 저장되는 경우가 많아 "_Color01~09" 같은 고정 이름으로는 매칭 미스 발생.
/// 따라서 런타임에 material의 shader를 스캔해 Color 프로퍼티를 전부 찾아 주입한다.
/// Emission/Outline/Specular 같은 비-팔레트 색은 제외.
///
/// 기존 ElementVisualFeedback의 MaterialPropertyBlock 기반 히트 펄스 로직과 분리되어
/// 독립 동작. 책임 분리로 실패 시 이 파일 제거만으로 롤백 가능.
/// </summary>
public class ElementNativePalette : MonoBehaviour
{
    /// <summary>원소별 대표 팔레트 색. None은 원본 유지.</summary>
    private static readonly Color[] ElementPalette =
    {
        new(1.00f, 0.92f, 0.23f, 1f), // Lightning — 노랑
        new(0.13f, 0.59f, 0.95f, 1f), // Water     — 파랑
        new(0.96f, 0.26f, 0.21f, 1f), // Fire      — 빨강
        new(0.30f, 0.69f, 0.31f, 1f), // Grass     — 초록
        new(0.55f, 0.43f, 0.39f, 1f), // Earth     — 갈색
    };

    /// <summary>팔레트 주입 대상에서 제외할 프로퍼티 이름의 부분 문자열.
    /// Emission/Outline/Specular/Tint 같은 색은 건드리지 않는다.</summary>
    private static readonly string[] ExcludedKeywords =
    {
        "Emission", "Outline", "Specular", "SpecColor", "Tint", "Rim", "Highlight",
    };

    // 머티리얼별 스캔한 팔레트 프로퍼티 ID를 캐시 (shader별 1회만 계산)
    private static readonly Dictionary<Shader, int[]> _paletteIdCache = new();

    // ── [SerializeField] ────────────────────────────────────────────
    [Header("Renderers (비워두면 자동 탐색)")]
    [SerializeField] private Renderer[] renderers;

    [Header("원소 색 주입 강도 (0 = 원본 유지, 1 = 완전 덮어씀)")]
    [SerializeField, Range(0f, 1f)] private float tintStrength = 0.35f;

    [Header("Emission 보강 (원소색으로 은은하게 빛남, 셰이더 지원 시)")]
    [SerializeField] private bool applyEmission = true;
    [SerializeField, Range(0f, 3f)] private float emissionIntensity = 0.6f;

    // ── Private ─────────────────────────────────────────────────────
    private Material[][] _instancedMaterials;
    // 머티리얼 인스턴스당 최초 팔레트 색 스냅샷 — 풀 재사용 시에도 누적 오염 없이 Apply마다 원본부터 Lerp
    private readonly Dictionary<Material, Color[]> _originalColors = new();
    private readonly Dictionary<Material, Color> _originalEmissions = new();
    private static readonly int EmissionColorID = Shader.PropertyToID("_EmissionColor");

    // ── Lifecycle ───────────────────────────────────────────────────
    private void Awake()
    {
        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<Renderer>(true);
    }

    private void OnDestroy()
    {
        if (_instancedMaterials == null) return;
        for (int i = 0; i < _instancedMaterials.Length; i++)
        {
            var mats = _instancedMaterials[i];
            if (mats == null) continue;
            for (int j = 0; j < mats.Length; j++)
                if (mats[j] != null) Destroy(mats[j]);
        }
        _instancedMaterials = null;
    }

    // ── Public Methods ───────────────────────────────────────────────
    /// <summary>원소에 대응하는 색으로 머티리얼의 모든 팔레트 색 프로퍼티를 일괄 주입.
    /// Apply 시작마다 캐시된 원본 색으로 복구 후 Lerp → 풀 재사용 시 색 누적 방지.
    /// None 원소는 원본 복구만 수행.</summary>
    public void Apply(ElementType element)
    {
        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<Renderer>(true);

        if (renderers == null || renderers.Length == 0)
        {
            Debug.LogWarning($"[ElementPalette] '{gameObject.name}': Renderer 0개 — Apply 스킵");
            return;
        }

        EnsureInstancedMaterials();
        if (_instancedMaterials == null)
        {
            Debug.LogWarning($"[ElementPalette] '{gameObject.name}': _instancedMaterials null — Apply 스킵");
            return;
        }

        // 매 Apply마다 원본부터 다시 시작 — 풀 재사용 시 이전 원소 색 누적 방지
        RestoreOriginal();

        if (!element.IsValid()) return; // None — 원본 유지(복구만)

        Color c = ElementPalette[(int)element];
        float t = Mathf.Clamp01(tintStrength);
        int paletteHits = 0;

        for (int i = 0; i < _instancedMaterials.Length; i++)
        {
            var mats = _instancedMaterials[i];
            if (mats == null) continue;
            for (int j = 0; j < mats.Length; j++)
            {
                var mat = mats[j];
                if (mat == null || mat.shader == null) continue;

                // 팔레트 프로퍼티: 원본(스냅샷) 색 × 원소색 Lerp
                if (_originalColors.TryGetValue(mat, out var snapshot))
                {
                    int[] paletteIds = GetPaletteIds(mat.shader);
                    int n = Mathf.Min(paletteIds.Length, snapshot.Length);
                    for (int k = 0; k < n; k++)
                    {
                        Color orig = snapshot[k];
                        Color blended = Color.Lerp(orig, c, t);
                        blended.a = orig.a; // 알파는 원본 보존
                        mat.SetColor(paletteIds[k], blended);
                    }
                    paletteHits += n;
                }

                // Emission 보강 — 셰이더가 _EmissionColor를 지원할 때만
                if (applyEmission && mat.HasProperty(EmissionColorID))
                {
                    Color emission = c * emissionIntensity;
                    emission.a = 1f;
                    mat.SetColor(EmissionColorID, emission);
                    mat.EnableKeyword("_EMISSION");
                }
            }
        }

        if (paletteHits == 0)
        {
            Debug.LogWarning($"[ElementPalette] '{gameObject.name}': 원소 {element} 적용됐지만 팔레트 프로퍼티 0개. " +
                             $"쉐이더가 PAMaskTint 호환이 아니거나 _originalColors 스냅샷이 비었을 가능성. " +
                             $"renderer수={renderers.Length}, mat수={(_instancedMaterials?.Length ?? 0)}");
        }
    }

    /// <summary>Shader의 모든 Color 프로퍼티 중 "팔레트"로 간주되는 것들의 ID 배열 반환.
    /// Emission/Outline 등 제외 키워드를 포함한 프로퍼티는 건너뜀.</summary>
    private static int[] GetPaletteIds(Shader shader)
    {
        if (_paletteIdCache.TryGetValue(shader, out var cached))
            return cached;

        var list = new List<int>();
        int count = shader.GetPropertyCount();
        for (int i = 0; i < count; i++)
        {
            if (shader.GetPropertyType(i) != ShaderPropertyType.Color) continue;
            string name = shader.GetPropertyName(i);
            if (string.IsNullOrEmpty(name)) continue;
            if (IsExcluded(name)) continue;
            list.Add(Shader.PropertyToID(name));
        }

        var arr = list.ToArray();
        _paletteIdCache[shader] = arr;
        return arr;
    }

    private static bool IsExcluded(string name)
    {
        for (int i = 0; i < ExcludedKeywords.Length; i++)
            if (name.IndexOf(ExcludedKeywords[i], System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
    }

    // ── Private Methods ──────────────────────────────────────────────
    private void EnsureInstancedMaterials()
    {
        if (_instancedMaterials != null) return;
        if (renderers == null) return;

        _instancedMaterials = new Material[renderers.Length][];
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null) continue;
            var mats = r.materials; // per-instance 복제
            _instancedMaterials[i] = mats;

            // 원본 색 스냅샷 — 이후 Apply는 매번 이 값에서 Lerp
            for (int j = 0; j < mats.Length; j++)
            {
                var mat = mats[j];
                if (mat == null || mat.shader == null) continue;
                if (_originalColors.ContainsKey(mat)) continue;

                int[] ids = GetPaletteIds(mat.shader);
                var snapshot = new Color[ids.Length];
                for (int k = 0; k < ids.Length; k++)
                    snapshot[k] = mat.GetColor(ids[k]);
                _originalColors[mat] = snapshot;

                if (mat.HasProperty(EmissionColorID))
                    _originalEmissions[mat] = mat.GetColor(EmissionColorID);
            }
        }
    }

    /// <summary>캐시된 원본 색을 모든 머티리얼 인스턴스에 되돌림. Apply 내부에서 매번 호출.</summary>
    private void RestoreOriginal()
    {
        if (_instancedMaterials == null) return;

        for (int i = 0; i < _instancedMaterials.Length; i++)
        {
            var mats = _instancedMaterials[i];
            if (mats == null) continue;
            for (int j = 0; j < mats.Length; j++)
            {
                var mat = mats[j];
                if (mat == null || mat.shader == null) continue;

                if (_originalColors.TryGetValue(mat, out var snapshot))
                {
                    int[] ids = GetPaletteIds(mat.shader);
                    int n = Mathf.Min(ids.Length, snapshot.Length);
                    for (int k = 0; k < n; k++)
                        mat.SetColor(ids[k], snapshot[k]);
                }

                if (_originalEmissions.TryGetValue(mat, out var origEmit) && mat.HasProperty(EmissionColorID))
                    mat.SetColor(EmissionColorID, origEmit);
            }
        }
    }
}
