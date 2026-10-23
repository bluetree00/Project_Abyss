using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

/// <summary>
/// 독립 보스 아레나 테스트씬 전용 NavMesh 런타임 빌더.
/// GameRunBootstrapper의 프로시저럴 맵 NavMesh 빌드(BuildMapNavMeshAsync)는
/// 절차적으로 생성되는 맵(zone/room/map root)에만 적용되어, 씬에 직접 배치된
/// 아레나 프리팹은 대상에서 제외된다. 이 컴포넌트가 자신과 자식의 지오메트리로
/// 등록된 모든 NavMesh 에이전트 타입의 NavMesh를 직접 빌드한다.
/// </summary>
public class ArenaNavMeshBuilder : MonoBehaviour
{
    private void Start() => Build(gameObject);

    /// <summary>
    /// <paramref name="root"/>와 자식 콜라이더로 모든 에이전트 타입의 NavMesh를 굽는다(인트로 아레나도 씀 — 10-18).
    /// <paramref name="volume"/>(root 로컬)을 주면 그 안의 콜라이더만 — 성당처럼 큰 아레나를 통째로 구우면 수 분 멈춘다.
    /// </summary>
    public static void Build(GameObject root, Bounds? volume = null)
    {
        int excludeMask = ~((1 << LayerMask.NameToLayer("Player")) | (1 << LayerMask.NameToLayer("Monster")));

        int agentCount = NavMesh.GetSettingsCount();
        for (int i = 0; i < agentCount; i++)
        {
            var agentSettings = NavMesh.GetSettingsByIndex(i);
            var surface = root.AddComponent<NavMeshSurface>();
            surface.agentTypeID    = agentSettings.agentTypeID;
            surface.collectObjects = volume.HasValue ? CollectObjects.Volume : CollectObjects.Children;
            if (volume.HasValue) { surface.center = volume.Value.center; surface.size = volume.Value.size; }
            surface.useGeometry    = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask      = excludeMask;
            surface.BuildNavMesh();
        }
    }
}
