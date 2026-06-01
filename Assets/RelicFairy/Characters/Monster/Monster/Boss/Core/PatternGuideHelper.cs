using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// VFX 없이 Unity Primitive로 공격 가이드를 시각화하는 정적 유틸리티.
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

    // ── 스폰 메서드 ────────────────────────────────────────────────

    /// <summary>구체 가이드 — 투사체, 폭발 충격점 등</summary>
    public static GameObject Sphere(Vector3 pos, float radius, Color color, float lifetime = -1f)
    {
        var go = Create(PrimitiveType.Sphere, "Guide_Sphere", color);
        go.transform.position   = pos;
        go.transform.localScale = Vector3.one * radius * 2f;
        AutoDestroy(go, lifetime);
        return go;
    }

    /// <summary>바닥 원형 가이드 — 범위 공격, AoE 표시</summary>
    public static GameObject Disc(Vector3 center, float radius, Color color, float lifetime = -1f)
    {
        var go = Create(PrimitiveType.Cylinder, "Guide_Disc", color);
        go.transform.position   = center + Vector3.up * 0.04f;
        go.transform.localScale = new Vector3(radius * 2f, 0.04f, radius * 2f);
        AutoDestroy(go, lifetime);
        return go;
    }

    /// <summary>직선 빔 가이드 — 레이, 투사체 경로 등</summary>
    public static GameObject Beam(Vector3 origin, Vector3 direction, float range, float width, Color color, float lifetime = -1f)
    {
        var go = Create(PrimitiveType.Cube, "Guide_Beam", color);
        direction = direction.normalized;
        go.transform.position   = origin + direction * (range * 0.5f);
        go.transform.rotation   = Quaternion.LookRotation(direction);
        go.transform.localScale = new Vector3(width, width, range);
        AutoDestroy(go, lifetime);
        return go;
    }

    // ── 조작 메서드 ────────────────────────────────────────────────

    /// <summary>가이드 오브젝트 색상 교체 (Telegraph → Active 전환 등)</summary>
    public static void SetColor(GameObject go, Color color)
    {
        if (go == null) return;
        var mr = go.GetComponent<MeshRenderer>();
        if (mr == null || mr.material == null) return;
        mr.material.color = color;
    }

    /// <summary>null 안전 파괴. ref로 전달해 자동 null 초기화.</summary>
    public static void SafeDestroy(ref GameObject go)
    {
        if (go == null) return;
        Object.Destroy(go);
        go = null;
    }

    // ── 내부 헬퍼 ─────────────────────────────────────────────────

    private static GameObject Create(PrimitiveType type, string name, Color color)
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

    private static void AutoDestroy(GameObject go, float lifetime)
    {
        if (lifetime > 0f) Object.Destroy(go, lifetime);
    }
}
}
