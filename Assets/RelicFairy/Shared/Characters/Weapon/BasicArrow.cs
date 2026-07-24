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

        // IDamageable은 루트(MonsterBase/TrainingDummy)에 있고 피격 콜라이더는 자식(MonsterHit 레이어)이다.
        // 콜라이더 자신만 보면(TryGetComponent) 대상을 못 찾아 화살이 맞아도 피해가 0이었다.
        // 근접 판정(ActAttackState)과 동일한 해석 규칙으로 부모까지 훑는다.
        var damageable = other.GetComponent<IDamageable>() ?? other.GetComponentInParent<IDamageable>();
        GameObject victim = damageable is Component c ? c.gameObject : other.gameObject;

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
                HitPoint            = other.ClosestPoint(transform.position),
                SourcePosition      = _instigator != null ? _instigator.transform.position : transform.position,
                IsRanged            = true,
                SkipHitVfx          = true,
            });
        }

        SpawnHitEffect(other);

        // 폭발
        if (_explode)
            DoExplosion(other.ClosestPoint(transform.position));

        // 관통 처리
        if (_pierce)
        {
            _pierced?.Add(victim);
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
