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
///   Telegraph (노란색) — 공격 예고, 선딜 구간
///   Active    (빨간색) — 공격 판정 활성 구간
///   Summon    (보라색) — 소환 범위
///
/// 사용 예시:
///   _guide = PatternGuideHelper.Disc(pos, radius, PatternGuideHelper.Telegraph);
///   PatternGuideHelper.SetColor(_guide, PatternGuideHelper.Active);
///   PatternGuideHelper.SafeDestroy(ref _guide);
/// </summary>
public static class PatternGuideHelper
{
    public static readonly Color Telegraph = new Color(1.00f, 0.85f, 0.00f); // 노란색
    public static readonly Color Active    = new Color(1.00f, 0.10f, 0.10f); // 빨간색
    public static readonly Color Summon    = new Color(0.55f, 0.00f, 1.00f); // 보라색
    public static readonly Color Safe      = new Color(0.00f, 1.00f, 0.40f); // 초록색 — 안전지대
    public static readonly Color Seal      = new Color(0.00f, 0.50f, 1.00f); // 파란색 — 봉인 해골 마커

    private static readonly int SectorId = Shader.PropertyToID("_Sector");

    // ── SkillIndicator 주입 머티리얼 ────────────────────────────────
    // null이면 프리미티브 폴백. 보스 초기화 시 SetMaterials()로 1회 주입한다.
    private static Material _circleSource;
    private static Material _arrowSource;
    private static Mesh     _quadMesh;

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
            var go  = CreateDecal("Guide_Disc", _circleSource, color);
            go.transform.position   = center + Vector3.up * 0.04f;
            go.transform.rotation   = Quaternion.Euler(90f, 0f, 0f); // Quad를 바닥에 눕힘
            go.transform.localScale = new Vector3(radius * 2f, radius * 2f, 1f);
            go.GetComponent<MeshRenderer>().material.SetFloat(SectorId, 0f); // 꽉 찬 원
            AutoDestroy(go, lifetime);
            return go;
        }

        var fallback = CreatePrimitiveGuide(PrimitiveType.Cylinder, "Guide_Disc", color);
        fallback.transform.position   = center + Vector3.up * 0.04f;
        fallback.transform.localScale = new Vector3(radius * 2f, 0.04f, radius * 2f);
        AutoDestroy(fallback, lifetime);
        return fallback;
    }

    /// <summary>직선 빔 가이드 — 레이, 투사체 경로 등</summary>
    public static GameObject Beam(Vector3 origin, Vector3 direction, float range, float width, Color color, float lifetime = -1f)
    {
        direction = direction.normalized;

        if (_arrowSource != null && direction.sqrMagnitude > 0.001f)
        {
            var go = CreateDecal("Guide_Beam", _arrowSource, color);
            go.transform.position   = origin + direction * (range * 0.5f) + Vector3.up * 0.04f;
            // Quad 노멀(+Z)을 위로, +Y(화살표 진행)를 빔 방향으로
            go.transform.rotation   = Quaternion.LookRotation(Vector3.up, direction);
            go.transform.localScale = new Vector3(width, range, 1f);
            AutoDestroy(go, lifetime);
            return go;
        }

        var fallback = CreatePrimitiveGuide(PrimitiveType.Cube, "Guide_Beam", color);
        fallback.transform.position   = origin + direction * (range * 0.5f);
        fallback.transform.rotation   = direction.sqrMagnitude > 0.001f
            ? Quaternion.LookRotation(direction)
            : Quaternion.identity;
        fallback.transform.localScale = new Vector3(width, width, range);
        AutoDestroy(fallback, lifetime);
        return fallback;
    }

    // ── 조작 메서드 ────────────────────────────────────────────────

    /// <summary>가이드 오브젝트 색상 교체 (Telegraph → Active 전환 등)</summary>
    public static void SetColor(GameObject go, Color color)
    {
        if (go == null) return;
        var mr = go.GetComponent<MeshRenderer>();
        if (mr == null || mr.material == null) return;
        mr.material.color = color; // _Color — Unlit/Color · SkillIndicator 공통
    }

    /// <summary>null 안전 파괴. ref로 전달해 자동 null 초기화.</summary>
    public static void SafeDestroy(ref GameObject go)
    {
        if (go == null) return;
        Object.Destroy(go);
        go = null;
    }

    // ── 내부 헬퍼 ─────────────────────────────────────────────────

    /// <summary>SkillIndicator 머티리얼을 입힌 Quad 데칼 GameObject 생성.</summary>
    private static GameObject CreateDecal(string name, Material source, Color color)
    {
        var go = new GameObject(name);
        go.AddComponent<MeshFilter>().sharedMesh = QuadMesh;

        var mr = go.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows    = false;
        mr.sharedMaterial    = source;
        mr.material.color    = color; // 렌더러 소유 인스턴스 생성 후 _Color 지정 (GO 파괴 시 자동 해제)
        return go;
    }

    private static GameObject CreatePrimitiveGuide(PrimitiveType type, string name, Color color)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;

        // 물리 판정 비활성화 — 가이드는 시각 전용
        if (go.TryGetComponent<Collider>(out var col))
            col.enabled = false;

        // 새 인스턴스 매터리얼 — Unlit/Color로 조명 무관하게 항상 보임
        var mat = new Material(Shader.Find("Unlit/Color")) { color = color };
        go.GetComponent<MeshRenderer>().material = mat;

        return go;
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
