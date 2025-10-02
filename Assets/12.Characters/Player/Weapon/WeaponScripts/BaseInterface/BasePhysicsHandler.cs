using UnityEngine;

/// <summary>
/// 베이스 물리 핸들러
/// - AbilitySO에 정의된 PhysicsForce, selfMovement 등을 사용하여 owner(플레이어)의 Rigidbody 혹은 target에 힘을 가함
/// - 넉백 적용(타겟 쪽 처리)용 헬퍼는 WeaponHitCollider/IHitReceiver를 통해 호출되는 편이 안전
/// </summary>
public class BasePhysicsHandler : IPhysicsHandler
{
    public void ExecutePhysics(WeaponAbilitySO ability, Transform owner)
    {


        // 3) 넉백 등은 히트 시점에 IHitReceiver 쪽에서 ability.knockbackMultiplier 등을 사용해 처리
    }
}
