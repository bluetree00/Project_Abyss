using UnityEngine;

namespace RelicFairy.Monster
{
public enum PropFlyStyle
{
    TipOver, // 의자: 옆으로 넘어지며 짧게 밀려남
    Slide,   // 카펫: 바닥을 타고 밀려남
}

[DisallowMultipleComponent]
public class EntrancePropDestructible : MonoBehaviour
{
    [SerializeField] private GameObject   _replacementPrefab;
    [SerializeField] private float        _flyForce     = 7f;
    [SerializeField] private float        _destroyDelay = 4f;
    [SerializeField] private PropFlyStyle _flyStyle     = PropFlyStyle.TipOver;

    public void SwapAndFly(Vector3 fromPos)
    {
        if (_replacementPrefab != null)
        {
            var replacement = Instantiate(_replacementPrefab, transform.position, transform.rotation);
            Launch(replacement, fromPos);
            Destroy(replacement, _destroyDelay);
        }
        Destroy(gameObject);
    }

    private void Launch(GameObject go, Vector3 fromPos)
    {
        foreach (var col in go.GetComponentsInChildren<Collider>(true))
            col.isTrigger = true;

        foreach (var childRb in go.GetComponentsInChildren<Rigidbody>(true))
            Destroy(childRb);

        var physCol = go.AddComponent<BoxCollider>();
        physCol.isTrigger = false;

        var mat = new PhysicsMaterial("_EntranceProp");
        mat.bounciness    = 0f;
        mat.bounceCombine = PhysicsMaterialCombine.Minimum;
        physCol.material  = mat;

        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = false;

        Vector3 raw = go.transform.position - fromPos;
        raw.y = 0f;
        Vector3 lateralDir = raw.sqrMagnitude > 0.001f ? raw.normalized : go.transform.forward;

        switch (_flyStyle)
        {
            case PropFlyStyle.TipOver:
                physCol.size   = new Vector3(0.7f, 0.9f, 0.7f);
                physCol.center = new Vector3(0f,   0.45f, 0f);
                mat.dynamicFriction = 0.35f;
                mat.staticFriction  = 0.35f;
                mat.frictionCombine = PhysicsMaterialCombine.Average;
                rb.mass           = 1.5f;
                rb.linearDamping  = 0.2f;
                rb.angularDamping = 4f;
                // Y 고정 해제 → 대각선 방향으로 눕힘 허용
                rb.AddForce(lateralDir * _flyForce * 2.0f, ForceMode.Impulse);
                // tipAxis: 이동방향으로 앞이 쓰러지는 주축
                // sideRatio: -1~1 랜덤 → lateralDir 축 토크가 좌우 대각선 성분 결정
                Vector3 tipAxis   = Vector3.Cross(Vector3.up, lateralDir).normalized;
                float   sideRatio = Random.Range(-1f, 1f);
                rb.AddTorque(tipAxis * _flyForce * 1.5f + lateralDir * (sideRatio * _flyForce * 0.9f),
                             ForceMode.Impulse);
                break;

            case PropFlyStyle.Slide:
                physCol.size   = new Vector3(1.4f, 0.08f, 1.4f);
                physCol.center = new Vector3(0f,   0.04f, 0f);
                mat.dynamicFriction = 0.02f;
                mat.staticFriction  = 0.02f;
                mat.frictionCombine = PhysicsMaterialCombine.Minimum;
                rb.mass           = 0.6f;
                rb.linearDamping  = 0.3f;
                rb.angularDamping = 999f;
                rb.constraints    = RigidbodyConstraints.FreezeRotationX
                                  | RigidbodyConstraints.FreezeRotationY
                                  | RigidbodyConstraints.FreezeRotationZ;
                rb.AddForce(lateralDir * _flyForce * 1.3f, ForceMode.Impulse);
                break;
        }
    }
}
}
