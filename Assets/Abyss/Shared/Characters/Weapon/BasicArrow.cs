using System.Collections.Generic;
using UnityEngine;

public class BasicArrow : MonoBehaviour
{
    [SerializeField] private float speed = 30f;
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

        SetModelVisible(true);
        gameObject.SetActive(true);
        ApplyRotation();
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

    /// <summary>투사체 비주얼을 이펙트로 교체 (스킬용). 모델/트레일을 숨기고 이펙트를 자식으로 부착.</summary>
    public async void SetVisualEffect(string effectKey, float scale = 1f)
    {
        _hideModel = true;
        SetModelVisible(false);

        // 이펙트는 발사 방향(direction) 기준 회전 — modelRotationOffset 적용 안 함
        Quaternion effectRot = direction != Vector3.zero
            ? Quaternion.LookRotation(direction)
            : transform.rotation;

        var fx = await Managers.ObjectPooler.SpawnAsync(
            effectKey, ObjectPoolerManager.PoolType.Effect,
            transform.position, effectRot);
        if (fx == null) return;

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
        var meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer != null) meshRenderer.enabled = visible;
        var trail = GetComponent<TrailRenderer>();
        if (trail != null) trail.enabled = visible;
    }

    private void ApplyRotation()
    {
        if (direction != Vector3.zero)
            transform.rotation = Quaternion.LookRotation(direction) * Quaternion.Euler(modelRotationOffset);
    }

    private void Update()
    {
        if (direction == Vector3.zero) return;

        transform.position += direction * speed * Time.deltaTime;

        _timer -= Time.deltaTime;
        if (_timer <= 0f)
            Deactivate();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.gameObject == _instigator) return;
        if (_pierce && _pierced != null && _pierced.Contains(other.gameObject)) return;

        if (other.TryGetComponent<IDamageable>(out var damageable))
        {
            var mgr = GameRunBootstrapper.Instance?.Run?.EffectManager;
            var weaponElem = GameRunBootstrapper.Instance?.Run?.Player?.WeaponManager?.CurrentWeaponData?.element ?? WeaponElement.None;
            var pkt = new DamagePacket(damage, _instigator, other.gameObject, weaponElem);
            mgr?.OnPreDealDamage(ref pkt);

            float finalDmg = pkt.Negated ? 0f : pkt.FinalDamage;
            var elemType = pkt.Element.ToElementType();
            damageable.TakeDamage(finalDmg, _instigator, 1f, elemType, finalDmg);

            var report = new DamageReport
            {
                DamageDealt = finalDmg,
                Attacker = _instigator,
                Target = other.gameObject,
                HitPosition = other.ClosestPoint(transform.position),
            };
            mgr?.OnPostDealDamage(report);
        }

        SpawnHitEffect(other);

        // 폭발
        if (_explode)
            DoExplosion(other.ClosestPoint(transform.position));

        // 관통 처리
        if (_pierce)
        {
            _pierced?.Add(other.gameObject);
            _pierceCount++;
            if (_pierceCount >= _maxPierceCount)
                Deactivate();
            // 관통 중이면 비활성화하지 않음
        }
        else
        {
            Deactivate();
        }
    }

    private void Deactivate()
    {
        CleanupVisualEffect();
        SetModelVisible(true);
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
            if (col.gameObject == _instigator) return;
            // 직접 맞은 대상은 이미 데미지 받음 — 주변 적만
            if (_pierce && _pierced != null && _pierced.Contains(col.gameObject)) continue;

            if (col.TryGetComponent<IDamageable>(out var d))
                d.TakeDamage(_explodeDamage, _instigator);
        }
    }

    private async void SpawnHitEffect(Collider other)
    {
        if (string.IsNullOrEmpty(hitEffectKey)) return;

        Vector3 hitPos = other.ClosestPoint(transform.position);
        var fx = await Managers.ObjectPooler.SpawnAsync(
            hitEffectKey, ObjectPoolerManager.PoolType.Effect,
            hitPos, Quaternion.identity);

        if (fx == null) return;
        fx.transform.localScale = Vector3.one * hitEffectScale;
    }

    private static async void SpawnEffectAt(Vector3 pos, string key, float scale)
    {
        var fx = await Managers.ObjectPooler.SpawnAsync(
            key, ObjectPoolerManager.PoolType.Effect,
            pos, Quaternion.identity);
        if (fx == null) return;
        fx.transform.localScale = Vector3.one * scale;
    }
}
