using UnityEngine;

[CreateAssetMenu(menuName = "Game/ColliderBehaviorSO")]
public class ColliderBehaviorSO : ScriptableObject
{
    [Header("Optional Parameters")]
    public float moveSpeed = 0f;          // 콜라이더 전진 속도
    public Vector3 rotationPerSecond;     // 회전
    public bool followTarget = false;     // 타겟 추적
    public float activeDuration = 1f;     // 활성화 시간
    public LayerMask targetLayer;         // 타격 대상 레이어

    /// <summary>
    /// 런타임 콜라이더에 적용되는 동작
    /// </summary>
    public virtual void ApplyColliderBehavior(GameObject col, Transform owner)
    {
        if (col == null) return;

        // 단순 이동
        if (moveSpeed != 0f)
        {
            col.transform.position += owner.forward * moveSpeed * Time.deltaTime;
        }

        // 회전
        col.transform.Rotate(rotationPerSecond * Time.deltaTime, Space.Self);

        // 타겟 추적
        if (followTarget && owner != null)
        {
            col.transform.LookAt(owner.position + Vector3.up * 1f);
        }

        // 특정 레이어만 충돌 처리 등 추가 가능
    }
}
