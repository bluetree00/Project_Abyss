using UnityEngine;

/// <summary>
/// 보스 아레나 출구 마커. Next_Ch 빈 오브젝트에 부착해 클리어 시 제거할 벽 목록을 보관한다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BossExitMarker : MonoBehaviour
{
    [SerializeField] private GameObject[] wallsToHide = System.Array.Empty<GameObject>();
    public GameObject[] WallsToHide => wallsToHide;
}
