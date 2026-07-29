using UnityEngine;

/// <summary>
/// 런 내 전투/진행 이벤트를 수신하는 인터페이스.
/// CovenantHandler가 GameRunSession/PlayerController 이벤트를 수신해 전달한다.
/// </summary>
public interface ICovenantEventListener
{
    void OnRoomEnter();
    void OnRoomClear();
    void OnKill(GameObject target);
    void OnAttackHit(GameObject target, float damage);
    void OnTakeDamage(float damage);
    void OnSkillUse(SkillType skill);

    /// <summary>무기 교체 시 호출(이전→다음). CovenantHandler가 WeaponManager.OnWeaponChanged를 어댑트.</summary>
    void OnWeaponSwap(WeaponData prev, WeaponData next);

    /// <summary>매 프레임 호출. 시간 기반 효과(쿨타임, 자동 발동 등)에 사용.</summary>
    void Tick(float deltaTime);
}
