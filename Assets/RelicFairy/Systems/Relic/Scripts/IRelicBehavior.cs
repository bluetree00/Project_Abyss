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

    /// <summary>피격 데미지 보정(갈라하드 방패 감소 등). 그대로면 dmg 반환.</summary>
    int ModifyIncomingDamage(PlayerController owner, int dmg, GameObject attacker);
}
