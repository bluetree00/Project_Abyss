/// <summary>
/// 조립 서약 — 원인(Cause) 발동 계기. 각 값은 CovenantBase의 이벤트 훅에 매핑.
/// (MVP: 확실한 훅이 있는 계기만. Crit/Dash 등은 전용 리스너 부재로 후속.)
/// </summary>
public enum CauseTriggerKind
{
    OnHitStreakSameTarget,  // 같은 적 N연타 (OnAttackHit)
    OnKillStreak,           // N연속 처치 (OnKill)
    OnWeaponSwapWindow,     // 무기 교체 후 N초 내 공격 (OnWeaponSwap + OnAttackHit)
    OnRoomClear,            // 방 클리어 (OnRoomClear)
    Periodic,               // N초 주기 (Tick)
    OnSkillUse,             // 스킬 사용 (OnSkillUse)
    OnKillThenHitWindow,    // 사냥 개시: 처치 직후 N초 내 공격 (OnKill 창 + OnAttackHit)
    OnFirstHitInRoom,       // 선제: 방 진입 후 첫 타격 (OnRoomEnter 무장 + OnAttackHit)
    OnProximity,            // 포위: 인접 적 N+ 유지 시 주기 발동 (Tick + 근접 카운트)
    OnMoveDistance,         // 행군: 누적 이동 거리 N마다 (Tick + 이동량)
}
