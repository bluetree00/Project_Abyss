// 접지 판정과 중력(낙하·호버)을 담당한다.
// [점프 폐기] Jump() / IsJumping 은 제거됐다 — 공중 상태는 낙하·넉백으로만 진입한다.
// 그래서 이름도 담당(접지)에 맞춘다(구 IJumpAbility).
public interface IGroundingAbility
{
    bool IsGrounded { get; }

    void UpdateGroundCheck(PlayerController controller);
    void ApplyGravity(PlayerController controller);
}
