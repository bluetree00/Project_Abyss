using UnityEngine;

[RequireComponent(typeof(Transform))]
public class EffectBehaviour : MonoBehaviour
{
    public float lifetime = 1f;
    public EffectBehaviorSO behaviorSO;
    public Transform owner;

    private float _elapsed;
    private bool _running;
    private Vector3 _moveDirection;

    private void OnEnable()
    {
        _elapsed = 0f;
        _running = true;
        behaviorSO?.OnSpawn(gameObject, owner);
    }

    private void Update()
    {
        if (!_running) return;

        float dt = Time.deltaTime;
        _elapsed += dt;

        behaviorSO?.OnUpdate(gameObject, owner, dt, _moveDirection);

        // Scale lerp
        if (behaviorSO != null && behaviorSO.startScale != behaviorSO.endScale)
        {
            float effectDuration = behaviorSO.duration > 0f ? behaviorSO.duration : lifetime;
            float t = Mathf.Clamp01(_elapsed / Mathf.Max(effectDuration, 0.01f));
            float s = Mathf.Lerp(behaviorSO.startScale, behaviorSO.endScale, t);
            transform.localScale = Vector3.one * s;
        }

        float maxLife = (behaviorSO != null && behaviorSO.duration > 0f) ? behaviorSO.duration : lifetime;
        if (maxLife > 0f && _elapsed >= maxLife)
            StopAndReturnToPool();
    }

    public void StopAndReturnToPool()
    {
        if (!_running) return;
        _running = false;

        behaviorSO?.OnDespawn(gameObject, owner);
        Managers.ObjectPooler.Despawn(gameObject);
    }

    /// <summary>
    /// WeaponEffectHandler에서 호출. 소켓 기준 방향을 moveDirection으로 설정.
    /// </summary>
    public void Initialize(EffectBehaviorSO so, Transform ownerTransform, float lifeMultiplier,
                           Vector3? spawnForward = null)
    {
        behaviorSO = so;
        owner = ownerTransform;
        _elapsed = 0f;
        _running = true;

        // 수명 설정
        if (so != null && so.duration > 0f)
            lifetime = so.duration * lifeMultiplier;
        else
            lifetime = lifeMultiplier;

        // 이동 방향: 전달받은 spawnForward 우선, 없으면 owner.forward
        _moveDirection = spawnForward ?? (owner != null ? owner.forward.normalized : transform.forward.normalized);

        // Scale 초기화
        if (so != null)
            transform.localScale = Vector3.one * so.startScale;

        so?.OnSpawn(gameObject, owner);
    }

    // 레거시 호환 오버로드
    public void Initialize(EffectBehaviorSO so, Transform ownerTransform, float lifeMultiplier)
    {
        Initialize(so, ownerTransform, lifeMultiplier, null);
    }
}
