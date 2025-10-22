using UnityEngine;
using System;

[DisallowMultipleComponent]
public class PlayerAnimationEventReceiver : MonoBehaviour
{
    public PlayerController Target { get; private set; }

    // 인스턴스 이벤트 — 플레이어/다른 시스템이 구독
    public event Action OnAttackEnd;
    public event Action<int> OnHitStep;
    public event Action OnOpenCombo;
    public event Action OnCloseCombo;
    public event Action<string> OnGenericTag;

    #region Wiring
    public void SetTarget(PlayerController target)
    {
        Target = target;
    }

    private void EnsureTarget()
    {
        if (Target == null)
        {
            Target = GetComponent<PlayerController>() ?? GetComponentInParent<PlayerController>();
            if (Target == null)
                Debug.LogWarning("[PlayerAnimationEventReceiver] Target not set/found.");
        }
    }
    #endregion

    // AE 메서드들은 이제 이벤트만 발행
    public void AE_OpenCombo()
    {
        EnsureTarget();
        OnOpenCombo?.Invoke();
        Debug.Log("[AE] OpenCombo");
    }

    public void AE_CloseCombo()
    {
        EnsureTarget();
        OnCloseCombo?.Invoke();
        Debug.Log("[AE] CloseCombo");
    }

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
}
