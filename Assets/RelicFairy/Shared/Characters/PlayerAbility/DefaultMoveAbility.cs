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

    public void Move(PlayerController owner, Vector3 direction)
    {
        if (owner.IsKnockback) return;

        if (direction.sqrMagnitude < 0.01f)
        {
            owner.StopHorizontalMovement();
            return;
        }

        Vector3 moveDir = direction.normalized;
        // 달리기 상태(LocoMoveState 결정)면 달리기 속도(baseRunSpeed 설정 시), 아니면 걷기(baseMoveSpeed).
        float baseSpd = (owner.IsRunning && owner.CharacterData.baseRunSpeed > 0.01f)
            ? owner.CharacterData.baseRunSpeed
            : owner.CharacterData.baseMoveSpeed;
        float speed = baseSpd * (owner.RuntimeStats?.MoveSpeedMultiplier ?? 1f);

        owner.Rigid.linearVelocity = new Vector3(moveDir.x * speed, owner.Rigid.linearVelocity.y, moveDir.z * speed);

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
