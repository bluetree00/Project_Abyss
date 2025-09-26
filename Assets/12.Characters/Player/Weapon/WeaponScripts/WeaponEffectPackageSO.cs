using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Game/EffectPackageSO")]
public class WeaponEffectPackageSO : ScriptableObject
{
    [Serializable]
    public class EventMapping {
        public int slotIndex; // combo index
        public int eventIndex; // anim event index
        // EffectSO 레퍼런스 리스트 (에디터에서 드래그 앤 드롭)
        public List<WeaponEffectSO> effects = new List<WeaponEffectSO>();
    }

    public List<EventMapping> mappings = new List<EventMapping>();

    // 런타임 조회 헬퍼
    public List<WeaponEffectSO> GetEffectsFor(int slotIndex, int eventIndex) {
        for (int i = 0; i < mappings.Count; i++) {
            var m = mappings[i];
            if (m.slotIndex == slotIndex && m.eventIndex == eventIndex)
                return m.effects;
        }
        return null;
    }

    // ID로 검색 (optional)
    public WeaponEffectSO GetEffectById(string id) {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var m in mappings) {
            foreach (var e in m.effects) {
                if (e != null && e.id == id) return e;
            }
        }
        return null;
    }
}
