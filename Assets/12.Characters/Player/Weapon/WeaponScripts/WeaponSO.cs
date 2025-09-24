using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName="Game/WeaponSO")]
public class WeaponSO : ScriptableObject {
    public string weaponKey;
    public string displayName;
    public string prefabKey;
    public string iconKey;
    public int stat_version;
    public float baseAttack;
    public float baseDefense;
    public int groundEndCount;
    public int airEndCount;
    public AnimationSetSO groundAnimSet; //NOTE: 애니메이션 클립을 넣게 변경해야함
    public AnimationSetSO airAnimSet;
    public WeaponAbilitySetSO abilitySet; //NOTE: 공격의 한개씩을 담당 내부에들어가는 어빌리티는 수치뿐만 아니라 SO의 이펙트와 콜라이더를 어떻게 할지 고민해야함
                                          //NOTE: 여기서 완성하지 않아도 하나씩 공격에 들어가는 구성을 완료하면 러너에서 애니메이션 이펙트 콜라이더 셋의 정보를
                                        //NOTE: 받아서 실행한다면 공격타이밍에 맞게 해당 공격들의 이펙트 콜라이더 애니메이션을 분리해서 호출 가능해짐. 
    public EffectPackageSO effectPackage;    //NOTE: 이펙트와 콜라이더는 형태만 잘 지정해주고 필요시 호출대기 러너에서 그대로 호출하기
    public ColliderPackageSO colliderPackage;
}


[System.Serializable]
public class AnimationSlot {
    public string slotName;
    public List<string> variantAnimKeys; // anim://... (Addressables keys)
    public int eventCount; // number of anim events expected
}


[System.Serializable]
public class EffectMapping
{
    public int slotIndex;
    public int eventIndex;
    public List<EffectEntry> effects;
}

[System.Serializable]
public class EffectEntry {
    public string effectKey; // vfx://...
    public string attachPoint; // "hand_r" ...
    public Vector3 localPos;
    public Vector3 localRot;
    public bool follow;
    public float lifetime;
    public string syncColliderId; // optional
}

// ColliderPackageSO, AbilitySetSO similar shape...
