using UnityEngine;
using RelicFairy.Monster;

/// <summary>
/// Mini dragon breath projectile. Added at runtime onto the spawned effect prefab.
/// 파티클 시스템을 수정하지 않고 실제 startSpeed를 읽어 GO 이동 속도를 맞춤.
/// 파티클 헤드 = GO 위치 = 콜라이더 위치 → 시각과 충돌 완전 동기화.
/// </summary>
public sealed class DragonBreathShot : MonoBehaviour
{
    private Vector3              _dir;
    private float                _speed;
    private float                _maxRange;
    private int                  _damage;
    private Vector3              _origin;
    private bool                 _hit;
    private PlayerStatusEffectSO _statusEffect;
    private GameObject           _attacker;   // 쏜 새끼 용(10-08 점검)
    private GameObject           _guide;      // 쏜 줄 바닥 예고 — 탄이 사라질 때 같이 치운다(10-08 점검)

    public void Init(Vector3 dir, float speed, float maxRange, int damage,
                     PlayerStatusEffectSO statusEffect = null, GameObject attacker = null, GameObject guide = null)
    {
        _attacker     = attacker;
        _guide        = guide;
        _dir          = dir.normalized;
        _maxRange     = maxRange;
        _damage       = damage;
        _statusEffect = statusEffect;
        _origin       = transform.position;
        _hit          = false;

        // 파티클 실제 startSpeed로 GO 속도를 맞춤 (파티클 시스템 설정은 변경하지 않음)
        var rootPs = GetComponent<ParticleSystem>() ?? GetComponentInChildren<ParticleSystem>(true);
        if (rootPs != null)
        {
            var   curve   = rootPs.main.startSpeed;
            float psSpeed = curve.mode == ParticleSystemCurveMode.TwoConstants
                ? (curve.constantMin + curve.constantMax) * 0.5f
                : curve.constant;
            _speed = psSpeed > 0.1f ? psSpeed : speed;
        }
        else
        {
            _speed = speed;
        }

        // Kinematic Rigidbody → OnTriggerEnter 신뢰성 보장
        if (!TryGetComponent<Rigidbody>(out _))
        {
            var rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity  = false;
        }

        if (!TryGetComponent<Collider>(out var col))
        {
            var sc       = gameObject.AddComponent<SphereCollider>();
            sc.radius    = 0.4f;
            sc.isTrigger = true;
        }
        else
        {
            col.isTrigger = true;
        }
    }

    private void Update()
    {
        if (_hit) return;
        transform.position += _dir * _speed * Time.deltaTime;
        if (Vector3.Distance(_origin, transform.position) >= _maxRange)
            Destroy(gameObject);
    }

    private void OnDestroy() => PatternGuideHelper.SafeDestroy(ref _guide);

    private void OnTriggerEnter(Collider other)
    {
        if (_hit) return;
        var player = other.GetComponent<PlayerController>()
                  ?? other.GetComponentInParent<PlayerController>();
        if (player == null) return;
        _hit = true;
        player.TakeDamage(_damage, _attacker != null ? _attacker : null);   // 공격자 = 새끼 용(사라졌으면 null) — 저스트 회피 · 넉백 방향 · 결과창(10-08 점검)
        _statusEffect?.Apply(player);
        Destroy(gameObject);
    }
}
