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
    private void Start()
    {
        int excludeMask = ~((1 << LayerMask.NameToLayer("Player")) | (1 << LayerMask.NameToLayer("Monster")));

        int agentCount = NavMesh.GetSettingsCount();
        for (int i = 0; i < agentCount; i++)
        {
            var agentSettings = NavMesh.GetSettingsByIndex(i);
            var surface = gameObject.AddComponent<NavMeshSurface>();
            surface.agentTypeID    = agentSettings.agentTypeID;
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry    = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask      = excludeMask;
            surface.BuildNavMesh();
        }
    }
}
