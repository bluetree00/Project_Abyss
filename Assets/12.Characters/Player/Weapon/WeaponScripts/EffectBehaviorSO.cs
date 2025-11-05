using UnityEngine;

[CreateAssetMenu(menuName = "Game/EffectBehaviorSO")]
public class EffectBehaviorSO : ScriptableObject
{
    [Header("Optional Parameters")]
    public float moveSpeed = 0f;
    public Vector3 rotationPerSecond;

    public virtual void OnSpawn(GameObject effectInstance, Transform owner) { }

    // --- 이동 방향을 외부에서 전달받음 ---
     public virtual void OnUpdate(GameObject effectInstance, Transform owner, float deltaTime, Vector3 moveDir)
    {
        if (effectInstance == null) return;

        if (moveSpeed != 0f)
        {
            effectInstance.transform.position += moveDir * moveSpeed * deltaTime;
        }

        if (rotationPerSecond != Vector3.zero)
            effectInstance.transform.Rotate(rotationPerSecond * deltaTime, Space.Self);
    }

    public virtual void OnDespawn(GameObject effectInstance, Transform owner) { }
}
