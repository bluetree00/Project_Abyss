using UnityEngine;

/// <summary>
/// 스타트 방 전용 Wisp 플레이어 컨트롤러.
/// WASD 이동 + F키로 CharacterDisplayStand 상호작용.
/// </summary>
[RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
public class WispController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float rotateSpeed = 720f;
    [SerializeField] private float interactRadius = 2.2f;

    private Rigidbody _rb;
    private IWispInteractable _nearbyStand;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
    }

    private void Update()
    {
        if (!Input.GetKeyDown(KeyCode.F))
            return;

        var target = _nearbyStand ?? FindNearestInteractable();
        if (target != null)
        {
            target.TrySelect(this);
            return;
        }

        Debug.Log("[WispController] No interactable target nearby.");
    }

    private void FixedUpdate()
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");

        var cam = Camera.main;
        Vector3 move;
        if (cam != null)
        {
            var forward = cam.transform.forward; forward.y = 0f; forward.Normalize();
            var right   = cam.transform.right;   right.y   = 0f; right.Normalize();
            move = forward * v + right * h;
        }
        else
        {
            move = new Vector3(h, 0f, v);
        }

        if (move.sqrMagnitude > 1f) move.Normalize();
        _rb.linearVelocity = new Vector3(move.x * moveSpeed, _rb.linearVelocity.y, move.z * moveSpeed);

        if (move.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(move),
                rotateSpeed * Time.fixedDeltaTime);
    }

    public void SetNearbyStand(IWispInteractable stand) => _nearbyStand = stand;

    public void ClearNearbyStand(IWispInteractable stand)
    {
        if (_nearbyStand == stand) _nearbyStand = null;
    }

    private IWispInteractable FindNearestInteractable()
    {
        var hits = Physics.OverlapSphere(transform.position, interactRadius, ~0, QueryTriggerInteraction.Collide);

        IWispInteractable nearest = null;
        float nearestSqr = float.MaxValue;

        foreach (var hit in hits)
        {
            if (hit == null) continue;

            var interactable = hit.GetComponentInParent<IWispInteractable>();
            if (interactable == null) continue;

            float sqr = (hit.transform.position - transform.position).sqrMagnitude;
            if (sqr >= nearestSqr) continue;

            nearest = interactable;
            nearestSqr = sqr;
        }

        return nearest;
    }
}
