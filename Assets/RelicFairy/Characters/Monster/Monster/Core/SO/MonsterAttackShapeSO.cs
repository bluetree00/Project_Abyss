using UnityEngine;
using RelicFairy.Monster;

/// <summary>
/// 몬스터 공격 형태 추상 SO.
/// MonsterStatSO.attackShape 에 할당하면 MonsterBase.DealDamageToPlayer()가
/// 기본 구체 판정 대신 이 SO에 위임한다.
/// </summary>
public abstract class MonsterAttackShapeSO : ScriptableObject
{
    [Header("VFX")]
    [Tooltip("공격 실행 시 스폰할 VFX 프리팹. null이면 생략.")]
    [SerializeField] private GameObject _vfxPrefab;
    [Tooltip("VFX 스케일 배율.")]
    [SerializeField] private float _vfxScale = 1f;
    [Tooltip("몬스터 위치 기준 스폰 오프셋.")]
    [SerializeField] private Vector3 _vfxOffset = Vector3.zero;
    [Tooltip("true 이면 VFX를 몬스터 Transform의 자식으로 붙인다.")]
    [SerializeField] private bool _vfxFollowTransform = false;

    /// <summary>
    /// 공격 판정 실행. 데미지, 넉백, 히트 대상 필터를 SO가 결정한다.
    /// </summary>
    /// <param name="ctx">몬스터 컨텍스트 (위치, 방향, SO 등 접근용).</param>
    /// <param name="damage">최종 데미지 (attackPower * AttackMultiplier 적용 후).</param>
    /// <param name="knockbackForce">적용할 넉백 힘.</param>
    public abstract void Execute(MonsterContext ctx, int damage, float knockbackForce);

    /// <summary>공격 실행 시점에 VFX를 스폰한다. _vfxPrefab이 null이면 아무것도 하지 않는다.</summary>
    /// <param name="parent">몬스터 루트 Transform (Follow 및 기본 위치 기준).</param>
    /// <param name="worldPosition">null이면 parent.position 사용. 경고장판 기준 위치 등 별도 지정 시 사용.</param>
    public void SpawnVFX(Transform parent, Vector3? worldPosition = null)
    {
        if (_vfxPrefab == null) return;

        Vector3 pos = (worldPosition ?? parent.position) + _vfxOffset;
        var go = Object.Instantiate(_vfxPrefab, pos, Quaternion.identity);
        go.transform.localScale = Vector3.one * _vfxScale;
        ApplyHierarchyScaling(go);

        if (_vfxFollowTransform)
            go.transform.SetParent(parent, true);

        AutoDestroy(go);
    }

    protected static void ApplyHierarchyScaling(GameObject go)
    {
        var systems = go.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < systems.Length; i++)
        {
            var main = systems[i].main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        }
    }

    private static void AutoDestroy(GameObject go)
    {
        var ps = go.GetComponent<ParticleSystem>()
              ?? go.GetComponentInChildren<ParticleSystem>();
        float lifetime = ps != null
            ? ps.main.duration + ps.main.startLifetimeMultiplier + 0.3f
            : 3f;
        Object.Destroy(go, lifetime);
    }
}
