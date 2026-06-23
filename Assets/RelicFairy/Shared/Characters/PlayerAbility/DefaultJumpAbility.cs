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

    // 플로팅: rideHeight + 이 값까지 적중하면 접지로 간주(호버 허용오차).
    private const float FloatGroundTolerance = 0.15f;

    //============================================================
    // Private Fields
    //============================================================
    private readonly CharacterData _data;
    private bool _isGrounded;
    private bool _isJumping;
    private float _landingLockTimer;
    private float _jumpCooldownTimer;

    // 플로팅 컨트롤러 상태
    private bool  _jumpSuppressSpring;   // 점프 상승 동안 스프링 끔(끌어내림 방지)
    private bool  _floatHasGround;       // 이번 프레임 호버 캐스트 적중 여부
    private float _floatHitDist;         // 호버 캐스트 적중 거리

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

        // 플로팅: 상승 동안 스프링을 꺼 호버가 끌어내리지 않게 한다(하강 시 자동 재개).
        if (_data != null && _data.useFloatingController)
            _jumpSuppressSpring = true;

        // 기존 수직 속도 제거 후 점프 속도 직접 적용 (mass 무관)
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        rb.AddForce(Vector3.up * _data.jumpForce, ForceMode.VelocityChange);
        rb.linearDamping = _data.airDrag;
    }

    public void UpdateGroundCheck(PlayerController controller)
    {
        if (_jumpCooldownTimer > 0f)
            _jumpCooldownTimer -= Time.fixedDeltaTime;

        if (_data != null && _data.useFloatingController)
        {
            UpdateFloatGroundCheck(controller);
            return;
        }

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
        if (_data != null && _data.useFloatingController)
        {
            ApplyFloat(controller);
            return;
        }

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

    //============================================================
    // 플로팅 컨트롤러 (useFloatingController=true 시)
    //============================================================
    // 캡슐 바닥(= 발 + rideHeight)에서 아래로 캐스트해 접지/높이 오차를 구한다.
    private void UpdateFloatGroundCheck(PlayerController controller)
    {
        float ride     = _data.floatRideHeight;
        float stepDown = _data.floatStepDownDistance;
        Vector3 origin = controller.transform.position + Vector3.up * ride;
        // 캐스트 길이는 스텝다운 거리까지 — 계단 하강 시 낮은 지면을 미리 본다.
        float len = ride + Mathf.Max(_data.floatProbeExtra, stepDown);

        bool wasGrounded = _isGrounded;
        _floatHasGround = Physics.Raycast(origin, Vector3.down, out var hit, len, _data.groundLayer);
        _floatHitDist = _floatHasGround ? hit.distance : len;
        Debug.DrawRay(origin, Vector3.down * len, _floatHasGround ? Color.green : Color.red);

        // 착지 락 중엔 접지 유지(플리커 방지)
        if (_landingLockTimer > 0f)
        {
            _landingLockTimer -= Time.fixedDeltaTime;
            _isGrounded = true;
            return;
        }

        // 일반/스텝업: 호버 범위 내 적중 = 접지.
        bool nearGround = _floatHasGround && _floatHitDist <= ride + FloatGroundTolerance;
        // 스텝다운: 직전 접지였고 낮은 지면이 stepDown 이내면 접지 유지 → 계단 하강 시 매 단 낙하/착지 대신
        //           스프링이 지면을 따라 내려간다(최소 단차 보장). stepDown 초과로 떨어지면 낙하 전환.
        bool snapDown   = wasGrounded && _floatHasGround && _floatHitDist <= ride + stepDown;
        // 점프 상승 중(_jumpSuppressSpring)엔 접지 아님(공중/중력).
        _isGrounded = (nearGround || snapDown) && !_jumpSuppressSpring;

        if (_isGrounded && !wasGrounded) OnLanded(controller);
        if (!_isGrounded && wasGrounded)
        {
            var rb = controller.Rigid;
            if (rb != null) rb.linearDamping = _data.airDrag;
        }
    }

    // 호버 스프링(위치 강제 없이 힘으로 rideHeight 유지) 또는 호버 밖 일반 중력.
    private void ApplyFloat(PlayerController controller)
    {
        var rb = controller.Rigid;
        if (rb == null) return;

        // 점프 상승이 끝나면(하강 전환) 스프링 재개 허용.
        if (_jumpSuppressSpring && rb.linearVelocity.y <= 0f)
            _jumpSuppressSpring = false;

        if (_isGrounded && _floatHasGround)
        {
            // 중력을 항상 작용시키고(자연 하강감) 스프링은 '밀어올리기'만 담당한다.
            //  · 평지: 스프링 support가 중력을 상쇄해 rideHeight 유지.
            //  · 스텝업(error>0): support↑ → 단차 위로 밀어올림.
            //  · 스텝다운(error<0): support가 0으로 클램프 → 중력이 자연스럽게 낙하시키고,
            //    바닥에 가까워지면 support(+ -vy 댐핑)가 다시 살아나 받아냄(쿵 박힘 방지).
            float gAbs    = Mathf.Abs(_data.gravity);
            rb.AddForce(Vector3.down * gAbs, ForceMode.Acceleration);   // 중력(항상)

            float error   = _data.floatRideHeight - _floatHitDist;
            float vy      = rb.linearVelocity.y;
            float support = error * _data.floatSpring - vy * _data.floatDamper + gAbs * rb.mass;
            if (support < 0f) support = 0f;   // 아래로 당기지 않음 — 하강은 중력 몫
            rb.AddForce(Vector3.up * support, ForceMode.Force);
            return;
        }

        // 호버 밖(점프/낙하) → 일반 중력(기존 로직과 동일).
        float gravity = _data.gravity;
        if (rb.linearVelocity.y < 0f) gravity *= _data.fallMultiplier;

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
}