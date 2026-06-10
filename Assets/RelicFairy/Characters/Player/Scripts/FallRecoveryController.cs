using UnityEngine;

/// <summary>
/// 플레이어 낙사 복구 컨트롤러.
/// SafeFloor 이하로 떨어지면 마지막 안전 지점(땅을 밟았던 위치)으로 텔레포트 + HP 감소 + 무적.
///
/// 동작:
///   · Update: 플레이어가 Ground layer 위에 있으면 현재 위치를 _lastSafe 로 갱신
///   · transform.position.y &lt; fallThresholdY → 복구 절차 실행
///
/// 복구 절차:
///   1. RuntimeStats.MaxHp × fallDamageRatio 만큼 HP 감소 (passive 트리거 없이 직접)
///   2. PlayerController.SetInvincible(invincibleSeconds) 로 리스폰 직후 짧게 무적
///   3. 위치를 _lastSafe + respawnOffsetY 로 이동, 속도 리셋
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class FallRecoveryController : MonoBehaviour
{
    [SerializeField, Tooltip("이 Y 이하로 떨어지면 낙사 판정. SafeFloor(=baseY-0.05) 보다 충분히 아래로.")]
    private float fallThresholdY = -5f;

    [SerializeField, Tooltip("지면 체크 레이어. 기본 Ground=3.")]
    private LayerMask groundLayer = 1 << 3;

    [SerializeField, Tooltip("지면 체크 Sphere 반경.")]
    private float groundCheckRadius = 0.35f;

    [SerializeField, Tooltip("지면 체크 Sphere 중심의 Y 오프셋 (발 위쪽).")]
    private float groundCheckCenterY = 0.3f;

    [SerializeField, Tooltip("리스폰 시 _lastSafe 위에 얹어지는 Y 오프셋 (자연스럽게 착지).")]
    private float respawnOffsetY = 0.5f;

    [SerializeField, Range(0f, 1f), Tooltip("낙사 시 최대 HP 대비 피해 비율.")]
    private float fallDamageRatio = 0.1f;

    [SerializeField, Min(0f), Tooltip("낙사 복구 직후 무적 시간(초).")]
    private float invincibleSeconds = 1.0f;

    private PlayerController _pc;
    private Rigidbody _rb;
    private Vector3 _lastSafe;
    private bool _hasSafe;
    private bool _recovering;

    private void Awake()
    {
        _pc = GetComponent<PlayerController>();
        _rb = GetComponent<Rigidbody>();
        _lastSafe = transform.position;
        _hasSafe = true;
    }

    private void Update()
    {
        if (_recovering) return;

        // 1) 낙사 판정
        if (transform.position.y < fallThresholdY)
        {
            Recover();
            return;
        }

        // 2) 지면 위 여부 확인 → 안전지점 갱신
        if (IsGrounded())
        {
            _lastSafe = transform.position;
            _hasSafe = true;
        }
    }

    private bool IsGrounded()
    {
        Vector3 center = transform.position + new Vector3(0f, groundCheckCenterY, 0f);
        return Physics.CheckSphere(center, groundCheckRadius, groundLayer, QueryTriggerInteraction.Ignore);
    }

    /// <summary>PitTrigger 등 외부에서 낙사 복구를 즉시 발동한다.</summary>
    public void ForceRecover() => Recover();

    private void Recover()
    {
        _recovering = true;

        // HP 차감 — passive 트리거 없이 직접 (낙사는 특수 원인)
        if (_pc != null && _pc.RuntimeStats != null)
        {
            int maxHp = _pc.RuntimeStats.MaxHp;
            int dmg = Mathf.Max(1, Mathf.RoundToInt(maxHp * fallDamageRatio));
            _pc.RuntimeStats.Damage(dmg);
        }

        // 리스폰 위치 — 안전 지점이 확보된 상태면 그 위에, 아니면 현재 XZ 유지한 상태에서 Y=0 복귀
        Vector3 target = _hasSafe
            ? _lastSafe + new Vector3(0f, respawnOffsetY, 0f)
            : new Vector3(transform.position.x, respawnOffsetY, transform.position.z);

        transform.position = target;

        if (_rb != null)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }

        // 짧은 무적
        _pc?.SetInvincible(invincibleSeconds);

        _recovering = false;
    }
}
