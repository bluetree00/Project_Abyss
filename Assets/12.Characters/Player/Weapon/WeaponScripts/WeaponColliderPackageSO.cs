using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 변경된 ColliderPackageSO : EventMapping now references ColliderSO objects
[CreateAssetMenu(menuName = "Game/ColliderPackageSO")]
public class WeaponColliderPackageSO : ScriptableObject
{
    [Serializable]
    public class EventMapping {
        public int slotIndex;
        public int eventIndex;

        // 직접 SO 레퍼런스를 갖도록 (에디터에서 드래그 앤 드롭)
        public List<WeaponColliderSO> colliders = new List<WeaponColliderSO>();
    }

    public List<EventMapping> mappings = new List<EventMapping>();

    // 런타임용 조회 헬퍼 (단순, 필요하면 캐시화)
    public List<WeaponColliderSO> GetCollidersFor(int slotIndex, int eventIndex) {
        for (int i = 0; i < mappings.Count; i++) {
            var m = mappings[i];
            if (m.slotIndex == slotIndex && m.eventIndex == eventIndex)
                return m.colliders;
        }
        return null;
    }

    // id로 검색하는 보조 메소드 (만약 ID로 참조하고 싶을 때)
    public WeaponColliderSO GetColliderById(string id) {
        foreach (var m in mappings) {
            foreach (var c in m.colliders) {
                if (c != null && c.id == id) return c;
            }
        }
        return null;
    }
}
