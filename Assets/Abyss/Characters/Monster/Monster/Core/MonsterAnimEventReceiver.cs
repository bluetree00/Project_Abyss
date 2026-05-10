using Abyss.Monster;
using Cysharp.Threading.Tasks;
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

    // MagicAttack1 클립의 CastSpell 이벤트 — ThrowRock은 타이머 기반으로 발사하므로 무시.
    public void CastSpell() { }

    // Third-party clips (Malbers) fire this event with a string key.
    public void PlaySound(string key)
    {
        if (!string.IsNullOrEmpty(key))
            Managers.Sound?.PlayEffectAsync(key).Forget();
    }

    // Malbers: no-arg or index-based overloads — no key, silently ignore.
    public void PlaySound() { }
    public void PlaySound(int index) { }
    public void StopSound() { }
}
