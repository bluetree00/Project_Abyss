using UnityEngine;

/// <summary>
/// 보스 클리어 드래프트로 획득한 <b>유물 파츠(개화)</b>의 런타임 핸들러.
///
/// 룬 효과(<see cref="IRuneEffect"/>)와 같은 전투 신호(적중·치명·피격·스킬·처치·Tick)에 반응하되,
/// 데이터 소스와 수명이 다르다:
///  · 룬  = 존 배치로 런 중 수시로 변동(단계 오르내림).
///  · 파츠 = 보스 드래프트로 <b>런 고정</b> 획득 — 한 번 얻으면 런 끝까지 유지, 중복 획득 없음.
///
/// 그래서 룬 디스패처에 얹지 않고 <see cref="RelicPartEffectHandler"/>가 따로 팬아웃한다.
/// 신호 배관(NotifyHit 등)은 룬과 같은 지점에서 한 줄씩 갈라 나온다.
///
/// 본문은 단계적으로 채운다 — effect_key마다 하나씩. 미구현 key는 <see cref="RelicPartEffect"/>
/// 스켈레톤(빈 훅)으로 연결만 보장하므로, 효과가 없어도 드래프트·획득·저장은 그대로 동작한다.
/// </summary>
public interface IRelicPartEffect
{
    /// <summary>이 효과를 만든 파츠 데이터의 effect_key(1:1). 중복 활성 방지·조회 키.</summary>
    string EffectKey { get; }

    /// <summary>유물 성장 v2 — 이 조각의 등급(드러난 줄 수: 흐릿 1 · 선명 2 · 찬란 3).</summary>
    RelicMemoryGrade Grade { get; }

    /// <summary>등급을 정한다(획득 직전 · 「선명하게」로 오를 때). 효과는 늘어난 줄을 켠다.</summary>
    void SetGrade(RelicMemoryGrade grade, PlayerController player);

    /// <summary>획득(활성화) 시 1회. 패시브 스탯 부여·상태 초기화에 쓴다.</summary>
    void OnAcquire(PlayerController player);

    /// <summary>플레이어 공격 적중 시.</summary>
    void OnHit(in HitInfo hit, PlayerController player);

    /// <summary>플레이어 치명타 적중 시(OnHit과 함께 발생).</summary>
    void OnCrit(in HitInfo hit, PlayerController player);

    /// <summary>플레이어 피격 시.</summary>
    void OnDamaged(in HitInfo hit, PlayerController player);

    /// <summary>스킬 사용 시.</summary>
    void OnSkillUsed(PlayerController player);

    /// <summary>적 처치 시. deadEnemy = 방금 죽은 적 GameObject(위치·잔여 상태 참조용, null 가능).</summary>
    void OnKill(GameObject deadEnemy, PlayerController player);

    /// <summary>매 프레임. 지속·게이지·장판 갱신용.</summary>
    void Tick(float dt, PlayerController player);

    /// <summary>해제 시(런 종료). 등록 정리·패시브 회수.</summary>
    void OnRemove(PlayerController player);
}
