using UnityEngine;

/// <summary>
/// 구역별 카메라 세팅. 플레이어가 트리거에 들어오면 이 구역의 오빗(높이·거리)과 시선각(heading)을 적용한다.
///
/// <b>릴레이 방식</b>이 기본이다 — 이탈해도 해제하지 않고, 다음 구역에 들어가면 그 구역이 덮어쓴다.
/// 그래서 되돌아가거나 구역 밖으로 잠깐 벗어나도 카메라가 튀지 않는다.
/// 잠깐 들르는 곳(제단 클로즈업 등)만 <see cref="restoreOnExit"/>를 켜서 이탈 시 기본값으로 돌린다.
///
/// 배치 규칙:
///  · 구역을 <b>겹치거나 비지 않게 이어붙인다</b> → 어느 순간에도 구역이 하나뿐이라 우선순위 로직이 필요 없다.
///  · 진행축 방향으로 <b>최소 5m</b> 이상 잡는다. 대시(약 20m/s)는 물리 스텝당 0.4m를 건너뛰므로
///    얇은 구역은 통과해도 트리거가 안 걸린다.
///
/// 획득·각성처럼 <b>위치가 아니라 사건</b>에 묶인 연출은 이 컴포넌트가 아니라
/// BaseCampOnboardingDirector(퀘스트 보고 → RevealAsync)가 담당한다.
/// </summary>
[RequireComponent(typeof(Collider))]
public sealed class CameraZone : MonoBehaviour
{
    public enum Axis { X, Y, Z }

    [Header("오빗 (높이, 거리)")]
    [SerializeField, Tooltip("끄면 오빗은 건드리지 않고 heading만 적용")]
    private bool applyOrbit = true;
    [SerializeField] private Vector2 orbitTop    = new Vector2(8.8f, 3.4f);
    [SerializeField] private Vector2 orbitMiddle = new Vector2(7.3f, 4.0f);
    [SerializeField] private Vector2 orbitBottom = new Vector2(4.8f, 3.8f);

    [Header("시선각 (heading)")]
    [SerializeField, Tooltip("끄면 시선각은 그대로 두고 오빗만 적용")]
    private bool applyHeading;
    [SerializeField, Tooltip("목표 각도(Y, 월드 기준). 0=+Z, 90=+X")]
    private float targetYaw = 90f;

    [Header("전환")]
    [SerializeField] private float transitionDuration = 1.2f;
    [SerializeField, Tooltip("켜면 이탈 시 기본 오빗으로 복귀(잠깐 들르는 구역용). 끄면 릴레이(유지)")]
    private bool restoreOnExit;

    [Header("역방향 복귀")]
    [SerializeField, Tooltip("진입 방향으로 되돌아 나가면 시선각을 되돌린다. 앞으로 통과해 나가면 유지")]
    private bool restoreHeadingOnBackExit;
    [SerializeField, Tooltip("되돌아 나갔을 때 복귀할 각도(보통 구역 진입 전 각도)")]
    private float backYaw;
    [SerializeField, Tooltip("앞/뒤를 가르는 축(구역 로컬). 보통 진행 방향축")]
    private Axis backExitAxis = Axis.X;
    [SerializeField, Tooltip("뒤쪽이 축의 +방향이면 켠다")]
    private bool backIsPositive;

    [Header("적용 조건")]
    [SerializeField, Tooltip("초회(온보딩 미완료)에만 작동")]
    private bool firstRunOnly;
    [SerializeField, Tooltip("재방문(온보딩 완료)에만 작동")]
    private bool repeatRunOnly;

    private bool _inside;

    private void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_inside || !IsPlayer(other) || !IsEnabledForThisRun()) return;
        _inside = true;

        var cam = GameCameraController.Instance;
        if (cam == null) return;

        if (applyOrbit)   cam.ApplyZoneOrbit(orbitTop, orbitMiddle, orbitBottom, transitionDuration);
        if (applyHeading) cam.RotateHeadingTo(targetYaw, transitionDuration);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!_inside || !IsPlayer(other)) return;
        _inside = false;

        var cam = GameCameraController.Instance;
        if (cam == null) return;

        // 되돌아 나감 → 시선각 복귀. 앞으로 통과해 나가면 그대로 유지(릴레이).
        // 축 하나로 판정하므로 구역을 진행 방향에 맞춰 놓아야 한다.
        if (applyHeading && restoreHeadingOnBackExit && ExitedBackward(other.transform.position))
            cam.RotateHeadingTo(backYaw, transitionDuration);

        // 릴레이가 기본 — 이탈해도 오빗은 유지한다. 복귀는 명시적으로 켠 구역만.
        if (restoreOnExit && applyOrbit)
            cam.RestoreZoneOrbit(transitionDuration);
    }

    private void OnDisable()
    {
        // 구역 비활성/파괴 시 복귀형 구역이 걸린 채 남지 않게 정리.
        if (_inside && restoreOnExit && applyOrbit)
            GameCameraController.Instance?.RestoreZoneOrbit(transitionDuration);
        _inside = false;
    }

    // ── Private Methods ───────────────────────────────────────

    /// <summary>구역 로컬 기준으로 뒤쪽(진입 방향)으로 나갔는지.</summary>
    private bool ExitedBackward(Vector3 worldPos)
    {
        Vector3 local = transform.InverseTransformPoint(worldPos);
        float v;
        switch (backExitAxis)
        {
            case Axis.Y: v = local.y; break;
            case Axis.Z: v = local.z; break;
            default:     v = local.x; break;
        }
        return backIsPositive ? v > 0f : v < 0f;
    }

    private bool IsEnabledForThisRun()
    {
        bool completed = BaseCampOnboardingDirector.IsCompleted;
        if (firstRunOnly  && completed)  return false;
        if (repeatRunOnly && !completed) return false;
        return true;
    }

    private static bool IsPlayer(Collider col)
        => col.GetComponentInParent<PlayerController>() != null;
}
