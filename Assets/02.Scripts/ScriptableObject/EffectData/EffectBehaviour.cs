using UnityEngine;

[RequireComponent(typeof(Transform))]
public class EffectBehaviour : MonoBehaviour
{
    public float lifetime = 1f;
    public EffectBehaviorSO behaviorSO;
    public Transform owner;

    private float _elapsed;
    private bool _running;
    private Vector3 _moveDirection; // 스폰 시 고정된 이동 방향

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

        // SO의 업데이트 실행
        behaviorSO?.OnUpdate(gameObject, owner, dt, _moveDirection);

        if (_elapsed >= lifetime)
            StopAndReturnToPool();
    }

    public void StopAndReturnToPool()
    {
        if (!_running) return;
        _running = false;

        behaviorSO?.OnDespawn(gameObject, owner);
        Managers.ObjectPooler.ReturnToPool(gameObject);
    }

    public void Initialize(EffectBehaviorSO so, Transform ownerTransform, float life)
    {
        behaviorSO = so;
        owner = ownerTransform;
        lifetime = life;
        _elapsed = 0f;
        _running = true;

        // 스폰 시 플레이어의 정면을 이동 방향으로 고정
        _moveDirection = owner != null ? owner.forward.normalized : transform.forward.normalized;

        //이펙트의 정면을 x축 기준으로 맞춤
        transform.rotation = Quaternion.LookRotation(owner.forward, Vector3.up) * Quaternion.Euler(0, -90f, 0);

        behaviorSO?.OnSpawn(gameObject, owner);
    }
}
