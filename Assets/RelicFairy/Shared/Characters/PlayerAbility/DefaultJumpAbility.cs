using UnityEngine;

/// <summary>
/// 플레이어 점프/접지. 접지 구현이 <b>두 갈래</b>이고 CharacterData.useFloatingController가 그중 하나를 고른다.
///
///   · true  → 플로팅(스프링 호버). UpdateFloatGroundCheck / ApplyFloat.
///   · false → 레이(스피어캐스트) 접지. UpdateGroundCheck / ApplyGravity 의 아래쪽 본문.
///
/// [현재 상태] 유일한 캐릭터 데이터인 PlayerCharacterData.useFloatingController = 1 이다
/// ('계단 접지 재설계' ddd82a8에서 0 → 1). 즉 <b>레이 접지 경로는 런타임에서 실행되지 않는다</b> —
/// 그 시절 튜닝인 groundCheckDistance(0.15)·캡슐 Center 보정·GroundSettleEpsilon·스텝다운 스냅도 함께 비활성이다.
/// 접지 관련 증상을 진단할 때 이쪽 코드를 먼저 읽으면 틀린 결론이 나온다.
///
/// 레이 경로는 플래그를 되돌릴 때를 대비한 대안 구현으로 남겨 둔다(삭제하면 useFloatingController=0이
/// 접지 없는 상태가 된다). 게임필 비교가 끝나 한쪽으로 확정되면 그때 정리할 것.
/// </summary>
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

    // 접지(호버) 중 허용할 최대 상승 속도(m/s).
    // 대시처럼 빠른 속도로 경사/단차에 진입하면 지면이 순식간에 발밑으로 솟아 _floatHitDist가 급감하고,
    // 스프링의 error(=rideHeight-hitDist)가 치솟아 캐릭터를 위로 쏘아올린다(램프 발사).
    // 상승 속도에 상한을 둬 계단·경사 올라타기는 유지하면서 발사만 막는다.
    // 점프는 _isGrounded=false 구간이라 이 클램프의 영향을 받지 않는다.
    private const float MaxGroundedRiseSpeed = 4f;

    // 호버 스프링이 낼 수 있는 '중력 상쇄분 위' 추가 상승 가속도 상한(m/s²).
    // 단차/경사를 밀어올리기엔 충분하고, 캐릭터를 공중으로 쏘아올리기엔 부족한 값.
    private const float MaxSpringLiftAccel = 25f;

    // 스프링이 아래로 당길 수 있는 추가 가속 상한. 중력과 합쳐 최대 2g 로 하강한다.
    // 이게 있어야 계단을 따라 붙어 내려간다 — 없으면 매 단마다 자유낙하가 된다.
    private const float MaxSpringPullAccel = 28f;

    // 스텝다운 유예 — 접지를 놓친 뒤에도 이 시간 안에는 하강 추종을 유지한다.
    private const float SnapDownGrace = 0.15f;
    private float _snapDownGraceUntil;

    // 하강 추종 속도 상한. 이 값이 곧 뒤처짐의 상한을 정한다 —
    // 정상상태 뒤처짐 ≈ 필요한 하강속도 × fixedDeltaTime, 단 이 상한에 걸리면 그 이상 뒤처진다.
    private const float MaxStepDownFollowSpeed = 12f;

    // 상승 추종 개입 문턱·속도 상한. 하강과 대칭이되 문턱은 조금 넉넉히 둔다 —
    // 평지의 미세한 요철마다 상승 구동이 걸리면 오히려 떨린다.
    private const float StepUpDeadband = 0.06f;
    private const float MaxStepUpFollowSpeed = 8f;

    // 지면으로 인정할 최소 법선 Y. 이보다 눕지 않은 면(벽·챌면)은 높이 기준으로 쓰지 않는다.
    // 넓은 프로브가 벽면을 지면으로 읽으면 캐릭터가 벽을 타고 올라간다.
    private const float MinWalkableNormalY = 0.5f;

    // 위치 서보의 시간상수 — 지면과의 오차를 이 시간 안에 없애는 속도를 목표로 삼는다.
    // 정상상태 뒤처짐 ≈ 필요한 추종속도 × 이 값. 작을수록 밀착하고, 크면 뒤처진다.
    //
    // 이 값은 단순한 승차감 노브가 아니라 '캡슐이 계단에 닿느냐'를 직접 결정한다.
    // 캡슐 바닥은 지면에서 0.333m 떠 있고(로컬 0.36984 × 스케일 0.9), 뒤처진 만큼 그 여유가 깎인다.
    // 계단 실측에서 상승률 4.6m/s × 0.05 = 0.23 만큼 잠겨 여유가 0.10까지 줄었고, 계단 한 단(약 0.17)이
    // 그 여유를 넘어서면서 캡슐이 챌면에 부딪혔다 — 실측 한 프레임에 vh 7.51 → 3.39, 동시에 vy 0 → +5.92.
    // 속도가 사라진 게 아니라 물리엔진이 수평을 수직으로 꺾은 것이고, 그 반동이 다시 오차를 만들어
    // 다음 단에서 또 부딪히는 왕복이 됐다. 계단이 길수록 심해진 이유다.
    //
    // 0.03 이면 같은 상승률에서 뒤처짐이 0.14 로 줄어 여유가 0.19 가 되고 한 단(0.17)을 넘어선다.
    // 더 줄이면 물리 스텝(0.02) 대비 루프 게인이 1 에 가까워져 떨리므로 여기가 실질 하한이다.
    private const float FollowTime = 0.03f;

    // 추종 속도를 한 프레임에 꽂으면 Y가 계단식으로 튀고, 카메라가 그 진동을 그대로 따라간다.
    // 목표 속도로 '가속도 상한을 지키며' 다가가게 해서 급변을 없앤다.
    // 값이 작을수록 부드럽고 무겁게, 클수록 즉각적이지만 흔들린다.
    // 70 은 계단 진입 첫 단에서 병목이었다. 평지에서 vy=0 으로 달려오다 첫 단을 만나면 서보가 정지
    // 상태에서 가속을 시작해야 하는데, 실측 램프가 Δvy=+1.29/+1.18(≈65m/s²)로 상한에 딱 붙어 있었고
    // 그 사이 지면이 계속 올라 오차가 0.35까지 벌어져 캡슐이 계단면 아래로 내려가 닿았다.
    // 둘째 단부터는 이미 vy 5~7 로 오르는 중이라 문제가 없어, 첫 단에서만 접촉이 남았다.
    // 평지에서는 목표 속도가 0 이라 이 상한이 하는 일이 없으므로 올려도 평상시 승차감에는 영향이 없다.
    private const float MaxFollowAccel = 200f;

    // 감속 전용 상한. 가속보다 훨씬 크게 둬야 내리막 끝에서 하강 속도를 제때 죽인다.
    private const float MaxFollowBrakeAccel = 250f;

    // 단차 저항 — 오를 단차가 이 높이면 저항이 최대가 된다.
    // FollowTime 을 줄여 평상시 뒤처짐이 0.14 수준으로 내려갔으므로 문턱도 같이 내린다.
    // 그러지 않으면 계단에서는 영영 발동하지 않는 죽은 코드가 된다.
    // 다만 계단 주행 중(뒤처짐 0.14)에는 상한이 이동속도 8 근처에 머물러 사실상 개입하지 않고,
    // 진짜 큰 턱에서만 물리도록 둔다 — 저항이 상시 걸리면 그 자체가 걸리는 느낌이 된다.
    private const float StepResistanceFullAt = 0.2f;

    // 저항은 '감쇠'가 아니라 '속도 상한'이다. 곱셈 감쇠는 접촉이 이어지는 동안 무한히 누적돼
    // 계단 위에서 끝없이 느려지지만, 상한은 누적되지 않아 느리되 일정한 속도로 수렴한다.
    // Max 는 평상시 이동 속도보다 높게 둬 단차가 얕을 때는 사실상 무제한이 되게 한다.
    private const float StepClimbSpeedCapMax = 12f;
    private const float StepClimbSpeedCapMin = 5f;
    // 상한을 즉시 꽂으면 그것대로 툭 걸리므로 감속도 상한을 둔다.
    private const float StepClimbDecel = 20f;

    /// <summary>
    /// 목표 수직 속도로 가속도 상한을 지키며 접근한다(급변 방지 = 카메라 흔들림 방지).
    /// 가속과 감속에 다른 상한을 쓴다 — 속도를 '올릴' 때 급변하면 카메라가 흔들리지만,
    /// '줄일' 때는 일회성이라 진동을 만들지 않는다. 감속을 느리게 두면 내리막 끝에서
    /// 하강 속도를 못 죽여 마지막에 낙하처럼 보인다.
    /// </summary>
    private static void FollowVertical(Rigidbody rb, float targetVy)
    {
        float vy = rb.linearVelocity.y;
        bool braking = Mathf.Abs(targetVy) < Mathf.Abs(vy) || Mathf.Sign(targetVy) != Mathf.Sign(vy);
        float accel = braking ? MaxFollowBrakeAccel : MaxFollowAccel;

        vy = Mathf.MoveTowards(vy, targetVy, accel * Time.fixedDeltaTime);
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, vy, rb.linearVelocity.z);
    }

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

    // 접지 판정 시점의 지면까지 실거리. 접지 밴드 안에서 떠 있는지 판별하는 데 쓴다.
    private float _groundHitDistance = float.PositiveInfinity;

    // 코요테 타임 만료 시각.
    private float _coyoteUntil;

    // 이번 프레임이 '계단 하강 스냅' 구간인지.
    private bool _stepDownActive;

    // 계단 하강 스냅으로 따라 내려갈 수 있는 최대 낙차.
    // 오를 수 있는 높이(StepMaxHeight 0.35)와 맞춘다 — 오르지 못하는 높이를 내려가서도 안 된다.
    private const float MaxStepDownHeight = 0.35f;

    // 하강 스냅 속도 상한. 너무 크면 계단에서 바닥에 꽂히듯 보인다.
    private const float MaxStepDownSpeed = 8f;

    // 캡슐이 지면에 실제로 얹혀 있을 때의 프로브 거리보다 이만큼 더 떠 있으면 '아직 안 닿음'으로 본다.
    private const float GroundSettleEpsilon = 0.02f;

    // 정착용 중력 배율. 접지 판정 상태에서 내릴 때는 약하게 준다 —
    // 온전한 중력을 주면 남은 몇 cm 를 내려오며 속도가 붙어 접촉 순간 튄다.
    private const float GroundSettleGravityScale = 0.35f;

    // 스텝 오르기 중 중력 배율. 0 이면 오르기 종료 후 상승 속도가 안 멈춰 튀어 오르고,
    // 1 이면 오르는 힘과 다퉈 떨린다. 그 사이 값으로 둘 다 피한다.
    private const float StepClimbGravityScale = 0.25f;

    // 접지를 놓친 뒤에도 이 시간만큼은 접지로 본다(코요테 타임).
    // 경사·요철을 지날 때 프로브가 한두 프레임 지면을 놓치면 Loco 가 Air 로 튀고,
    // 돌아올 때 MoveBlend 크로스페이드가 다시 재생돼 애니메이션이 꼬인다.
    // 점프로 떠난 경우에는 적용하지 않는다.
    private const float GroundCoyoteTime = 0.12f;

    // 접지 스피어 반지름 — 캡슐 반지름을 스케일 반영해 쓰되 살짝 줄인다.
    // 콜라이더는 한 번만 캐싱한다(FixedUpdate 마다 GetComponent 금지).
    private CapsuleCollider _capsule;
    private bool _capsuleResolved;

    /// <summary>캡슐이 지면에 얹혔을 때 프로브가 재게 되는 거리.</summary>
    private float GetRestProbeDistance(PlayerController controller)
    {
        if (!_capsuleResolved)
        {
            controller.TryGetComponent(out _capsule);
            _capsuleResolved = true;
        }
        if (_capsule == null) return 0.15f;

        var s = controller.transform.lossyScale;
        // 캡슐 하단의 로컬 오프셋(보통 음수) → 월드 기준 발밑까지의 거리
        float bottomLocal = _capsule.center.y - Mathf.Max(_capsule.height, _capsule.radius * 2f) * 0.5f;
        float bottomWorld = Mathf.Abs(bottomLocal * s.y);
        // 프로브는 transform + 0.1 에서 시작하고, 물리 접촉은 contactOffset 만큼 띄워 붙는다.
        return 0.1f + bottomWorld + Physics.defaultContactOffset;
    }

    private float GetGroundProbeRadius(PlayerController controller)
    {
        if (!_capsuleResolved)
        {
            controller.TryGetComponent(out _capsule);
            _capsuleResolved = true;
        }
        if (_capsule == null) return 0.2f;

        var s = controller.transform.lossyScale;
        float scale = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z));
        return Mathf.Max(0.05f, _capsule.radius * scale * 0.9f);
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

        // 접지 판정은 중심 레이 하나가 아니라 캡슐 굵기의 스피어캐스트로 한다.
        //
        // 레이 하나면 살짝 높은 블록 가장자리에 서 있을 때 중심 레이가 그 블록을 빗나가
        // 아래 바닥을 맞고, 거리가 허용치를 넘겨 공중 판정이 된다. 그러면 Loco 가
        // Air → Idle 을 오가며 MoveBlend 크로스페이드를 다시 재생해 "걷기가 계속 시작되는" 것처럼 보인다.
        // groundCheckDistance 가 넉넉하던 시절에는 아래 바닥까지의 거리도 허용치 안이라 가려져 있었다.
        //
        // 스피어는 캡슐 발바닥 넓이를 대신하므로, 발이 조금이라도 얹혀 있으면 접지로 잡힌다.
        // 반지름을 캡슐보다 살짝 작게 잡아 벽면을 바닥으로 오인하는 것은 막는다.
        Vector3 rayOrigin = controller.transform.position + Vector3.up * 0.1f;
        float probeRadius = GetGroundProbeRadius(controller);

        // 프로브 길이는 접지 밴드보다 길게 잡는다.
        // 계단을 내려갈 때 한 단 아래 지면을 '미리' 봐야 낙하 대신 따라 내려갈 수 있다.
        // 밴드 길이만 쓰면 매 단마다 모서리에서 허공으로 나가 낙하 → 착지가 반복된다.
        float rayLength = _data.groundCheckDistance + 0.1f + MaxStepDownHeight;

        bool wasGrounded = _isGrounded;

        bool probeHit = Physics.SphereCast(
            rayOrigin + Vector3.up * probeRadius, probeRadius, Vector3.down,
            out var hit, rayLength, _data.groundLayer, QueryTriggerInteraction.Ignore);

        // 실제 지면까지의 거리를 기억해 둔다. 접지 여부와 별개로 "얼마나 떠 있는지"를 알아야
        // 밴드 안에서 붕 뜬 채 멈추는 것을 막을 수 있다.
        _groundHitDistance = probeHit ? hit.distance : float.PositiveInfinity;

        bool rawGrounded = probeHit && hit.distance <= _data.groundCheckDistance + 0.05f;

        // 코요테 타임 — 경사·요철을 지날 때 프로브가 한두 프레임 지면을 놓쳐도 접지를 유지한다.
        // 이 유예가 없으면 Loco 가 Air 로 튀었다 돌아오며 MoveBlend 크로스페이드를 다시 재생해
        // "조금만 높은 곳을 지나가면 걷기가 다시 시작되는" 증상이 난다.
        // 점프로 떠난 경우에는 적용하지 않는다(공중 판정이 즉시 서야 한다).
        // 계단 하강 스냅 — 직전에 접지였고 점프가 아닌데 한 단 아래에 지면이 있으면,
        // 낙하로 처리하지 않고 그 지면까지 따라 내려간다.
        float rest = GetRestProbeDistance(controller);
        _stepDownActive = !rawGrounded && wasGrounded && !_isJumping && probeHit &&
                          _groundHitDistance <= rest + MaxStepDownHeight;

        if (rawGrounded) _coyoteUntil = Time.time + GroundCoyoteTime;
        _isGrounded = rawGrounded || _stepDownActive || (!_isJumping && Time.time < _coyoteUntil);

        Debug.DrawRay(rayOrigin, Vector3.down * rayLength, _isGrounded ? Color.green : Color.red);

#if UNITY_EDITOR
        // [임시 진단] 접지·부양 수치를 초당 1회 찍는다. 원인 규명 후 제거.
        if (Time.frameCount % 60 == 0)
        {
            float hover = _groundHitDistance - rest;   // 양수면 캡슐이 그만큼 떠 있다
            float groundY = probeHit ? hit.point.y : float.NaN;

            // 실제 발 본 높이 — Renderer.bounds 는 스킨드 메시의 사전 계산 바운드라 실제 발바닥이 아니다.
            // 시각적 부양은 캡슐이 아니라 이 값과 지면의 차이로 판단해야 한다.
            float footY = float.NaN;
            var anim = controller.Anim;
            if (anim != null && anim.isHuman)
            {
                foreach (var bone in new[] { HumanBodyBones.LeftToes, HumanBodyBones.RightToes,
                                             HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
                {
                    var t = anim.GetBoneTransform(bone);
                    if (t == null) continue;
                    if (float.IsNaN(footY) || t.position.y < footY) footY = t.position.y;
                }
            }

            // 실제 화면에 보이는 최저점 — Toes 본은 발가락 관절이라 밑창보다 위다.
            // updateWhenOffscreen 을 켜면 Unity 가 현재 포즈로 타이트 바운드를 매 프레임 계산하므로
            // bounds.min.y 가 실제 신발 밑창이 된다. 진단 전용(비용 있음).
            float meshMinY = float.NaN;
            foreach (var smr in controller.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (!smr.updateWhenOffscreen) smr.updateWhenOffscreen = true;
                if (float.IsNaN(meshMinY) || smr.bounds.min.y < meshMinY) meshMinY = smr.bounds.min.y;
            }

            // 스피어는 반지름 안의 '가장 높은 면'을 잡는다. 발밑 정중앙을 가는 레이로 따로 재서
            // 둘이 어긋나면 스피어가 타일 테두리 같은 돌출부를 지면으로 읽고 있다는 뜻이다.
            float rayGroundY = float.NaN;
            if (Physics.Raycast(rayOrigin, Vector3.down, out var thin, rayLength + 0.5f,
                                _data.groundLayer, QueryTriggerInteraction.Ignore))
                rayGroundY = thin.point.y;

            Debug.Log($"[Ground] y={controller.transform.position.y:0.###} " +
                      $"구면지면={groundY:0.###} 레이지면={rayGroundY:0.###} 차={(groundY - rayGroundY):0.###} " +
                      $"캡슐부양={hover:0.###} ★밑창-구면={(meshMinY - groundY):0.###} " +
                      $"★밑창-레이={(meshMinY - rayGroundY):0.###} vy={(controller.Rigid != null ? controller.Rigid.linearVelocity.y : 0f):0.###}");
        }
#endif

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

        // 접지로 "판정"됐다고 중력을 끄면, 캐릭터는 밴드에 진입한 높이 그대로 공중에 멈춘다.
        // groundCheckDistance 0.15 기준 최대 10cm 까지 뜬 채로 정지할 수 있다 —
        // 캡슐을 아무리 내려도 캡슐이 바닥에 닿지 않으므로 해결되지 않는다.
        // 실제로 얹혀 있을 때만 중력을 끄고, 그 전까지는 약한 중력으로 마저 내린다.
        bool restingOnGround = _isGrounded &&
                               _groundHitDistance <= GetRestProbeDistance(controller) + GroundSettleEpsilon;
        if (restingOnGround) return;

        var rb = controller.Rigid;
        if (rb == null) return;

        // 계단 하강 — 중력에 맡기면 매 단마다 가속 낙하가 붙어 "한 칸씩 떨어지며" 내려간다.
        // 남은 낙차를 한 프레임에 덮을 속도를 직접 주어 계단면을 따라 붙게 한다.
        if (_stepDownActive)
        {
            float excess = _groundHitDistance - GetRestProbeDistance(controller);
            if (excess > 0f)
            {
                float need = Mathf.Min(excess / Time.fixedDeltaTime, MaxStepDownSpeed);
                rb.linearVelocity = new Vector3(rb.linearVelocity.x, -need, rb.linearVelocity.z);
            }
            return;
        }

        float gravity = _data.gravity;

        if (controller.IsStepClimbing)
        {
            // 스텝 오르기 중에는 중력을 약하게 준다.
            //
            // 완전히 끄면 오르기가 끝난 뒤 상승 속도(최대 12m/s)를 멈출 것이 없어
            // IsStepClimbing 유효시간(0.08초) 동안 계속 솟는다 — 캐릭터가 계단에서 튀어 오른다.
            // 반대로 온전한 중력(하강 시 2배)을 주면 오르는 힘과 매 프레임 다퉈 떨린다.
            // 약한 중력이면 오르기는 유지되면서 끝나는 즉시 감속된다.
            gravity *= StepClimbGravityScale;
        }
        else if (_isGrounded && !float.IsPositiveInfinity(_groundHitDistance))
        {
            // 접지 판정 안에서 아직 안 닿은 상태 — 남은 몇 cm 를 정착시키는 구간이다.
            // 온전한 중력을 주면 짧은 거리에 속도가 붙어 접촉 순간 튀므로 약하게 준다.
            // 코요테 유예로만 접지인 경우(프로브 미적중)는 실제로 공중이므로 여기 들어오지 않는다.
            gravity *= GroundSettleGravityScale;
        }
        else if (rb.linearVelocity.y < 0f)
        {
            // 낙하 중 중력 배율 증가 (빠른 하강감)
            gravity *= _data.fallMultiplier;
        }

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

        // 두 프로브의 역할을 나눈다 — 하나로는 상승·하강 중 한쪽이 반드시 깨진다.
        //
        //  · 얇은 레이  = "발밑 정중앙의 지면 높이". 스프링의 error 는 이 값이어야 한다.
        //  · 스피어캐스트 = "발밑 어딘가에 지면이 있는가". 계단 모서리를 넘는 순간에도 접지를 유지한다.
        //
        // 스피어 하나만 쓰면 올라갈 때 반지름이 위쪽 계단 모서리를 먼저 물어 거리가 짧게 나온다.
        // 실측에서 hitDist=0.054, 즉 감지된 지면이 캐릭터보다 0.35m 위로 잡혔고,
        // error 가 +0.35 로 치솟아 스프링이 밀고 댐퍼가 제동하기를 반복해 한 단마다 걸리는 느낌이 났다.
        // 반대로 레이 하나만 쓰면 내려갈 때 아래가 빈 지점을 지나며 지면을 놓쳐 낙하한다.
        float probeRadius = GetGroundProbeRadius(controller);
        bool rayHit = Physics.Raycast(origin, Vector3.down, out var hitRay, len,
                                      _data.groundLayer, QueryTriggerInteraction.Ignore);
        bool sphereHit = Physics.SphereCast(origin + Vector3.up * probeRadius, probeRadius,
                                            Vector3.down, out var hitSphere, len,
                                            _data.groundLayer, QueryTriggerInteraction.Ignore);

        _floatHasGround = rayHit || sphereHit;

        // 높이는 '캡슐이 실제로 차지하는 폭'으로 재야 한다.
        //
        // 얇은 레이는 캐릭터 중심 한 점만 잰다. 그런데 캡슐 앞면은 중심보다 반지름(약 0.24m) 앞에 있어,
        // 앞면이 다음 계단 챌면에 닿는 순간에도 레이는 아직 낮은 단을 읽는다 — 서보는 계단이 있는 줄도
        // 모르는 상태로 부딪힌다. 실측 계단(단 높이 0.25)에서 이 어긋남이 그대로 나왔다:
        // 한 프레임에 vh 8 → 3.93 으로 꺾이면서 vy 가 그만큼 솟았다(수평이 수직으로 꺾인 충돌 반사).
        //
        // 스피어캐스트 반지름은 이미 캡슐 반지름에 맞춰져 있다. 그 값으로 높이를 재면 프로브가 캡슐과
        // 같은 순간에 계단을 감지해 서보가 제때 올라간다. 진행 방향을 보지 않는 등방 측정이라
        // 벽에 비비거나 가장자리를 달릴 때 엉뚱한 지면을 읽는 문제가 없다.
        //
        // 단, 벽면을 지면으로 오인하면 벽을 타고 올라가므로 위를 향한 면만 인정한다.
        // 시작 지점이 벽에 파묻힌 경우 스피어캐스트는 거리 0·법선 0 을 주는데 이 검사에 함께 걸린다.
        bool sphereIsGround = sphereHit && hitSphere.normal.y > MinWalkableNormalY;
        if (sphereIsGround && (!rayHit || hitSphere.distance < hitRay.distance))
            _floatHitDist = hitSphere.distance;
        else
            _floatHitDist = rayHit ? hitRay.distance : (sphereHit ? hitSphere.distance : len);

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
        //
        // wasGrounded(직전 프레임)만 보면 한 프레임만 놓쳐도 스텝다운이 영구히 끊긴다.
        // 실측에서 그 순간이 잡혔다 — 지면을 다시 찾았는데도(hasGround=True, hitDist 0.8 ≤ 0.9)
        // wasGrounded 가 이미 false 라 snapDown 이 성립하지 않아 -6.3m/s 로 계속 낙하했다.
        // 짧은 유예를 둬서 순간적인 미적중이 하강 추종을 끊지 못하게 한다.
        if (nearGround) _snapDownGraceUntil = Time.time + SnapDownGrace;
        bool recentlyGrounded = wasGrounded || Time.time < _snapDownGraceUntil;
        bool snapDown   = recentlyGrounded && _floatHasGround && _floatHitDist <= ride + stepDown;
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

        // 계단 하강 — 스프링에 맡기면 못 따라간다.
        //
        // 접지 상태에서 순가속이 0 이 되는 지점을 풀면  error·k − vy·c + g·m = g·m  →  vy = error·k/c.
        // 우리 값(k 4000, c 900)으로는 error −0.15 에서 −0.67m/s, 한계인 −0.5 에서도 −2.22m/s 가 상한이다.
        // 댐퍼의 −vy·c 항이 하강 속도에 비례해 위로 밀기 때문이다.
        // 걸어서 계단을 내려갈 때 필요한 하강 속도가 이 상한을 넘으면 캐릭터가 계단면에서 떨어져 나가고,
        // 그 순간부터 낙하가 된다 — 실측에서 vy 가 상한(−0.9~−1.4)에 붙어 있던 것이 그 증거다.
        //
        // 그래서 '떠 있는 만큼'을 짧은 시간에 덮는 속도를 직접 준다. 스프링은 평지·상승만 담당한다.
        // 정상상태 뒤처짐 = 필요한 하강속도 × 시간상수.
        // 시간상수를 0.08 로 뒀더니 3.5m/s 하강 시 0.28m 를 뜬 채 끌려갔다(로그와 정확히 일치).
        // 물리 스텝(0.02s)으로 줄여 한 프레임에 덮게 하고, 속도 상한으로 과격함만 막는다.
        // 발동 사각지대도 FloatGroundTolerance(0.15) 대신 좁은 데드밴드로 바꿔 일찍 개입한다.
        float rideH = _data.floatRideHeight;
        if (_isGrounded && _floatHasGround)
        {
            // 하나의 위치 서보로 처리한다. 예전에는 하강·상승·스프링 세 갈래였는데,
            // 각 갈래가 "더 빠르게만" 만들고 줄이지는 않아 경계에서 속도가 그대로 남았다.
            // 내리막 끝에서 평지에 닿는 순간 excess 가 0 이 되어 분기를 벗어나고,
            // -6m/s 를 안은 채 스프링으로 넘어가 제동하는 동안 계속 내려갔다 — 그게 마지막의 낙하다.
            //
            // 오차에 비례한 목표 속도를 매 프레임 계산하고 그쪽으로 '가속도 상한을 지키며' 접근한다.
            //   · 오차가 줄면 목표 속도도 줄어 자연히 제동된다(별도 브레이크 불필요)
            //   · 목표가 오차로 묶여 있어 위로 쏘아 올릴 여지가 없다
            //   · 가속도 상한이 곧 보간이라 Y 가 계단식으로 튀지 않는다(카메라 흔들림 방지)
            float err = _floatHitDist - rideH;          // + 면 떠 있음(내려가야), − 면 눌림(올라가야)

            float demandVy = -err / FollowTime;         // 오차를 FollowTime 안에 없애려면 필요한 속도
            float targetVy = Mathf.Clamp(demandVy, -MaxStepDownFollowSpeed, MaxStepUpFollowSpeed);
            FollowVertical(rb, targetVy);

            // 단차를 오르는 동안 수평 속도를 제한한다. 사람도 계단을 평지처럼 달리지는 않는다.
            //
            // 예전에는 매 프레임 속도에 감쇠 계수를 곱했는데, 그게 계단에서 걸리는 느낌의 원인이었다.
            // 위치 서보는 원리상 '필요 추종속도 × FollowTime' 만큼 항상 뒤처진다(실측 오차 +0.25 고정,
            // 필요속도 5m/s × FollowTime 0.05 와 일치). 계단을 오르는 내내 오차가 남는다는 뜻이다.
            // 곱셈 감쇠는 접촉이 이어지는 동안 무한히 누적되므로, 그 상시 오차와 만나 계단 위에서
            // 계속 깎였다 — 실측 수평속도 8 → 5.95. 저항 계수를 절반으로 줄여도 누적은 그대로다.
            //
            // 그래서 감쇠가 아니라 '속도 상한'으로 바꾼다. 상한은 누적되지 않으므로 계단에서는
            // 느리지만 일정한 속도로 수렴하고, 단차를 벗어나면 곧바로 원래 속도로 돌아온다.
            float deficit = -err;                        // 올라가야 할 높이
            if (deficit > StepUpDeadband)
            {
                float t = Mathf.Clamp01((deficit - StepUpDeadband) /
                                        (StepResistanceFullAt - StepUpDeadband));
                float cap = Mathf.Lerp(StepClimbSpeedCapMax, StepClimbSpeedCapMin, t);

                Vector3 v = rb.linearVelocity;
                Vector3 h = new Vector3(v.x, 0f, v.z);
                float sp = h.magnitude;
                if (sp > cap)
                {
                    // 상한을 즉시 꽂으면 그것대로 툭 걸리는 느낌이 난다. 감속도 상한을 지켜 접근한다.
                    float capped = Mathf.MoveTowards(sp, cap, StepClimbDecel * Time.fixedDeltaTime);
                    h *= capped / sp;
                    rb.linearVelocity = new Vector3(h.x, v.y, h.z);
                }
            }
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