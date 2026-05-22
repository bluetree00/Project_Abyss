using UnityEngine;
using System;

[DisallowMultipleComponent]
public class PlayerAnimationEventReceiver : MonoBehaviour
{
    public PlayerController Target { get; private set; }

    // 인스턴스 이벤트 — 플레이어/다른 시스템이 구독
    public event Action OnAttackEnd;
    public event Action<int> OnHitStep;
    public event Action<int> OnEffectStep;
    public event Action<string> OnGenericTag;
    public event Action OnBeginTrail;
    public event Action OnEndTrail;

    public void AE_EffectStep(int step)
    {
        EnsureTarget();
        OnEffectStep?.Invoke(step);  // 플레이어가 구독 중이면 step 전달됨
        Debug.Log($"[AE] EffectStep {step}");
    }

    #region Wiring
    public void SetTarget(PlayerController target)
    {
        Target = target;
    }

    private void EnsureTarget()
    {
        if (Target != null) return;

        if (!TryGetComponent<PlayerController>(out var found))
            found = GetComponentInParent<PlayerController>();

        Target = found;
        if (Target == null)
            Debug.LogWarning("[PlayerAnimationEventReceiver] Target not set/found.");
    }
    #endregion

    // AE 메서드들은 이제 이벤트만 발행
    public void AE_AttackEnd()
    {
        EnsureTarget();
        OnAttackEnd?.Invoke();
        Debug.Log("[AE] AttackEnd");
    }

    public void AE_HitStep(int stepIndex)
    {
        EnsureTarget();
        OnHitStep?.Invoke(stepIndex);
        Debug.Log($"[AE] HitStep {stepIndex}");
    }

    public void AE_HitStepFloat(float stepIndexFloat) => AE_HitStep(Mathf.RoundToInt(stepIndexFloat));

    public void AE_GenericString(string tag)
    {
        EnsureTarget();
        OnGenericTag?.Invoke(tag);
        Debug.Log($"[AE] GenericTag '{tag}'");
    }

    public void AE_BeginTrail()
    {
        EnsureTarget();
        OnBeginTrail?.Invoke();
        Debug.Log("[AE] BeginTrail");
    }

    public void AE_EndTrail()
    {
        EnsureTarget();
        OnEndTrail?.Invoke();
        Debug.Log("[AE] EndTrail");
    }

    // 슬래시 이펙트 애니메이션 이벤트 — 번호별로 EffectStep에 매핑
    public void SpawnSlashEffect0() => AE_EffectStep(0);
    public void SpawnSlashEffect1() => AE_EffectStep(1);
    public void SpawnSlashEffect2() => AE_EffectStep(2);
    public void SpawnSlashEffect3() => AE_EffectStep(3);
    public void SpawnSlashEffect4() => AE_EffectStep(4);

}
