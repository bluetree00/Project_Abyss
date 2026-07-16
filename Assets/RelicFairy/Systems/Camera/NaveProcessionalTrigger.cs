using UnityEngine;

/// <summary>
/// 대성당 나브(진입 아케이드) 구간 카메라 트리거.
/// 플레이어가 존에 들어오면 게임 카메라를 <b>낮은 정면 프로세셔널 시점</b>으로 전환해
/// 위로 솟은 아치 아케이드가 타워링·수렴하는 웅장함을 만든다. 이탈하면 게임플레이 시점 복원.
///
/// 탑다운 기본 시점은 수직 스케일을 눌러 아케이드가 옆으로 지나치는 방해물처럼 보인다 →
/// 이 구간만 카메라를 낮추고(옵션: 진행방향 리센터) 정면으로 본다. 실제 전환은 <see cref="GameCameraController"/>가 수행.
/// </summary>
[RequireComponent(typeof(Collider))]
public sealed class NaveProcessionalTrigger : MonoBehaviour
{
    [SerializeField] private float transitionDuration = 1.2f;

    private bool _active;

    private void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_active || !IsPlayer(other)) return;
        _active = true;
        GameCameraController.Instance?.ActivateProcessionalView(transitionDuration);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!_active || !IsPlayer(other)) return;
        _active = false;
        GameCameraController.Instance?.DeactivateProcessionalView(transitionDuration);
    }

    private void OnDisable()
    {
        // 존 비활성/파괴 시 시점이 낮은 채로 남지 않게 복원.
        if (_active)
        {
            _active = false;
            GameCameraController.Instance?.DeactivateProcessionalView(transitionDuration);
        }
    }

    private static bool IsPlayer(Collider col)
        => col.GetComponentInParent<PlayerController>() != null;
}
