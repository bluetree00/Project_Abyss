using UnityEngine;
using System;

/// <summary>
/// 애니메이션 이벤트를 수신해서 플레이어(또는 등록된 타겟)에 전달하는 중앙 리시버.
/// - Animation 이벤트에서 public 메서드로 직접 호출합니다.
/// - 메서드 이름(AE_OpenCombo 등)은 애니에서 동일하게 설정하세요.
/// </summary>
[DisallowMultipleComponent]
public class PlayerAnimationEventReceiver : MonoBehaviour
{
    // 전달 대상 (보통 PlayerController)
    public PlayerController Target { get; private set; }

    #region Helpers for wiring
    public void SetTarget(PlayerController target)
    {
        Target = target;
    }

    private void EnsureTarget()
    {
        if (Target == null)
        {
            // 자동 탐색(같은 GameObject 또는 부모에서)
            Target = GetComponent<PlayerController>() ?? GetComponentInParent<PlayerController>();
            if (Target == null)
                Debug.LogWarning("[PlayerAnimationEventReceiver] Target PlayerController not set and not found in parents.");
        }
    }
    #endregion

    // --------------------------
    // 애니메이션 이벤트로 호출되는 public API
    // (애니메이션 에디터에 이 메서드 이름을 넣어 호출)
    // --------------------------

    // 콤보 윈도우 열기
    public void AE_OpenCombo()
    {
        EnsureTarget();
        if (Target == null) return;
        Target.OpenComboWindow();
        if (Target != null) Debug.Log("[AE] OpenCombo");
    }

    // 콤보 윈도우 닫기
    public void AE_CloseCombo()
    {
        EnsureTarget();
        if (Target == null) return;
        Target.CloseComboWindow();
        Debug.Log("[AE] CloseCombo");
    }

    // 공격 애니 종료 (애니에서 AttackEnd 이벤트에 연결)
    public void AE_AttackEnd()
    {
        EnsureTarget();
        if (Target == null) return;
        Target.OnAttackAnimationEnd();
        Debug.Log("[AE] AttackEnd");
    }

    // 히트 스텝: 애니메이션 이벤트에서 int 매개변수로 스텝 인덱스 지정 가능
    // 예: AnimationEvent.functionName = "AE_HitStep"; parameter = 1
    public void AE_HitStep(int stepIndex)
    {
        EnsureTarget();
        if (Target == null) return;
        // PlayerController 안에 OnAttackHitStep 같은 공개 메서드를 구현해서 처리
        Target.OnAttackHitStep(stepIndex);
        Debug.Log($"[AE] HitStep {stepIndex}");
    }

    // 유연성: float / string 파라미터 받는 버전도 제공
    public void AE_HitStepFloat(float stepIndexFloat) => AE_HitStep(Mathf.RoundToInt(stepIndexFloat));
    public void AE_GenericString(string tag)
    {
        EnsureTarget();
        if (Target == null) return;
        Target.OnAnimationEventTag(tag);
        Debug.Log($"[AE] GenericTag '{tag}'");
    }
}
