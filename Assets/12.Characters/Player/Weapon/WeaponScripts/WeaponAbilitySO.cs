using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// AbilitySO: 하나의 공격(여러 스텝 포함)
// (이전 설계의 AbilitySO/AbilityStep 사용)
[CreateAssetMenu(menuName = "Game/AbilitySO")]
public class WeaponAbilitySO : ScriptableObject
{
    public string abilityKey;
    public float cooldown;
    public List<AbilityStep> steps = new List<AbilityStep>();

    private Dictionary<int, AbilityStep> _stepMap;
    public void EnsureCache() {
        if (_stepMap != null) return;
        _stepMap = new Dictionary<int, AbilityStep>();
        foreach (var s in steps) _stepMap[s.eventIndex] = s;
    }

    // public void HandleStep(int eventIndex, IAbilityExecutor executor, AbilityContext ctx) {
    //     EnsureCache();
    //     if (_stepMap != null && _stepMap.TryGetValue(eventIndex, out var step)) {
    //         step.Execute(executor, ctx, this);
    //     } else {
    //         // 폴백: eventIndex-1로 접근
    //         if (steps.Count > 0) steps[Mathf.Clamp(eventIndex-1, 0, steps.Count-1)].Execute(executor, ctx, this);
    //         else Debug.LogWarning($"Ability {abilityKey} no steps for eventIndex {eventIndex}");
    //     }
    // }
}

[Serializable]
public class AbilityStep {
    public int eventIndex = 1;
    public string effectId;
    public List<string> colliderIds = new List<string>();
    public float damage = 10f;
    public string actionKey;

    // public void Execute(IAbilityExecutor executor, AbilityContext ctx, AbilitySO parent) {
    //     executor.SpawnEffectsForStep(parent, this, ctx);
    //     executor.SpawnCollidersForStep(parent, this, ctx);
    //     executor.OnStepAction(parent, this, ctx);
    //     // damage often applied via hit detection callback; optional immediate:
    //     // executor.ApplyDamageForStep(parent, this, ctx);
    // }
}
