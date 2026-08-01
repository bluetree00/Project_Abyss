using UnityEngine;

public class DefaultMoveAbility : IMoveAbility<PlayerController>
{
    // 회전 각속도 폴백(도/초). CharacterData.turnSpeedDegPerSec 미설정 시 사용. 즉발 액션 지향 → 빠른 기본값.
    private const float DefaultTurnSpeed = 720f;

    // 오를 수 있는 계단 최대 높이 (m)
    private const float StepMaxHeight = 0.35f;
    // 계단 감지 레이 거리 (m). 캡슐 반경보다 커야 충돌 전에 감지해 매끄럽게 오름.
    private const float StepProbeDistance = 0.4f;
    // 계단 상승률(m/s) 범위. 실제 상승률은 수평 접근속도에 비례시켜 이 범위로 클램프(속도 무관 일관 스텝).
    private const float MinStepClimbSpeed = 3f;
    private const float MaxStepClimbSpeed = 12f;
    // 계단 오르기 최소 전진 속도(m/s). 이동방향으로 실제 나아갈 때만 오른다 —
    // 벽에 막혀 속도≈0인데 입력만 들어오는 상황에서 캐릭터를 들어올려 끼이는(고정) 문제 방지.
    private const float MinStepForwardSpeed = 1f;

    // ── 가속 모델 폴백 상수 (CharacterData 미설정 시) ──
    private const float DefaultAccel = 90f;        // ≈ 0→8m/s 90ms
    private const float DefaultDecel = 110f;       // ≈ 8→0 73ms (정밀 멈춤 위해 accel 이상)
    private const float DefaultReverseMult = 1.75f;

    // ── 급반전(마찰 제동) 폴백 상수 ──
    // 이 각도(현재 facing↔입력 방향) 이상이면 '급반전' 구간 — 마찰 제동 + 빠른 정면 피벗.
    private const float DefaultSharpTurnAngle = 135f;
    // 급반전 시 이동속도 감속 비율(0~1). CharacterData.sharpTurnMoveSlowdown 미설정(0) 시 사용.
    private const float DefaultSharpTurnBrake = 0.6f;

    public void Move(PlayerController owner, Vector3 direction)
    {
        if (owner.IsKnockback) return;

        var rb = owner.Rigid;
        var cd = owner.CharacterData;
        float dt = Time.deltaTime;
        // 권위 소스(rb)에서 현재 수평속도를 읽어 적분 → 회피 잔여속도/버프와 자동 정합(즉시 0 깎임 없음).
        Vector2 curHoriz = new Vector2(rb.linearVelocity.x, rb.linearVelocity.z);

        // 입력 없음 → 정지 감속 램프(즉시 0 차단 대신 빠른 감속, 정밀 멈춤). 이동 회전 슬루도 정지(마지막 facing 유지).
        if (direction.sqrMagnitude < 0.01f)
        {
            float decel = (cd != null && cd.moveDecel > 0.01f) ? cd.moveDecel : DefaultDecel;
            Vector2 stopped = Vector2.MoveTowards(curHoriz, Vector2.zero, decel * dt);
            rb.linearVelocity = new Vector3(stopped.x, rb.linearVelocity.y, stopped.y);
            owner.IntendedSpeed01 = 0f;
            owner.StopFacingSlew();
            return;
        }

        Vector3 moveDir = direction.normalized;
        Vector2 moveDir2 = new Vector2(moveDir.x, moveDir.z);

        // 현재 facing ↔ 이동 방향의 각도차로 '급반전' 여부 판정.
        //  · 미만(안쪽): 속도 유지하며 정면으로 즉시 전환(선회감 제거).
        //  · 이상(바깥): 마찰 제동(감속) + 빠른 정면 피벗 → 무게감 있는 급반전.
        float currentYaw = rb.rotation.eulerAngles.y;
        float targetYaw = Quaternion.LookRotation(moveDir).eulerAngles.y;
        float facingDiff = Mathf.Abs(Mathf.DeltaAngle(currentYaw, targetYaw));
        float sharpAngle = (cd != null && cd.sharpTurnAngle > 0.01f) ? cd.sharpTurnAngle : DefaultSharpTurnAngle;
        bool sharpTurn = facingDiff >= sharpAngle;

        // ① 느린 목표 레이어: 걷기→달리기 연속 램프(RunBlend01, 0=걷기/1=달리기) × 버프배율 = 현재 최대속도.
        float walkSpd = cd.baseMoveSpeed;
        float runSpd = cd.baseRunSpeed > 0.01f ? cd.baseRunSpeed : walkSpd;
        float baseSpd = Mathf.Lerp(walkSpd, runSpd, Mathf.Clamp01(owner.RunBlend01));
        float currentMaxSpeed = baseSpd * (owner.RuntimeStats?.MoveSpeedMultiplier ?? 1f);

        // 저스트 회피 슬로모 — 세계는 느려져도 플레이어는 빠르게 움직인다(평소 1이라 무영향).
        currentMaxSpeed *= owner.BonusMoveSpeedMultiplier;

        // 애니 블렌드용 '의도 속도비' 공개 — 급반전 마찰 제동을 적용하기 전 값이다.
        // 제동은 의도된 순간 감속이라 애니가 그대로 따라가면 달리다가 걷기로 튄다.
        owner.IntendedSpeed01 = runSpd > 0.01f ? Mathf.Clamp01(currentMaxSpeed / runSpd) : 0f;

        // [마찰 제동] 급반전 구간(sharpTurn)에서만 이동속도를 깎아 무게감 부여 → 안쪽 각도는 감속 없이 속도 유지.
        // 조준이 facing을 주도하는 공격/스킬 중에는 적용하지 않는다(move-vs-aim 오판 방지).
        if (sharpTurn && !owner.IsActionControllingFacing)
        {
            float brake = cd != null && cd.sharpTurnMoveSlowdown > 0.0001f
                ? Mathf.Clamp01(cd.sharpTurnMoveSlowdown)
                : DefaultSharpTurnBrake;
            // sharpAngle→180° 사이에서 제동 깊이를 비례 적용(임계 진입은 가볍게, 정반대일수록 강하게).
            float brake01 = Mathf.Clamp01((facingDiff - sharpAngle) / Mathf.Max(1f, 180f - sharpAngle));
            currentMaxSpeed *= Mathf.Lerp(1f, 1f - brake, brake01);
        }

        // ② 빠른 추격 레이어: 목표(moveDir×currentMaxSpeed)를 가속으로 추격.
        Vector2 target = moveDir2 * currentMaxSpeed;
        float accel = (cd != null && cd.moveAccel > 0.01f) ? cd.moveAccel : DefaultAccel;
        // 역방향 전환이면 가속 부스트(빠릿한 반전).
        if (curHoriz.sqrMagnitude > 0.01f && Vector2.Dot(curHoriz, target) < 0f)
            accel *= (cd != null && cd.reverseAccelMultiplier > 0.01f) ? cd.reverseAccelMultiplier : DefaultReverseMult;
        // 초기 부스트: 정지→출발 첫 프레임 최소 출발속도(walkMax 비율).
        float boost = cd != null ? Mathf.Clamp(cd.initialBoost, 0f, 0.15f) : 0f;
        if (boost > 0f && curHoriz.sqrMagnitude < 0.0001f)
            curHoriz = moveDir2 * (walkSpd * boost);

        Vector2 newHoriz = Vector2.MoveTowards(curHoriz, target, accel * dt);
        newHoriz = Vector2.ClampMagnitude(newHoriz, currentMaxSpeed); // 합속도 제한
        rb.linearVelocity = new Vector3(newHoriz.x, rb.linearVelocity.y, newHoriz.y);

        // 회전 권한 일원화: 공격/스킬 등 Act 상태가 facing을 소유 중이면 이동 회전(슬루)을 정지하고 양보.
        if (owner.IsActionControllingFacing) { owner.StopFacingSlew(); return; }

        // 이동 방향(정면)을 향해 일정 각속도로 회전. step을 직접 계산하지 않고 '목표 Yaw + 각속도'만 넘긴다.
        // 실제 적분은 PlayerController.FixedUpdate(ApplyFacing)에서 fixedDeltaTime으로 수행 → 프레임률 독립
        // (Update에서 Rigidbody.rotation을 읽어 step을 계산하면 물리 스텝 사이엔 같은 값이라 고FPS에서 회전이 느려진다).
        float turnSpeed = (cd != null && cd.turnSpeedDegPerSec > 0.01f) ? cd.turnSpeedDegPerSec : DefaultTurnSpeed;
        owner.RequestFacingSlew(targetYaw, turnSpeed);
    }

    // FixedUpdate에서 호출 — 진행방향(실제 수평속도) 기준으로 앞 계단을 감지해, 위치 강제(MovePosition) 없이
    // 수직 "속도"로 올린다(물리 피드백). 목적지 헤드룸을 확인해 끼임을 차단하고, 통과 못 하면 일절 손대지 않는다
    // (벽에선 순수 물리대로 멈춤 → 고정/끼임 없음).
    public void StepClimb(PlayerController owner, Vector3 moveDir)
    {
        var rb = owner.Rigid;
        var cd = owner.CharacterData;
        if (rb == null) return;

        // 플로팅 컨트롤러가 켜져 있으면 호버 스프링이 단차를 처리하므로 StepClimb는 비활성(충돌 방지).
        if (cd != null && cd.useFloatingController) return;

        // 진행 방향 = 실제 수평 속도(입력 moveDir이 아님 — 관성/미끄러짐/회전에도 실제 이동 기준).
        // 전진 속도가 충분할 때만 — 벽에 막혀 속도≈0인데 입력만 있는 상태에선 오르지 않음(고정 방지).
        Vector3 horizVel = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        float speed = horizVel.magnitude;
        if (speed < MinStepForwardSpeed) return;
        Vector3 dir = horizVel / speed;

        int groundMask = cd != null ? cd.groundLayer.value : Physics.DefaultRaycastLayers;
        Vector3 pos = owner.transform.position;

        // 1) 발치 전방 계단 면 감지 — 지면 레이어만(소품/적 오감지 방지).
        if (!Physics.Raycast(pos + Vector3.up * 0.05f, dir, out var footHit, StepProbeDistance, groundMask, QueryTriggerInteraction.Ignore))
            return;

        // 2) StepMaxHeight 위가 막혀있으면 계단이 아니라 벽 — 모든 솔리드 기준 차단(오르기 스킵).
        if (Physics.Raycast(pos + Vector3.up * (StepMaxHeight + 0.05f), dir, StepProbeDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return;

        // 3) 계단 상단 표면 탐색 — 지면 레이어만.
        Vector3 probeStart = pos + dir * (footHit.distance + 0.05f) + Vector3.up * (StepMaxHeight + 0.05f);
        if (!Physics.Raycast(probeStart, Vector3.down, out var topHit, StepMaxHeight + 0.1f, groundMask, QueryTriggerInteraction.Ignore))
            return;

        float deltaY = topHit.point.y - pos.y;
        if (deltaY <= 0.02f || deltaY > StepMaxHeight) return;

        // 4) 목적지 헤드룸 — 상단에 캡슐이 들어갈 천장 여유 확인(벽/천장으로 기어올라 끼임 차단).
        if (!HasHeadroom(owner, topHit.point)) return;

        // 5) 물리 피드백 상승 — 위치 강제 없이 수직 속도로. remaining/dt 캡으로 오버슈트 0(상단에 정확히 안착).
        float dt = Time.fixedDeltaTime;
        float remaining = topHit.point.y - pos.y;
        float climbRate = Mathf.Clamp(deltaY * speed / Mathf.Max(0.05f, footHit.distance), MinStepClimbSpeed, MaxStepClimbSpeed);
        float vy = Mathf.Min(climbRate, remaining / dt);
        if (rb.linearVelocity.y < vy)
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, vy, rb.linearVelocity.z);

        owner.MarkStepClimbing();   // 상승 중 공중/낙하 상태 억제(groundCheckDistance < 스텝높이 대응)
    }

    // 계단 상단에 캐릭터 캡슐이 설 공간(천장 여유)이 있는지 확인. 콜라이더는 1회만 캐싱.
    private CapsuleCollider _capsule;
    private bool _capsuleResolved;
    private bool HasHeadroom(PlayerController owner, Vector3 groundPoint)
    {
        if (!_capsuleResolved) { owner.TryGetComponent(out _capsule); _capsuleResolved = true; }
        float h = _capsule != null ? _capsule.height * Mathf.Max(owner.transform.lossyScale.y, 0.01f) : 1.6f;
        // 상단 발 위치에서 위로 캡슐 높이만큼 막혔는지(천장) — 약간 띄워 바닥 자기충돌 회피.
        return !Physics.Raycast(groundPoint + Vector3.up * 0.1f, Vector3.up, h - 0.15f,
                                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
    }
}
