/// <summary>
/// 사망 상태를 외부에서 확인할 수 있는 인터페이스.
/// 몬스터 등 IDamageable 구현체가 함께 구현하면
/// 킬 패시브(PassiveTrigger.OnKill) 감지에 활용된다.
/// </summary>
public interface IKillable
{
    bool IsDead { get; }
}
