using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// AbilitySetSO: 무기(또는 캐릭터)가 가진 모든 Ability 그룹을 정리
[CreateAssetMenu(menuName = "Game/AbilitySetSO")]
public class WeaponAbilitySetSO : ScriptableObject
{
    [Serializable]
    public class AbilityGroup {
        public WeaponActionType actionType;           // Light, Heavy, QSkill, etc.
        // 여러 종류: 콤보(여러 WeaponAbilitySO)일 수 있으니 리스트로 둔다.
        // 예: Light의 경우 리스트의 index(0..n-1)를 comboIndex-1로 매핑 가능
        public List<WeaponAbilitySO> abilities = new List<WeaponAbilitySO>();
        // 혹은 딕셔너리 매핑: comboIndex -> WeaponAbilitySO (Inspector 직렬화 어려움 때문에 list 사용)
        public WeaponAbilitySO GetAbilityForComboIndex(int comboIndex) {
            if (abilities == null || abilities.Count == 0) return null;
            // comboIndex는 1..N, map to list index mod or clamp
            int idx = Mathf.Clamp(comboIndex - 1, 0, abilities.Count - 1);
            return abilities[idx];
        }
    }

    public List<AbilityGroup> groups = new List<AbilityGroup>();

    // 런타임 헬퍼 (간단 선형 검색; 캐시화 가능)
    public WeaponAbilitySO GetAbility(WeaponActionType actionType, int comboIndex) {
        foreach (var g in groups) {
            if (g.actionType == actionType) return g.GetAbilityForComboIndex(comboIndex);
        }
        return null;
    }
}
