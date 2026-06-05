/// <summary>
/// 멀린 룬 속성 단계 효과의 런타임 핸들러 인터페이스.
/// 단계 도달 시 RuneEffectDispatcher에 등록되고, 캐릭터 전투 이벤트에 반응한다.
///
/// 본문 구현은 단계적으로 채운다(스켈레톤). 현재는 빈 구현(RuneEffect)으로 연결만 보장.
/// 전투 신호는 HitFeedbackService.OnHit(공격/피격) + 캐릭터 직접 호출(스킬/처치/Tick)로 들어온다.
/// </summary>
public interface IRuneEffect
{
    string EffectType { get; }
    RuneSynergyEntry Entry { get; }

    /// <summary>단계 도달 시 1회. 패시브 스탯 적용·상태 초기화에 사용.</summary>
    void OnActivate(PlayerController player);

    /// <summary>플레이어 공격 적중 시.</summary>
    void OnHit(in HitInfo hit, PlayerController player);

    /// <summary>플레이어 치명타 적중 시(OnHit과 함께 발생).</summary>
    void OnCrit(in HitInfo hit, PlayerController player);

    /// <summary>플레이어 피격 시.</summary>
    void OnDamaged(in HitInfo hit, PlayerController player);

    /// <summary>스킬 사용 시(캐릭터에서 NotifySkillUsed 호출).</summary>
    void OnSkillUsed(PlayerController player);

    /// <summary>적 처치 시(캐릭터에서 NotifyKill 호출).</summary>
    void OnKill(PlayerController player);

    /// <summary>매 프레임. 지속/장판/게이지 갱신용.</summary>
    void Tick(float dt, PlayerController player);

    /// <summary>효과 해제 시(런 종료·단계 하락). 등록 정리.</summary>
    void OnDeactivate();
}
