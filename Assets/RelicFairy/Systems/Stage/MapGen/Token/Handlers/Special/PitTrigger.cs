using UnityEngine;

/// <summary>
/// 낙사 구멍(Pt 토큰) 위에 배치되는 즉시 감지 트리거.
/// 플레이어가 구멍으로 진입하는 순간 FallRecoveryController.ForceRecover()를 호출한다.
/// </summary>
public sealed class PitTrigger : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        var recovery = other.GetComponentInParent<FallRecoveryController>();
        recovery?.ForceRecover();
    }
}
