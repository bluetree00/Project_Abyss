using UnityEngine;

[CreateAssetMenu(menuName = "Game/EffectBehaviorSO")]
public class EffectBehaviorSO : ScriptableObject
{
    // ── Movement ──────────────────────────────────────────────
    public enum MoveType { None, Forward, Homing, Parabolic }

    [Header("Movement")]
    public MoveType moveType = MoveType.None;
    public float moveSpeed = 0f;
    public float acceleration = 0f;
    public float homingStrength = 5f;

    // ── Rotation ──────────────────────────────────────────────
    [Header("Rotation")]
    public Vector3 rotationPerSecond;
    public bool faceMovementDir = false;

    // ── Lifetime ──────────────────────────────────────────────
    [Header("Lifetime")]
    [Tooltip("0 이하면 EffectBehaviour.lifetime 사용")]
    public float duration = 0f;
    public float fadeOutTime = 0f;
    public bool destroyOnHit = false;

    // ── Scale ─────────────────────────────────────────────────
    [Header("Scale")]
    public float startScale = 1f;
    public float endScale = 1f;

    // ── Callbacks ─────────────────────────────────────────────

    public virtual void OnSpawn(GameObject effectInstance, Transform owner) { }

    public virtual void OnUpdate(GameObject effectInstance, Transform owner, float deltaTime, Vector3 moveDir)
    {
        if (effectInstance == null) return;

        // Movement
        if (moveType != MoveType.None && moveSpeed != 0f)
        {
            effectInstance.transform.position += moveDir * moveSpeed * deltaTime;
        }

        // Rotation
        if (rotationPerSecond != Vector3.zero)
            effectInstance.transform.Rotate(rotationPerSecond * deltaTime, Space.Self);

        if (faceMovementDir && moveDir != Vector3.zero)
            effectInstance.transform.rotation = Quaternion.LookRotation(moveDir);
    }

    public virtual void OnDespawn(GameObject effectInstance, Transform owner) { }
}
