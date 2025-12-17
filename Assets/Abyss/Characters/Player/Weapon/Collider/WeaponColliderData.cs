using System;
using UnityEngine;

/// <summary>
/// WeaponColliderSO 데이터를 Runtime에서 안전하게 복사하여 사용하는 클래스
/// 멀티 인스턴스 안전하며, 서버 JSON 데이터로 덮어쓰기도 가능
/// </summary>
[Serializable]
public class WeaponColliderData
{
    // Identifier
    public string id;

    // Shape
    public ColliderShape shape;
    public Vector3 size;

    // Transform
    public Vector3 localPosition;
    public Vector3 localEuler;
    public Vector3 localScale;

    // Timing
    public float activeDelay;
    public float activeDuration;

    // Hit / Logic
    public LayerMask hitLayers;
    public int maxHits;
    public bool singleTargetPerSpawn;

    // Damage / Tick
    public float damage;
    public float damageMultiplier;
    public float tickInterval;
    public int hitsPerTick;
    public bool clearOnHit;

    // Ability Link
    public string abilityId;
    public ScriptableObject abilitySOReference;

    // Motion
    public WeaponColliderSO.MotionProfile motion;

    // Runtime prefab
    public string prefabAddressableKey;

    // 생성자: SO 기반 RuntimeData 초기화
    public WeaponColliderData(WeaponColliderSO so)
    {
        if (so == null) throw new ArgumentNullException(nameof(so));

        id = so.id;
        shape = so.shape;
        size = so.size;

        localPosition = so.localPosition;
        localEuler = so.localEuler;
        localScale = so.localScale;

        activeDelay = so.activeDelay;
        activeDuration = so.activeDuration;

        hitLayers = so.hitLayers;
        maxHits = so.maxHits;
        singleTargetPerSpawn = so.singleTargetPerSpawn;

        damage = so.damage;
        damageMultiplier = so.damageMultiplier;
        tickInterval = so.tickInterval;
        hitsPerTick = so.hitsPerTick;
        clearOnHit = so.clearOnHit;

        abilityId = so.abilityId;
        abilitySOReference = so.abilitySOReference;

        motion = so.motion;

        prefabAddressableKey = so.prefabAddressableKey;
    }

    /// <summary>
    /// 서버 데이터나 런타임 변경 사항 덮어쓰기
    /// </summary>
    public void ApplyServerData(WeaponColliderData serverData)
    {
        if (serverData == null) return;

        damage = serverData.damage;
        damageMultiplier = serverData.damageMultiplier;
        tickInterval = serverData.tickInterval;
        hitsPerTick = serverData.hitsPerTick;
        activeDuration = serverData.activeDuration;
        // 필요 시 다른 필드 선택적 덮어쓰기
    }
}
