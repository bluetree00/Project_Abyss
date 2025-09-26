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

    // 콤보 길이(검/활 등 무기별 최대 콤보 수)
    public int groundEndCount;
    public int airEndCount;

    // 애니메이션 세트 (기존 유지)
    public WeaponAnimationSetSO groundAnimSet; // NOTE: 애니메이션 클립 키(주소)로 변경 권장
    public WeaponAnimationSetSO airAnimSet;

    // 능력/이펙트/콜라이더 패키지 (주소/메타데이터)
    public WeaponAbilitySetSO abilitySet;
    public WeaponEffectPackageSO effectPackage;
    public WeaponColliderPackageSO colliderPackage;

    // ===== 콤보 / 이벤트 매핑(Inspector 편집용) =====
    // 콤보 매핑은 ground / air 각각에 대해 작성 가능
    public List<ComboMapping> groundComboMappings = new List<ComboMapping>();
    public List<ComboMapping> airComboMappings = new List<ComboMapping>();

    // Helper: 런타임에서 빠르게 검색할 수 있도록 간단한 조회 메소드 제공.
    // (런타임 캐싱(예: Dictionary) 는 WeaponController 같은 런타임 객체에서 생성하세요.)
    public EventMapping GetEventMapping(bool isAir, int comboIndex, int eventIndex)
    {
        var list = isAir ? airComboMappings : groundComboMappings;
        if (list == null) return null;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].comboIndex == comboIndex)
            {
                var events = list[i].events;
                if (events == null) return null;
                for (int j = 0; j < events.Count; j++)
                {
                    if (events[j].eventIndex == eventIndex)
                        return events[j];
                }
            }
        }
        return null;
    }

    // Helper: 이펙트 목록 반환 (null 가능)
    public List<EffectEntry> GetEffects(bool isAir, int comboIndex, int eventIndex)
    {
        var m = GetEventMapping(isAir, comboIndex, eventIndex);
        return m?.effects;
    }

    // Helper: 콜라이더 id 목록 반환
    public List<string> GetColliderIds(bool isAir, int comboIndex, int eventIndex)
    {
        var m = GetEventMapping(isAir, comboIndex, eventIndex);
        return m?.colliderIds;
    }

    // Helper: abilityKey 반환
    public string GetAbilityKey(bool isAir, int comboIndex, int eventIndex)
    {
        var m = GetEventMapping(isAir, comboIndex, eventIndex);
        return m?.abilityKey;
    }
}


[System.Serializable]
public class AnimationSlot {
    public string slotName;
    public List<string> variantAnimKeys; // anim://... (Addressables keys)
    public int eventCount; // number of anim events expected
}


// ---------- NEW: Combo / Event mapping classes ----------

[System.Serializable]
public class ComboMapping {
    [Tooltip("콤보 인덱스 (1부터 시작). groundEndCount/airEndCount 범위 내로 맞춰주세요.")]
    public int comboIndex = 1;

    [Tooltip("이 콤보의 애니메이션 내에 정의된 이벤트 매핑들")]
    public List<EventMapping> events = new List<EventMapping>();
}

[System.Serializable]
public class EventMapping {
    [Tooltip("애니메이션 이벤트 인덱스 (해당 애니클립 내 순번). 예: 1,2,3 ...")]
    public int eventIndex = 1;

    [Tooltip("이 이벤트에서 실행할 Ability 키(또는 enum key string). 우선순위: 파라로 넘긴 abilityKey가 있으면 그것을 사용합니다.")]
    public string abilityKey;

    [Tooltip("이벤트에서 재생할(스폰할) 이펙트들 (addressable keys 을 EffectEntry.effectKey로 사용)")]
    public List<EffectEntry> effects = new List<EffectEntry>();

    [Tooltip("이벤트에서 활성화할 콜라이더 id 들 (ColliderPackageSO에서 정의한 id)")]
    public List<string> colliderIds = new List<string>();
}


// 기존 EffectEntry 재사용 (주소 기반)
[System.Serializable]
public class EffectEntry {
    public string effectKey; // vfx://... 또는 addressable key
    public string attachPoint; // "hand_r" ...
    public Vector3 localPos;
    public Vector3 localRot;
    public bool follow;
    public float lifetime;
    public string syncColliderId; // optional (event와 연동시킬 콜라이더 id)
}


// 기존 EffectMapping은 필요에 따라 유지 가능 (레거시). 
// 그러나 새로운 구조에서는 ComboMapping -> EventMapping -> EffectEntry 를 권장.
// 아래는 호환용으로 남겨둘 수 있음.

[System.Serializable]
public class EffectMapping
{
    public int slotIndex;
    public int eventIndex;
    public List<EffectEntry> effects;
}
