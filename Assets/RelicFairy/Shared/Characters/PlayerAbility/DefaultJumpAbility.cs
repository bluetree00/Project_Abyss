using UnityEngine;

public class DefaultJumpAbility : IJumpAbility
{
    //============================================================
    // Constants
    //============================================================
    private const float AirAttackGravityScale = 0.05f;
    private const float AirAttackMaxFallSpeed = -1f;

    // 착지 직후 일정 시간 동안 ground 상태를 유지 (바운스 플리커 방지)
    private const float LandingLockDuration = 0.1f;

    // 착지 후 방향 입력을 받을 수 있도록 점프 재사용 대기 시간
    private const float JumpCooldownAfterLanding = 0.18f;

    //============================================================
    // Private Fields
    //============================================================
    private readonly CharacterData _data;
    private bool _isGrounded;
    private bool _isJumping;
    private float _landingLockTimer;
    private float _jumpCooldownTimer;

    //============================================================
    // Properties
    //============================================================
    public bool IsGrounded => _isGrounded;
    public bool IsJumping => _isJumping;

    //============================================================
    // Constructor
    //============================================================
    public DefaultJumpAbility(CharacterData data)
    {
        _data = data;
    }

    //============================================================
    // Public Methods
    //============================================================
    public void Jump(PlayerController controller)
    {
        if (!_isGrounded) return;
        if (_jumpCooldownTimer > 0f) return;

        var rb = controller.Rigid;
        if (rb == null) return;

        _isJumping = true;
        _landingLockTimer = 0f;

        // 기존 수직 속도 제거 후 점프 속도 직접 적용 (mass 무관)
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        rb.AddForce(Vector3.up * _data.jumpForce, ForceMode.VelocityChange);
        rb.linearDamping = _data.airDrag;
    }

    public void UpdateGroundCheck(PlayerController controller)
    {
        if (_jumpCooldownTimer > 0f)
            _jumpCooldownTimer -= Time.fixedDeltaTime;

        // 착지 락 타이머 진행 중이면 ground 상태 유지
        if (_landingLockTimer > 0f)
        {
            _landingLockTimer -= Time.fixedDeltaTime;
            return;
        }

        Vector3 rayOrigin = controller.transform.position + Vector3.up * 0.1f;
        float rayLength = _data.groundCheckDistance + 0.1f;

        bool wasGrounded = _isGrounded;

        _isGrounded =
            Physics.Raycast(rayOrigin, Vector3.down, out var hit, rayLength, _data.groundLayer) &&
            hit.distance <= _data.groundCheckDistance + 0.05f;

        Debug.DrawRay(rayOrigin, Vector3.down * rayLength, _isGrounded ? Color.green : Color.red);

        // 착지 순간
        if (_isGrounded && !wasGrounded)
        {
            OnLanded(controller);
        }

        // 공중 진입 순간 (낙하 포함)
        if (!_isGrounded && wasGrounded)
        {
            var rb = controller.Rigid;
            if (rb != null)
                rb.linearDamping = _data.airDrag;
        }
    }

    public void ApplyGravity(PlayerController controller)
    {
        if (_isGrounded) return;

        var rb = controller.Rigid;
        if (rb == null) return;

        float gravity = _data.gravity;

        // 낙하 중 중력 배율 증가 (빠른 하강감)
        if (rb.linearVelocity.y < 0f)
            gravity *= _data.fallMultiplier;

        // 공중 공격 체공
        bool isAirAttacking = controller.AirAttackUsed
                           && controller.Combo != null
                           && controller.Combo.IsAttacking;
        if (isAirAttacking)
        {
            gravity *= AirAttackGravityScale;
            if (rb.linearVelocity.y < AirAttackMaxFallSpeed)
                rb.linearVelocity = new Vector3(rb.linearVelocity.x, AirAttackMaxFallSpeed, rb.linearVelocity.z);
        }

        rb.AddForce(Vector3.up * gravity, ForceMode.Acceleration);
    }

    //============================================================
    // Private Methods
    //============================================================
    private void OnLanded(PlayerController controller)
    {
        _isJumping = false;
        _landingLockTimer = LandingLockDuration;
        _jumpCooldownTimer = JumpCooldownAfterLanding;
        controller.AirAttackUsed = false;

        var rb = controller.Rigid;
        if (rb != null)
        {
            // 착지 시 수직 속도 강제 0 (바운스 방지) + drag 복원
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
            rb.linearDamping = _data.groundDrag;
        }
    }
}