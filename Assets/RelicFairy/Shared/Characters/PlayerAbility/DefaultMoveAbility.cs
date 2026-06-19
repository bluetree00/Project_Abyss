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

    // FixedUpdate에서 호출 — 물리 충돌 처리 전 위치를 보정해 계단 수직면과의 충돌 없이 올라감.
    // rb.position 직접 대입 대신 MovePosition + MoveTowards를 사용해 카메라 튀는 현상 방지.
    public void StepClimb(PlayerController owner, Vector3 moveDir)
    {
        var rb = owner.Rigid;
        var cd = owner.CharacterData;
        Vector3 pos = owner.transform.position;
        int groundMask = cd != null ? cd.groundLayer.value : Physics.DefaultRaycastLayers;

        // 실제로 이동방향으로 전진 중일 때만 오른다 — 벽에 막혀 속도≈0(정지/끼임)인데
        // 입력만 들어오는 상태에서 캐릭터를 들어올려 모서리에 끼이는 고정 현상 방지.
        Vector3 horizVel = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        if (Vector3.Dot(horizVel, moveDir) < MinStepForwardSpeed) return;

        // 발 높이에서 앞 계단 면 감지 — 지면 레이어만(소품/적 오감지 방지).
        if (!Physics.Raycast(pos + Vector3.up * 0.05f, moveDir, out var footHit, StepProbeDistance, groundMask, QueryTriggerInteraction.Ignore))
            return;

        // StepMaxHeight 위가 막혀있으면 계단이 아니라 벽 — 모든 솔리드 기준으로 보수적으로 차단(오르기 스킵만).
        if (Physics.Raycast(pos + Vector3.up * (StepMaxHeight + 0.05f), moveDir, StepProbeDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return;

        // 계단 상단 표면 탐색 — 지면 레이어만.
        Vector3 probeStart = pos + moveDir * (footHit.distance + 0.01f) + Vector3.up * (StepMaxHeight + 0.05f);
        if (!Physics.Raycast(probeStart, Vector3.down, out var topHit, StepMaxHeight + 0.1f, groundMask, QueryTriggerInteraction.Ignore))
            return;

        float deltaY = topHit.point.y - pos.y;
        if (deltaY <= 0.01f || deltaY > StepMaxHeight) return;

        float dt = Time.fixedDeltaTime;

        // 상승률을 수평 접근에 비례시킨다(남은 접근거리에 걸쳐 계단 높이를 올림) → 이동속도와 무관하게
        // 계단 모서리에서 상단에 정확히 도달. 미리 떠오르거나 팝업되는 느낌 제거. 너무 가까우면 최대치로 마무리.
        float horizSpeed = new Vector2(rb.linearVelocity.x, rb.linearVelocity.z).magnitude;
        float climbRate = footHit.distance > 0.02f ? deltaY * horizSpeed / footHit.distance : MaxStepClimbSpeed;
        climbRate = Mathf.Clamp(climbRate, MinStepClimbSpeed, MaxStepClimbSpeed);
        float newY = Mathf.MoveTowards(pos.y, topHit.point.y, climbRate * dt);

        // 수평 전진을 유지하면서 Y만 올린다(수평 위치 고정 제거 → 멈칫/지터 해소).
        // StepClimb은 물리 충돌 처리 전에 위치를 보정하므로, 위로 올려 캡슐이 계단 면에 끼지 않게 한 뒤 전진을 이어간다.
        // MovePosition 목표의 수평 성분을 이번 스텝 수평 속도와 일치시켜 velocity 지정과 충돌하지 않게 한다.
        Vector3 horizStep = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z) * dt;
        rb.MovePosition(new Vector3(pos.x + horizStep.x, newY, pos.z + horizStep.z));

        // 수직 속도만 0(바운스 방지). 수평 속도는 유지.
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
    }
}
