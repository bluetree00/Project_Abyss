using UnityEngine;

public class DefaultMoveAbility : IMoveAbility<PlayerController>
{
    private const float MoveRotationSpeed = 22f;

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
        float speed = owner.CharacterData.baseMoveSpeed * (owner.RuntimeStats?.MoveSpeedMultiplier ?? 1f);

        owner.Rigid.linearVelocity = new Vector3(moveDir.x * speed, owner.Rigid.linearVelocity.y, moveDir.z * speed);

        Quaternion targetRot = Quaternion.LookRotation(moveDir);
        owner.transform.rotation = Quaternion.Slerp(owner.transform.rotation, targetRot, Time.deltaTime * MoveRotationSpeed);
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
