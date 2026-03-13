using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 하나의 공격/스킬 실행 동안 생성된 오브젝트의 소유권을 추적하고 정리합니다.
/// 각 전투 상태(ActAttackState, ActQSkillState 등)가 Enter 시 생성하고 Exit 시 Cleanup 합니다.
///
/// [합쳐진 프리팹 모드]
///   이펙트 프리팹 안에 ColliderInstance가 포함된 경우,
///   RegisterEffect() 하나로 이펙트+콜라이더를 모두 추적합니다.
///   EffectBehaviour.StopAndReturnToPool()이 전체 수명을 관리합니다.
///
/// [별도 콜라이더 모드]
///   RegisterCollider()로 따로 추적합니다. (투명 판정 등 VFX 없는 경우)
/// </summary>
public class AbilityExecution
{
    // 이펙트 (합쳐진 프리팹 포함) — 정상 종료 시 자체 소멸, 캔슬 시 강제 종료
    private readonly List<GameObject> _effects = new();

    // 별도 콜라이더 — 항상 즉시 제거
    private readonly List<GameObject> _colliders = new();

    // oneShot 스텝 추적 — 공유 SO에 상태를 두지 않기 위해 실행 컨텍스트가 관리
    private readonly HashSet<WeaponAbilitySO.AbilityStep> _firedOneShotSteps = new();

    public void RegisterEffect(GameObject obj)
    {
        if (obj != null) _effects.Add(obj);
    }

    public void RegisterCollider(GameObject obj)
    {
        if (obj != null) _colliders.Add(obj);
    }

    /// <summary>
    /// oneShot 스텝을 이번 실행에서 처음 발동하면 true, 이미 발동했으면 false.
    /// </summary>
    public bool TryFireOneShot(WeaponAbilitySO.AbilityStep step)
    {
        if (_firedOneShotSteps.Contains(step)) return false;
        _firedOneShotSteps.Add(step);
        return true;
    }

    /// <summary>
    /// 별도 콜라이더는 항상 즉시 제거.
    /// forceEffects=true 이면 이펙트(합쳐진 프리팹 포함)도 강제 종료.
    /// </summary>
    public void Cleanup(bool forceEffects = false)
    {
        // 별도 콜라이더는 항상 즉시 제거
        foreach (var col in _colliders)
        {
            if (col == null) continue;
            var ci = col.GetComponent<ColliderInstance>();
            if (ci != null && !string.IsNullOrEmpty(ci.payloadKey))
                Managers.ObjectPooler.Despawn(col);
            else
                UnityEngine.Object.Destroy(col);
        }
        _colliders.Clear();

        if (forceEffects)
        {
            foreach (var fx in _effects)
            {
                if (fx == null) continue;
                var behaviour = fx.GetComponent<EffectBehaviour>();
                if (behaviour != null)
                    behaviour.StopAndReturnToPool(); // EffectBehaviour가 ColliderInstance도 같이 제거
                else
                    UnityEngine.Object.Destroy(fx);
            }
            _effects.Clear();
        }
    }
}
