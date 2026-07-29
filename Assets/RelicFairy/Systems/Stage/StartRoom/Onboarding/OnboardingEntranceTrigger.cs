using UnityEngine;

/// <summary>
/// 온보딩 구역 입구 트리거. 플레이어가 최초로 진입하면 Director.OnEntered()를 1회 호출 →
/// 유물 연출부터 온보딩 시퀀스 시작. 구역으로 들어오는 길목에 배치한다.
/// </summary>
[RequireComponent(typeof(Collider))]
public sealed class OnboardingEntranceTrigger : MonoBehaviour
{
    [SerializeField] private BaseCampOnboardingDirector director;

    private bool _fired;

    private void Reset()
    {
        if (TryGetComponent<Collider>(out var col)) col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_fired || director == null) return;
        if (other.GetComponentInParent<PlayerController>() == null) return;

        _fired = true;
        director.OnEntered();
    }
}
