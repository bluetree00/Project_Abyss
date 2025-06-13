using UnityEngine;

public interface ILightAttackAbility<T> where T : PlayerCharacter
{
    void LightAttack(T controller);

    // 이펙트 스폰을 위한 메서드 선언
    public virtual void SpawnEffect(PlayerCharacter controller, Vector3 forwardOffset, Vector3? additionalRotation = null)
    {
        // 기본 구현 (필요하면 override해서 사용)
    }

}
