using UnityEngine;

/// <summary>
/// 지상 이동 전략 — 수평 가속/감속·회전 요청(Move)과 단차 오르기(StepClimb).
/// PlayerController가 소유하고, 이동 상태(LocoIdle/LocoMove)가 Move를, FixedUpdate가 StepClimb를 부른다.
/// </summary>
public interface IMoveAbility
{
    void Move(PlayerController owner, Vector3 direction);
    void StepClimb(PlayerController owner, Vector3 moveDir);
}
