using UnityEngine;

public class DefaultMoveAbility : IMoveAbility<PlayerController>
{
    // 회전 부드러움(임계 감쇠 SmoothDamp). 작을수록 빠릿, 클수록 느긋. 0.08s = 반응성/자연스러움 균형.
    private const float RotationSmoothTime = 0.08f;
    // SmoothDampAngle 상태 — 프레임 간 각속도를 보존해야 임계 감쇠가 성립한다.
    private float _yawVelocity;

    // 오를 수 있는 계단 최대 높이 (m)
    private const float StepMaxHeight = 0.35f;
    // 계단 감지 레이 거리 (m)
    private const float StepProbeDistance = 0.4f;
    // 계단 오르는 속도 (m/s) — 값이 클수록 빠르게 오름, 카메라 흔들림과 트레이드오프
    private const float StepClimbSpeed = 8f;

    // ── 가속 모델 폴백 상수 (CharacterData 미설정 시) ──
    private const float DefaultAccel = 90f;        // ≈ 0→8m/s 90ms
    private const float DefaultDecel = 110f;       // ≈ 8→0 73ms (정밀 멈춤 위해 accel 이상)
    private const float DefaultReverseMult = 1.75f;

    public void Move(PlayerController owner, Vector3 direction)
    {
        if (owner.IsKnockback) return;

        var rb = owner.Rigid;
        var cd = owner.CharacterData;
        float dt = Time.deltaTime;
        // 권위 소스(rb)에서 현재 수평속도를 읽어 적분 → 회피 잔여속도/버프와 자동 정합(즉시 0 깎임 없음).
        Vector2 curHoriz = new Vector2(rb.linearVelocity.x, rb.linearVelocity.z);

        // 입력 없음 → 정지 감속 램프(즉시 0 차단 대신 빠른 감속, 정밀 멈춤). StopHorizontalMovement는 외력/연출용으로 유지.
        if (direction.sqrMagnitude < 0.01f)
        {
            float decel = (cd != null && cd.moveDecel > 0.01f) ? cd.moveDecel : DefaultDecel;
            Vector2 stopped = Vector2.MoveTowards(curHoriz, Vector2.zero, decel * dt);
            rb.linearVelocity = new Vector3(stopped.x, rb.linearVelocity.y, stopped.y);
            return;
        }

        Vector3 moveDir = direction.normalized;
        Vector2 moveDir2 = new Vector2(moveDir.x, moveDir.z);

        // ① 느린 목표 레이어: 걷기→달리기 연속 램프(RunBlend01, 0=걷기/1=달리기) × 버프배율 = 현재 최대속도.
        float walkSpd = cd.baseMoveSpeed;
        float runSpd = cd.baseRunSpeed > 0.01f ? cd.baseRunSpeed : walkSpd;
        float baseSpd = Mathf.Lerp(walkSpd, runSpd, Mathf.Clamp01(owner.RunBlend01));
        float currentMaxSpeed = baseSpd * (owner.RuntimeStats?.MoveSpeedMultiplier ?? 1f);

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

        // 회전 권한 일원화: 공격/스킬 등 Act 상태가 facing을 소유 중이면 이동 회전은 양보(매 프레임 경합 방지).
        if (owner.IsActionControllingFacing) return;

        // 이동 방향(=실제 진행 방향)으로 Yaw만 임계 감쇠 회전 — 프레임률 독립, 오버슈트 없음.
        // 실제 적용은 PlayerController.FixedUpdate(ApplyFacing)에서 Rigidbody에 한다.
        // (Update에서 Rigidbody.rotation 직접 대입 시 보간 타이밍과 어긋나 회전 각도에서 진동 발생)
        float currentYaw = owner.Rigid.rotation.eulerAngles.y;
        float targetYaw = Quaternion.LookRotation(moveDir).eulerAngles.y;
        float yaw = Mathf.SmoothDampAngle(currentYaw, targetYaw, ref _yawVelocity, RotationSmoothTime);
        owner.RequestFacing(Quaternion.Euler(0f, yaw, 0f));
    }

    // FixedUpdate에서 호출 — 물리 충돌 처리 전 위치를 보정해 계단 수직면과의 충돌 없이 올라감.
    // rb.position 직접 대입 대신 MovePosition + MoveTowards를 사용해 카메라 튀는 현상 방지.
    public void StepClimb(PlayerController owner, Vector3 moveDir)
    {
        Vector3 pos = owner.transform.position;

        // 발 높이에서 앞 장애물 감지
        if (!Physics.Raycast(pos + Vector3.up * 0.05f, moveDir, out var footHit, StepProbeDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return;

        // StepMaxHeight 위는 열려있어야 계단 (막혀있으면 벽)
        if (Physics.Raycast(pos + Vector3.up * (StepMaxHeight + 0.05f), moveDir, StepProbeDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return;

        // 계단 상단 표면 탐색
        Vector3 probeStart = pos + moveDir * (footHit.distance + 0.01f) + Vector3.up * (StepMaxHeight + 0.05f);
        if (!Physics.Raycast(probeStart, Vector3.down, out var topHit, StepMaxHeight + 0.1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return;

        float deltaY = topHit.point.y - pos.y;
        if (deltaY <= 0.01f || deltaY > StepMaxHeight) return;

        // 한 프레임에 StepClimbSpeed * dt 만큼씩 이동 → 카메라가 부드럽게 따라옴
        float newY = Mathf.MoveTowards(pos.y, topHit.point.y, StepClimbSpeed * Time.fixedDeltaTime);
        owner.Rigid.MovePosition(new Vector3(pos.x, newY, pos.z));
        var rb = owner.Rigid;
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
    }
}
