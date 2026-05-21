using Unity.AI.Navigation;
using UnityEngine;

namespace RelicFairy.Runtime
{
    /// <summary>
    /// NavMeshSurface 의 navMeshData 가 할당돼 있음에도 play 시작 시 AddData 가
    /// 누락되어 NavMesh.SamplePosition 이 실패하는 케이스를 방지한다.
    /// Awake 에서 Remove → Add 를 강제 실행해 runtime NavMesh 에 확실히 적재한다.
    /// </summary>
    [RequireComponent(typeof(NavMeshSurface))]
    public class NavMeshSurfaceRuntimeLoader : MonoBehaviour
    {
        private NavMeshSurface _surface;

        private void Awake()
        {
            _surface = GetComponent<NavMeshSurface>();
        }

        private void OnEnable()
        {
            if (_surface == null) return;
            if (_surface.navMeshData == null)
            {
                Debug.LogWarning(
                    $"[NavMeshSurfaceRuntimeLoader] {name}: navMeshData 미할당. 베이크 먼저 실행 필요.",
                    this);
                return;
            }

            _surface.RemoveData();
            _surface.AddData();
            Debug.Log(
                $"[NavMeshSurfaceRuntimeLoader] {name}: NavMesh 강제 재로드 완료.",
                this);
        }

        private void OnDisable()
        {
            if (_surface != null && _surface.navMeshData != null)
                _surface.RemoveData();
        }
    }
}
