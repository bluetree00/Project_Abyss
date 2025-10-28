using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[CreateAssetMenu(menuName = "Game/WeaponAbilitySO (Steps with Effect/Collider)")]
public class WeaponAbilitySO : ScriptableObject
{
    [Header("Ability Steps")]
    [Tooltip("애니메이션 이벤트의 stepIndex에 매칭되는 스텝 리스트")]
    public List<AbilityStep> steps = new List<AbilityStep>();

    /// <summary>
    /// 해당 stepIndex에 매칭되는 스텝 리스트 반환
    /// </summary>
    public List<AbilityStep> GetSteps(int stepIndex)
    {
        if (steps == null || steps.Count == 0) return new List<AbilityStep>();
        return steps.Where(s => s.stepIndex == stepIndex).OrderBy(s => s.order).ToList();
    }

    /// <summary>
    /// OneShot 초기화
    /// </summary>
    public void ResetOneShots()
    {
        if (steps == null) return;
        foreach (var s in steps) s.triggeredThisActivation = false;
    }

    [Serializable]
    public class AbilityStep
    {
        [Header("Step Identity")]
        public int stepIndex = 0;
        public int order = 0;

        [Space(5)]
        [Header("Damage & Impact Values")]
        public float baseDamage = 0f;
        public float knockbackMultiplier = 1f;
        public Vector3 selfMovement = Vector3.zero;

        [Space(5)]
        [Header("Effect / Visual")]
        public EffectStep effect;

        [Space(5)]
        [Header("Collider / Damage")]
        public ColliderStep collider;

        [Tooltip("한번만 실행되는 스텝 여부")]
        public bool oneShot = false;

        [NonSerialized] public bool triggeredThisActivation = false;
    }

    [Serializable]
    public class EffectStep
    {
        public string payloadKey;
        public Vector3 positionOffset = Vector3.zero;
        public Vector3 rotationEuler = Vector3.zero;
        public Vector3 forwardOffset = Vector3.zero;
        public float scaleMultiplier = 1f;
        public float lifeTimeMultiplier = 1f;

        [Tooltip("특수 행동 로직이 필요한 경우")]
        public EffectBehaviorSO behavior;
    }

    // Collider 모양 enum
    public enum ColliderShape
    {
        Box,
        Sphere,
        Capsule
    }

    [Serializable]
    public class ColliderStep
    {
        public string payloadKey;
        public Vector3 positionOffset = Vector3.zero;
        public Vector3 rotationEuler = Vector3.zero;
        public Vector3 forwardOffset = Vector3.zero;
        public float sizeMultiplier = 1f;
        public float durationMultiplier = 1f;
        public float damage = 0f;
        public float hitInterval = 0.1f;
        public float duration = 2f;
        public ColliderShape shape = ColliderShape.Box;

        [Tooltip("특수 콜라이더 로직이 필요한 경우")]
        public ColliderBehaviorSO behavior;

        [Tooltip("Prefab/Addressable Key, 있으면 이걸로 생성")]
        public string colliderPrefabKey; 
    }

}
