using System.Collections.Generic;
using RelicFairy.Monster;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 보스 예고 가이드(바닥 표시)를 씬에서 읽어 「이 자리가 가이드 안인가 · 가이드가 뜬 지 몇 초인가」를 답한다(에디터 전용 · 검증 도구).
/// 읽는 것: <see cref="PatternGuideHelper"/>의 Guide_Disc · Guide_Sector · Guide_Beam · Guide_Sphere(씬 루트 또는 그 자식 —
/// 화룡 WarningZone은 자식에 둔다)와 <see cref="MonsterGroundWarning"/>(원 · 채움 원 · 직사각형 · 격자 칸 — LineRenderer 꼭짓점).
/// 모양은 가이드가 그려진 그대로(트랜스폼 · 부채꼴 각도 MPB)에서 다시 계산한다 — 게임 코드는 건드리지 않는다.
/// 안전 표시(초록 안전지대 · 흰 안전 원 · 봉인 마커 파랑 · 깰 수 있는 청록 · 플레이어 봉인 금빛)는 <see cref="Shape.Safe"/>로 따로 둔다 — 피할 곳이 아니라 갈 곳.
/// </summary>
public static class BossGuideShapes
{
    // ── Constants ──────────────────────────────────────────────
    private const int MaxDepth = 2;

    public enum Kind { Circle, Sector, Rect, Poly }

    public struct Shape
    {
        public Kind      Kind;
        public string    Name;
        public Vector3   Center;                // 원 · 부채꼴 중심 · 직사각형 가운데
        public float     Radius;                // 원 · 부채꼴
        public float     HalfAngle;             // 부채꼴(도)
        public Vector3   Axis;                  // 부채꼴 가운데 방향 · 직사각형 길이 방향(수평 단위 벡터)
        public float     HalfLength, HalfWidth; // 직사각형
        public Vector3[] Poly;                  // 다각형(월드, 높이 무시)
        public float     Age;                   // 처음 본 뒤 경과(초, Time.time)
        public bool      Safe;                  // 안전 표시(판정 아님)
    }

    // ── Static ─────────────────────────────────────────────────
    private static readonly Dictionary<int, float> s_firstSeen = new();
    private static readonly List<Shape>            s_shapes    = new();
    private static readonly List<GameObject>       s_roots     = new();
    private static readonly MaterialPropertyBlock  s_mpb       = new();
    private static readonly int AngleId  = Shader.PropertyToID("_Angle");
    private static readonly int SectorId = Shader.PropertyToID("_Sector");
    private static readonly int ColorId  = Shader.PropertyToID("_Color");
    // 판정이 아닌 표시 색 — PatternGuideHelper.Safe · Seal · Breakable · PlayerSeal, 리치 · 숲 SafeWhite, 기사 SafeWhite(흰색)
    private static readonly Color[] SafeColors =
    {
        PatternGuideHelper.Safe, PatternGuideHelper.Seal, PatternGuideHelper.Breakable, PatternGuideHelper.PlayerSeal,
        new Color(0.95f, 0.95f, 0.90f), Color.white,
    };
    private static readonly Vector3[] s_line = new Vector3[64];
    private static Scene s_ddol;

    private static Scene DdolScene()
    {
        if (s_ddol.IsValid() || !Application.isPlaying) return s_ddol;
        var probe = new GameObject("~BossGuideShapesProbe");
        Object.DontDestroyOnLoad(probe);
        s_ddol = probe.scene;
        Object.Destroy(probe);
        return s_ddol;
    }

    // ── Public Methods ─────────────────────────────────────────
    public static void Reset()
    {
        s_firstSeen.Clear();
        s_ddol = default;
    }

    /// <summary>지금 씬의 가이드를 다시 읽는다. 돌려준 목록은 다음 호출 때 바뀐다.</summary>
    public static List<Shape> Scan()
    {
        s_shapes.Clear();
        float now = Time.time;
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;
            scene.GetRootGameObjects(s_roots);
            foreach (var go in s_roots)
                if (go.activeInHierarchy) Visit(go.transform, 0, now);
        }
        // 화룡 예고 타일 풀(@QuadTilePool)은 DontDestroyOnLoad — 그 씬은 SceneManager 목록에 없다
        var ddol = DdolScene();
        if (ddol.IsValid() && ddol.isLoaded)
        {
            ddol.GetRootGameObjects(s_roots);
            foreach (var go in s_roots)
                if (go.activeInHierarchy && go.name == "@QuadTilePool") Visit(go.transform, 0, now);
        }
        foreach (var w in Object.FindObjectsByType<MonsterGroundWarning>(FindObjectsSortMode.None))
            if (w != null && w.isActiveAndEnabled) AddGroundWarning(w, now);
        return s_shapes;
    }

    /// <summary>점 <paramref name="p"/>가 가이드 안인가(수평 · <paramref name="margin"/> m 여유 = 플레이어 몸 반경).</summary>
    public static bool Contains(in Shape s, Vector3 p, float margin)
    {
        Vector3 d = Flat(p - s.Center);
        switch (s.Kind)
        {
            case Kind.Circle:
                return d.magnitude <= s.Radius + margin;
            case Kind.Sector:
            {
                float m = d.magnitude;
                if (m <= margin) return true;
                if (m > s.Radius + margin) return false;
                float slack = Mathf.Asin(Mathf.Min(1f, margin / m)) * Mathf.Rad2Deg;
                return Vector3.Angle(s.Axis, d) <= s.HalfAngle + slack;
            }
            case Kind.Rect:
            {
                Vector3 right = Vector3.Cross(Vector3.up, s.Axis);
                return Mathf.Abs(Vector3.Dot(d, s.Axis)) <= s.HalfLength + margin
                    && Mathf.Abs(Vector3.Dot(d, right))  <= s.HalfWidth  + margin;
            }
            default:
                return InsidePoly(s.Poly, p, margin);
        }
    }

    /// <summary>점을 덮는 (판정) 가이드 중 가장 오래 떠 있던 것. 없으면 false.</summary>
    public static bool Covering(List<Shape> shapes, Vector3 p, float margin, out Shape best)
    {
        best = default;
        bool found = false;
        foreach (var s in shapes)
        {
            if (s.Safe || !Contains(s, p, margin)) continue;
            if (!found || s.Age > best.Age) { best = s; found = true; }
        }
        return found;
    }

    /// <summary><paramref name="minAge"/>초 넘게 떠 있던 가이드 중 하나라도 점을 덮는가 — 플레이어가 「본」 가이드만 피한다.</summary>
    public static bool InsideAny(List<Shape> shapes, Vector3 p, float margin, float minAge)
    {
        foreach (var s in shapes)
            if (!s.Safe && s.Age >= minAge && Contains(s, p, margin)) return true;
        return false;
    }

    /// <summary>
    /// <paramref name="minAge"/>초 넘게 떠 있던 안전 표시가 있으면 true — <paramref name="inside"/> = 이미 그 안, <paramref name="target"/> = 가장 가까운 안전 표시 중심.
    /// </summary>
    public static bool TryNearestSafe(List<Shape> shapes, Vector3 p, float minAge, out Vector3 target, out bool inside)
    {
        target = p; inside = false;
        bool found = false;
        float best = float.MaxValue;
        foreach (var s in shapes)
        {
            if (!s.Safe || s.Age < minAge || s.Kind != Kind.Circle) continue;   // 안전지대는 원만(마커 · 사슬 고리는 갈 곳이 아니다)
            if (s.Radius < 1f) continue;
            found = true;
            if (Contains(s, p, -0.3f)) { inside = true; return true; }
            float d = Flat(s.Center - p).sqrMagnitude;
            if (d < best) { best = d; target = new Vector3(s.Center.x, p.y, s.Center.z); }
        }
        return found;
    }

    /// <summary>점에서 가장 가까운 (판정) 가이드와 어긋난 정도 — 「가이드 밖」 피격 진단용. 없으면 빈 문자열.</summary>
    public static string NearestMiss(List<Shape> shapes, Vector3 p)
    {
        string best = "";
        float  bestD = float.MaxValue;
        foreach (var s in shapes)
        {
            if (s.Safe) continue;
            Vector3 d = Flat(p - s.Center);
            float   dist = d.magnitude;
            if (dist >= bestD) continue;
            bestD = dist;
            string extra = s.Kind switch
            {
                Kind.Sector => $" · 가운데에서 {Vector3.Angle(s.Axis, d):0}°(반각 {s.HalfAngle:0}°)",
                Kind.Rect   => $" · 축 방향 {Vector3.Dot(d, s.Axis):0.0} / 옆 {Vector3.Dot(d, Vector3.Cross(Vector3.up, s.Axis)):0.0}(반폭 {s.HalfWidth:0.0})",
                _           => "",
            };
            best = $"가장 가까운 {Describe(s)} 중심에서 {dist:0.0} m{extra} · 나이 {s.Age:0.00}초";
        }
        return $"가이드 {shapes.Count}개" + (best.Length > 0 ? " · " + best : "");
    }

    public static string Describe(in Shape s) => s.Kind switch
    {
        Kind.Circle => $"{s.Name} 원 r{s.Radius:0.0}",
        Kind.Sector => $"{s.Name} 부채꼴 r{s.Radius:0.0} {s.HalfAngle * 2f:0}°",
        Kind.Rect   => $"{s.Name} 직선 {s.HalfLength * 2f:0.0}×{s.HalfWidth * 2f:0.0}",
        _           => $"{s.Name} 칸",
    };

    // ── Private Methods ────────────────────────────────────────
    private static void Visit(Transform t, int depth, float now)
    {
        string n = t.name;
        if (n.StartsWith("Guide_")) { AddGuide(t, now); return; }
        if (IsTile(n)) { AddTile(t, now); return; }
        if (depth >= MaxDepth) return;
        for (int i = 0; i < t.childCount; i++)
        {
            var c = t.GetChild(i);
            if (c.gameObject.activeInHierarchy) Visit(c, depth + 1, now);
        }
    }

    private static void AddGuide(Transform t, float now)
    {
        string n = t.name;
        if (n.StartsWith("Guide_Pillar") || n.StartsWith("Guide_Link")) return;   // 시각 전용(판정 없음)
        var s = new Shape { Name = n, Center = t.position, Age = AgeOf(t.gameObject, now), Safe = IsSafe(GuideColor(t)) };
        Vector3 sc = t.lossyScale;
        bool decal = t.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null && mf.sharedMesh.name == "Quad";

        if (n.StartsWith("Guide_Beam"))
        {
            s.Kind       = Kind.Rect;
            s.Axis       = FlatDir(decal ? t.up : t.forward);   // 데칼은 +Y가 빔 방향(앞면이 위를 보게 눕힘), 폴백 상자는 +Z
            s.HalfWidth  = Mathf.Abs(sc.x) * 0.5f;
            s.HalfLength = Mathf.Abs(decal ? sc.y : sc.z) * 0.5f;
        }
        else if (n.StartsWith("Guide_Sector") && decal && IsSector(t, out float angle))
        {
            s.Kind      = Kind.Sector;
            s.Radius    = Mathf.Abs(sc.x) * 0.5f;
            s.HalfAngle = angle * 0.5f;
            // 부채꼴 가운데 = 쿼드 −X(PatternGuideHelper.SectorRotation). Euler(90, …)은 짐벌 잠금이라 eulerAngles.y로 되읽으면 y/z가 섞인다
            s.Axis      = FlatDir(-t.right);
        }
        else
        {
            s.Kind   = Kind.Circle;                              // 원 · 구체(충격점) · 부채꼴 폴백(원으로 그림)
            s.Radius = Mathf.Abs(sc.x) * 0.5f;
        }
        s_shapes.Add(s);
    }

    private static Color GuideColor(Transform t)
    {
        if (!t.TryGetComponent<MeshRenderer>(out var mr)) return Color.clear;
        mr.GetPropertyBlock(s_mpb);
        return s_mpb.GetColor(ColorId);
    }

    private static bool IsSafe(Color c)
    {
        foreach (var s in SafeColors)
            if (Mathf.Abs(c.r - s.r) < 0.06f && Mathf.Abs(c.g - s.g) < 0.06f && Mathf.Abs(c.b - s.b) < 0.06f) return true;
        return false;
    }

    // 화룡 옛 예고 — 바닥에 눕힌 풀 쿼드 타일(셀 한 칸) · 휩쓸기 잔불 자국
    private static bool IsTile(string n)
        => n == "BreathSweepWarn" || n == "MeteorWarn" || n == "FireballWarn" || n == "PassiveMeteorWarn" || n == "ScorchMark";

    private static void AddTile(Transform t, float now)
    {
        Vector3 sc = t.lossyScale;
        s_shapes.Add(new Shape
        {
            Kind = Kind.Rect, Name = t.name, Center = t.position, Age = AgeOf(t.gameObject, now),
            Axis = FlatDir(t.up), HalfWidth = Mathf.Abs(sc.x) * 0.5f, HalfLength = Mathf.Abs(sc.y) * 0.5f,
        });
    }

    private static bool IsSector(Transform t, out float angle)
    {
        angle = 360f;
        if (!t.TryGetComponent<MeshRenderer>(out var mr)) return false;
        mr.GetPropertyBlock(s_mpb);
        if (s_mpb.GetFloat(SectorId) < 0.5f) return false;
        angle = s_mpb.GetFloat(AngleId);
        return angle > 0.1f && angle < 359.9f;
    }

    private static void AddGroundWarning(MonsterGroundWarning w, float now)
    {
        float age = AgeOf(w.gameObject, now);
        float circleR = 0f;
        bool  safe    = false;
        foreach (var lr in w.GetComponentsInChildren<LineRenderer>())
        {
            safe |= IsSafe(lr.startColor);
            int count = Mathf.Min(lr.positionCount, s_line.Length);
            lr.GetPositions(s_line);
            if (count == 4)
            {
                var poly = new Vector3[4];
                for (int i = 0; i < 4; i++) poly[i] = lr.useWorldSpace ? s_line[i] : lr.transform.TransformPoint(s_line[i]);
                s_shapes.Add(new Shape { Kind = Kind.Poly, Name = w.name, Center = w.transform.position, Poly = poly, Age = age, Safe = IsSafe(lr.startColor) });
            }
            else if (count >= 8)
            {
                for (int i = 0; i < count; i++)
                {
                    Vector3 p = lr.useWorldSpace ? s_line[i] : lr.transform.TransformPoint(s_line[i]);
                    circleR = Mathf.Max(circleR, Flat(p - w.transform.position).magnitude);
                }
            }
        }
        if (circleR > 0.01f)   // 채움 원은 바깥 고정선 반경 = 판정 반경
            s_shapes.Add(new Shape { Kind = Kind.Circle, Name = w.name, Center = w.transform.position, Radius = circleR, Age = age, Safe = safe });
    }

    private static float AgeOf(GameObject go, float now)
    {
        int id = go.GetInstanceID();
        if (!s_firstSeen.TryGetValue(id, out float at)) s_firstSeen[id] = at = now;
        return now - at;
    }

    private static bool InsidePoly(Vector3[] poly, Vector3 p, float margin)
    {
        if (poly == null || poly.Length < 3) return false;
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            Vector3 a = poly[i], b = poly[j];
            if ((a.z > p.z) != (b.z > p.z) && p.x < (b.x - a.x) * (p.z - a.z) / (b.z - a.z) + a.x) inside = !inside;
        }
        if (inside) return true;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            if (SegDist(Flat(p), Flat(poly[j]), Flat(poly[i])) <= margin) return true;
        return false;
    }

    private static float SegDist(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
        return (p - (a + ab * t)).magnitude;
    }

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    private static Vector3 FlatDir(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
    }
}
