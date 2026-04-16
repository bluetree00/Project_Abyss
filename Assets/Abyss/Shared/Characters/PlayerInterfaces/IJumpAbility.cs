public interface IJumpAbility
{
    bool IsGrounded { get; }
    bool IsJumping { get; }

    void Jump(PlayerController controller);
    void UpdateGroundCheck(PlayerController controller);
    void ApplyGravity(PlayerController controller);
}