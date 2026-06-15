using UnityEngine;

/// <summary>
/// Hovl Studio 호환 스텁 — 원본 에셋에서 누락된 스크립트 대체.
/// </summary>
public class HS_ProjectileMover : MonoBehaviour
{
    public float speed = 15f;
    public float hitOffset = 0f;
    public bool UseFirePointRotation;
    public Vector3 rotationOffset = new Vector3(0, 0, 0);
    public GameObject hit;
    public GameObject flash;
    public GameObject[] Detached;

    private Rigidbody rb;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        if (rb != null) rb.linearVelocity = transform.forward * speed;
    }
}
