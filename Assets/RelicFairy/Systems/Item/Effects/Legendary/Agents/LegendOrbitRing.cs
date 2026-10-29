using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 전설 룬 둘레 고리(10-24 개편 2차) — 플레이어 둘레를 도는 것 N개(뇌전 탄창 구체 · 호위 성검 · 영혼 수확 낫). 개수 = 쌓인 자원.
/// 하나를 적에게 날리거나(<see cref="Launch"/> — 꽂혀 있다가 돌아오거나 닿으면 사라진다) 도는 것 전부를 앞으로 날린다(<see cref="LaunchAllForward"/>).
/// 도는 동안 닿는 피해는 <see cref="OnContact"/>(낫). 맞을 것이 없는 방(대기방 · 상점)에서는 그림만 숨긴다(LegendaryProjectileBase V-10과 같은 규칙).
/// </summary>
public sealed class LegendOrbitRing : MonoBehaviour
{
    private const float FlySpeed = 26f, BackSpeed = 14f, LineSpeed = 20f, FanStepDeg = 9f;
    private const float ContactInterval = 0.1f, IdleCheckInterval = 0.5f, IdleRadius = 25f;

    private enum State { Orbit, Fly, Hold, Back, Line }

    private sealed class Item
    {
        public Transform tf, visual;
        public State     state;
        public GameObject target;
        public Vector3   dir, start;
        public float     holdUntil;
        public bool      consume;
        public System.Action<GameObject> onArrive;
        public readonly HashSet<GameObject> lineHit = new();
    }

    private static readonly List<GameObject> s_idleBuf = new(1);

    private readonly List<Item> _items = new();
    private readonly List<GameObject> _buf = new(8);
    private readonly Dictionary<GameObject, float> _lastContact = new();
    private Transform _center;
    private GameObject _prefab;
    private float _vfxScale, _radius, _degPerSec, _angle, _contactTimer, _idleCheck;
    private float _lineDistance;
    private System.Action<GameObject> _onLineHit;
    private bool _idle;

    public object Owner { get; private set; }
    /// <summary>도는 동안 닿은 적(같은 적 <see cref="ContactGap"/>초에 한 번) — 비우면 닿아도 아무것도 안 한다.</summary>
    public System.Action<GameObject> OnContact { get; set; }
    public float ContactRadius { get; set; } = 0.7f;
    public float ContactGap    { get; set; } = 0.5f;
    /// <summary>그림 자전(초당 도 · 낫).</summary>
    public float Spin { get; set; }

    /// <summary>날아간 것까지 포함한 개수.</summary>
    public int Count => _items.Count;
    /// <summary>지금 둘레를 도는(날릴 수 있는) 개수.</summary>
    public int ReadyCount
    {
        get { int n = 0; foreach (var it in _items) if (it.state == State.Orbit) n++; return n; }
    }

    public static LegendOrbitRing Spawn(object owner, Transform center, GameObject prefab, float vfxScale, float radius, float degPerSec)
    {
        var go = new GameObject("LegendOrbitRing");
        var ring = go.AddComponent<LegendOrbitRing>();
        ring.Owner      = owner;
        ring._center    = center;
        ring._prefab    = prefab;
        ring._vfxScale  = vfxScale;
        ring._radius    = radius;
        ring._degPerSec = degPerSec;
        LegendaryRuntime.Instance?.TrackAgent(go);
        return ring;
    }

    /// <summary>둘레에 n개를 더한다.</summary>
    public void Add(int n = 1)
    {
        for (int i = 0; i < n; i++) _items.Add(Create());
    }

    /// <summary>
    /// 도는 것 하나를 <paramref name="target"/>에게 날린다. 닿으면 onArrive(target) — <paramref name="holdSeconds"/> &lt; 0이면 닿으며 사라지고,
    /// 아니면 그만큼 꽂혀 있다 돌아온다. 도는 것이 없으면 false.
    /// </summary>
    public bool Launch(GameObject target, float holdSeconds, System.Action<GameObject> onArrive)
    {
        if (target == null) return false;
        foreach (var it in _items)
        {
            if (it.state != State.Orbit) continue;
            it.state     = State.Fly;
            it.target    = target;
            it.consume   = holdSeconds < 0f;
            it.holdUntil = Mathf.Max(0f, holdSeconds);
            it.onArrive  = onArrive;
            return true;
        }
        return false;
    }

    /// <summary>도는 것 전부를 <paramref name="dir"/> 쪽으로 부채꼴로 날린다 — 지나가며 닿은 적마다 한 번 onHit · 끝에서 사라진다. 날린 수를 돌려준다.</summary>
    public int LaunchAllForward(Vector3 dir, float distance, System.Action<GameObject> onHit)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f) return 0;
        dir.Normalize();
        _lineDistance = distance;
        _onLineHit    = onHit;
        int ready = ReadyCount, k = 0;
        foreach (var it in _items)
        {
            if (it.state != State.Orbit) continue;
            float yaw = (k - (ready - 1) * 0.5f) * FanStepDeg;
            it.dir   = Quaternion.Euler(0f, yaw, 0f) * dir;
            it.start = it.tf.position;
            it.state = State.Line;
            it.lineHit.Clear();
            k++;
        }
        return k;
    }

    private void Update()
    {
        if (_center == null) { Destroy(gameObject); return; }
        float dt = Time.deltaTime;
        _angle += _degPerSec * dt;
        float y = _center.position.y + LegendaryProjectileBase.FlyHeight;

        int orbiting = ReadyCount, slot = 0;
        for (int i = 0; i < _items.Count; i++)
        {
            var it = _items[i];
            switch (it.state)
            {
                case State.Orbit:
                    it.tf.position = SlotPos(slot++, orbiting, y);
                    Face(it, Tangent(slot - 1, orbiting));
                    break;
                case State.Fly:
                    if (it.target == null || !it.target.activeInHierarchy) { it.state = State.Back; it.consume = false; break; }   // 닿기 전에 사라졌다 — 돌아온다
                    Vector3 to = it.target.transform.position + Vector3.up * LegendaryProjectileBase.FlyHeight;
                    if (MoveTo(it, to, FlySpeed * dt)) Arrive(it);
                    break;
                case State.Hold:
                    if (it.target != null && it.target.activeInHierarchy)
                        it.tf.position = it.target.transform.position + Vector3.up * LegendaryProjectileBase.FlyHeight;
                    if (Time.time >= it.holdUntil || it.target == null || !it.target.activeInHierarchy)
                    {
                        it.state = State.Back;
                        if (it.visual != null) it.visual.gameObject.SetActive(!_idle);
                    }
                    break;
                case State.Back:
                    if (MoveTo(it, SlotPos(orbiting, orbiting + 1, y), BackSpeed * dt)) it.state = State.Orbit;
                    break;
                case State.Line:
                    it.tf.position += it.dir * LineSpeed * dt;
                    Face(it, it.dir);
                    LineHits(it);
                    if ((it.tf.position - it.start).sqrMagnitude >= _lineDistance * _lineDistance) it.consume = true;
                    break;
            }
            if (Spin != 0f && it.visual != null) it.visual.localRotation *= Quaternion.Euler(0f, Spin * dt, 0f);
        }
        for (int i = _items.Count - 1; i >= 0; i--)
            if (_items[i].consume && _items[i].state != State.Fly) Remove(i);

        ContactHits(dt);
        UpdateIdle(dt);
    }

    private void OnDestroy() => LegendaryRuntime.Instance?.RemoveAgent(gameObject);

    private Item Create()
    {
        var tf = new GameObject("Orbit").transform;
        tf.SetParent(transform, false);
        tf.position = _center != null ? _center.position + Vector3.up * LegendaryProjectileBase.FlyHeight : Vector3.zero;
        Transform visual = null;
        if (_prefab != null)
        {
            var vfx = Instantiate(_prefab, tf);
            LegendaryRuntime.DisableGrabPassRenderers(vfx);
            vfx.transform.localPosition = Vector3.zero;
            vfx.transform.localScale    = Vector3.one * _vfxScale;
            foreach (var col in vfx.GetComponentsInChildren<Collider>(true)) col.enabled = false;
            vfx.SetActive(!_idle);
            visual = vfx.transform;
        }
        return new Item { tf = tf, visual = visual, state = State.Orbit };
    }

    private void Remove(int i)
    {
        if (_items[i].tf != null) Destroy(_items[i].tf.gameObject);
        _items.RemoveAt(i);
    }

    private Vector3 SlotPos(int slot, int count, float y)
    {
        float rad = (_angle + slot * 360f / Mathf.Max(1, count)) * Mathf.Deg2Rad;
        Vector3 c = _center.position;
        return new Vector3(c.x + Mathf.Cos(rad) * _radius, y, c.z + Mathf.Sin(rad) * _radius);
    }

    private Vector3 Tangent(int slot, int count)
    {
        float rad = (_angle + slot * 360f / Mathf.Max(1, count)) * Mathf.Deg2Rad;
        return new Vector3(-Mathf.Sin(rad), 0f, Mathf.Cos(rad)) * Mathf.Sign(_degPerSec);
    }

    private static void Face(Item it, Vector3 dir)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude > 1e-4f) it.tf.rotation = Quaternion.LookRotation(dir);
    }

    /// <summary>step만큼 다가간다 — 닿았으면 true.</summary>
    private static bool MoveTo(Item it, Vector3 to, float step)
    {
        Vector3 d = to - it.tf.position;
        Face(it, d);
        if (d.magnitude <= step) { it.tf.position = to; return true; }
        it.tf.position += d.normalized * step;
        return false;
    }

    private void Arrive(Item it)
    {
        var target = it.target;
        it.state     = it.consume ? State.Orbit : State.Hold;   // 사라질 것은 아래 정리에서 지운다(Fly가 아니면)
        it.holdUntil = Time.time + it.holdUntil;
        // 꽂혀 있는 동안 그림은 숨긴다 — 탄환 그림(성검)이 멈추면 뒤로 뿜는 입자가 화면을 가로지르는 빛줄기가 됐다(10-24 실측 95 사진) · 꽂힘은 타격 이펙트가 알린다
        if (!it.consume && it.visual != null) it.visual.gameObject.SetActive(false);
        var cb = it.onArrive;
        it.onArrive = null;
        cb?.Invoke(target);
    }

    private void LineHits(Item it)
    {
        int n = CombatQuery.GetNearbyDamageables(it.tf.position, ContactRadius, _center.gameObject, 8, _buf);
        for (int k = 0; k < n; k++)
        {
            var go = _buf[k];
            if (go == null || !it.lineHit.Add(go)) continue;
            _onLineHit?.Invoke(go);
        }
    }

    private void ContactHits(float dt)
    {
        if (OnContact == null || _idle) return;
        _contactTimer += dt;
        if (_contactTimer < ContactInterval) return;
        _contactTimer = 0f;
        float now = Time.time;
        for (int i = 0; i < _items.Count; i++)   // 닿아 쓰러뜨리면 효과가 고리에 더할 수 있다 — foreach 금지
        {
            var it = _items[i];
            if (it.state != State.Orbit) continue;
            int n = CombatQuery.GetNearbyDamageables(it.tf.position, ContactRadius, _center.gameObject, 4, _buf);
            for (int k = 0; k < n; k++)
            {
                var go = _buf[k];
                if (go == null || (_lastContact.TryGetValue(go, out float at) && now - at < ContactGap)) continue;
                _lastContact[go] = now;
                OnContact(go);
            }
        }
    }

    private void UpdateIdle(float dt)
    {
        _idleCheck -= dt;
        if (_idleCheck > 0f) return;
        _idleCheck = IdleCheckInterval;
        bool idle = CombatQuery.GetNearbyDamageables(_center.position, IdleRadius, _center.gameObject, 1, s_idleBuf) == 0;
        if (idle == _idle) return;
        _idle = idle;
        foreach (var it in _items) if (it.visual != null) it.visual.gameObject.SetActive(!idle);
    }
}
