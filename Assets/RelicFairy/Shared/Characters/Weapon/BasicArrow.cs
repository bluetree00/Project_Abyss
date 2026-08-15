using System.Collections.Generic;
using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 기본 화살. <b>풀에서 재사용</b>되므로 발사 간 상태가 새지 않게 하는 게 핵심이다
/// (<see cref="IPooledObject"/>로 스폰·복귀 시점을 잡는다).
/// </summary>
public class BasicArrow : MonoBehaviour, IPooledObject
{
    /// <summary>히트 VFX 안전 수명(초). CombatDamage의 클램프 구간(0.5~3s) 안쪽 값.</summary>
    private const float HitVfxLife = 2f;

    /// <summary>유도 목표 탐색 반경(m)·최대 후보 수. 매 프레임이 아니라 목표를 잃었을 때만 돈다.</summary>
    private const float HomingSearchRadius = 18f;
    private const int   HomingSearchMax    = 8;

    /// <summary>유도 탐색용 공유 버퍼 — 투사체마다 리스트를 만들면 발사마다 할당이 생긴다.</summary>
    private static readonly List<MonsterBase> s_homingBuffer = new();

    /// <summary>스윕 1회가 담을 수 있는 최대 충돌 수.</summary>
    private const int SweepMax = 12;
    private static readonly RaycastHit[] s_sweepHits = new RaycastHit[SweepMax];
    private static readonly SweepDistanceComparer s_sweepOrder = new();

    /// <summary>가까운 충돌부터 처리해야 화살이 첫 대상에서 멈춘다.</summary>
    private sealed class SweepDistanceComparer : System.Collections.Generic.IComparer<RaycastHit>
    {
        public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
    }

    [SerializeField] private float speed = 30f;

    /// <summary>
    /// 스윕 판정 반경(m). 프리팹 콜라이더(반지름 0.05)는 속도 30m/s에 비해 너무 얇아
    /// 물리 스텝 사이(0.6m)를 통째로 건너뛴다 — 판정은 콜라이더가 아니라 이 값으로 한다.
    /// </summary>
    [SerializeField] private float hitRadius = 0.35f;

    [SerializeField] private float damage = 20f;
    [SerializeField] private float lifetime = 5f;
    [SerializeField] private Vector3 modelRotationOffset = new Vector3(0f, -90f, 0f);
    [SerializeField] private string hitEffectKey = "BlueShootHit";
    [SerializeField] private float hitEffectScale = 0.5f;

    private Vector3 direction;
    private GameObject _instigator;
    private float _timer;

    // ── 스킬 전용 옵션 (Fire 시 외부에서 설정) ──
    private bool _pierce;
    private int _maxPierceCount = 99;
    private int _pierceCount;
    private HashSet<GameObject> _pierced;

    private bool _explode;
    private float _explodeRadius;
    private float _explodeDamage;
    private string _explodeEffectKey;
    private float _explodeEffectScale;

    // 투사체 비주얼 이펙트 (스킬용)
    private GameObject _visualEffect;
    private bool _hideModel;

    // ── 원거리 파츠 옵션 (CombatSpawner가 설정) ──
    /// <summary>속도 배율. 프리팹 speed는 [SerializeField]라 직접 못 바꾸므로 배율로 얹는다.</summary>
    private float _speedMult = 1f;
    /// <summary>유도 선회 강도(0=직선). 초당 회전 각도로 쓴다 — 에임 보정이 아니라 궤적 조작.</summary>
    private float _homing;
    /// <summary>관통 후 뒤로 선회해 되돌아올지(유도 × 관통 시너지).</summary>
    private bool  _returnOnPierce;
    private bool  _returning;
    private Transform _homingTarget;

    // ── 풀 재사용 안전장치 ──────────────────────────────────
    private TrailRenderer _trail;
    private MeshRenderer  _meshRenderer;

    /// <summary>이미 회수됐는지. 이중 Despawn을 막는다 — 같은 인스턴스가 큐에 두 번 들어가면
    /// 다음 두 발이 같은 오브젝트를 받아 한 발이 다른 발 위치로 순간이동한다.</summary>
    private bool _dead;

    /// <summary>발사 세대. 비동기 이펙트 로드가 끝났을 때 "그때 그 발사"가 맞는지 확인하는 토큰.</summary>
    private int _generation;

    /// <summary>
    /// 첫 이동 전까지 트레일 기록을 막는 구간인지.
    ///
    /// 발사까지 좌표가 여러 번 옮겨진다 — 풀에서 소켓 위치로 꺼내고, 총구 위치로 옮기고,
    /// 부채꼴 갈래는 각자 옆으로 벌어진 지점에서 시작한다. 그 사이 트레일이 정점을 기록하면
    /// 이전 지점과 새 지점을 잇는 줄이 남아 "화살은 여기 있는데 꼬리는 저기서 온" 모양이 된다.
    /// Clear만으로는 옮겨질 때마다 쫓아다녀야 하므로, 아예 첫 Update까지 기록을 끈다.
    /// </summary>
    private bool _trailWarmup;

    // ── Lifecycle ───────────────────────────────────────────

    private void Awake()
    {
        _trail        = GetComponent<TrailRenderer>();
        _meshRenderer = GetComponent<MeshRenderer>();
    }

    // ── 풀 훅 ───────────────────────────────────────────────

    /// <summary>
    /// 풀에서 꺼내진 직후. 풀러가 위치를 먼저 세팅한 뒤 부르므로, 여기서 지운 트레일은
    /// 새 총구 위치에서 다시 시작한다.
    /// </summary>
    public void OnSpawn(object param = null)
    {
        _dead = false;
        _generation++;
        ResetTrail();
    }

    /// <summary>풀로 돌아가기 직전. 죽은 자리의 잔상을 남기지 않는다.</summary>
    public void OnDespawn()
    {
        CleanupVisualEffect();
        ResetTrail();
    }

    /// <summary>
    /// 트레일 정점 제거. <b>비활성화만으로는 지워지지 않는다</b> — Clear를 부르지 않으면
    /// 이전에 죽은 위치와 새 발사 위치를 잇는 선이 한 프레임 그려져, 저쪽에서 화살이
    /// 되돌아오는 것처럼 보인다.
    /// </summary>
    private void ResetTrail()
    {
        if (_trail == null) return;
        _trail.emitting = false;   // 이 시점 이후의 좌표 이동을 기록하지 않는다
        _trail.Clear();
        _trailWarmup = true;
    }

    /// <summary>첫 이동을 마친 뒤 트레일을 실제 비행 경로에서 다시 시작시킨다.</summary>
    private void BeginTrail()
    {
        _trailWarmup = false;
        if (_trail == null) return;
        _trail.Clear();            // 워밍업 동안 혹시 남은 정점 제거
        _trail.emitting = true;
    }

    /// <summary>기본 발사</summary>
    public void Fire(Vector3 dir, GameObject instigator = null, float dmg = -1f)
    {
        direction = dir.normalized;
        _instigator = instigator;
        if (dmg >= 0f) damage = dmg;
        // 아이템 사거리 보너스: speed 기반으로 lifetime 연장
        float rangeBonus = GameRunBootstrapper.Instance?.Run?.Player?.RuntimeStats?.RangedRangeBonus ?? 0f;
        float bonusTime = speed > 0f ? rangeBonus / speed : 0f;
        _timer = lifetime + bonusTime;

        _pierce = false;
        _explode = false;
        _pierceCount = 0;
        _pierced = null;
        _hideModel = false;
        _visualEffect = null;

        // 파츠 옵션은 발사마다 초기화 — 풀에서 재사용되므로 이전 발사의 설정이 남으면 안 된다.
        _speedMult      = 1f;
        _homing         = 0f;
        _returnOnPierce = false;
        _returning      = false;
        _homingTarget   = null;

        SetModelVisible(true);
        gameObject.SetActive(true);
        ApplyRotation();

        // 풀을 거치지 않고 직접 Fire되는 경로(이미 활성인 인스턴스 재발사)에서도 잔상이 남지 않게.
        _dead = false;
        ResetTrail();
    }

    /// <summary>관통 설정</summary>
    public void SetPierce(int maxCount = 99)
    {
        _pierce = true;
        _maxPierceCount = maxCount;
        _pierceCount = 0;
        _pierced = new HashSet<GameObject>();
    }

    /// <summary>폭발 설정</summary>
    public void SetExplosion(float radius, float explosionDamage, string effectKey = "", float effectScale = 1f)
    {
        _explode = true;
        _explodeRadius = radius;
        _explodeDamage = explosionDamage;
        _explodeEffectKey = effectKey;
        _explodeEffectScale = effectScale;
    }

    /// <summary>속도 배율 설정(원거리 파츠). 프리팹 speed에 곱해진다.</summary>
    public void SetSpeedMultiplier(float mult) => _speedMult = Mathf.Max(0.05f, mult);

    /// <summary>
    /// 유도 설정(원거리 파츠). strength=초당 선회 각도, 0이면 직선.
    /// returnOnPierce가 켜지면 관통 후 뒤로 돌아 되돌아온다 — 유도 × 관통 결합 시너지.
    /// </summary>
    public void SetHoming(float strength, bool returnOnPierce)
    {
        _homing         = Mathf.Max(0f, strength);
        _returnOnPierce = returnOnPierce;
    }

    /// <summary>투사체 비주얼을 이펙트로 교체 (스킬용). 모델/트레일을 숨기고 이펙트를 자식으로 부착.</summary>
    public async void SetVisualEffect(string effectKey, float scale = 1f)
    {
        _hideModel = true;
        SetModelVisible(false);

        // 이펙트는 발사 방향(direction) 기준 회전 — modelRotationOffset 적용 안 함
        Quaternion effectRot = direction != Vector3.zero
            ? Quaternion.LookRotation(direction)
            : transform.rotation;

        int gen = _generation;   // 이 발사의 세대를 기억해 둔다

        var fx = await Managers.ObjectPooler.SpawnAsync(
            effectKey, ObjectPoolerManager.PoolType.Effect,
            transform.position, effectRot);
        if (fx == null) return;

        // 로드를 기다리는 동안 화살이 죽고 풀에서 재사용됐다면, 이 이펙트는 '남의 발사'에 붙는다.
        // 그대로 두면 이전 발사의 비주얼이 새 화살을 타고 다닌다.
        if (gen != _generation || _dead || this == null)
        {
            Managers.ObjectPooler?.Despawn(fx);
            return;
        }

        fx.transform.SetParent(transform, true);  // worldPositionStays=true
        fx.transform.localScale = Vector3.one * scale;
        _visualEffect = fx;

        // 트레일/파티클 잔상 제거
        foreach (var trail in fx.GetComponentsInChildren<TrailRenderer>(true))
            trail.Clear();
        foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
        {
            ps.Clear(true);
            ps.Play(true);
        }
    }

    private void SetModelVisible(bool visible)
    {
        if (_meshRenderer != null) _meshRenderer.enabled = visible;
        if (_trail != null) _trail.enabled = visible;
    }

    private void ApplyRotation()
    {
        if (direction != Vector3.zero)
            transform.rotation = Quaternion.LookRotation(direction) * Quaternion.Euler(modelRotationOffset);
    }

    private void Update()
    {
        if (direction == Vector3.zero) return;

        if (_homing > 0f) SteerHoming(Time.deltaTime);

        // 이동은 물리가 아니라 좌표 대입이라, 트리거만 믿으면 물리 스텝 사이(속도 30 → 0.6m)를
        // 통째로 건너뛴다. 프리팹 콜라이더가 반지름 0.05m라 그 틈에 몬스터가 통으로 들어가
        // "직선 화살은 안 맞고 선회하는 유도 화살만 맞는" 증상이 났다.
        // → 이번 프레임에 지나갈 구간을 먼저 훑고, 그다음에 옮긴다.
        float stepDist = speed * _speedMult * Time.deltaTime;
        SweepForward(transform.position, direction, stepDist);
        if (_dead) return;

        transform.position += direction * stepDist;

        // 좌표 재배치가 모두 끝난 뒤 첫 이동을 마친 지금부터 기록을 시작한다.
        if (_trailWarmup) BeginTrail();

        _timer -= Time.deltaTime;
        if (_timer <= 0f)
            Deactivate();
    }

    /// <summary>이동 구간을 구체로 훑어 충돌을 찾는다. 프레임률·물리 틱과 무관하게 같은 판정이 나온다.</summary>
    private void SweepForward(Vector3 from, Vector3 dir, float dist)
    {
        if (dist <= 0f) return;

        int n = Physics.SphereCastNonAlloc(from, hitRadius, dir, s_sweepHits, dist,
                                           ~0, QueryTriggerInteraction.Collide);
        if (n <= 0) return;
        if (n > 1) System.Array.Sort(s_sweepHits, 0, n, s_sweepOrder);

        for (int i = 0; i < n; i++)
        {
            if (_dead) return;   // 앞쪽 대상에서 이미 소멸했으면 뒤는 보지 않는다

            var col = s_sweepHits[i].collider;
            if (col == null) continue;

            // 시작 지점에 이미 겹쳐 있으면 point가 (0,0,0)으로 온다 — 그때는 출발점을 쓴다.
            Vector3 point = s_sweepHits[i].distance > 0f ? s_sweepHits[i].point : from;
            HandleHit(col, point);
        }
    }

    /// <summary>
    /// 유도 — 목표를 향해 <b>궤적을 선회</b>시킨다(순간 방향 전환이 아니라 초당 각도 제한).
    /// 되돌아오는 중이면 시전자를 목표로 삼아 부메랑처럼 돌아온다.
    /// </summary>
    private void SteerHoming(float dt)
    {
        Transform goal = _returning
            ? (_instigator != null ? _instigator.transform : null)
            : AcquireTarget();
        if (goal == null) return;

        Vector3 desired = (goal.position + Vector3.up * 0.8f) - transform.position;
        if (desired.sqrMagnitude < 0.01f) return;

        direction = Vector3.RotateTowards(direction, desired.normalized,
                                          _homing * Mathf.Deg2Rad * dt, 0f).normalized;
        ApplyRotation();
    }

    /// <summary>가장 가까운 살아있는 적. 이미 관통한 대상은 제외해 같은 적을 다시 쫓지 않는다.</summary>
    private Transform AcquireTarget()
    {
        // 매 프레임 재탐색하지 않는다 — 대상이 살아있으면 그대로 유지(탐색 비용·궤적 흔들림 방지).
        if (_homingTarget != null && _homingTarget.gameObject.activeInHierarchy) return _homingTarget;

        int n = CombatQuery.GetNearbyEnemies(transform.position, HomingSearchRadius, _instigator,
                                             HomingSearchMax, s_homingBuffer);
        for (int i = 0; i < n; i++)
        {
            var mb = s_homingBuffer[i];
            if (mb == null) continue;
            if (_pierced != null && _pierced.Contains(mb.gameObject)) continue;   // 이미 뚫은 적은 건너뛴다
            _homingTarget = mb.transform;
            return _homingTarget;
        }
        return null;
    }

    /// <summary>
    /// 몬스터가 스스로 화살 위로 걸어 들어오는 경우를 위한 보조 경로.
    /// 주 판정은 <see cref="SweepForward"/>이며, 중복은 _dead(비관통)와 _pierced(관통)가 막는다.
    /// </summary>
    private void OnTriggerEnter(Collider other)
    {
        if (other == null) return;
        HandleHit(other, other.ClosestPoint(transform.position));
    }

    private void HandleHit(Collider other, Vector3 hitPoint)
    {
        if (_dead) return;

        // 시전자 본인은 건너뛴다. 스윕은 총구(플레이어 앞 1m)에서 시작하므로 자식 콜라이더까지 확인해야
        // 자기 몸에 맞고 사라지지 않는다 — 예전 트리거 경로는 루트만 봤다.
        if (_instigator != null &&
            (other.gameObject == _instigator || other.transform.IsChildOf(_instigator.transform))) return;
        if (other.transform == transform || other.transform.IsChildOf(transform)) return;

        // IDamageable은 루트(MonsterBase/TrainingDummy)에 있고 피격 콜라이더는 자식(MonsterHit 레이어)이다.
        // 콜라이더 자신만 보면(TryGetComponent) 대상을 못 찾아 화살이 맞아도 피해가 0이었다.
        // 근접 판정(ActAttackState)과 동일한 해석 규칙으로 부모까지 훑는다.
        var damageable = other.GetComponent<IDamageable>() ?? other.GetComponentInParent<IDamageable>();
        GameObject victim = damageable is Component c ? c.gameObject : other.gameObject;

        // 피해 대상이 아닌 '트리거 볼륨'은 통과시킨다.
        // 이 아래는 어떤 콜라이더든 히트 이펙트를 띄우고 Deactivate() 한다 — 즉 예전엔 게이트·배리어·
        // 존 진입 트리거·제단 상호작용 범위 같은 비물리 볼륨에 화살이 닿는 즉시 사라졌다.
        // 보스룸처럼 트리거가 깔린 방에서 "화살이 안 맞는" 증상의 원인.
        // 벽·바닥 같은 실체 콜라이더(비트리거)에는 그대로 막혀야 하므로 isTrigger인 것만 무시한다.
        if (damageable == null && other.isTrigger) return;

        // 관통 중복 판정도 콜라이더가 아니라 대상 단위로 — 몬스터가 콜라이더를 여러 개 가지면 중복 피격된다.
        if (_pierce && _pierced != null && _pierced.Contains(victim)) return;

        if (damageable != null)
        {
            // 주 피해 파이프라인 위임 — 예전엔 이 아래로 파이프라인(사전보정·서약·크릿·타격감·사후효과)을
            // 통째로 복제해 두고 있었다. 콜라이더 경로와 따로 놀며 드리프트하던 원인이라 단일 경로로 합쳤다.
            //  · IsRanged   : 확정크릿/다음공격강화가 화살로 새지 않도록(기존 meleeAttack:false 보존)
            //  · SkipHitVfx : 화살은 아래 SpawnHitEffect로 자체 히트 VFX를 띄운다(이중 스폰 방지)
            CombatDamage.Deal(new CombatDamage.Request
            {
                Target              = victim,
                BaseDamage          = damage,
                Owner               = _instigator,
                ActionType          = WeaponActionType.GroundLight,
                KnockbackMultiplier = 1f,
                HitPoint            = hitPoint,
                SourcePosition      = _instigator != null ? _instigator.transform.position : transform.position,
                IsRanged            = true,
                SkipHitVfx          = true,
            });
        }

        SpawnHitEffect(hitPoint);

        // 폭발
        if (_explode)
            DoExplosion(hitPoint);

        // 관통 처리
        if (_pierce)
        {
            _pierced?.Add(victim);
            _pierceCount++;
            _homingTarget = null;   // 뚫은 적은 놓아주고 다음 목표를 찾는다

            if (_pierceCount >= _maxPierceCount)
            {
                // [유도 × 관통] 관통을 다 쓰면 사라지는 대신 뒤로 돌아 되돌아온다.
                // 돌아오는 동안 관통 기록을 비워 같은 적을 다시 때릴 수 있게 한다(재타격이 이 조합의 보상).
                if (_returnOnPierce && !_returning && _homing > 0f)
                {
                    _returning = true;
                    _pierceCount = 0;
                    _pierced?.Clear();
                }
                else Deactivate();
            }
            // 관통 중이면 비활성화하지 않음
        }
        else
        {
            Deactivate();
        }
    }

    private void Deactivate()
    {
        // 같은 프레임에 타이머 만료와 충돌이 겹치면 두 번 불릴 수 있다. 그대로 두면 Despawn이
        // 두 번 돌아 같은 인스턴스가 큐에 중복 등록되고, 다음 두 발이 같은 오브젝트를 받는다.
        if (_dead) return;
        _dead = true;

        CleanupVisualEffect();
        SetModelVisible(true);

        // SetActive(false)만 하면 풀의 대기열로 돌아가지 않는다 — 그 인스턴스는 영영 재사용되지 않고
        // 화살을 쏠 때마다 풀이 새 인스턴스를 찍어낸다(평타 1발 = 1누수). Despawn이 비활성화까지 처리한다.
        //
        // 풀러 자체가 없는 시점(씬 정리·종료 등)에는 Despawn을 못 부른다. 그때 아무것도 안 하면
        // 화살이 활성인 채로 남아 계속 날아가며 충돌한다 — 최소한 비활성화는 보장한다.
        if (Managers.ObjectPooler != null)
            Managers.ObjectPooler.Despawn(gameObject);
        else
            gameObject.SetActive(false);
    }

    private void CleanupVisualEffect()
    {
        if (_visualEffect != null)
        {
            _visualEffect.transform.SetParent(null);
            Managers.ObjectPooler.Despawn(_visualEffect);
            _visualEffect = null;
        }
    }

    private void DoExplosion(Vector3 center)
    {
        // 폭발 이펙트
        if (!string.IsNullOrEmpty(_explodeEffectKey))
            SpawnEffectAt(center, _explodeEffectKey, _explodeEffectScale);

        // 범위 데미지
        var hits = Physics.OverlapSphere(center, _explodeRadius);
        foreach (var col in hits)
        {
            // continue여야 한다 — return이면 시전자 콜라이더를 만나는 순간 나머지 대상이 통째로 스킵됐다.
            if (col.gameObject == _instigator) continue;

            // 직접 판정과 동일하게 루트의 IDamageable을 찾는다(콜라이더는 자식에 있다).
            var d = col.GetComponent<IDamageable>() ?? col.GetComponentInParent<IDamageable>();
            if (d == null) continue;

            GameObject victim = d is Component c ? c.gameObject : col.gameObject;
            // 직접 맞은 대상은 이미 데미지 받음 — 주변 적만
            if (_pierce && _pierced != null && _pierced.Contains(victim)) continue;

            d.TakeDamage(_explodeDamage, _instigator);
        }
    }

    private async void SpawnHitEffect(Vector3 hitPos)
    {
        if (string.IsNullOrEmpty(hitEffectKey)) return;

        var fx = await Managers.ObjectPooler.SpawnAsync(
            hitEffectKey, ObjectPoolerManager.PoolType.Effect,
            hitPos, Quaternion.identity);

        if (fx == null) return;
        fx.transform.localScale = Vector3.one * hitEffectScale;
        EnsureVfxLifetime(fx);
    }

    private static async void SpawnEffectAt(Vector3 pos, string key, float scale)
    {
        var fx = await Managers.ObjectPooler.SpawnAsync(
            key, ObjectPoolerManager.PoolType.Effect,
            pos, Quaternion.identity);
        if (fx == null) return;
        fx.transform.localScale = Vector3.one * scale;
        EnsureVfxLifetime(fx);
    }

    /// <summary>
    /// 스스로 회수되지 못하는 히트 VFX에 수명을 붙인다(CombatDamage.SpawnHitEffectAsync와 같은 방식).
    /// EffectBehaviour가 있으면 그쪽이 회수하므로 건드리지 않는다 — 이중 Despawn 방지.
    /// </summary>
    private static void EnsureVfxLifetime(GameObject fx)
    {
        if (fx.GetComponent<EffectBehaviour>() != null) return;

        if (!fx.TryGetComponent<PooledVfxLifetime>(out var life))
            life = fx.AddComponent<PooledVfxLifetime>();
        life.SetLife(HitVfxLife);
    }
}
