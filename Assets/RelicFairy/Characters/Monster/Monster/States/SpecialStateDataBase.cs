using UnityEngine;


namespace RelicFairy.Monster
{
/// <summary>
/// 특수 상태 데이터 베이스 ScriptableObject.
/// 모든 특수 상태 데이터는 이를 상속해 독립 .asset 파일로 생성된 뒤
/// MonsterConfigSO 의 제약 타입별 슬롯에 드래그하여 참조한다.
///
/// CreateState() 를 구현해 자신에 맞는 특수 상태 인스턴스를 반환한다.
/// LeeMonsterBase 가 초기화 시 자동으로 호출한다.
/// </summary>
public abstract class SpecialStateDataBase : ScriptableObject
{
    [Header("VFX")]
    [Tooltip("특수 상태 진입 시 스폰할 VFX 프리팹. null이면 생략.")]
    [SerializeField] private GameObject _vfxPrefab;
    [Tooltip("VFX 오브젝트 전체 스케일 배율.")]
    [SerializeField] private float _vfxScale = 1f;
    [Tooltip("몬스터 위치 기준 VFX 스폰 오프셋.")]
    [SerializeField] private Vector3 _vfxOffset = Vector3.zero;
    [Tooltip("true 이면 VFX 오브젝트를 몬스터 Transform의 자식으로 붙인다.")]
    [SerializeField] private bool _vfxFollowTransform = false;

    // ── Public Methods ─────────────────────────────────────────────────────────

    /// <summary>이 데이터에 대응하는 특수 상태 인스턴스를 생성해 반환한다.</summary>
    public abstract SpecialStateBase CreateState();

    /// <summary>
    /// 특수 상태 진입 시 VFX를 스폰한다.
    /// _vfxPrefab 이 null 이면 아무것도 하지 않는다.
    /// </summary>
    /// <param name="parent">몬스터 루트 Transform.</param>
    /// <returns>스폰된 VFX GameObject. null이면 프리팹 미할당.</returns>
    public GameObject SpawnVFX(Transform parent)
    {
        if (_vfxPrefab == null) return null;

        var position = parent.position + _vfxOffset;
        var go = Object.Instantiate(_vfxPrefab, position, Quaternion.identity);
        go.transform.localScale = Vector3.one * _vfxScale;
        ApplyHierarchyScaling(go);

        if (_vfxFollowTransform)
            go.transform.SetParent(parent, true);

        AutoDestroyVFX(go);
        return go;
    }

    // ── Private Methods ────────────────────────────────────────────────────────

    private static void ApplyHierarchyScaling(GameObject go)
    {
        var systems = go.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < systems.Length; i++)
        {
            var main = systems[i].main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        }
    }

    private static void AutoDestroyVFX(GameObject go)
    {
        if (go == null) return;

        var ps = go.GetComponent<ParticleSystem>()
              ?? go.GetComponentInChildren<ParticleSystem>();

        float lifetime = ps != null
            ? ps.main.duration + ps.main.startLifetimeMultiplier + 0.3f
            : 3f;

        Object.Destroy(go, lifetime);
    }
}
}
