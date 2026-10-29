using System.Collections.Generic;
using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 바닥 장판(10-24 전설 룬 2차 · 10-26 무기 개편) — 원(불바다 웅덩이 · 독안개 · 속성 검 장판) · 선분(불꽃 질주 불길 띠) · 호 띠.
/// 틱(0.25초)마다 영역 안 몬스터에게 onTick을 부른다 — 무엇을 거는지는 부른 쪽이 정한다.
/// 그림 = 속성 장판(반경 = 판정 반경 · 선분은 반경 원을 1.2 m마다 · 호는 점마다) — 이펙트 = 판정.
/// </summary>
public sealed class LegendGroundField : MonoBehaviour
{
    private const float TickInterval = 0.25f;
    private const float SegmentStep  = 1.2f;
    private const float VfxHoldSeconds = 9999f;

    private readonly List<GameObject> _buf   = new(16);
    private readonly List<int>        _auras = new(8);
    private GameObject _exclude;
    private Vector3[] _points;   // 원 = 같은 두 점 · 선분 = 두 끝 · 호 = 점 여러 개(반경 원들의 합집합)
    private bool _segment;
    private Vector3 _center;
    private float _reach, _radius, _life, _age, _tick;
    private System.Action<MonsterBase> _onTick;

    public object Owner { get; private set; }
    /// <summary>장판 가운데(바닥) · 반경 — 같은 자리 장판을 다시 채울지 고를 때.</summary>
    public Vector3 Center => _center;
    public float Radius => _radius;

    /// <summary>원이면 a == b.</summary>
    public static LegendGroundField Spawn(object owner, ItemEffectContext ctx, Vector3 a, Vector3 b, float radius, float life,
                                          RuneElement element, System.Action<MonsterBase> onTick)
        => Create(owner, ctx?.Player != null ? ctx.Player.gameObject : null, new[] { a, b }, true, radius, life, element, onTick);

    /// <summary>원 장판(가운데 · 반경) — 그림은 반경에 맞춘 속성 장판 하나.</summary>
    public static LegendGroundField SpawnCircle(object owner, GameObject exclude, Vector3 center, float radius, float life,
                                                RuneElement element, System.Action<MonsterBase> onTick)
        => Create(owner, exclude, new[] { center, center }, true, radius, life, element, onTick);

    /// <summary>
    /// 호 띠 — <paramref name="center"/>에서 <paramref name="forward"/> 양쪽 <paramref name="spanDeg"/>/2, 반지름 <paramref name="arcRadius"/> 위에
    /// <paramref name="halfWidth"/> 반경 원을 halfWidth × 2 간격으로 둔다(합집합 = 폭 halfWidth × 2 띠).
    /// </summary>
    public static LegendGroundField SpawnArc(object owner, GameObject exclude, Vector3 center, Vector3 forward, float arcRadius, float spanDeg,
                                             float halfWidth, float life, RuneElement element, System.Action<MonsterBase> onTick)
    {
        forward.y = 0f;
        forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
        float arcLen = arcRadius * spanDeg * Mathf.Deg2Rad;
        int n = Mathf.Max(2, Mathf.CeilToInt(arcLen / (halfWidth * 2f)) + 1);
        var pts = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            float a = -spanDeg * 0.5f + spanDeg * i / (n - 1);
            pts[i] = center + Quaternion.Euler(0f, a, 0f) * forward * arcRadius;
        }
        return Create(owner, exclude, pts, false, halfWidth, life, element, onTick);
    }

    /// <summary>점이 장판 안인가(바닥 평면).</summary>
    public bool Contains(Vector3 p) => Distance(p) <= _radius;

    /// <summary>수명을 처음부터 다시 센다(같은 자리에서 계속 휘두를 때 장판을 겹겹이 깔지 않는다).</summary>
    public void Refresh(float life)
    {
        _age  = 0f;
        _life = life;
    }

    private static LegendGroundField Create(object owner, GameObject exclude, Vector3[] points, bool segment, float radius, float life,
                                            RuneElement element, System.Action<MonsterBase> onTick)
    {
        Vector3 c = Vector3.zero;
        foreach (var p in points) c += p;
        c /= points.Length;
        var go = new GameObject("LegendGround");
        go.transform.position = c;
        var f = go.AddComponent<LegendGroundField>();
        float far = 0f;
        foreach (var p in points) far = Mathf.Max(far, Vector3.Distance(c, p));
        f.Owner    = owner;
        f._exclude = exclude;
        f._points  = points;
        f._segment = segment;
        f._center  = c;
        f._reach   = far + radius;
        f._radius  = radius;
        f._life    = life;
        f._onTick  = onTick;
        f._tick    = TickInterval;   // 깔리는 순간 한 번
        LegendaryRuntime.Instance?.TrackAgent(go);
        f.AttachVisuals(element);
        return f;
    }

    private void Update()
    {
        _age += Time.deltaTime;
        if (_age >= _life) { Destroy(gameObject); return; }
        _tick += Time.deltaTime;
        if (_tick < TickInterval) return;
        _tick = 0f;

        int n = CombatQuery.GetNearbyDamageables(_center, _reach, _exclude, 16, _buf);
        for (int i = 0; i < n; i++)
        {
            var mb = _buf[i] != null ? _buf[i].GetComponentInParent<MonsterBase>() : null;
            if (mb == null || mb.IsDead) continue;
            if (Distance(mb.transform.position) > _radius + CombatQuery.BodyReach(mb)) continue;
            _onTick?.Invoke(mb);
        }
    }

    private void OnDestroy()
    {
        foreach (int h in _auras) ElementVfxPlayer.ReleaseAura(h);
        _auras.Clear();
        LegendaryRuntime.Instance?.RemoveAgent(gameObject);
    }

    /// <summary>바닥 평면 거리 — 선분이면 선분까지, 아니면 가장 가까운 점까지.</summary>
    private float Distance(Vector3 p)
    {
        if (_segment) return FormStrike.DistToSegmentXZ(p, _points[0], _points[1]);
        float best = float.MaxValue;
        foreach (var q in _points)
        {
            float dx = p.x - q.x, dz = p.z - q.z;
            best = Mathf.Min(best, dx * dx + dz * dz);
        }
        return Mathf.Sqrt(best);
    }

    private void AttachVisuals(RuneElement element)
    {
        if (!_segment)
        {
            foreach (var p in _points) AddAura(element, p);
            return;
        }
        Vector3 ab = _points[1] - _points[0];
        ab.y = 0f;
        int n = ab.magnitude < 0.1f ? 1 : Mathf.CeilToInt(ab.magnitude / SegmentStep) + 1;
        for (int i = 0; i < n; i++)
            AddAura(element, n == 1 ? _points[0] : Vector3.Lerp(_points[0], _points[1], i / (float)(n - 1)));
    }

    private void AddAura(RuneElement element, Vector3 at)
    {
        var dot = new GameObject("Area").transform;
        dot.SetParent(transform, false);
        dot.position = at;
        int h = ElementVfxPlayer.AttachAura(element, dot, VfxHoldSeconds, _radius);   // 그림 수명은 실시간이라 길게 주고 OnDestroy에서 해제(판정은 게임 시간 · 10-26 검토)
        if (h != 0) _auras.Add(h);
    }
}
