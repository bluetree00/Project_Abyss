using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Polyart의 PAMaskTint 셰이더 등 컬러 팔레트 기반 셰이더의 모든 Color 프로퍼티를
/// 원소 색 하나로 통일해서 주입하는 컴포넌트.
///
/// [설계 메모] (원본 sharedMaterial, ElementType) 조합별로 틴트된 머티리얼 인스턴스를
/// static 캐시에 보관하고, Apply 시 renderer.sharedMaterials를 캐시된 머티리얼로 스왑한다.
/// - 런타임 머티리얼 복제(renderer.materials)가 발생하지 않음 → GC 스파이크 제거
/// - 같은 (원본, 원소) 조합을 쓰는 몬스터끼리 머티리얼을 공유 → SRP Batcher 배칭 극대화
/// - URP+ShaderGraph 환경에서 MPB 전환보다 유리 (MPB는 SRP Batcher를 깬다)
///
/// 원소별 쉐이더 수치(색/tint 강도/emission)는 ElementPaletteSO로 외부화.
/// AppBootstrapper 등 진입점에서 SetPaletteSO(so) 호출로 전역 주입. 미주입 시 내장 기본값 사용.
///
/// ShaderGraph로 만든 셰이더는 프로퍼티 ReferenceName이 랜덤 ID(예: Color_E64BA0E)로
/// 저장되는 경우가 많아 "_Color01~09" 같은 고정 이름으로는 매칭 미스 발생.
/// 따라서 런타임에 material의 shader를 스캔해 Color 프로퍼티를 전부 찾아 주입한다.
/// Emission/Outline/Specular 같은 비-팔레트 색은 제외.
/// </summary>
public class ElementNativePalette : MonoBehaviour
{
    // ── Constants ───────────────────────────────────────────────────
    /// <summary>Rim 색 = 원소 tintColor × 이 배율. HDR 강도 조정용. 모든 프리팹에서 동일 값을 사용해
    /// static _rimMatCache가 일관된 머티리얼을 공유할 수 있도록 const로 고정.</summary>
    private const float RimColorMultiplier = 1.5f;

    /// <summary>팔레트 주입 대상에서 제외할 프로퍼티 이름의 부분 문자열.
    /// Emission/Outline/Specular/Tint 같은 색은 건드리지 않는다.</summary>
    private static readonly string[] ExcludedKeywords =
    {
        "Emission", "Outline", "Specular", "SpecColor", "Tint", "Rim", "Highlight",
    };

    // ── Static ──────────────────────────────────────────────────────
    // 셰이더별 팔레트 프로퍼티 ID 캐시 (shader별 1회만 계산)
    private static readonly Dictionary<Shader, int[]> _paletteIdCache = new();

    // (원본 sharedMaterial, 원소) 조합별 틴트된 머티리얼 인스턴스 캐시.
    // 동일 조합을 쓰는 몬스터가 많을수록 머티리얼 수가 줄어 SRP Batcher 배칭이 좋아진다.
    private static readonly Dictionary<MatElementKey, Material> _tintedMatCache = new();

    // 원소별 Rim 오버레이 머티리얼 캐시 (전 몬스터 공유 — 5개 상한).
    private static readonly Dictionary<ElementType, Material> _rimMatCache = new();
    private static Shader _rimShader;

    private static readonly int EmissionColorID = Shader.PropertyToID("_EmissionColor");
    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
    private static readonly int ElementTypeID = Shader.PropertyToID("_ElementType");

    // 전역 원소 팔레트 SO (Bootstrapper에서 SetPaletteSO로 주입). null이면 내장 기본값 사용.
    private static ElementPaletteSO _paletteSO;

    // 프리팹의 sharedMaterial이 비었거나 missing일 때 사용할 최종 안전망 머티리얼.
    // URP Lit으로 만들어 정상 렌더링을 보장 (비어 있는 슬롯으로 인한 분홍색/보라색 잔상 방지).
    private static Material _fallbackMaterial;

    // ── [SerializeField] ────────────────────────────────────────────
    [Header("Renderers (비워두면 자동 탐색)")]
    [SerializeField] private Renderer[] renderers;

    [Header("Rim 오버레이 (Fresnel 원소 발광)")]
    [Tooltip("Apply 시 sharedMaterials 마지막 슬롯에 원소별 Rim 머티리얼을 자동 추가한다. submesh 1개 케이스에만 적용.")]
    [SerializeField] private bool useRim = true;

    // ── Private ─────────────────────────────────────────────────────
    // 몬스터별 원본 sharedMaterials 스냅샷 — 풀 재사용/원소 변경 시에도 참조 안정.
    private Material[][] _originalSharedMaterials;
    // renderer.sharedMaterials setter에 전달할 작업용 배열 (재사용하여 GC 회피).
    private Material[][] _workingMaterials;
    // Rim 포함 배열 (원본 길이 + 1). submesh 1개 Renderer에서만 사용.
    private Material[][] _workingMaterialsWithRim;

    // ── Lifecycle ───────────────────────────────────────────────────
    private void Awake()
    {
        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<Renderer>(true);

        // Awake가 디졸브 연출 중에 실행되면 sharedMaterial이 임시 "DissolveMaterial"일 수 있다.
        // 그 상태를 "원본"으로 고정하면 캐시 전체가 오염되므로, 임시 머티리얼 감지 시 거부하고
        // 디졸브 종료까지 프레임 yield로 재시도한다.
        if (!TryCaptureAndPrewarm())
            RetryCaptureAsync().Forget();
    }

    /// <summary>풀 반환(OnDisable) 시 sharedMaterial을 캡처된 원본(PAMaskTint)으로 되돌린다.
    /// 다음 스폰의 디졸브가 이전 원소 틴트 머티리얼이 아닌 원본 기반으로 시작되도록 보장.
    /// OnDestroy 경로에서도 안전하게 호출됨 — 이미 destroy된 renderer는 스킵.</summary>
    private void OnDisable()
    {
        RestoreOriginalMaterials();
    }

    // ── Public Methods ───────────────────────────────────────────────
    /// <summary>전역 원소 팔레트 SO 주입. AppBootstrapper/GameRunBootstrapper에서 1회 호출.
    /// SO가 바뀌면 기존 틴트 캐시가 오래된 색을 가지므로 자동 invalidate.</summary>
    public static void SetPaletteSO(ElementPaletteSO so)
    {
        if (ReferenceEquals(_paletteSO, so)) return;
        _paletteSO = so;
        InvalidateTintedCache();
    }

    /// <summary>원소에 대응하는 공유 머티리얼로 각 renderer를 스왑.
    /// 조합별로 머티리얼을 캐싱하므로 이미 생성된 조합은 재할당만 수행(복제 없음).
    /// None 원소는 원본 sharedMaterial로 복원.</summary>
    public void Apply(ElementType element)
    {
        // renderers 참조만 보강. 원본 재캡처는 금지 — DissolveEffect 등이 일시 교체해둔 material을
        // 실수로 "원본"으로 기록해 캐시가 DissolveMaterial 기반으로 만들어지는 사고를 막는다.
        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<Renderer>(true);

        if (_originalSharedMaterials == null)
        {
            Debug.LogWarning($"[ElementPalette] '{gameObject.name}': 원본 sharedMaterials 미포착 (Awake 누락?) — Apply 스킵. 풀 재사용 타이밍 문제 가능성.");
            return;
        }

        bool wantsTint = element.IsValid();

        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null) continue;

            var originals = _originalSharedMaterials[i];
            if (originals == null) continue;

            // Rim 오버레이는 Multi-material Multi-submesh 규칙 상 submesh 1개 Renderer에만 안전하게 적용.
            // 원본 material이 2개 이상이면 Rim은 마지막 submesh에만 덮히므로 Rim 비적용.
            // None 원소도 Rim 쉐이더를 적용한다 (색상은 원본 유지, Rim만 추가).
            bool applyRim = useRim && originals.Length == 1;

            var working = applyRim ? _workingMaterialsWithRim[i] : _workingMaterials[i];
            if (working == null) continue;

            for (int j = 0; j < originals.Length; j++)
            {
                var orig = originals[j];
                // CaptureOriginalSharedMaterials에서 null은 이미 fallback으로 치환됐지만,
                // 만약 런타임 중 shader가 언로드되는 엣지 케이스에도 안전하도록 한 번 더 방어.
                if (orig == null || orig.shader == null)
                {
                    working[j] = GetFallbackMaterial();
                    continue;
                }
                if (!wantsTint)
                {
                    working[j] = orig;
                    continue;
                }
                working[j] = GetOrCreateTintedMaterial(orig, element);
            }

            if (applyRim)
            {
                // 마지막 슬롯에 원소 Rim 머티리얼 — Unity의 multi-material 규칙으로 동일 submesh가 중첩 렌더됨.
                var rim = GetOrCreateRimMaterial(element);
                working[originals.Length] = rim != null ? rim : working[originals.Length - 1];
            }

            r.sharedMaterials = working;
        }
    }

    // ── Private Methods ──────────────────────────────────────────────
    /// <summary>각 렌더러의 sharedMaterials를 Awake에서 캡처한 원본(_originalSharedMaterials)으로 복원.
    /// 풀 반환 시 호출되어 "이전 스폰의 원소 틴트 머티리얼" 상태를 제거하고 프리팹 원본으로 돌려놓는다.</summary>
    private void RestoreOriginalMaterials()
    {
        if (renderers == null || _originalSharedMaterials == null) return;

        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null) continue;
            var orig = _originalSharedMaterials[i];
            if (orig == null) continue;
            r.sharedMaterials = orig;
        }
    }

    /// <summary>캡처 + 프리워밍. 현재 sharedMaterial이 디졸브 임시 머티리얼이면 실패(false) 반환하고
    /// 호출부가 재시도하도록 위임. 성공 시에만 _originalSharedMaterials를 확정하고 프리워밍 수행.</summary>
    private bool TryCaptureAndPrewarm()
    {
        if (renderers == null) return false;

        // 임시(디졸브) 머티리얼 감지 — 하나라도 "Dissolve"를 포함하면 아직 원본 상태가 아님
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null) continue;
            var src = r.sharedMaterials;
            for (int j = 0; j < src.Length; j++)
            {
                var m = src[j];
                if (m != null && !string.IsNullOrEmpty(m.name) &&
                    m.name.IndexOf("Dissolve", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return false; // 디졸브 진행 중 — 캡처 거부
                }
            }
        }

        CaptureOriginalSharedMaterials();
        PrewarmAllElements();
        return true;
    }

    /// <summary>TryCaptureAndPrewarm이 임시 머티리얼을 감지해 거부한 경우 디졸브 종료까지 프레임 yield로 재시도.
    /// 몬스터 수명/파괴 시 자동 취소.</summary>
    private async UniTaskVoid RetryCaptureAsync()
    {
        while (this != null && gameObject != null)
        {
            try { await UniTask.Yield(destroyCancellationToken); }
            catch (System.OperationCanceledException) { return; }

            if (TryCaptureAndPrewarm()) return;
        }
    }

    /// <summary>원본 sharedMaterials를 복사해 보관. null/missing 슬롯은 fallback material로 치환.
    /// 작업용 배열 2벌(Rim 없는 버전 + Rim 포함 버전)을 함께 준비해 런타임 재할당을 피한다.</summary>
    private void CaptureOriginalSharedMaterials()
    {
        if (renderers == null) return;

        _originalSharedMaterials = new Material[renderers.Length][];
        _workingMaterials = new Material[renderers.Length][];
        _workingMaterialsWithRim = new Material[renderers.Length][];

        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null) continue;

            var src = r.sharedMaterials;
            var copy = new Material[src.Length];
            for (int j = 0; j < src.Length; j++)
            {
                var m = src[j];
                if (m == null || m.shader == null)
                {
                    Debug.LogWarning($"[ElementPalette] '{gameObject.name}': Renderer[{i}].sharedMaterial[{j}] 누락 — fallback 머티리얼로 대체");
                    copy[j] = GetFallbackMaterial();
                }
                else
                {
                    copy[j] = m;
                }
            }

            _originalSharedMaterials[i] = copy;
            _workingMaterials[i] = new Material[src.Length];
            _workingMaterialsWithRim[i] = new Material[src.Length + 1];
        }
    }

    /// <summary>이 몬스터의 모든 원본 material × 유효 원소 5종 조합을 미리 생성해 캐시에 적재.
    /// Rim 머티리얼은 None 포함 6종을 전역 static 캐시에 미리 적재 (전 몬스터 공유이므로 첫 몬스터에서 1회면 충분).
    /// 풀 첫 생성 시점(Awake)에 1회 수행 → 런타임 Apply는 순수 바인딩만 수행.</summary>
    private void PrewarmAllElements()
    {
        if (_originalSharedMaterials == null) return;

        for (int i = 0; i < _originalSharedMaterials.Length; i++)
        {
            var origs = _originalSharedMaterials[i];
            if (origs == null) continue;

            for (int j = 0; j < origs.Length; j++)
            {
                var orig = origs[j];
                if (orig == null || orig.shader == null) continue;

                for (int e = 0; e < ElementTypeUtil.Count; e++)
                    GetOrCreateTintedMaterial(orig, (ElementType)e);
            }
        }

        if (useRim)
        {
            GetOrCreateRimMaterial(ElementType.None); // None도 Rim 프리워밍
            for (int e = 0; e < ElementTypeUtil.Count; e++)
                GetOrCreateRimMaterial((ElementType)e);
        }
    }

    /// <summary>(원본 material, 원소) 조합 키로 캐시된 틴트 머티리얼 반환. 없으면 SO 또는 기본값으로 생성·캐시.
    ///
    /// [색상 방식] Luminance-based Colorize — 원본의 명암(luminance)만 유지하고 색(Hue/Sat)을 원소색으로 교체.
    /// 단순 Lerp는 원본이 진한 유채색(녹색 Slime 등)이면 반대색(파랑 Water) 적용이 약해지는 문제가 있다.
    /// 이 방식은 원본 색을 "비우고" 원소색으로 다시 칠하므로 어떤 원본이든 원소 구분이 명확해진다.
    /// tintStrength 의미: 0 = 원본 유지, 1 = 완전 원소화 (명암만 유지, 색은 완전 교체).</summary>
    private static Material GetOrCreateTintedMaterial(Material original, ElementType element)
    {
        var key = new MatElementKey(original, element);
        if (_tintedMatCache.TryGetValue(key, out var cached) && cached != null)
            return cached;

        var entry = ResolveEntry(element);

        var mat = new Material(original) { name = $"{original.name}__{element}" };
        int[] ids = GetPaletteIds(mat.shader);
        for (int k = 0; k < ids.Length; k++)
        {
            Color orig = original.GetColor(ids[k]);

            // 1) 원본 색의 luminance만 추출 (sRGB 가중치 — 눈의 민감도 반영)
            float lum = orig.r * 0.2126f + orig.g * 0.7152f + orig.b * 0.0722f;

            // 2) grayscale × 원소색 = 명암 유지한 원소화 색
            Color elemized = new Color(
                entry.tintColor.r * lum,
                entry.tintColor.g * lum,
                entry.tintColor.b * lum,
                orig.a);

            // 3) tintStrength로 강도 조절 (0=원본, 1=완전 원소화)
            Color blended = Color.Lerp(orig, elemized, entry.tintStrength);
            blended.a = orig.a; // 알파는 원본 보존
            mat.SetColor(ids[k], blended);
        }

        if (entry.applyEmission && mat.HasProperty(EmissionColorID))
        {
            Color emission = entry.useCustomEmissionColor
                ? entry.customEmissionColor
                : entry.tintColor * entry.emissionIntensity;
            emission.a = 1f;
            mat.SetColor(EmissionColorID, emission);
            mat.EnableKeyword("_EMISSION");
        }

        _tintedMatCache[key] = mat;
        return mat;
    }

    /// <summary>원소별 Rim 오버레이 머티리얼을 static 캐시에서 조회. 없으면 쉐이더+색 주입해 생성.
    /// 전 몬스터가 동일 원소에 대해 같은 Rim 머티리얼을 공유 → SRP Batcher 효율적 배칭.</summary>
    private Material GetOrCreateRimMaterial(ElementType element)
    {
        if (_rimMatCache.TryGetValue(element, out var cached) && cached != null)
            return cached;

        if (_rimShader == null)
        {
            _rimShader = Shader.Find("Abyss/Elements/ElementRim");
            if (_rimShader == null)
            {
                Debug.LogError("[ElementPalette] Rim 쉐이더 'Abyss/Elements/ElementRim' 로드 실패 — Rim 오버레이 스킵");
                return null;
            }
        }

        var entry = ResolveEntry(element);
        var mat = new Material(_rimShader) { name = $"ElementRim__{element}" };

        // HDR 강도를 위해 tintColor × RimColorMultiplier. alpha는 1로 고정해 Fresnel 투명도만 작용.
        Color rimColor = entry.tintColor * RimColorMultiplier;
        rimColor.a = 1f;
        mat.SetColor(BaseColorID, rimColor);
        // 쉐이더 내부 원소별 애니메이션 분기 식별자 — 쉐이더의 ElementModulation이 사용.
        mat.SetFloat(ElementTypeID, (int)element);

        _rimMatCache[element] = mat;
        return mat;
    }

    /// <summary>주입된 SO에서 엔트리 조회. 없으면 내장 기본값으로 폴백.</summary>
    private static ElementPaletteSO.Entry ResolveEntry(ElementType element)
    {
        if (_paletteSO != null && _paletteSO.TryGet(element, out var entry))
            return entry;
        return DefaultEntry(element);
    }

    /// <summary>SO 미주입 시 사용되는 내장 기본값. ElementPaletteSO.Reset과 수치 일치.</summary>
    private static ElementPaletteSO.Entry DefaultEntry(ElementType element)
    {
        switch (element)
        {
            case ElementType.None:      return new ElementPaletteSO.Entry { element = element, tintColor = new Color(0.82f, 0.82f, 0.82f, 1f), tintStrength = 0f, applyEmission = false, emissionIntensity = 0f };
            case ElementType.Lightning: return new ElementPaletteSO.Entry { element = element, tintColor = new Color(1.00f, 0.92f, 0.23f, 1f), tintStrength = 0.7f, applyEmission = true, emissionIntensity = 0.6f };
            case ElementType.Water:     return new ElementPaletteSO.Entry { element = element, tintColor = new Color(0.13f, 0.59f, 0.95f, 1f), tintStrength = 0.7f, applyEmission = true, emissionIntensity = 0.6f };
            case ElementType.Fire:      return new ElementPaletteSO.Entry { element = element, tintColor = new Color(0.96f, 0.26f, 0.21f, 1f), tintStrength = 0.7f, applyEmission = true, emissionIntensity = 0.6f };
            case ElementType.Grass:     return new ElementPaletteSO.Entry { element = element, tintColor = new Color(0.30f, 0.69f, 0.31f, 1f), tintStrength = 0.7f, applyEmission = true, emissionIntensity = 0.6f };
            case ElementType.Earth:     return new ElementPaletteSO.Entry { element = element, tintColor = new Color(0.55f, 0.43f, 0.39f, 1f), tintStrength = 0.7f, applyEmission = true, emissionIntensity = 0.6f };
            default:                    return new ElementPaletteSO.Entry { element = element, tintColor = Color.white, tintStrength = 0f, applyEmission = false, emissionIntensity = 0f };
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

    /// <summary>프리팹 sharedMaterial 누락 시 사용할 최종 안전망 머티리얼을 지연 생성.
    /// URP Lit으로 정상 렌더링을 보장 — 쉐이더 누락 시에는 Unity 내장 Standard로 추가 폴백.</summary>
    private static Material GetFallbackMaterial()
    {
        if (_fallbackMaterial != null) return _fallbackMaterial;

        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (shader == null)
        {
            Debug.LogError("[ElementPalette] fallback shader(Universal Render Pipeline/Lit, Standard) 찾기 실패 — 머티리얼 보장 불가");
            return null;
        }

        _fallbackMaterial = new Material(shader) { name = "ElementPalette_Fallback" };
        return _fallbackMaterial;
    }

    /// <summary>틴트 머티리얼 + Rim 머티리얼 캐시 invalidate. Destroy는 호출하지 않는다 —
    /// 현재 렌더러가 바인딩 중인 머티리얼을 즉시 Destroy하면 렌더러가 missing material(분홍색)로 보이므로,
    /// 참조 해제를 렌더러 스왑에 맡긴다. 옛 머티리얼은 플레이 종료/도메인 리로드 시 Unity가 일괄 수거한다.</summary>
    private static void InvalidateTintedCache()
    {
        _tintedMatCache.Clear();
        _rimMatCache.Clear();
    }

    /// <summary>도메인 리로드 시 static 캐시 초기화 — 에디터 플레이 재진입 시 이전 머티리얼 누수 방지.
    /// "Reload Domain" 옵션이 꺼져 있을 때 static 필드가 유지되는 Unity 특성에 대한 방어.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        // 도메인 리로드 직후엔 씬의 렌더러가 아직 참조 중일 가능성이 있으므로 Destroy하지 않는다.
        // 캐시 참조만 비워 다음 Apply에서 신규 머티리얼이 재생성되도록 한다.
        // Unity 측의 Material UnloadUnusedAssets로 자연 수거됨.
        _tintedMatCache.Clear();
        _rimMatCache.Clear();
        _paletteIdCache.Clear();
        _paletteSO = null;
        _fallbackMaterial = null;
        _rimShader = null;
    }

    // ── Nested Types ─────────────────────────────────────────────────
    /// <summary>_tintedMatCache의 키. Material 참조 + ElementType 조합.</summary>
    private readonly struct MatElementKey : System.IEquatable<MatElementKey>
    {
        private readonly int _matId;
        private readonly ElementType _element;

        public MatElementKey(Material mat, ElementType element)
        {
            _matId = mat != null ? mat.GetInstanceID() : 0;
            _element = element;
        }

        public bool Equals(MatElementKey other) => _matId == other._matId && _element == other._element;
        public override bool Equals(object obj) => obj is MatElementKey k && Equals(k);
        public override int GetHashCode() => (_matId * 397) ^ (int)_element;
    }
}
