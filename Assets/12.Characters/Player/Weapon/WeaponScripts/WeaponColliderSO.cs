using System;
using UnityEngine;

public enum ColliderShape { Sphere, Box, Capsule }

[CreateAssetMenu(menuName = "Game/ColliderSO")]
public class WeaponColliderSO : ScriptableObject
{
    [Header("Identifier")]
    public string id; // unique id, ex: "sword_hit_01"

    [Header("Shape")]
    public ColliderShape shape = ColliderShape.Sphere;

    [Tooltip("radius for sphere (x), box size, capsule: x=radius y=height")]
    public Vector3 size = Vector3.one;

    [Header("Transform")]
    public Vector3 localPosition = Vector3.zero;
    public Vector3 localEuler = Vector3.zero;
    public Vector3 localScale  = Vector3.one; // 추가: 런타임에 스케일 보정 가능

    [Header("Timing")]
    public float activeDelay = 0f;
    public float activeDuration = 0.2f;

    [Header("Hit / Logic")]
    public LayerMask hitLayers = ~0;
    [Tooltip("한 번 스폰된 동안 전체 허용 히트 수 (0 = 제한 없음)")]
    public int maxHits = 0; // 0이면 무한
    [Tooltip("같은 대상에 대해 한 번만 히트할지 여부 (true면 같은 타겟 중복 방지)")]
    public bool singleTargetPerSpawn = true;

    [Header("Damage / Tick (연타)")]
    [Tooltip("스텝(스텝당) 기본 데미지")]
    public float damage = 10f;

    [Tooltip("데미지 배수 (스킬/무기 등에서 곱해질 때 사용)")]
    public float damageMultiplier = 1f;

    [Tooltip("틱 기반 데미지 실행 주기 (0 = 단일 히트, >0 = 지속 틱 데미지)")]
    public float tickInterval = 0f;

    [Tooltip("한 틱에서 최대 몇번의 판정을 허용할지 (예: 넓은 범위를 여러번 체크할 때)")]
    public int hitsPerTick = 1;

    [Tooltip("힛 발생 시 즉시 콜라이더를 제거할지 여부 (예: single-hit)")]
    public bool clearOnHit = true;

    [Header("Ability Link")]
    [Tooltip("연결할 능력 ID (키 기반) — 에디터 검증 또는 SO 참조 권장")]
    public string abilityId;

    // 런타임 안전을 위해 SO 참조(옵션)
    [Tooltip("가능하면 Ability SO(또는 DamageProfileSO)를 직접 참조하는 것이 안전합니다.")]
    public ScriptableObject abilitySOReference; // cast to concrete SO in editor / runtime

    [Header("Motion (optional)")]
    public MotionProfile motion;

    [Header("Runtime prefab (Addressables key or local prefab)")]
    public string prefabAddressableKey; // if you spawn a prefab for hitbox/visuals

    [Serializable]
    public class MotionProfile {
        public float forwardDistance = 0f;
        public float duration = 0f;
        public AnimationCurve curve = AnimationCurve.Linear(0,0,1,1);
    }
}
