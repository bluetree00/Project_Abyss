using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Game/WeaponAnimationSetSO")]
public class WeaponAnimationSetSO : ScriptableObject
{

    [Serializable]
    public class ClipMapping
    {
        public string baseClipName;      // Animator Controller 상태 이름
        public string addressableKey;    // Addressables Key
        public WeaponActionType actionType;
        public int comboIndex = 0;       // 콤보 인덱스
    }

    [Serializable]
    public class AnimGroupMapping
    {
        public WeaponAnimGroup groupType;
        public List<ClipMapping> clipMappings = new List<ClipMapping>();

        // 그룹 안에서 액션 타입별 조회
        public IEnumerable<ClipMapping> GetMappingsForAction(WeaponActionType action)
        {
            foreach (var m in clipMappings)
                if (m.actionType == action) yield return m;
        }
    }

    // Ground / Air 그룹 리스트
    public List<AnimGroupMapping> animGroups = new List<AnimGroupMapping>();

    // 그룹과 액션으로 조회
    public IEnumerable<ClipMapping> GetMappings(WeaponAnimGroup group, WeaponActionType action)
    {
        var grp = animGroups.Find(g => g.groupType == group);
        if (grp != null)
            return grp.GetMappingsForAction(action);
        return new List<ClipMapping>();
    }

    // 모든 매핑 반환 (모든 그룹 포함)
    public IEnumerable<ClipMapping> GetAllMappings()
    {
        foreach (var g in animGroups)
            foreach (var m in g.clipMappings)
                yield return m;
    }
}
