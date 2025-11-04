// EffectBehaviorSO.cs (기본에 OnDespawn 추가)
using UnityEngine;

[CreateAssetMenu(menuName = "Game/EffectBehaviorSO")]
public class EffectBehaviorSO : ScriptableObject //상속을 통해 내부 기능을 확장하는것으로 다양한 공격 구현
{
    [Header("Optional Parameters")]
    public float moveSpeed = 0f;
    public Vector3 rotationPerSecond;
    public bool followTarget = false;

    public virtual void OnSpawn(GameObject effectInstance, Transform owner) { }

    public virtual void OnUpdate(GameObject effectInstance, Transform owner, float deltaTime)
    {
        if (effectInstance == null) return;

        if (moveSpeed != 0f && owner != null)
            effectInstance.transform.position += owner.forward * moveSpeed * deltaTime;

        if (rotationPerSecond != Vector3.zero)
            effectInstance.transform.Rotate(rotationPerSecond * deltaTime, Space.Self);

        if (followTarget && owner != null)
        {
            var targetPos = owner.position + Vector3.up * 1f;
            var dir = (targetPos - effectInstance.transform.position);
            if (dir.sqrMagnitude > 0.001f)
            {
                effectInstance.transform.rotation = Quaternion.Slerp(
                    effectInstance.transform.rotation,
                    Quaternion.LookRotation(dir.normalized),
                    Mathf.Clamp01(deltaTime * 10f)
                );
            }
        }
    }

    // 반환/정리 시 호출 (파티클 Stop 등)
    public virtual void OnDespawn(GameObject effectInstance, Transform owner) { }
}
