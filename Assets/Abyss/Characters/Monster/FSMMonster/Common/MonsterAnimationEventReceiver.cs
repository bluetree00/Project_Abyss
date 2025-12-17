using System;
using UnityEngine;

public abstract class MonsterAnimationEventReceiver : MonoBehaviour
{
    protected MonsterController controller;

    public event Action OnAttackStartEvent;
    public event Action OnAttackEndEvent;

    protected void RaiseAttackStartEvent() => OnAttackStartEvent?.Invoke();
    protected void RaiseAttackEndEvent() => OnAttackEndEvent?.Invoke();

    protected virtual void Awake()
    {
        if (controller == null)
            controller = GetComponent<MonsterController>() ?? GetComponentInParent<MonsterController>();

        if (controller == null)
            Debug.LogWarning($"[{GetType().Name}] MonsterController를 찾을 수 없습니다.");
        else
            OnAfterControllerResolved();
    }

    protected virtual void OnAfterControllerResolved() { }

    public abstract void OnAttackStart();
    public abstract void OnAttackEnd();

    protected virtual bool TryGetAttackEffect(out string effectName, out Vector3 offset, out Vector3 rotationEuler)
    {
        effectName = default;
        offset = default;
        rotationEuler = default;

        if (controller == null || controller.EffectProfile == null)
            return false;

        effectName = controller.EffectProfile.attackEffect;
        offset = controller.EffectProfile.attackEffectOffset;
        rotationEuler = controller.EffectProfile.attackEffectRotation;
        return true;
    }

    protected virtual void SpawnAttackEffect(string effectName, Vector3 spawnPos, Quaternion spawnRot)
    {
        // 기본 구현: 효과 생성은 선택 사항. 필요 시 하위 클래스에서 오버라이드하거나
        // 프로젝트의 이펙트/오브젝트 풀 매니저를 호출하세요.
        // 예) Managers.ObjectPool.Spawn(effectName, spawnPos, spawnRot);
    }
}
