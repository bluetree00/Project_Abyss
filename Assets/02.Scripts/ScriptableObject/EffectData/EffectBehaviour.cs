// EffectBehaviour.cs
using UnityEngine;

[RequireComponent(typeof(Transform))]
public class EffectBehaviour : MonoBehaviour
{
    public float lifetime = 1f; // 기본 생존 시간
    public EffectBehaviorSO behaviorSO; // 이펙트 동작 정의 (optional)
    public Transform owner; // 보통 플레이어/무기 같은 것

    private float _elapsed;
    private bool _running;

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

        // SO의 업데이트 로직 호출
        behaviorSO?.OnUpdate(gameObject, owner, dt);

        // (추가) 자체적인 업데이트 로직도 가능
        // 예: lifetime 외의 조건으로 꺼야할 때

        if (_elapsed >= lifetime)
        {
            StopAndReturnToPool();
        }
    }

    /// <summary>
    /// 이 이펙트를 즉시 중지하고 풀로 반환합니다.
    /// </summary>
    public void StopAndReturnToPool()
    {
        if (!_running) return;
        _running = false;

        behaviorSO?.OnDespawn(gameObject, owner);

        // 풀로 반환 (Managers.ObjectPooler은 사용자 구현)
        Managers.ObjectPooler.ReturnToPool(gameObject);
    }

    private void OnDisable()
    {
        // 풀에서 비활성화 될 때 추가 정리 필요하면 여기에
        _running = false;
    }

    /// <summary>
    /// 외부에서 초기화 편의용 메서드
    /// </summary>
    public void Initialize(EffectBehaviorSO so, Transform ownerTransform, float life)
    {
        behaviorSO = so;
        owner = ownerTransform;
        lifetime = life;
        _elapsed = 0f;
        _running = true;
    }
}
