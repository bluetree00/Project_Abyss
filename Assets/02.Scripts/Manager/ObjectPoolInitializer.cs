using System.Collections.Generic;
using UnityEngine;

public static class ObjectPoolInitializer
{
    public static List<ObjectPoolerManager.Pool> GetInitialPools()
    {
        //이펙트 풀러의 추가 사용방법 Resources -> Prefabs -> Effects 에 사용할 이펙트 넣기 이름과 태그 맞춰주기 
        //이펙트의 정보를 생성하는 방법 프로젝트 폴더 마우스 우클릭 크리에이트 -> 맨 위쪽의 Effects -> Effect Data 로 새로운 이펙트 정보 생성
        //사용할 이펙트 오브젝트에 EffectComponent, EffectBehaviour 스크립트 부착후 생성한 이펙트 정보 스크립터블 오브젝트를 부착
        return new List<ObjectPoolerManager.Pool>
        {
            new ObjectPoolerManager.Pool { tag = "ShinySlash", resourcePath = "Effects/ShinySlash", initialSize = 10 },
            new ObjectPoolerManager.Pool { tag = "HitEffect_02", resourcePath = "Effects/HitEffect_02", initialSize = 10 },
            new ObjectPoolerManager.Pool { tag = "DieEffect_01", resourcePath = "Effects/DieEffect_01", initialSize = 5 }
        };
    }
}
