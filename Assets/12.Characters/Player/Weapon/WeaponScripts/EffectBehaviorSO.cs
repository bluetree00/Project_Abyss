using UnityEngine;

[CreateAssetMenu(menuName = "Game/EffectBehaviorSO")]
public class EffectBehaviorSO : ScriptableObject
{
    [Header("Optional Parameters")]
    public float moveSpeed = 0f;      // 전진 속도
    public Vector3 rotationPerSecond; // 지속 회전 속도
    public bool followTarget = false; // 타겟 추적 여부

    /// <summary>
    /// 런타임 이펙트에 적용되는 동작
    /// </summary>
    public virtual void ApplyEffectBehavior(GameObject effectInstance, Transform owner)
    {
        if (effectInstance == null) return;

        // 예시: 전진
        if (moveSpeed != 0f)
        {
            effectInstance.transform.position += owner.forward * moveSpeed * Time.deltaTime;
        }

        // 예시: 회전
        effectInstance.transform.Rotate(rotationPerSecond * Time.deltaTime, Space.Self);

        // 타겟 추적 (선택적)
        if (followTarget && owner != null)
        {
            effectInstance.transform.LookAt(owner.position + Vector3.up * 1f);
        }
    }
}
