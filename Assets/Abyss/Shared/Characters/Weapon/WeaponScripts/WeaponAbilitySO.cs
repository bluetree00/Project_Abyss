using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Game/WeaponAbilitySO (Steps with Effect/Collider)")]
public class WeaponAbilitySO : ScriptableObject
{
    [Header("Ability Steps")]
    [Tooltip("애니메이션 이벤트의 stepIndex에 매칭되는 스텝 리스트")]
    public List<AbilityStep> steps = new List<AbilityStep>();

    // stepIndex → 정렬된 AbilityStep 리스트 (GC 없는 조회용)
    private Dictionary<int, List<AbilityStep>> _stepCache;
    private static readonly IReadOnlyList<AbilityStep> _emptySteps = Array.Empty<AbilityStep>();

    private void OnEnable() => RebuildCache();

    private void RebuildCache()
    {
        _stepCache = new Dictionary<int, List<AbilityStep>>();
        if (steps == null) return;

        foreach (var s in steps)
        {
            if (!_stepCache.TryGetValue(s.stepIndex, out var list))
                _stepCache[s.stepIndex] = list = new List<AbilityStep>();
            list.Add(s);
        }

        foreach (var list in _stepCache.Values)
            list.Sort((a, b) => a.order.CompareTo(b.order));
    }

    /// <summary>
    /// 해당 stepIndex에 매칭되는 스텝 리스트 반환 (캐시 기반, GC 없음)
    /// </summary>
    public IReadOnlyList<AbilityStep> GetSteps(int stepIndex)
    {
        if (_stepCache == null) RebuildCache();
        return _stepCache.TryGetValue(stepIndex, out var list) ? list : _emptySteps;
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

        [Tooltip("실행 컨텍스트(AbilityExecution)당 한 번만 실행")]
        public bool oneShot = false;

        [Tooltip("이 스텝 시작 시 플레이어가 마우스 방향으로 회전할지 여부")]
        public bool rotateToMouse = false;
    }

    [Serializable]
    public class EffectStep
    {
        [Tooltip("Addressables 키 (EffectBehaviour가 붙은 프리팹)")]
        public string payloadKey;

        [Tooltip("플레이어 로컬 스페이스 기준 위치 오프셋")]
        public Vector3 positionOffset = Vector3.zero;

        [Tooltip("이펙트 회전 (오일러각)")]
        public Vector3 rotationEuler = Vector3.zero;

        public float scaleMultiplier = 1f;
        public float lifeTimeMultiplier = 1f;

        [Tooltip("특수 행동 로직이 필요한 경우")]
        public EffectBehaviorSO behavior;
    }

    public enum ColliderMode
    {
        Trail,   // 무기 Root→Tip SphereCast (기본 근접 공격)
        Spawned, // 풀에서 오브젝트 스폰 (스킬, 장판, 투사체)
    }

    public enum ColliderShape
    {
        Box,
        Sphere,
        Capsule
    }

    [Serializable]
    public class ColliderStep
    {
        [Header("판정 방식")]
        public ColliderMode mode = ColliderMode.Trail;

        [Header("공통 - 데미지")]
        public float damage = 0f;

        [Header("Trail 모드")]
        [Tooltip("0이면 WeaponInstance.hitRadius 사용")]
        public float trailRadiusOverride = 0f;

        [Header("Spawned 모드")]
        [Tooltip("Addressables 키 (ColliderInstance가 붙은 프리팹). 비어있으면 런타임 생성")]
        public string colliderPrefabKey;

        [Tooltip("핸드 트랜스폼 로컬 스페이스 기준 위치 오프셋")]
        public Vector3 positionOffset = Vector3.zero;

        [Tooltip("콜라이더 회전 (오일러각)")]
        public Vector3 rotationEuler = Vector3.zero;

        public float sizeMultiplier = 1f;
        public float hitInterval = 0.1f;
        public float duration = 2f;
        public ColliderShape shape = ColliderShape.Box;

        [Tooltip("특수 콜라이더 로직이 필요한 경우")]
        public ColliderBehaviorSO behavior;
    }
}
