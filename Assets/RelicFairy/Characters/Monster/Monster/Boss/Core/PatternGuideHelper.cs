using UnityEngine;
using UnityEngine.Rendering;

namespace RelicFairy.Monster
{
/// <summary>
/// 공격 가이드(텔레그래프)를 바닥 데칼로 시각화하는 정적 유틸리티.
///
/// SetMaterials()로 SkillIndicator 머티리얼이 주입되면 Quad + Circle/Arrow 셰이더로 표시하고,
/// 주입되지 않으면 Unity Primitive(Unlit/Color)로 폴백한다. 호출 API는 두 경우 모두 동일하다.
///
/// 색상 규칙:
///   Telegraph (노란색) — 공격 예고, 선딜 구간 (리치는 쓰지 않는다 — 원칙 문서에서 노랑은 「안전」)
///   Active    (빨간색) — 공격 판정 활성 구간
///   Summon    (보라색) — 소환 범위
///
/// 예고 진행도: <see cref="SetProgress"/>(0→1)로 데칼을 채운다 — 크기를 줄이고 키우는 흉내 대신.
///
/// 사용 예시:
///   _guide = PatternGuideHelper.Disc(pos, radius, PatternGuideHelper.Telegraph);
///   PatternGuideHelper.SetColor(_guide, PatternGuideHelper.Active);
///   PatternGuideHelper.SafeDestroy(ref _guide);
/// </summary>
public static class PatternGuideHelper
{
    /// <summary>데칼을 바닥에서 띄우는 높이(m). 바닥 타일·풀 메시 윗면이 0.1 m 안팎 솟아 있어 0.04면 가이드가 타일에 가려졌다(09-28 실측).</summary>
    private const float DecalLift = 0.2f;

    public static readonly Color Telegraph = new Color(1.00f, 0.85f, 0.00f); // 노란색
    public static readonly Color Active    = new Color(1.00f, 0.10f, 0.10f); // 빨간색
    public static readonly Color Summon    = new Color(0.55f, 0.00f, 1.00f); // 보라색
    public static readonly Color Safe      = new Color(0.00f, 1.00f, 0.40f); // 초록색 — 안전지대
    public static readonly Color Seal      = new Color(0.00f, 0.50f, 1.00f); // 파란색 — 봉인 해골 마커
    public static readonly Color Breakable = new Color(0.20f, 0.90f, 0.95f); // 청록 — 공격해서 끊을·깨울 수 있음
    public static readonly Color PlayerSeal = new Color(1.00f, 0.80f, 0.25f); // 금색 — 플레이어의 봉인(판정 색으로 쓰지 않는다)
    public static readonly Color Reversed  = new Color(0.60f, 0.15f, 0.95f); // 보라 — 역류한 봉인

    private static readonly int SectorId = Shader.PropertyToID("_Sector");
    private static readonly int ColorId  = Shader.PropertyToID("_Color");
    private static readonly int MainTexStId = Shader.PropertyToID("_MainTex_ST");
    private static readonly int AngleId     = Shader.PropertyToID("_Angle");
    private static readonly int DurationId  = Shader.PropertyToID("_Duration");
    private static readonly int FlowColorId = Shader.PropertyToID("_FlowColor");
    private static readonly int FlowFadeId  = Shader.PropertyToID("_FlowFade");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

    // ── SkillIndicator 주입 머티리얼 ────────────────────────────────
    // null이면 프리미티브 폴백. 보스 초기화 시 SetMaterials()로 1회 주입한다.
    private static Material _circleSource;
    private static Material _arrowSource;
    private static Mesh     _quadMesh;

    // ── 1회 캐시 (보스전 GC 회피) ───────────────────────────────────
    // 셰이더 룩업·머티리얼 인스턴스를 매 스폰 생성하던 것을 static 1회 캐시로 대체.
    // 색/섹터는 공유 머티리얼 위에 MaterialPropertyBlock으로 per-renderer 지정 → 인스턴스 0개.
    private static Shader                _unlitShader;
    private static Material              _unlitShared; // 프리미티브 폴백 공유 머티리얼
    private static MaterialPropertyBlock _mpb;         // 재사용 — 매 스폰 alloc 회피

    /// <summary>지금 주입된 원 · 화살표 재질(없으면 null) — 잠깐 바꿔 쓰는 쪽이 끝나고 돌려놓을 때 읽는다(이벤트방 놀이).</summary>
    public static Material CircleMaterial => _circleSource;
    public static Material ArrowMaterial  => _arrowSource;

    /// <summary>SkillIndicator 머티리얼을 주입한다. null 전달 시 프리미티브 폴백으로 동작.</summary>
    public static void SetMaterials(Material circle, Material arrow)
    {
        _circleSource = circle;
        _arrowSource  = arrow;
    }

    // ── 스폰 메서드 ────────────────────────────────────────────────

    /// <summary>구체 가이드 — 투사체, 폭발 충격점 등. 공중 3D 점이므로 항상 프리미티브 구체.</summary>
    public static GameObject Sphere(Vector3 pos, float radius, Color color, float lifetime = -1f)
    {
        var go = CreatePrimitiveGuide(PrimitiveType.Sphere, "Guide_Sphere", color);
        go.transform.position   = pos;
        go.transform.localScale = Vector3.one * radius * 2f;
        AutoDestroy(go, lifetime);
        return go;
    }

    /// <summary>바닥 원형 가이드 — 범위 공격, AoE 표시</summary>
    public static GameObject Disc(Vector3 center, float radius, Color color, float lifetime = -1f)
    {
        if (_circleSource != null)
        {
            var go  = CreateDecal("Guide_Disc", _circleSource, color, setSector: true, sector: 0f); // 꽉 찬 원
            go.transform.position   = center + Vector3.up * DecalLift;
            go.transform.rotation   = Quaternion.Euler(90f, 0f, 0f); // Quad를 바닥에 눕힘
            go.transform.localScale = new Vector3(radius * 2f, radius * 2f, 1f);
            AutoDestroy(go, lifetime);
            return go;
        }

        var fallback = CreatePrimitiveGuide(PrimitiveType.Cylinder, "Guide_Disc", color);
        fallback.transform.position   = center + Vector3.up * DecalLift;   // 폴백도 바닥 잔해 위로(기사 아레나 09-28 실측)
        fallback.transform.localScale = new Vector3(radius * 2f, 0.04f, radius * 2f);
        AutoDestroy(fallback, lifetime);
        return fallback;
    }

    /// <summary>
    /// 바닥 부채꼴 가이드 — 방위 <paramref name="yaw"/>(도, +Z 기준 시계 방향)를 가운데로 <paramref name="angle"/>도 벌린다.
    /// 프리미티브 폴백으로는 부채꼴을 그릴 수 없어 원으로 대신한다.
    /// </summary>
    public static GameObject Sector(Vector3 center, float radius, float angle, float yaw, Color color, float lifetime = -1f)
    {
        if (_circleSource == null) return Disc(center, radius, color, lifetime);

        var go = CreateDecal("Guide_Sector", _circleSource, color, setSector: true, sector: 1f);
        go.transform.position   = center + Vector3.up * DecalLift;
        go.transform.rotation   = SectorRotation(yaw);
        go.transform.localScale = new Vector3(radius * 2f, radius * 2f, 1f);
        if (go.TryGetComponent<MeshRenderer>(out var mr))
        {
            mr.GetPropertyBlock(_mpb);
            _mpb.SetFloat(AngleId, angle);
            mr.SetPropertyBlock(_mpb);
        }
        AutoDestroy(go, lifetime);
        return go;
    }

    /// <summary>
    /// 부채꼴 데칼 회전 — 셰이더(taecg Circle)의 부채꼴 가운데는 쿼드 <b>−X</b>(uv u=0 쪽)다.
    /// Euler(90, r, 0)에서 쿼드 −X의 방위는 r − 90 → 방위 yaw를 가리키려면 r = yaw + 90.
    /// </summary>
    public static Quaternion SectorRotation(float yaw) => Quaternion.Euler(90f, yaw + 90f, 0f);

    /// <summary>직선 빔 가이드 — 레이, 투사체 경로 등</summary>
    public static GameObject Beam(Vector3 origin, Vector3 direction, float range, float width, Color color, float lifetime = -1f)
    {
        direction = direction.normalized;

        var go = _arrowSource != null && direction.sqrMagnitude > 0.001f
            ? CreateDecal("Guide_Beam", _arrowSource, color, setSector: false, sector: 0f)
            : CreatePrimitiveGuide(PrimitiveType.Cube, "Guide_Beam", color);
        PlaceBeam(go, origin, direction, range, width);
        AutoDestroy(go, lifetime);
        return go;
    }

    /// <summary><see cref="Beam"/>으로 만든 빔을 다시 놓는다 — 시전자를 따라 도는 직선 예고(브레스 등).</summary>
    public static void PlaceBeam(GameObject go, Vector3 origin, Vector3 direction, float range, float width)
    {
        if (go == null) return;
        direction.y = 0f;
        direction = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector3.forward;
        if (TryDecal(go, out _))
        {
            go.transform.position   = origin + direction * (range * 0.5f) + Vector3.up * DecalLift;
            // 내장 Quad의 앞면 노멀은 -Z다 — +Z를 아래로 눕혀야 앞면이 위를 본다(뒷면 컬링 셰이더라 반대면 안 보인다).
            // +Y(화살표 진행)는 빔 방향으로.
            go.transform.rotation   = Quaternion.LookRotation(Vector3.down, direction);
            go.transform.localScale = new Vector3(width, range, 1f);
            KeepArrowHeadAspect(go, range / Mathf.Max(0.01f, width));
            return;
        }
        // 폴백 — 납작한 판(예전엔 높이도 폭만큼이라 바닥에 선 벽처럼 보였다, 기사 09-28)
        go.transform.position   = origin + direction * (range * 0.5f) + Vector3.up * DecalLift;
        go.transform.rotation   = Quaternion.LookRotation(direction);
        go.transform.localScale = new Vector3(width, 0.04f, range);
    }

    /// <summary>세운 기둥 — 봉인석 등. <paramref name="basePos"/> = 바닥 중심. 시각 전용(판정 없음).</summary>
    public static GameObject Pillar(Vector3 basePos, float radius, float height, Color color)
    {
        var go = CreatePrimitiveGuide(PrimitiveType.Cylinder, "Guide_Pillar", color);
        go.transform.position   = basePos + Vector3.up * (height * 0.5f);
        go.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);   // 원기둥 원형은 높이 2
        return go;
    }

    /// <summary>두 점을 잇는 막대 — 사슬·빛줄기. 끝점이 움직이면 <see cref="PlaceLink"/>로 다시 놓는다. 시각 전용.</summary>
    public static GameObject Link(Vector3 from, Vector3 to, float width, Color color)
    {
        var go = CreatePrimitiveGuide(PrimitiveType.Cube, "Guide_Link", color);
        PlaceLink(go.transform, from, to, width);
        return go;
    }

    public static void PlaceLink(Transform link, Vector3 from, Vector3 to, float width)
    {
        if (link == null) return;
        Vector3 delta = to - from;
        float   len   = delta.magnitude;
        link.position   = from + delta * 0.5f;
        link.rotation   = len > 0.001f ? Quaternion.LookRotation(delta / len) : Quaternion.identity;
        link.localScale = new Vector3(width, width, len);
    }

    // ── 조작 메서드 ────────────────────────────────────────────────

    /// <summary>가이드 오브젝트 색상 교체 (Telegraph → Active 전환 등)</summary>
    public static void SetColor(GameObject go, Color color)
    {
        if (go == null) return;
        if (!go.TryGetComponent<MeshRenderer>(out var mr)) return;
        ApplyGuideColor(mr, color, setSector: false, sector: 0f); // 기존 섹터(꽉찬원 등) 보존하고 색만 교체
    }

    /// <summary>
    /// 예고 진행도 — 셰이더의 <c>_Duration</c>(0~1). 원·부채꼴은 중심에서 바깥으로, 화살표는 꼬리에서 머리로 차오른다.
    /// 채움 색은 <see cref="SetFlow"/>로 정한다(안 정하면 머티리얼 값). 프리미티브 폴백에는 효과가 없다.
    /// </summary>
    public static void SetProgress(GameObject go, float t)
    {
        if (!TryDecal(go, out var mr)) return;
        mr.GetPropertyBlock(_mpb);
        _mpb.SetFloat(DurationId, Mathf.Clamp01(t));
        mr.SetPropertyBlock(_mpb);
    }

    /// <summary>채움 색과 번짐 폭(<paramref name="fade"/> 1 = 중심까지 칠한 면, 작을수록 퍼져 나가는 얇은 띠).</summary>
    public static void SetFlow(GameObject go, Color color, float fade = 1f)
    {
        if (!TryDecal(go, out var mr)) return;
        mr.GetPropertyBlock(_mpb);
        _mpb.SetColor(FlowColorId, color);
        _mpb.SetFloat(FlowFadeId, Mathf.Clamp01(fade));
        mr.SetPropertyBlock(_mpb);
    }

    /// <summary>밝기 배율(셰이더 <c>_Intensity</c>, 기본 1) — 판정 직전 번쩍임에 쓴다.</summary>
    public static void SetIntensity(GameObject go, float intensity)
    {
        if (!TryDecal(go, out var mr)) return;
        mr.GetPropertyBlock(_mpb);
        _mpb.SetFloat(IntensityId, intensity);
        mr.SetPropertyBlock(_mpb);
    }

    /// <summary>예고 시작 — 채움 색을 정하고 비운 채로 둔다(이후 <see cref="SetProgress"/>로 채운다).</summary>
    public static GameObject Prepare(GameObject go, Color flow)
    {
        SetFlow(go, flow);
        SetProgress(go, 0f);
        return go;
    }

    /// <summary>
    /// 예고가 끝나 판정만 남은 순간 — 판정 색으로 꽉 채우고 밝힌다. 판정이 끝날 때까지 두었다가 <see cref="SafeDestroy"/>로 치운다.
    /// (예고가 끝나자마자 지우면 휘두르는 동안 바닥에 아무것도 없어 피할 자리가 안 보인다)
    /// </summary>
    public static void Arm(GameObject go, float intensity = 1.6f)
    {
        SetColor(go, Active);
        SetFlow(go, Active);
        SetProgress(go, 1f);
        SetIntensity(go, intensity);
    }

    private static bool TryDecal(GameObject go, out MeshRenderer mr)
    {
        mr = null;
        if (go == null || !go.TryGetComponent(out mr)) return false;
        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        return mr.sharedMaterial != null && mr.sharedMaterial != _unlitShared;
    }

    /// <summary>null 안전 파괴. ref로 전달해 자동 null 초기화.</summary>
    public static void SafeDestroy(ref GameObject go)
    {
        if (go == null) return;
        Object.Destroy(go);
        go = null;
    }

    // ── 내부 헬퍼 ─────────────────────────────────────────────────

    /// <summary>SkillIndicator 머티리얼을 입힌 Quad 데칼 GameObject 생성.
    /// 공유 머티리얼 + MPB로 색/섹터 지정 — 머티리얼 인스턴스 생성 없음.</summary>
    private static GameObject CreateDecal(string name, Material source, Color color, bool setSector, float sector)
    {
        var go = new GameObject(name);
        go.AddComponent<MeshFilter>().sharedMesh = QuadMesh;

        var mr = go.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows    = false;
        mr.sharedMaterial    = source; // 공유 — 인스턴스 미생성
        ApplyGuideColor(mr, color, setSector, sector);
        return go;
    }

    private static GameObject CreatePrimitiveGuide(PrimitiveType type, string name, Color color)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;

        // 물리 판정 비활성화 — 가이드는 시각 전용
        if (go.TryGetComponent<Collider>(out var col))
            col.enabled = false;

        // 공유 Unlit/Color 머티리얼 — 조명 무관하게 항상 보임. 색은 MPB로 per-renderer.
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = UnlitShared;
        ApplyGuideColor(mr, color, setSector: false, sector: 0f);

        return go;
    }

    /// <summary>공유 머티리얼 위에 MPB로 색(_Color)/섹터(_Sector)를 per-renderer 지정.
    /// setSector=false면 기존 섹터 값 보존(색만 교체).</summary>
    private static void ApplyGuideColor(MeshRenderer mr, Color color, bool setSector, float sector)
    {
        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        mr.GetPropertyBlock(_mpb); // 기존 블록 값 유지 (섹터 보존)
        _mpb.SetColor(ColorId, color);
        if (setSector) _mpb.SetFloat(SectorId, sector);
        mr.SetPropertyBlock(_mpb);
    }

    /// <summary>
    /// 화살표 텍스처(Clamp)의 머리를 빔 끝에 제 비율로 둔다 — 머리 아래는 텍스처 아래 가장자리가 늘어나 몸통으로 이어진다.
    /// 머티리얼 타일링 대신 렌더러별로 준다(빔마다 길이가 달라서).
    /// </summary>
    private static void KeepArrowHeadAspect(GameObject go, float aspect)
    {
        if (!go.TryGetComponent<MeshRenderer>(out var mr)) return;
        aspect = Mathf.Max(1f, aspect);
        mr.GetPropertyBlock(_mpb);
        _mpb.SetVector(MainTexStId, new Vector4(1f, aspect, 0f, 1f - aspect));
        mr.SetPropertyBlock(_mpb);
    }

    /// <summary>프리미티브 폴백용 공유 Unlit/Color 머티리얼 — Shader.Find/new Material 1회만.</summary>
    private static Material UnlitShared
    {
        get
        {
            if (_unlitShared == null)
            {
                if (_unlitShader == null) _unlitShader = Shader.Find("Unlit/Color");
                _unlitShared = new Material(_unlitShader);
            }
            return _unlitShared;
        }
    }

    /// <summary>내장 Quad 메시를 1회 생성·캐시한다.</summary>
    private static Mesh QuadMesh
    {
        get
        {
            if (_quadMesh == null)
            {
                var temp = GameObject.CreatePrimitive(PrimitiveType.Quad);
                _quadMesh = temp.GetComponent<MeshFilter>().sharedMesh;
                Object.Destroy(temp);
            }
            return _quadMesh;
        }
    }

    private static void AutoDestroy(GameObject go, float lifetime)
    {
        if (lifetime > 0f) Object.Destroy(go, lifetime);
    }
}
}
