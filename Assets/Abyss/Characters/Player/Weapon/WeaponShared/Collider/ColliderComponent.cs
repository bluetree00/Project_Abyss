using UnityEngine;

public class ColliderComponent : MonoBehaviour
{
    public WeaponColliderData colliderData;

    public void Initialize(WeaponColliderData data)
    {
        colliderData = data;

        // Transform 초기화
        transform.localPosition = data.localPosition;
        transform.localEulerAngles = data.localEuler;
        transform.localScale = data.localScale;

        // Collider 크기/모양 초기화
        // 예: SphereCollider, BoxCollider 등
        SetupColliderShape();
    }

    private void SetupColliderShape()
    {
        switch (colliderData.shape)
        {
            case ColliderShape.Sphere:
                var sphere = gameObject.AddComponent<SphereCollider>();
                sphere.radius = colliderData.size.x;
                sphere.isTrigger = true;
                break;
            case ColliderShape.Box:
                var box = gameObject.AddComponent<BoxCollider>();
                box.size = colliderData.size;
                box.isTrigger = true;
                break;
            case ColliderShape.Capsule:
                var capsule = gameObject.AddComponent<CapsuleCollider>();
                capsule.radius = colliderData.size.x;
                capsule.height = colliderData.size.y;
                capsule.isTrigger = true;
                break;
        }
    }

    public void ActivateCollider()
    {
        // RuntimeData 기반으로 활성화, Hit 체크, Tick 처리 등
        Debug.Log($"Activate collider {colliderData.id} for {colliderData.activeDuration}s");
    }
}
