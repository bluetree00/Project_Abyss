using UnityEngine;
using System;
using System.Diagnostics;
using Debug = UnityEngine.Debug;

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
        Trace($"[AE] EffectStep {step}");
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
        Trace("[AE] AttackEnd");
    }

    public void AE_HitStep(int stepIndex)
    {
        EnsureTarget();
        OnHitStep?.Invoke(stepIndex);
        Trace($"[AE] HitStep {stepIndex}");
    }

    public void AE_HitStepFloat(float stepIndexFloat) => AE_HitStep(Mathf.RoundToInt(stepIndexFloat));

    public void AE_GenericString(string tag)
    {
        EnsureTarget();
        OnGenericTag?.Invoke(tag);
        Trace($"[AE] GenericTag '{tag}'");
    }

    public void AE_BeginTrail()
    {
        EnsureTarget();
        OnBeginTrail?.Invoke();
        Trace("[AE] BeginTrail");
    }

    public void AE_EndTrail()
    {
        EnsureTarget();
        OnEndTrail?.Invoke();
        Trace("[AE] EndTrail");
    }

    /// <summary>일부 클립(JumpAttack02_Root_1)에 배선된 FrontAttack 이벤트 수신부.
    /// 없으면 재생 때마다 "수신 메서드 없음" 경고가 뜬다. 태그 채널로 흘려보낸다.</summary>
    public void FrontAttack() => AE_GenericString("FrontAttack");

    /// <summary>애니 이벤트 추적 로그. 릴리스 빌드에서는 호출 자체가 컴파일에서 제거된다
    /// (인자 문자열 보간도 함께 사라짐) — 콤보당 4~5회 찍히던 비용을 없앤다.</summary>
    [Conditional("UNITY_EDITOR")]
    private static void Trace(string message) => Debug.Log(message);

    // 슬래시 이펙트 애니메이션 이벤트 — 번호별로 EffectStep에 매핑
    public void SpawnSlashEffect0() => AE_EffectStep(0);
    public void SpawnSlashEffect1() => AE_EffectStep(1);
    public void SpawnSlashEffect2() => AE_EffectStep(2);
    public void SpawnSlashEffect3() => AE_EffectStep(3);
    public void SpawnSlashEffect4() => AE_EffectStep(4);

}
