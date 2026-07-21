using UnityEngine;

/// <summary>
/// 우주 공중(void) 부유 오브젝트에 붙이는 경량 표류 연출 컴포넌트.
/// 상하 부유(sine) + 등속 회전(yaw/pitch/roll) + 완만한 표류를 무할당으로 처리한다.
/// 인스턴스마다 phaseOffset을 달리 주면 서로 다른 리듬으로 움직여 시차(parallax) 공간감을 만든다.
/// AnimationClip/트윈 의존 없이 Update 한 번으로 끝내는 최소 구현.
/// </summary>
public sealed class VoidDrifter : MonoBehaviour
{
    // ── [SerializeField] ─────────────────────────────────────────────
    [Header("위상 (인스턴스마다 다르게)")]
    [SerializeField] private float phaseOffset;

    [Header("상하 부유")]
    [SerializeField] private float bobAmplitude = 0.6f;   // m
    [SerializeField] private float bobSpeed     = 0.4f;    // rad/s

    [Header("등속 회전 (deg/s)")]
    [SerializeField] private Vector3 spinSpeed = new Vector3(0f, 3f, 0f);

    [Header("수평 표류")]
    [SerializeField] private float driftAmplitude = 0.3f; // m
    [SerializeField] private float driftSpeed     = 0.25f; // rad/s
    [SerializeField] private Vector3 driftAxis    = new Vector3(1f, 0f, 0.5f);

    // ── Private ──────────────────────────────────────────────────────
    private Vector3 _startLocalPos;
    private Vector3 _driftDir;

    // ── Lifecycle ────────────────────────────────────────────────────
    private void Awake()
    {
        _startLocalPos = transform.localPosition;
        _driftDir = driftAxis.sqrMagnitude > 0.0001f ? driftAxis.normalized : Vector3.right;
    }

    private void Update()
    {
        float t = Time.time + phaseOffset;

        float bob   = Mathf.Sin(t * bobSpeed) * bobAmplitude;
        float drift = Mathf.Sin(t * driftSpeed) * driftAmplitude;

        transform.localPosition = _startLocalPos
                                  + Vector3.up * bob
                                  + _driftDir * drift;

        if (spinSpeed != Vector3.zero)
            transform.Rotate(spinSpeed * Time.deltaTime, Space.Self);
    }
}
