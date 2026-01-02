using UnityEngine;

public class EffectColliderSpawner : MonoBehaviour
{
    public GameObject effectPrefab;   // 실제 사용할 Effect Prefab
    public bool useTrigger = true;    // Trigger로 만들지 여부
    public LayerMask hitLayer;        // 충돌 대상 레이어
    public float colliderDuration = 0.3f; // Collider 유지 시간

    private void Start()
    {
        SpawnEffectWithCollider();
    }

    public void SpawnEffectWithCollider()
    {
        if (effectPrefab == null) return;

        // 1️⃣ Prefab Instantiate
        GameObject effectInstance = Instantiate(effectPrefab, transform.position, transform.rotation, transform);

        // 2️⃣ ColliderData 가져오기
        var ecd = effectInstance.GetComponent<EffectColliderData>();
        if (ecd == null)
        {
            Debug.LogWarning("EffectColliderData가 없음");
            return;
        }

        // 3️⃣ Collider 생성
        GameObject colliderGO = new GameObject("EffectCollider");
        colliderGO.transform.SetParent(effectInstance.transform, false);
        colliderGO.transform.localPosition = ecd.localCenter;

        BoxCollider box = colliderGO.AddComponent<BoxCollider>();
        box.size = ecd.localSize;
        box.isTrigger = useTrigger;

        // // 4️⃣ 충돌 감지용 스크립트 추가 (옵션)
        // var hit = colliderGO.AddComponent<EffectHitbox>();
        // hit.hitLayer = hitLayer;

        // 5️⃣ 일정 시간 후 Collider 제거
        Destroy(colliderGO, colliderDuration);
    }
}
