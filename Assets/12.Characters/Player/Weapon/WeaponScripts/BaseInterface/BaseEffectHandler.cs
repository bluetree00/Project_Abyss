using System.Collections;
using UnityEngine;

/// <summary>
/// 간단한 베이스 이펙트 핸들러
/// - SpawnEffectAction의 addressableKey로 prefab을 찾아 인스턴스화
/// - lifeTime 후 자동 파괴(또는 비활성화)
/// - 실제 프로젝트에서는 Addressables.InstantiateAsync로 교체 권장
/// </summary>
public class BaseEffectHandler : IEffectHandler
{
    public void ExecuteEffects(WeaponAbilitySO ability, int comboIndex, Transform owner, WeaponEffectPackageSO effectPackage)
    {
        //어빌리티에 있는 값으로 어드레서블 풀러를 사용해서 해당 타이밍에 이펙트 재생
    }


}
