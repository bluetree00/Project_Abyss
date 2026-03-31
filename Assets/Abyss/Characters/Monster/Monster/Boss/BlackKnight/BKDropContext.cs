using UnityEngine;

/// <summary>
/// BKConcurrentDrop 및 BKRainAttackState 에 풀과 프리팹 정보를 묶어 전달하는 컨텍스트.
/// 풀이 null 이면 MeteorPrefab / HitVfxPrefab 으로 폴백.
/// </summary>
public struct BKDropContext
{
    public BKEffectPool       MeteorPool;
    public BKEffectPool       HitVfxPool;
    public BKGroundCirclePool CirclePool;

    // 풀이 없을 때 사용하는 프리팹 폴백
    public GameObject MeteorPrefab;
    public GameObject HitVfxPrefab;

    // [GroupDrop] 코루틴 호스트 GO 의 부모 (씬 루트 오염 방지)
    public Transform Parent;

    public BKDropContext(
        BKEffectPool       meteorPool,
        BKEffectPool       hitVfxPool,
        BKGroundCirclePool circlePool,
        GameObject         meteorPrefab = null,
        GameObject         hitVfxPrefab = null,
        Transform          parent       = null)
    {
        MeteorPool   = meteorPool;
        HitVfxPool   = hitVfxPool;
        CirclePool   = circlePool;
        MeteorPrefab = meteorPrefab;
        HitVfxPrefab = hitVfxPrefab;
        Parent       = parent;
    }
}
