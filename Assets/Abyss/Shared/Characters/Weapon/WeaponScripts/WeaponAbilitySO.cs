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

        [Space(5)]
        [Header("Hit Effect")]
        [Tooltip("타격 시 생성할 이펙트 Addressable 키 (피격 위치에 스폰)")]
        public string hitEffectKey;
        public float hitEffectScale = 1f;

        [Tooltip("실행 컨텍스트(AbilityExecution)당 한 번만 실행")]
        public bool oneShot = false;

        [Tooltip("이 스텝 시작 시 플레이어가 마우스 방향으로 회전할지 여부")]
        public bool rotateToMouse = false;
    }

    /// <summary>이펙트 생성 기준점</summary>
    public enum EffectSocket
    {
        Player,       // 캐릭터 중심
        WeaponMount,  // 무기 장착점
        WeaponTip,    // 무기 끝 (베기/찌르기)
        WeaponRoot,   // 무기 손잡이
    }

    /// <summary>이펙트 생성 후 부모 설정</summary>
    public enum EffectSpace
    {
        World,  // 월드에 독립 (발사체, 슬래시, 폭발)
        Local,  // 소켓에 부모로 부착 (오라, 무기 강화)
    }

    [Serializable]
    public class EffectStep
    {
        [Tooltip("Addressables 키 (EffectBehaviour가 붙은 프리팹)")]
        public string payloadKey;

        [Header("스폰 기준")]
        [Tooltip("어디를 기준으로 생성할지")]
        public EffectSocket socket = EffectSocket.Player;

        [Tooltip("생성 후 독립(World) vs 부모에 부착(Local)")]
        public EffectSpace space = EffectSpace.World;

        [Header("오프셋")]
        [Tooltip("소켓 로컬 기준 위치 오프셋")]
        public Vector3 positionOffset = Vector3.zero;

        [Tooltip("소켓 로컬 기준 회전 오프셋 (오일러각)")]
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
