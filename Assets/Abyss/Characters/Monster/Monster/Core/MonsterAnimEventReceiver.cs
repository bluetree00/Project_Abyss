using Abyss.Monster;
using UnityEngine;

/// <summary>
/// Receives AnimationEvent callbacks and forwards them to MonsterBase.
/// Keep compatibility methods for third-party clip event names.
/// </summary>
public class MonsterAnimEventReceiver : MonoBehaviour
{
    private MonsterBase _monster;

    private void Awake()
    {
        _monster = GetComponent<MonsterBase>() ?? GetComponentInParent<MonsterBase>();

        if (_monster == null)
            Debug.LogWarning("[MonsterAnimEventReceiver] MonsterBase was not found.", this);
    }

    public void OnAttackStart() { }

    public void OnAttackEnd() { }

    public void OnAttackHit()
    {
        _monster?.OnAnimAttackHit();
    }

    // Third-party clips sometimes use this name.
    public void OnAttackEvent()
    {
        _monster?.OnAnimAttackHit();
    }

    // Compatibility aliases for imported clips.
    public void OnStartHitEvent()
    {
        OnAttackStart();
    }

    public void OnHitEvent()
    {
        _monster?.OnAnimAttackHit();
    }

    public void OnEndHitEvent()
    {
        OnAttackEnd();
    }

    public void OnGetHitEnd() { }

    public void OnDieEnd() { }

    // Third-party clips (Malbers) fire this event; silently ignore.
    public void PlaySound() { }

    // Malbers audio event aliases
    public void PlaySound(int index) { }
    public void StopSound() { }
}
