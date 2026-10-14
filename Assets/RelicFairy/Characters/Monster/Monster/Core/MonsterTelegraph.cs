using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 통일 판정 예고(10-12 · 83 몬스터 전수조사 §4 「하나의 규칙」).
///
/// 판정 모양의 얇은 외곽선이 맞기 전에 뜨고, 안쪽 <b>앞줄</b>이 몬스터 쪽에서 차올라 끝에 닿는 순간이 피해 순간이다.
/// 반짝임(MonsterAttackGlint)은 「누가」, 이 예고는 「어디 · 언제」, 시전 이펙트는 「무엇」만 작게 맡는다.
///
/// 쓰는 법:
/// <code>
/// var t = MonsterTelegraph.Cone(origin, forward, range, halfAngle, secondsUntilHit, MonsterTelegraph.Normal);
/// // 매 프레임(공격 속도 배율 · 히트스톱이 박자를 바꿀 때) — NotifyAttackIncoming과 같은 값
/// t.Sync(secondsUntilHit);
/// // 공격이 끊기면 — 헛예고가 남지 않게
/// t.Cancel();
/// </code>
/// 면(face)은 위협 · 정예 큰 공격만 켠다(일반 몬스터는 선만 — 결정 D3).
/// 톤: 얇은 선(0.06) · 따뜻한 흰빛 ~ 주홍 한 계열 · 맞는 순간 뒤 0.1초에 사라진다.
/// </summary>
public sealed class MonsterTelegraph : MonoBehaviour
{
    // ── Constants ────────────────────────────────────────────────
    /// <summary>일반 공격 — 따뜻한 흰빛 금(10-11 외곽선과 같은 색).</summary>
    public static readonly Color Normal = new(1f, 0.82f, 0.55f, 1f);
    /// <summary>위협 · 정예 큰 공격 · 특수 상태 — 같은 계열의 주홍.</summary>
    public static readonly Color Heavy = new(1f, 0.55f, 0.32f, 1f);
    /// <summary>맞기 전 최소 예고(반짝임과 같은 박자). 원 범위는 0.6 ~ 1.0초를 권장한다.</summary>
    public const float MinLead = 0.45f;

    private const float LineWidth  = 0.06f;   // 높은 부감에서 1~2픽셀(10-11 실측 — 0.04는 안 보였다)
    private const float SweepWidth = 0.05f;
    private const float FaceAlpha  = 0.15f;
    private const float FadeOut    = 0.1f;
    private const float Lift       = 0.05f;
    private const int   ArcSegments = 40;

    private enum Shape { Circle, Cone, Rect }

    // ── Static ───────────────────────────────────────────────────
    private static Material s_material;

    // ── Private ──────────────────────────────────────────────────
    private Shape  _shape;
    private Vector3 _origin, _forward, _right;
    private float  _radius, _halfAngle, _length, _width;
    private Color  _color;
    private float  _total, _remaining, _fade = -1f;
    private LineRenderer _outline, _sweep;
    private MeshRenderer _face;
    private Mesh _faceMesh;

    // ── Public API ───────────────────────────────────────────────
    /// <summary>원 — 중심에서 앞줄(고리)이 바깥으로 차오른다.</summary>
    public static MonsterTelegraph Circle(Vector3 center, float radius, float secondsUntilHit, Color color, bool face = false)
        => Create(Shape.Circle, center, Vector3.forward, radius, 180f, 0f, 0f, secondsUntilHit, color, face);

    /// <summary>부채꼴 — 몬스터 자리에서 호(앞줄)가 사거리 끝으로 차오른다. halfAngleDeg = 한쪽 반각.</summary>
    public static MonsterTelegraph Cone(Vector3 origin, Vector3 forward, float range, float halfAngleDeg, float secondsUntilHit, Color color, bool face = false)
        => Create(Shape.Cone, origin, forward, range, Mathf.Clamp(halfAngleDeg, 1f, 180f), 0f, 0f, secondsUntilHit, color, face);

    /// <summary>직선(직사각형) — 몬스터 쪽 끝에서 가로줄이 먼 끝으로 나아간다.</summary>
    public static MonsterTelegraph Line(Vector3 origin, Vector3 forward, float length, float width, float secondsUntilHit, Color color, bool face = false)
        => Create(Shape.Rect, origin, forward, 0f, 0f, length, width, secondsUntilHit, color, face);

    /// <summary>판정 칸(격자) — 칸들의 바깥 상자를 직선 예고로(히트 판정 IsInsideGrid와 같은 반폭 0.5).</summary>
    public static MonsterTelegraph Grid(Vector3 origin, Vector3 forward, MonsterGroundWarning.GridShape shape, float secondsUntilHit, Color color, bool face = false)
    {
        MonsterGroundWarning.GridBounds(shape, out float minX, out float maxX, out float minY, out float maxY);
        Vector3 f = Flat(forward), r = Vector3.Cross(Vector3.up, f);
        Vector3 start = origin + r * ((minX + maxX) * 0.5f) + f * minY;
        return Line(start, f, maxY - minY, maxX - minX, secondsUntilHit, color, face);
    }

    /// <summary>남은 시간을 다시 맞춘다 — 공격 속도 배율 · 히트스톱이 박자를 바꿔도 앞줄이 피해 순간에 끝에 닿게.</summary>
    public void Sync(float secondsUntilHit)
    {
        if (this == null || _fade >= 0f) return;
        _remaining = Mathf.Max(0f, secondsUntilHit);
        if (_remaining > _total) _total = _remaining;
    }

    /// <summary>공격이 끊겼다 — 바로 지운다(헛예고 방지).</summary>
    public void Cancel()
    {
        if (this != null) Destroy(gameObject);
    }

    // ── Lifecycle ────────────────────────────────────────────────
    private void Update()
    {
        float dt = Time.deltaTime;
        if (_fade >= 0f)
        {
            _fade += dt;
            Apply(1f, 1f - Mathf.Clamp01(_fade / FadeOut));
            if (_fade >= FadeOut) Destroy(gameObject);
            return;
        }
        _remaining -= dt;
        float t = _total > 0f ? 1f - Mathf.Clamp01(_remaining / _total) : 1f;
        Apply(t, 1f);
        if (_remaining <= 0f) _fade = 0f;   // 맞는 순간 — 앞줄이 끝에 닿았다
    }

    private void OnDestroy()
    {
        if (_faceMesh != null) Destroy(_faceMesh);
    }

    // ── Private Methods ──────────────────────────────────────────
    private static MonsterTelegraph Create(Shape shape, Vector3 origin, Vector3 forward, float radius, float halfAngle,
                                           float length, float width, float secondsUntilHit, Color color, bool face)
    {
        var go = new GameObject($"[MonsterTelegraph.{shape}]");
        var t = go.AddComponent<MonsterTelegraph>();
        t._shape = shape; t._origin = origin; t._forward = Flat(forward); t._right = Vector3.Cross(Vector3.up, t._forward);
        t._radius = radius; t._halfAngle = halfAngle; t._length = length; t._width = width;
        t._color = color;
        t._total = t._remaining = Mathf.Max(0.05f, secondsUntilHit);
        t._outline = t.MakeLine("Outline", LineWidth, true);
        t._sweep   = t.MakeLine("Sweep", SweepWidth, false);
        t.BuildOutline();
        if (face) t.BuildFace();
        t.Apply(0f, 1f);
        return t;
    }

    private LineRenderer MakeLine(string name, float width, bool loop)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.loop = loop;
        lr.widthMultiplier = width;
        lr.material = GetMaterial();
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.numCapVertices = 2;
        return lr;
    }

    private Vector3 P(float right, float fwd) => _origin + _right * right + _forward * fwd + Vector3.up * Lift;

    private void BuildOutline()
    {
        switch (_shape)
        {
            case Shape.Circle:
                _outline.positionCount = ArcSegments;
                for (int i = 0; i < ArcSegments; i++)
                {
                    float a = i * Mathf.PI * 2f / ArcSegments;
                    _outline.SetPosition(i, P(Mathf.Cos(a) * _radius, Mathf.Sin(a) * _radius));
                }
                break;
            case Shape.Cone:
                _outline.positionCount = ArcSegments + 2;
                _outline.SetPosition(0, P(0f, 0f));
                for (int i = 0; i <= ArcSegments; i++) _outline.SetPosition(i + 1, ArcPoint(_radius, i));
                break;
            default:
                _outline.positionCount = 4;
                float hw = _width * 0.5f;
                _outline.SetPosition(0, P(-hw, 0f));
                _outline.SetPosition(1, P(hw, 0f));
                _outline.SetPosition(2, P(hw, _length));
                _outline.SetPosition(3, P(-hw, _length));
                break;
        }
    }

    private Vector3 ArcPoint(float r, int i)
    {
        float a = Mathf.Lerp(-_halfAngle, _halfAngle, i / (float)ArcSegments) * Mathf.Deg2Rad;
        return P(Mathf.Sin(a) * r, Mathf.Cos(a) * r);
    }

    /// <summary>앞줄 — 진행 t(0 → 1)만큼 몬스터 쪽에서 끝으로.</summary>
    private void UpdateSweep(float t)
    {
        switch (_shape)
        {
            case Shape.Circle:
                _sweep.loop = true;
                _sweep.positionCount = ArcSegments;
                float rc = Mathf.Max(0.02f, _radius * t);
                for (int i = 0; i < ArcSegments; i++)
                {
                    float a = i * Mathf.PI * 2f / ArcSegments;
                    _sweep.SetPosition(i, P(Mathf.Cos(a) * rc, Mathf.Sin(a) * rc));
                }
                break;
            case Shape.Cone:
                _sweep.loop = false;
                _sweep.positionCount = ArcSegments + 1;
                float ra = Mathf.Max(0.02f, _radius * t);
                for (int i = 0; i <= ArcSegments; i++) _sweep.SetPosition(i, ArcPoint(ra, i));
                break;
            default:
                _sweep.loop = false;
                _sweep.positionCount = 2;
                float y = _length * t, hw = _width * 0.5f;
                _sweep.SetPosition(0, P(-hw, y));
                _sweep.SetPosition(1, P(hw, y));
                break;
        }
    }

    private void BuildFace()
    {
        var go = new GameObject("Face");
        go.transform.SetParent(transform, false);
        _faceMesh = new Mesh { name = "MonsterTelegraphFace" };
        var verts = new System.Collections.Generic.List<Vector3>();
        var tris  = new System.Collections.Generic.List<int>();
        if (_shape == Shape.Rect)
        {
            float hw = _width * 0.5f;
            verts.Add(P(-hw, 0f)); verts.Add(P(hw, 0f)); verts.Add(P(hw, _length)); verts.Add(P(-hw, _length));
            tris.AddRange(new[] { 0, 2, 1, 0, 3, 2 });
        }
        else
        {
            verts.Add(_shape == Shape.Circle ? P(0f, 0f) : P(0f, 0f));
            int n = ArcSegments;
            for (int i = 0; i <= n; i++)
            {
                if (_shape == Shape.Circle)
                {
                    float a = i * Mathf.PI * 2f / n;
                    verts.Add(P(Mathf.Cos(a) * _radius, Mathf.Sin(a) * _radius));
                }
                else verts.Add(ArcPoint(_radius, i));
            }
            for (int i = 1; i <= n; i++) { tris.Add(0); tris.Add(i + 1); tris.Add(i); }
        }
        _faceMesh.SetVertices(verts);
        _faceMesh.SetTriangles(tris, 0);
        go.AddComponent<MeshFilter>().sharedMesh = _faceMesh;
        _face = go.AddComponent<MeshRenderer>();
        _face.sharedMaterial = GetMaterial();
        _face.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _face.receiveShadows = false;
    }

    /// <summary>t = 진행(0 → 1) · k = 사라짐 배율(1 → 0). 외곽선은 옅게 시작해 맞는 순간 가장 진하다.</summary>
    private void Apply(float t, float k)
    {
        Color c = _color;
        c.a = Mathf.Lerp(0.2f, 0.85f, t * t) * k * _color.a;
        _outline.startColor = c; _outline.endColor = c;
        Color s = _color; s.a = 0.85f * k * _color.a;
        _sweep.startColor = s; _sweep.endColor = s;
        UpdateSweep(t);
        if (_face != null)
        {
            var fc = _color; fc.a = FaceAlpha * Mathf.Lerp(0.4f, 1f, t) * k * _color.a;
            var cols = new Color[_faceMesh.vertexCount];
            for (int i = 0; i < cols.Length; i++) cols[i] = fc;
            _faceMesh.colors = cols;
        }
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
    }

    private static Material GetMaterial()
    {
        if (s_material == null) s_material = new Material(Shader.Find("Sprites/Default"));
        return s_material;
    }
}
}
