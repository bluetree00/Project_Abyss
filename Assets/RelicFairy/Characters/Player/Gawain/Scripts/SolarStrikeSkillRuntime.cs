using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using RelicFairy.Monster;

/// <summary>
/// 가웨인 Q스킬 — 태양의 검흔.
/// 1) 전방에 검격 오브젝트(GawainSwingZone) 생성 — SphereCollider(isTrigger)로 1회 충돌 판정
/// 2) 동일 위치에 불 장판(SolarFireZone) 생성 — SphereCollider(isTrigger)로 지속 충돌 판정
/// 두 오브젝트 모두 자체 Collider + kinematic Rigidbody 를 가지며, OnTriggerEnter/Exit 로 대상 추적한다.
/// 강화 구간(SolarTimer) 시 범위·피해가 증가한다.
/// </summary>
public class SolarStrikeSkillRuntime : ISkillRuntime
{
    // ── Constants ─────────────────────────────────────────────
    private const string AnimName        = "QSkill_01";
    private const float  AnimDuration    = 1.1f;
    private const float  HitTime         = 0.35f;

    // 검격 (즉시 광역 1회 타격)
    private const float  SwingBaseDamage     = 40f;
    private const float  SwingBaseRadius     = 4.0f;
    private const float  SwingForward        = 1.5f;
    private const float  SwingVisualDuration = 0.4f;

    // 불 장판 (지속 피해)
    private const float  TrailBaseDamage    = 17f;
    private const float  TrailBaseRadius    = 3.0f;
    private const float  TrailBaseDuration  = 4.0f;
    private const float  TrailTickInterval  = 0.4f;

    private const float  EmpoweredDamageMult = 2.0f;  // 강화 구간 피해 +100% (반경은 페이즈와 무관하게 고정)

    // ── Private ───────────────────────────────────────────────
    private readonly SolarTimer _solarTimer;
    private float _elapsed;
    private bool  _hitTriggered;
    private bool  _empowered;

    public SolarStrikeSkillRuntime(SolarTimer solarTimer)
    {
        _solarTimer = solarTimer;
    }

    public void OnEnter(SkillExecutionContext ctx)
    {
        _elapsed      = 0f;
        _hitTriggered = false;
        _empowered    = _solarTimer?.IsEmpowered ?? false;

        ctx.RotateToMouse();
        ctx.SetMoveScale(0f);
        ctx.Animator?.CrossFade(AnimName, 0.1f);

        // 필살기 카메라 연출 — 유물 우선, 없으면 캐릭터 데이터 폴백 (설정 있을 때만)
        var cine = ctx.Controller?.RelicClass?.QSkillCinematic ?? ctx.Controller?.CharacterData?.QSkillCinematic;
        if (cine != null)
            UltimateCinematicService.Play(cine, ctx.Controller.transform).Forget();
    }

    public void OnUpdate(SkillExecutionContext ctx)
    {
        _elapsed += Time.deltaTime;

        if (!_hitTriggered && _elapsed >= HitTime)
        {
            _hitTriggered = true;
            SpawnSwing(ctx);
            SpawnFireTrail(ctx);
        }

        if (_elapsed >= AnimDuration)
            ctx.RequestEnd?.Invoke();
    }

    public void OnExit(SkillExecutionContext ctx)
    {
        ctx.SetMoveScale(1f);
    }

    // ── Private Methods ───────────────────────────────────────
    private void SpawnSwing(SkillExecutionContext ctx)
    {
        var pt = ctx.PlayerTransform;
        var origin = pt.position + pt.forward * SwingForward;
        float radius = SwingBaseRadius;
        float damage = SwingBaseDamage * (_empowered ? EmpoweredDamageMult : 1f);
        damage = ctx.CalculateDamage(damage);

        // 검격으로 사망 시 그 자리에 생성될 장판 파라미터 (트레일과 동일 규격)
        var onKillTrail = new SolarTrailParams
        {
            Damage       = ctx.CalculateDamage(TrailBaseDamage * (_empowered ? EmpoweredDamageMult : 1f)),
            Radius       = TrailBaseRadius,
            Duration     = TrailBaseDuration,
            TickInterval = TrailTickInterval,
        };

        GawainSwingZone.Spawn(origin, pt.rotation, damage, radius, SwingVisualDuration,
            ctx.Controller.gameObject, onKillTrail);
    }

    private void SpawnFireTrail(SkillExecutionContext ctx)
    {
        var pt = ctx.PlayerTransform;
        var origin = pt.position + pt.forward * SwingForward;
        float radius   = TrailBaseRadius;
        float damage   = TrailBaseDamage * (_empowered ? EmpoweredDamageMult : 1f);
        float duration = TrailBaseDuration;
        damage = ctx.CalculateDamage(damage);

        SolarFireZone.Spawn(origin, damage, radius, duration, TrailTickInterval, ctx.Controller.gameObject, chainGen: 0);
    }
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// SolarTrailParams — 검격 사망 시 / 기타 트리거에서 SolarFireZone 을 생성할 때 사용하는 파라미터 묶음.
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
public struct SolarTrailParams
{
    public float Damage;
    public float Radius;
    public float Duration;
    public float TickInterval;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// GawainSwingZone — 검격 1회 판정 오브젝트.
// SphereCollider(isTrigger) + kinematic Rigidbody 를 가지며,
// OnTriggerEnter/Stay 콜백으로 들어온 IDamageable 에 1회만 데미지를 적용한다.
// 또한 피격된 MonsterBase 가 검격 수명 내에 사망하면 그 자리에 SolarFireZone 을 생성한다.
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
public sealed class GawainSwingZone : MonoBehaviour
{
    private readonly HashSet<GameObject> _hit = new();
    private readonly HashSet<MonsterBase> _tracked = new();
    private float _damage;
    private float _remaining;
    private GameObject _instigator;
    private SolarTrailParams _onKillTrail;

    public static GawainSwingZone Spawn(Vector3 pos, Quaternion rot, float damage, float radius,
        float duration, GameObject instigator, SolarTrailParams onKillTrail)
    {
        var go = new GameObject("~GawainSwing");
        go.transform.SetPositionAndRotation(pos, rot);

        // 트리거 충돌체
        var col = go.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius    = radius;

        // 정적 콜라이더(몬스터)와의 OnTrigger 이벤트 발생을 위해 kinematic Rigidbody 부착
        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity  = false;

        var zone = go.AddComponent<GawainSwingZone>();
        zone._damage      = damage;
        zone._remaining   = duration;
        zone._instigator  = instigator;
        zone._onKillTrail = onKillTrail;

        SkillGuideVisual.BuildDisc(go.transform, "Guide_SwingArea", radius,
            new Color(1f, 0.55f, 0.1f, 0.40f));
        return zone;
    }

    private void Update()
    {
        _remaining -= Time.deltaTime;
        if (_remaining <= 0f) Destroy(gameObject);
    }

    private void OnDestroy()
    {
        // 수명이 끝났는데 남아 있는 OnDied 구독을 해제 — 늦은 사망(다른 출처)으로 인한 ghost spawn 방지
        foreach (var mb in _tracked)
            if (mb != null) mb.OnDied -= HandleMonsterDied;
        _tracked.Clear();
    }

    // 스폰 직후 첫 물리 스텝에서 들어오는 콜라이더
    private void OnTriggerEnter(Collider other) => TryHit(other);
    // 스폰 시점에 이미 트리거 안에 있던 콜라이더는 OnTriggerEnter 가 안 뜨므로 Stay 도 받음
    private void OnTriggerStay(Collider other)  => TryHit(other);

    private void TryHit(Collider col)
    {
        if (_instigator != null &&
            (col.transform == _instigator.transform || col.transform.IsChildOf(_instigator.transform)))
            return;

        var d = col.GetComponent<IDamageable>() ?? col.GetComponentInParent<IDamageable>();
        if (d == null) return;

        var hostGo = (d as Component)?.gameObject;
        if (hostGo == null || !_hit.Add(hostGo)) return;

        // 사망 추적 구독 (MonsterBase 만 대상) — 동일 인스턴스 중복 가입은 _tracked HashSet 으로 방지
        if (hostGo.TryGetComponent<MonsterBase>(out var mb) && _tracked.Add(mb))
            mb.OnDied += HandleMonsterDied;

        d.TakeDamage(_damage, _instigator, knockbackMultiplier: 1f);
    }

    private void HandleMonsterDied(MonsterBase mb)
    {
        if (mb == null) return;

        mb.OnDied -= HandleMonsterDied;
        _tracked.Remove(mb);

        // 사망 지점에 정식 장판 생성 — 이 장판이 다시 죽인 적이 있으면 정상 체인 진행
        SolarFireZone.Spawn(
            pos:          mb.transform.position,
            damage:       _onKillTrail.Damage,
            radius:       _onKillTrail.Radius,
            duration:     _onKillTrail.Duration,
            tickInterval: _onKillTrail.TickInterval,
            instigator:   _instigator,
            chainGen:     0);
    }
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// SolarFireZone — 태양의 검흔이 남기는 지속 피해 장판.
// SphereCollider(isTrigger) + kinematic Rigidbody 를 가지며,
// OnTriggerEnter/Exit 로 내부에 있는 IDamageable 집합을 추적해 주기적으로 데미지를 가한다.
// 본 장판 안에서 몬스터가 사망하면 그 자리에 축소된 연쇄 장판을 남긴다.
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
public sealed class SolarFireZone : MonoBehaviour
{
    // ── Chain 파라미터 ────────────────────────────────────────
    private const int   MaxChainGeneration  = 2;
    private const float ChainDamageRatio    = 0.6f;
    private const float ChainRadiusRatio    = 0.7f;
    private const float ChainDurationRatio  = 0.7f;

    // ── 상태 ──────────────────────────────────────────────────
    private float _damage;
    private float _radius;
    private float _remaining;
    private float _tickInterval;
    private float _tickAccum;
    private GameObject _instigator;
    private int _chainGeneration;

    // 한 IDamageable 호스트가 여러 자식 콜라이더를 가질 수 있어 ref-count
    private readonly Dictionary<IDamageable, int> _insideRefCount = new();
    private readonly HashSet<MonsterBase> _tracked = new();
    private readonly List<IDamageable> _tickSnapshot = new();

    // ── Factory ──────────────────────────────────────────────
    public static SolarFireZone Spawn(Vector3 pos, float damage, float radius, float duration,
        float tickInterval, GameObject instigator, int chainGen = 0)
    {
        var go = new GameObject(chainGen == 0 ? "~SolarFireZone" : $"~SolarFireZone_Chain{chainGen}");
        go.transform.position = pos;

        var col = go.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius    = radius;

        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity  = false;

        var zone = go.AddComponent<SolarFireZone>();
        zone.Initialize(damage, radius, duration, tickInterval, instigator, chainGen);
        return zone;
    }

    public void Initialize(float damage, float radius, float duration, float tickInterval,
        GameObject instigator, int chainGen = 0)
    {
        _damage           = damage;
        _radius           = radius;
        _remaining        = duration;
        _tickInterval     = tickInterval;
        _instigator       = instigator;
        _chainGeneration  = chainGen;

        float alpha = Mathf.Lerp(0.45f, 0.25f, chainGen / (float)MaxChainGeneration);
        SkillGuideVisual.BuildDisc(transform, "Guide_FireZoneArea", _radius,
            new Color(1f, 0.35f, 0.05f, alpha));
    }

    // ── Lifecycle ────────────────────────────────────────────
    private void Update()
    {
        _remaining -= Time.deltaTime;
        _tickAccum += Time.deltaTime;

        if (_tickAccum >= _tickInterval)
        {
            ApplyTickDamage();
            _tickAccum -= _tickInterval;
        }

        if (_remaining <= 0f) Destroy(gameObject);
    }

    private void OnDestroy()
    {
        foreach (var mb in _tracked)
            if (mb != null) mb.OnDied -= HandleMonsterDied;
        _tracked.Clear();
    }

    // ── 트리거 콜백 ──────────────────────────────────────────
    private void OnTriggerEnter(Collider other)
    {
        if (IsInstigator(other)) return;

        var d = other.GetComponent<IDamageable>() ?? other.GetComponentInParent<IDamageable>();
        if (d == null) return;

        _insideRefCount.TryGetValue(d, out int n);
        _insideRefCount[d] = n + 1;

        // 첫 진입 시(n==0) 연쇄 사망 구독
        if (n == 0 && _chainGeneration < MaxChainGeneration)
        {
            var hostGo = (d as Component)?.gameObject;
            if (hostGo != null && hostGo.TryGetComponent<MonsterBase>(out var mb) && _tracked.Add(mb))
                mb.OnDied += HandleMonsterDied;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (IsInstigator(other)) return;

        var d = other.GetComponent<IDamageable>() ?? other.GetComponentInParent<IDamageable>();
        if (d == null) return;

        if (_insideRefCount.TryGetValue(d, out int n))
        {
            if (n <= 1) _insideRefCount.Remove(d);
            else        _insideRefCount[d] = n - 1;
        }
    }

    private bool IsInstigator(Collider col)
    {
        if (_instigator == null) return false;
        return col.transform == _instigator.transform || col.transform.IsChildOf(_instigator.transform);
    }

    // ── 피해 적용 ────────────────────────────────────────────
    private void ApplyTickDamage()
    {
        // TakeDamage 도중 OnDied → Destroy → OnTriggerExit 가 재진입하며 사전을 변경할 수 있어 스냅샷
        _tickSnapshot.Clear();
        foreach (var d in _insideRefCount.Keys) _tickSnapshot.Add(d);

        for (int i = 0; i < _tickSnapshot.Count; i++)
        {
            var d = _tickSnapshot[i];
            if (d == null) continue;
            d.TakeDamage(_damage, _instigator, knockbackMultiplier: 0f);
        }
    }

    // ── 연쇄 처리 ────────────────────────────────────────────
    private void HandleMonsterDied(MonsterBase mb)
    {
        if (mb == null) return;

        mb.OnDied -= HandleMonsterDied;
        _tracked.Remove(mb);

        Spawn(
            pos:          mb.transform.position,
            damage:       _damage   * ChainDamageRatio,
            radius:       _radius   * ChainRadiusRatio,
            duration:     _remaining > 0f ? _remaining * ChainDurationRatio : 2.0f,
            tickInterval: _tickInterval,
            instigator:   _instigator,
            chainGen:     _chainGeneration + 1);
    }
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// SkillGuideVisual — placeholder 가이드 디스크 빌더.
// 추후 VFX 프리팹이 같은 부모에 attach 되면 이 디스크는 제거 또는 비활성화 가능.
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
public static class SkillGuideVisual
{
    public static Transform BuildDisc(Transform parent, string name, float radius, Color color)
    {
        var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        disc.name = name;
        if (disc.TryGetComponent<Collider>(out var c)) Object.Destroy(c);

        disc.transform.SetParent(parent, false);
        disc.transform.localPosition = new Vector3(0f, 0.02f, 0f);
        disc.transform.localScale    = new Vector3(radius * 2f, 0.01f, radius * 2f);

        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Standard");
        var mat = new Material(shader) { color = color };
        if (shader != null && shader.name.Contains("Universal"))
        {
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend",   0f);
            mat.SetFloat("_ZWrite",  0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = 3000;
        }
        disc.GetComponent<MeshRenderer>().material = mat;
        return disc.transform;
    }
}
