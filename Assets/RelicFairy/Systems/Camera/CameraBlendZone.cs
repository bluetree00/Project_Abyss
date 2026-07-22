using UnityEngine;

/// <summary>
/// <b>위치 비율</b>로 카메라 시선각을 전환하는 구역.
///
/// <see cref="CameraZone"/>이 "진입 순간 시간을 두고 한 번 돌리는" 방식이라면, 이쪽은
/// 구역을 지나는 <b>진행률(0→1)에 비례해</b> 각도를 갱신한다. 지형을 따라 시선이 함께 도는 느낌이 되고,
/// 위치에 묶여 있어 <b>되돌아가면 각도도 되돌아간다</b>.
///
/// <para><b>진행축은 수평(X/Z)을 권장한다.</b> 계단에서 높이(Y)로 진행률을 재면, 호버 스프링이 계단참을
/// 넘을 때마다 캐릭터 Y가 출렁이고 그 진동이 그대로 각도로 전달돼 카메라가 심하게 떨린다.
/// 수평 이동은 매끄러우므로 진동이 생기지 않는다.</para>
///
/// 남은 미세 진동은 <see cref="responsiveness"/> 감쇠로 흡수한다(목표각으로 지수 수렴).
/// 갱신은 물리 스텝이 아니라 <b>프레임 단위</b>(Update)로 한다 — OnTriggerStay(50Hz)로 각도를 넣으면
/// 렌더 프레임과 어긋나 끊겨 보인다.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public sealed class CameraBlendZone : MonoBehaviour
{
    public enum Axis { X, Y, Z }

    [Header("진행 축 (구역 로컬 기준)")]
    [SerializeField, Tooltip("진행률 0→1을 잴 축. 계단이라도 높이(Y)가 아니라 이동 방향(X/Z)을 쓸 것 — Y는 캐릭터 상하 출렁임이 그대로 떨림이 된다")]
    private Axis progressAxis = Axis.X;
    [SerializeField, Tooltip("진행 방향을 뒤집는다")]
    private bool invert;

    [Header("시선각 전환")]
    [SerializeField, Tooltip("진행률 0에서의 각도(Y, 월드). 0=+Z")]
    private float fromYaw;
    [SerializeField, Tooltip("진행률 1에서의 각도(Y, 월드). 90=+X")]
    private float toYaw = 90f;

    [Header("보정")]
    [SerializeField, Range(0f, 1f), Tooltip("0=선형, 1=양 끝을 부드럽게(SmoothStep)")]
    private float smoothing = 1f;
    [SerializeField, Tooltip("목표각 수렴 속도. 낮을수록 부드럽고 둔하다. 떨림이 남으면 낮춘다")]
    private float responsiveness = 8f;

    private BoxCollider _box;
    private Transform _player;

    private void Awake()
    {
        _box = GetComponent<BoxCollider>();
        _box.isTrigger = true;
    }

    private void Update()
    {
        if (_player == null) return;

        var cam = GameCameraController.Instance;
        if (cam == null) return;

        float target = Mathf.LerpAngle(fromYaw, toYaw, Progress01(_player.position));

        // 지수 감쇠로 목표각에 수렴 — 진행률에 남은 미세 진동을 흡수한다(프레임률 독립).
        float k = 1f - Mathf.Exp(-Mathf.Max(0.01f, responsiveness) * Time.deltaTime);
        cam.SetHeadingRaw(Mathf.LerpAngle(cam.CurrentHeading, target, k));
    }

    private void OnTriggerEnter(Collider other)
    {
        var player = other.GetComponentInParent<PlayerController>();
        if (player != null) _player = player.transform;
    }

    private void OnTriggerExit(Collider other)
    {
        var player = other.GetComponentInParent<PlayerController>();
        if (player != null && _player == player.transform) _player = null;
    }

    private void OnDisable() => _player = null;

    // ── Private Methods ───────────────────────────────────────

    /// <summary>구역 로컬 공간에서 진행 축 기준 0~1 진행률.</summary>
    private float Progress01(Vector3 worldPos)
    {
        Vector3 local = transform.InverseTransformPoint(worldPos) - _box.center;

        float value, size;
        switch (progressAxis)
        {
            case Axis.Y: value = local.y; size = _box.size.y; break;
            case Axis.Z: value = local.z; size = _box.size.z; break;
            default:     value = local.x; size = _box.size.x; break;
        }

        float half = Mathf.Max(0.0001f, size * 0.5f);
        float t = Mathf.InverseLerp(-half, half, value);
        if (invert) t = 1f - t;

        // smoothing=1이면 양 끝에서 서서히 붙어 시작/끝의 각속도 튐이 사라진다.
        return Mathf.Lerp(t, Mathf.SmoothStep(0f, 1f, t), smoothing);
    }
}
