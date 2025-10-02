using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Game/WeaponColliderPackageSO (Indexed Steps)")]
public class WeaponColliderPackageSO : ScriptableObject
{
    /// <summary>
    /// 하나의 EffectIndex 내 단계(step)별 콜라이더 정의
    /// </summary>
    [Serializable]
    public class StepEntry
    {
        [Header("Step Identity")]
        [Tooltip("EffectIndex 내 순서 / Step 번호")]
        public int step = 0;

        [Header("Collider Reference")]
        [Tooltip("실제 사용할 콜라이더 ScriptableObject 참조")]
        public WeaponColliderSO colliderSO;
    }

    /// <summary>
    /// 특정 EffectIndex에 포함된 StepEntry 리스트
    /// </summary>
    [Serializable]
    public class IndexEntry
    {
        [Header("Effect Index Identity")]
        [Tooltip("해당 액션 내 EffectIndex / 몇 번째 이벤트인지")]
        public int effectIndex = 0;

        [Header("Step List")]
        [Tooltip("EffectIndex 내 각 Step별 콜라이더 리스트")]
        public List<StepEntry> steps = new List<StepEntry>();
    }

    /// <summary>
    /// 특정 Group + ActionType의 콜라이더 묶음
    /// </summary>
    [Serializable]
    public class ActionEntry
    {
        [Header("Action Identity")]
        [Tooltip("Ground / Air 구분")]
        public WeaponAnimGroup group = WeaponAnimGroup.Ground;

        [Tooltip("Light / Heavy / QSkill 등 액션 타입")]
        public WeaponActionType actionType;

        [Header("Collider Indices")]
        [Tooltip("해당 액션에 포함된 EffectIndex 리스트")]
        public List<IndexEntry> effectIndices = new List<IndexEntry>();
    }

    [Header("Weapon Collider Package")]
    [Tooltip("장비 단위로 구성된 콜라이더 패키지")]
    public List<ActionEntry> actions = new List<ActionEntry>();

    // ------------------ 런타임 캐시 ------------------
    private Dictionary<(WeaponAnimGroup, WeaponActionType, int, int), WeaponColliderSO> _entryMap;

    private void OnEnable()
    {
        _entryMap = new Dictionary<(WeaponAnimGroup, WeaponActionType, int, int), WeaponColliderSO>();

        foreach (var action in actions)
        {
            foreach (var indexEntry in action.effectIndices)
            {
                foreach (var stepEntry in indexEntry.steps)
                {
                    var key = (action.group, action.actionType, indexEntry.effectIndex, stepEntry.step);
                    if (!_entryMap.ContainsKey(key) && stepEntry.colliderSO != null)
                        _entryMap.Add(key, stepEntry.colliderSO);
                }
            }
        }
    }

    /// <summary>
    /// 런타임 조회: Group, ActionType, EffectIndex, Step으로 검색
    /// </summary>
    public WeaponColliderSO GetCollider(WeaponAnimGroup group, WeaponActionType action, int effectIndex, int step = 0)
    {
        if (_entryMap == null) OnEnable();
        _entryMap.TryGetValue((group, action, effectIndex, step), out var colliderSO);
        return colliderSO;
    }
}
