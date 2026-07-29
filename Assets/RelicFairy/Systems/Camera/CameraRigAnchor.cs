using Cinemachine;
using UnityEngine;

/// <summary>
/// 카메라 리그 기준점을 플레이어보다 앞에 둔다 — 구도(각도·회전)는 그대로, 위치만 앞으로.
///
/// FreeLook은 <b>Follow</b>로 궤도 중심(=카메라 위치)을, <b>LookAt</b>으로 조준점(=카메라 회전)을 잡는다.
/// 둘을 <b>같은 벡터만큼 함께</b> 옮기면 카메라도 정확히 그만큼 평행이동하고,
/// 조준점과의 상대 위치가 불변이라 <b>회전은 한 치도 바뀌지 않는다</b>.
/// (궤도 Height/Radius를 건드리면 피치각 atan2(H,R)이 같이 움직여 구도가 틀어진다 — 그래서 안 쓴다.)
///
/// 결과: 화면 기울기·시야각은 동일하고, 카메라가 앞으로 나가면서 플레이어가 화면 아래로 내려간다
/// (앞쪽 바닥이 더 보이는 쿼터뷰).
///
/// '앞'의 기준은 <b>카메라 수평 정면</b>이다. 플레이어 정면을 쓰면 액션 중 캐릭터가 회전할 때마다
/// 카메라가 휘둘려 멀미가 난다.
///
/// 바인딩은 매 프레임 가로채 교정한다 — PlayerController/GameCameraController 여러 곳에서
/// Follow를 플레이어로 다시 꽂기 때문. 보스 시점·탑다운처럼 Follow가 플레이어가 아닐 때는
/// 개입하지 않는다.
///
/// PlayerController가 런타임에 자동 부착한다(CombatCameraFraming과 동일 패턴).
/// </summary>
[DisallowMultipleComponent]
public class CameraRigAnchor : MonoBehaviour
{
    // ── SerializeField ────────────────────────────────────────────
    [Header("전방 밀기")]
    [Tooltip("카메라 수평 정면으로 리그를 밀어내는 거리(m). 0이면 기존과 동일(플레이어 추적).")]
    [SerializeField] private float pushDistance = 1.2f;

    // ── Private ───────────────────────────────────────────────────
    private PlayerController    _player;
    private CinemachineFreeLook _cam;
    private Transform           _anchor;
    private Vector3             _forward = Vector3.forward;   // 카메라 수평 정면(마지막 유효값)

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake() => _player = GetComponent<PlayerController>();

    private void OnDestroy()
    {
        if (_anchor != null) Destroy(_anchor.gameObject);
    }

    // Cinemachine Brain이 LateUpdate에서 포즈를 확정하므로 그전에 앵커를 갱신한다.
    private void LateUpdate()
    {
        if (_player == null) return;

        if (_cam == null)
        {
            _cam = _player.CinemachineCamera;
            if (_cam == null) return;
        }

        EnsureAnchor();

        // Follow/LookAt이 플레이어를 직접 가리키고 있으면 앵커로 교체.
        // 보스 시점·탑다운 등 다른 대상을 추적 중이면 그대로 둔다(연출 점유 존중).
        if (_cam.Follow == transform) _cam.Follow = _anchor;
        if (_cam.LookAt == transform) _cam.LookAt = _anchor;

        // 앵커가 물려 있지 않으면(연출 중) 위치만 갱신하고 빠진다 — 연출 복귀 시 튀지 않도록.
        UpdateAnchorPosition();
    }

    // ── Private Methods ───────────────────────────────────────────
    private void EnsureAnchor()
    {
        if (_anchor != null) return;

        // 플레이어의 자식으로 두지 않는다 — 프리팹 스케일(0.9)과 회전을 상속받으면 안 되기 때문.
        var go = new GameObject("CameraRigAnchor") { hideFlags = HideFlags.HideInHierarchy };
        _anchor = go.transform;
        _anchor.position = transform.position;
    }

    private void UpdateAnchorPosition()
    {
        if (_anchor == null) return;

        Vector3 fwd = _cam.State.FinalOrientation * Vector3.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude > 0.0001f) _forward = fwd.normalized;   // 수평 성분이 0이면 마지막 값 유지

        _anchor.position = transform.position + _forward * pushDistance;
    }
}
