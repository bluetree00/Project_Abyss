// [점프 폐기] Jump() / IsJumping 제거 — 공중 상태는 낙하·넉백으로만 진입한다.
// 이름은 유지한다(접지 판정·중력 담당자로서 호출부가 그대로 쓴다).
public interface IJumpAbility
{
    bool IsGrounded { get; }

    void UpdateGroundCheck(PlayerController controller);
    void ApplyGravity(PlayerController controller);
}
