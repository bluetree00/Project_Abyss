using UnityEngine;


/// <summary>
/// 애니메이션 이벤트를 실행할때 ExecuteAbility를 호출해서 이펙트, 콜라이더, 물리력을 처리
/// </summary>
public class PlayerWeaponHandler : MonoBehaviour
{
    [Header("패키지 참조")]
    public WeaponEffectPackageSO effectPackage;
    public WeaponColliderPackageSO colliderPackage;

    // 핸들러 참조
    public IEffectHandler effectHandler;
    public IColliderHandler colliderHandler;
    public IPhysicsHandler physicsHandler;

    private void Awake()
    {
        // 기본 핸들러 생성 (필요 시 커스텀 핸들러로 교체 가능)
        effectHandler ??= new BaseEffectHandler();
        colliderHandler ??= new BaseColliderHandler();
        physicsHandler ??= new BasePhysicsHandler();
    }

    //이런식의 함수로 어빌리티 실행인데 코보 인덱스와 스텝을 기반으로 어빌리티를 사용해야함
    //현재 플레이어의 상태가 air 인지 ground인지 구분하고 해당 상태의 인덱스 스텝을 사용해야함.
    //그럼 해당 스텝에 맞는 수치를 가져올수 있는데 그것을 기반으로 이펙트, 콜라이더, 물리력을 처리


    //핸들러의 경우 외부에서 집어넣으수 있게하는 함수를 파서 교체할수 있게하는것이 좋을듯
    
    public void ExecuteAbility(WeaponAbilitySO ability, int comboIndex, Transform owner)
    {

        if (ability == null) return;

        effectHandler.ExecuteEffects(ability, comboIndex, owner, effectPackage);
        colliderHandler.ExecuteColliders(ability, comboIndex, owner, colliderPackage);
        physicsHandler.ExecutePhysics(ability, owner);
    }

    public void ResetAbility()
    {
        // 필요 시 초기화
    }
}