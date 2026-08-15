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

/// <summary>
/// 원인의 <b>형상</b> — 같은 효과라도 "어떤 상황에서 터졌는가"에 따라 결과를 질적으로 바꾸는 축.
///
/// trigger(어떤 훅으로 발동하는가)와 굳이 나눈 이유: 훅이 같아도 형상이 다를 수 있고
/// (연격·선제는 둘 다 OnAttackHit지만 하나는 근접 집착, 하나는 개전 신호다),
/// 무엇보다 <b>효과 쪽 코드가 원인 id를 이름으로 알아보면 안 되기</b> 때문이다.
/// id로 if를 쓰면 원인을 하나 추가할 때마다 효과 구현 전부를 다시 훑어야 한다.
/// 효과는 형상만 보고 분기하고, 어떤 원인이 어떤 형상인지는 팔레트(데이터)가 쥔다.
/// </summary>
public enum CauseClass
{
    Passive,    // 상황과 무관하게 흐르는 것(심장박동)
    Melee,      // 한 대상에 붙어 때리는 집착(연격)
    Mobility,   // 움직임·전환(행군·전환)
    Boundary,   // 방의 경계 — 개전과 종전(선제·개선)
    Skill,      // 스킬 사용(연주)
    Kill,       // 처치 그 자체(사냥 개시·학살)
    Danger,     // 위험에 둘러싸인 상태(포위)
}
