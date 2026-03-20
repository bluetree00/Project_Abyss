/// <summary>
/// 패시브 발동 조건 열거형.
/// PlayerController가 해당 이벤트 발생 시 FirePassive()로 전파한다.
/// </summary>
public enum PassiveTrigger
{
    OnAttackHit,    // 공격이 적에게 적중
    OnKill,         // 적 처치 (IKillable 구현체가 IsDead == true 일 때)
    OnSkillUse,     // Q/E/R 스킬 사용
    OnDodge,        // 회피 시작
    OnComboFinish,  // 콤보 마지막 타 완료
    OnTakeDamage,   // 피격
}
