using UnityEngine;

/// <summary>
/// 유물 클래스의 코드 로직(패시브 셋업/고유스킬/피해 훅). RelicId → 구현체 매핑은 RelicRegistry.
/// 데이터(패시브SO/오라/UI)는 RelicClassSO, 로직은 이 전략 객체로 분리(상속 → 합성).
/// </summary>
public interface IRelicBehavior
{
    /// <summary>유물 적용 시 1회 — 패시브 등록, 컴포넌트 추가, 이벤트 구독 등.</summary>
    void OnAttach(PlayerController owner);

    /// <summary>정리 — 구독 해제 등.</summary>
    void OnDetach(PlayerController owner);

    /// <summary>고유 스킬 런타임 (없으면 null).</summary>
    ISkillRuntime CreateSkillRuntime(PlayerController owner, SkillType slot);

    /// <summary>고유 스킬 쿨다운(초). 0이면 무기 쿨다운 사용.</summary>
    float GetSkillCooldown(SkillType slot);

    /// <summary>
    /// 고유 스킬 발동 가능 여부(리소스 게이팅). 정오 구간 한정·스택 조건 등.
    /// true=발동 허용(레거시 기본). 자동 발동형(랜슬롯)은 수동 입력을 false로 막고 내부에서 발동.
    /// </summary>
    bool CanUseSkill(SkillType slot);

    /// <summary>
    /// UI용 — 지금 Q를 몇 번 더 쓸 수 있나(못 쓰면 0). HUD Q칸의 잠김 · 「×N」은 이 값과 <see cref="CanUseSkill"/>을 읽는다(10-12 A4).
    /// 정오 · 광란만 보던 IRelicResource.IsSkillReady는 실제 문과 달랐다(가웨인 붙든 새벽 · 황혼 사용권 · 정오에 쓴 뒤, 랜슬롯 심판 소진).
    /// </summary>
    int QUsesLeft { get; }

    /// <summary>피격 데미지 보정(갈라하드 방패 감소 등). 그대로면 dmg 반환.</summary>
    int ModifyIncomingDamage(PlayerController owner, int dmg, GameObject attacker);
}
