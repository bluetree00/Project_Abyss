using UnityEngine;

/// <summary>
/// 카메라 시선 방향(heading) 전환 존.
///
/// 플레이어가 이 구역에 들어오면 게임 카메라의 수평 각도를 지정 방향으로 <b>부드럽게</b> 돌린다.
/// 인트로처럼 "내부에서 나오다가 어느 지점에서 목적지가 보이도록 시선이 돌아가는" 연출용이다.
/// 플레이어를 회전시키지 않고 카메라만 돌리므로 조작감을 건드리지 않는다.
///
/// CinemachineFreeLook의 수평각은 평소 이전 값을 유지하므로, 이런 전환은 명시적으로 걸어줘야 한다.
/// 실제 회전은 <see cref="GameCameraController.RotateHeadingTo"/>가 수행.
/// </summary>
[RequireComponent(typeof(Collider))]
public sealed class CameraHeadingZone : MonoBehaviour
{
    [Header("목표 방향")]
    [SerializeField, Tooltip("돌릴 목표 각도(Y, 월드 기준). 0=+Z, 90=+X")]
    private float targetYaw = 90f;

    [SerializeField, Tooltip("이 오브젝트의 forward를 목표 방향으로 사용(위 각도값 무시). 씬에서 화살표로 맞추기 편함")]
    private bool useTransformForward;

    [Header("연출")]
    [SerializeField, Tooltip("회전에 걸리는 시간(초)")]
    private float duration = 1.5f;

    [SerializeField, Tooltip("한 번만 작동(초회 연출). 끄면 들어올 때마다 회전")]
    private bool once = true;

    private bool _fired;

    private void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_fired || !IsPlayer(other)) return;
        if (once) _fired = true;

        float yaw = useTransformForward ? transform.eulerAngles.y : targetYaw;
        GameCameraController.Instance?.RotateHeadingTo(yaw, duration);
    }

    private static bool IsPlayer(Collider col)
        => col.GetComponentInParent<PlayerController>() != null;
}
